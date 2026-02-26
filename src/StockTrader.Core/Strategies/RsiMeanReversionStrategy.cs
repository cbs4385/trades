using StockTrader.Core.Engine;
using StockTrader.Core.Indicators;
using StockTrader.Core.Models;

namespace StockTrader.Core.Strategies;

/// RSI Mean Reversion strategy.
///
/// Buy when RSI drops below oversold level (stock is undervalued).
/// Sell when RSI rises above overbought level (stock is overvalued).
/// Uses Bollinger Bands as additional confirmation.
public class RsiMeanReversionStrategy : ITradingStrategy
{
    private readonly int _rsiPeriod;
    private readonly decimal _rsiOverbought;
    private readonly decimal _rsiOversold;
    private readonly int _bbPeriod;

    public string Name => $"RSI Mean Reversion (RSI {_rsiPeriod}: {_rsiOversold}/{_rsiOverbought})";

    public RsiMeanReversionStrategy(
        int rsiPeriod = 14,
        decimal rsiOverbought = 70m,
        decimal rsiOversold = 30m,
        int bbPeriod = 20)
    {
        _rsiPeriod = rsiPeriod;
        _rsiOverbought = rsiOverbought;
        _rsiOversold = rsiOversold;
        _bbPeriod = bbPeriod;
    }

    public List<TradingSignal> Evaluate(StockQuote quote, Portfolio portfolio)
    {
        var signals = new List<TradingSignal>();
        var prices = quote.Prices;

        if (prices.Count < Math.Max(_rsiPeriod, _bbPeriod) + 2)
            return signals;

        var rsi = TechnicalIndicators.RSI(prices, _rsiPeriod);
        var bb = TechnicalIndicators.BollingerBands(prices, _bbPeriod);

        var i = prices.Count - 1;
        var currentRsi = rsi[i].Value;
        var currentPrice = prices[i].Close;
        var currentDate = prices[i].Date;
        var hasPosition = portfolio.Positions.ContainsKey(quote.Symbol);

        if (!currentRsi.HasValue) return signals;

        // BUY: RSI oversold, preferably near lower Bollinger Band
        if (!hasPosition && currentRsi.Value < _rsiOversold)
        {
            var reason = $"RSI oversold ({currentRsi.Value:F1})";

            if (bb[i].Lower is { } lower && currentPrice <= lower)
                reason += ", price at lower Bollinger Band";

            signals.Add(new TradingSignal(
                quote.Symbol, SignalType.Buy, currentPrice, currentDate, reason));
        }

        // SELL: RSI overbought, preferably near upper Bollinger Band
        if (hasPosition && currentRsi.Value > _rsiOverbought)
        {
            var reason = $"RSI overbought ({currentRsi.Value:F1})";

            if (bb[i].Upper is { } upper && currentPrice >= upper)
                reason += ", price at upper Bollinger Band";

            signals.Add(new TradingSignal(
                quote.Symbol, SignalType.Sell, currentPrice, currentDate, reason));
        }

        return signals;
    }
}
