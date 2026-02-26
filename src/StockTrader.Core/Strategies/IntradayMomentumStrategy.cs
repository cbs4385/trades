using StockTrader.Core.Engine;
using StockTrader.Core.Indicators;
using StockTrader.Core.Models;

namespace StockTrader.Core.Strategies;

/// Intraday momentum strategy: buy at Open, sell at Close same day.
///
/// Scores each stock daily using multiple factors to find the best
/// intraday opportunity. The engine (in intraday mode) picks the
/// highest-scored stock each day.
///
/// Scoring factors:
///   - Trend: price vs 20 SMA, 5-day momentum
///   - Momentum: RSI in 40-65 zone, MACD histogram positive
///   - Candle: prior day closed near its high (bullish follow-through)
///   - Volume: above-average volume confirms institutional interest
public class IntradayMomentumStrategy : ITradingStrategy
{
    private readonly decimal _minimumScore;
    private readonly int _smaPeriod;
    private readonly int _rsiPeriod;

    public string Name => "Intraday Momentum (multi-factor scoring)";

    public IntradayMomentumStrategy(
        decimal minimumScore = 5m,
        int smaPeriod = 20,
        int rsiPeriod = 14)
    {
        _minimumScore = minimumScore;
        _smaPeriod = smaPeriod;
        _rsiPeriod = rsiPeriod;
    }

    public List<TradingSignal> Evaluate(StockQuote quote, Portfolio portfolio)
    {
        var prices = quote.Prices;

        // Need enough history for indicators (at least 35 bars for MACD slow=26 + signal=9)
        if (prices.Count < 35)
            return new();

        // Don't buy if we already hold this symbol (shouldn't happen in intraday mode)
        if (portfolio.Positions.ContainsKey(quote.Symbol))
            return new();

        var today = prices[^1];
        var yesterday = prices[^2];

        // Calculate indicators using all available data
        var sma = TechnicalIndicators.SMA(prices, _smaPeriod);
        var rsi = TechnicalIndicators.RSI(prices, _rsiPeriod);
        var macd = TechnicalIndicators.MACD(prices);

        var yi = prices.Count - 2; // yesterday's index

        // All indicators must be available
        if (!sma[yi].Value.HasValue || !rsi[yi].Value.HasValue || !macd[yi].Histogram.HasValue)
            return new();

        var score = 0m;
        var reasons = new List<string>();

        // --- Factor 1: Trend (0-3 points) ---
        // Price above 20 SMA = uptrend
        if (yesterday.Close > sma[yi].Value!.Value)
        {
            score += 2m;
            reasons.Add("above SMA");
        }

        // 5-day positive return = short-term momentum
        if (prices.Count >= 7 && yesterday.Close > prices[^7].Close)
        {
            score += 1m;
            reasons.Add("5d momentum+");
        }

        // --- Factor 2: RSI momentum (0-2 points) ---
        var rsiVal = rsi[yi].Value!.Value;
        if (rsiVal > 40m && rsiVal < 65m)
        {
            // Sweet spot: bullish momentum without being overextended
            score += 2m;
            reasons.Add($"RSI={rsiVal:F0}");
        }
        else if (rsiVal >= 30m && rsiVal <= 70m)
        {
            // Acceptable range
            score += 1m;
            reasons.Add($"RSI={rsiVal:F0}");
        }
        // RSI > 70 or < 30: no points (overextended)

        // --- Factor 3: MACD momentum (0-2 points) ---
        var hist = macd[yi].Histogram!.Value;
        if (hist > 0)
        {
            score += 1m;
            reasons.Add("MACD+");
        }
        // Histogram increasing (accelerating momentum)
        if (yi >= 1 && macd[yi - 1].Histogram.HasValue && hist > macd[yi - 1].Histogram!.Value)
        {
            score += 1m;
            reasons.Add("MACD accel");
        }

        // --- Factor 4: Candle quality (0-2 points) ---
        // Yesterday closed near its high → bullish follow-through
        var range = yesterday.High - yesterday.Low;
        if (range > 0)
        {
            var closeLocationValue = (yesterday.Close - yesterday.Low) / range;
            if (closeLocationValue > 0.7m)
            {
                score += 2m;
                reasons.Add($"CLV={closeLocationValue:F2}");
            }
            else if (closeLocationValue > 0.5m)
            {
                score += 1m;
            }
        }

        // --- Factor 5: Volume (0-1 point) ---
        var lookback = Math.Min(20, yi);
        if (lookback > 0)
        {
            var avgVolume = prices.Skip(yi - lookback).Take(lookback).Average(p => (decimal)p.Volume);
            if (avgVolume > 0 && yesterday.Volume > (long)(avgVolume * 1.1m))
            {
                score += 1m;
                reasons.Add("vol+");
            }
        }

        // Only signal if score meets threshold
        if (score >= _minimumScore)
        {
            return new List<TradingSignal>
            {
                new(quote.Symbol, SignalType.Buy, today.Open, today.Date,
                    $"Score {score:F1}: {string.Join(", ", reasons)}",
                    Score: score)
            };
        }

        return new();
    }
}
