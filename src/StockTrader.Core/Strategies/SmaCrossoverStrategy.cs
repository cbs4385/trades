using StockTrader.Core.Engine;
using StockTrader.Core.Indicators;
using StockTrader.Core.Models;

namespace StockTrader.Core.Strategies;

/// SMA Crossover strategy with RSI and MACD confirmation.
///
/// Buy when:
///   - Fast SMA crosses above slow SMA (golden cross)
///   - RSI is below the overbought threshold (not overbought)
///   - MACD histogram is positive (momentum confirmation)
///
/// Sell when:
///   - Fast SMA crosses below slow SMA (death cross)
///   - OR RSI exceeds overbought threshold
///   - OR MACD histogram turns negative
public class SmaCrossoverStrategy : ITradingStrategy
{
    private readonly int _fastPeriod;
    private readonly int _slowPeriod;
    private readonly int _rsiPeriod;
    private readonly decimal _rsiOverbought;
    private readonly decimal _rsiOversold;
    private readonly int _macdSellBars;

    public string Name => $"SMA Crossover ({_fastPeriod}/{_slowPeriod}) + RSI({_rsiPeriod}) + MACD";

    public SmaCrossoverStrategy(
        int fastPeriod = 10,
        int slowPeriod = 30,
        int rsiPeriod = 14,
        decimal rsiOverbought = 70m,
        decimal rsiOversold = 30m,
        int macdSellBars = 1)
    {
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _rsiPeriod = rsiPeriod;
        _rsiOverbought = rsiOverbought;
        _rsiOversold = rsiOversold;
        _macdSellBars = macdSellBars;
    }

    public List<TradingSignal> Evaluate(StockQuote quote, Portfolio portfolio)
    {
        var signals = new List<TradingSignal>();
        var prices = quote.Prices;

        if (prices.Count < _slowPeriod + 2)
            return signals;

        var fastSma = TechnicalIndicators.SMA(prices, _fastPeriod);
        var slowSma = TechnicalIndicators.SMA(prices, _slowPeriod);
        var rsi = TechnicalIndicators.RSI(prices, _rsiPeriod);
        var macd = TechnicalIndicators.MACD(prices);

        var i = prices.Count - 1;
        var prev = i - 1;

        // Need current and previous indicator values
        if (!fastSma[i].Value.HasValue || !slowSma[i].Value.HasValue ||
            !fastSma[prev].Value.HasValue || !slowSma[prev].Value.HasValue)
            return signals;

        var currentFast = fastSma[i].Value!.Value;
        var currentSlow = slowSma[i].Value!.Value;
        var prevFast = fastSma[prev].Value!.Value;
        var prevSlow = slowSma[prev].Value!.Value;

        var currentRsi = rsi[i].Value;
        var currentMacdHist = macd[i].Histogram;

        var currentPrice = prices[i].Close;
        var currentDate = prices[i].Date;
        var hasPosition = portfolio.Positions.ContainsKey(quote.Symbol);

        // BUY signal: golden cross + RSI not overbought + positive MACD
        if (!hasPosition
            && prevFast <= prevSlow
            && currentFast > currentSlow)
        {
            var reasons = new List<string> { $"SMA golden cross ({_fastPeriod} > {_slowPeriod})" };

            bool rsiOk = !currentRsi.HasValue || currentRsi.Value < _rsiOverbought;
            bool macdOk = !currentMacdHist.HasValue || currentMacdHist.Value > 0;

            if (rsiOk && macdOk)
            {
                if (currentRsi.HasValue) reasons.Add($"RSI={currentRsi.Value:F1}");
                if (currentMacdHist.HasValue) reasons.Add($"MACD hist={currentMacdHist.Value:F4}");

                signals.Add(new TradingSignal(
                    quote.Symbol, SignalType.Buy, currentPrice,
                    currentDate, string.Join(", ", reasons)));
            }
        }

        // SELL signal: death cross OR overbought RSI OR negative MACD
        if (hasPosition)
        {
            var sellReasons = new List<string>();

            if (prevFast >= prevSlow && currentFast < currentSlow)
                sellReasons.Add($"SMA death cross ({_fastPeriod} < {_slowPeriod})");

            if (currentRsi.HasValue && currentRsi.Value > _rsiOverbought)
                sellReasons.Add($"RSI overbought ({currentRsi.Value:F1})");

            // MACD sell: require histogram to be negative for N consecutive bars
            if (currentMacdHist.HasValue && currentMacdHist.Value < 0 && _macdSellBars > 0)
            {
                int consecutiveNegative = 0;
                for (int j = i; j >= 0 && j > i - _macdSellBars - 10; j--)
                {
                    if (macd[j].Histogram.HasValue && macd[j].Histogram!.Value < 0)
                        consecutiveNegative++;
                    else
                        break;
                }
                if (consecutiveNegative >= _macdSellBars)
                    sellReasons.Add($"MACD histogram negative ({consecutiveNegative} bars)");
            }

            if (sellReasons.Count > 0)
            {
                signals.Add(new TradingSignal(
                    quote.Symbol, SignalType.Sell, currentPrice,
                    currentDate, string.Join(", ", sellReasons)));
            }
        }

        return signals;
    }
}
