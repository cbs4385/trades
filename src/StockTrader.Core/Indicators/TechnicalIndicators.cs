using StockTrader.Core.Models;

namespace StockTrader.Core.Indicators;

public static class TechnicalIndicators
{
    /// Simple Moving Average over the given period.
    public static List<IndicatorValue> SMA(List<StockPrice> prices, int period)
    {
        var results = new List<IndicatorValue>();

        for (int i = 0; i < prices.Count; i++)
        {
            if (i < period - 1)
            {
                results.Add(new IndicatorValue(prices[i].Date, null));
                continue;
            }

            var sum = 0m;
            for (int j = i - period + 1; j <= i; j++)
                sum += prices[j].Close;

            results.Add(new IndicatorValue(prices[i].Date, sum / period));
        }

        return results;
    }

    /// Exponential Moving Average over the given period.
    public static List<IndicatorValue> EMA(List<StockPrice> prices, int period)
    {
        var results = new List<IndicatorValue>();
        var multiplier = 2m / (period + 1);

        for (int i = 0; i < prices.Count; i++)
        {
            if (i < period - 1)
            {
                results.Add(new IndicatorValue(prices[i].Date, null));
                continue;
            }

            if (i == period - 1)
            {
                // Seed EMA with SMA
                var sum = 0m;
                for (int j = 0; j < period; j++)
                    sum += prices[j].Close;
                results.Add(new IndicatorValue(prices[i].Date, sum / period));
                continue;
            }

            var prevEma = results[i - 1].Value!.Value;
            var ema = (prices[i].Close - prevEma) * multiplier + prevEma;
            results.Add(new IndicatorValue(prices[i].Date, ema));
        }

        return results;
    }

    /// Relative Strength Index over the given period (default 14).
    public static List<IndicatorValue> RSI(List<StockPrice> prices, int period = 14)
    {
        var results = new List<IndicatorValue>();

        if (prices.Count < period + 1)
        {
            results.AddRange(prices.Select(p => new IndicatorValue(p.Date, null)));
            return results;
        }

        // First value is null (no previous price to compare)
        results.Add(new IndicatorValue(prices[0].Date, null));

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        // Calculate initial gains and losses
        for (int i = 1; i < prices.Count; i++)
        {
            var change = prices[i].Close - prices[i - 1].Close;
            gains.Add(Math.Max(change, 0));
            losses.Add(Math.Max(-change, 0));
        }

        // First RSI values use simple average
        for (int i = 1; i <= period; i++)
            results.Add(new IndicatorValue(prices[i].Date, null));

        var avgGain = gains.Take(period).Average();
        var avgLoss = losses.Take(period).Average();

        var rs = avgLoss == 0 ? 100m : avgGain / avgLoss;
        var rsi = 100m - (100m / (1m + rs));
        results[period] = new IndicatorValue(prices[period].Date, rsi);

        // Subsequent RSI values use smoothed average
        for (int i = period; i < gains.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;

            rs = avgLoss == 0 ? 100m : avgGain / avgLoss;
            rsi = 100m - (100m / (1m + rs));

            if (i + 1 < results.Count)
                results[i + 1] = new IndicatorValue(prices[i + 1].Date, rsi);
            else
                results.Add(new IndicatorValue(prices[i + 1].Date, rsi));
        }

        return results;
    }

    /// MACD (Moving Average Convergence Divergence).
    /// Default: fast=12, slow=26, signal=9.
    public static List<MacdResult> MACD(
        List<StockPrice> prices,
        int fastPeriod = 12,
        int slowPeriod = 26,
        int signalPeriod = 9)
    {
        var fastEma = EMA(prices, fastPeriod);
        var slowEma = EMA(prices, slowPeriod);

        // MACD line = fast EMA - slow EMA
        var macdLine = new List<IndicatorValue>();
        for (int i = 0; i < prices.Count; i++)
        {
            var fast = fastEma[i].Value;
            var slow = slowEma[i].Value;
            var macd = (fast.HasValue && slow.HasValue) ? fast.Value - slow.Value : (decimal?)null;
            macdLine.Add(new IndicatorValue(prices[i].Date, macd));
        }

        // Signal line = EMA of MACD line
        var signalLine = EmaOfValues(macdLine, signalPeriod);

        // Build results
        var results = new List<MacdResult>();
        for (int i = 0; i < prices.Count; i++)
        {
            var macd = macdLine[i].Value;
            var signal = signalLine[i].Value;
            var histogram = (macd.HasValue && signal.HasValue)
                ? macd.Value - signal.Value
                : (decimal?)null;

            results.Add(new MacdResult(prices[i].Date, macd, signal, histogram));
        }

        return results;
    }

    /// Bollinger Bands (middle = SMA, upper/lower = SMA +/- stddev * multiplier).
    public static List<(DateTime Date, decimal? Upper, decimal? Middle, decimal? Lower)> BollingerBands(
        List<StockPrice> prices, int period = 20, decimal multiplier = 2m)
    {
        var sma = SMA(prices, period);
        var results = new List<(DateTime, decimal?, decimal?, decimal?)>();

        for (int i = 0; i < prices.Count; i++)
        {
            if (!sma[i].Value.HasValue)
            {
                results.Add((prices[i].Date, null, null, null));
                continue;
            }

            var mean = sma[i].Value!.Value;
            var sumSqDiff = 0m;
            for (int j = i - period + 1; j <= i; j++)
            {
                var diff = prices[j].Close - mean;
                sumSqDiff += diff * diff;
            }
            var stdDev = (decimal)Math.Sqrt((double)(sumSqDiff / period));

            results.Add((prices[i].Date, mean + multiplier * stdDev, mean, mean - multiplier * stdDev));
        }

        return results;
    }

    private static List<IndicatorValue> EmaOfValues(List<IndicatorValue> values, int period)
    {
        var results = new List<IndicatorValue>();
        var multiplier = 2m / (period + 1);

        // Find the first non-null values to start from
        var validValues = values.Where(v => v.Value.HasValue).ToList();

        if (validValues.Count < period)
        {
            return values.Select(v => new IndicatorValue(v.Date, null)).ToList();
        }

        int validCount = 0;
        decimal? prevEma = null;

        for (int i = 0; i < values.Count; i++)
        {
            if (!values[i].Value.HasValue)
            {
                results.Add(new IndicatorValue(values[i].Date, null));
                continue;
            }

            validCount++;

            if (validCount < period)
            {
                results.Add(new IndicatorValue(values[i].Date, null));
                continue;
            }

            if (validCount == period)
            {
                // Seed with SMA of first 'period' valid values
                var seedValues = values.Where(v => v.Value.HasValue).Take(period);
                prevEma = seedValues.Average(v => v.Value!.Value);
                results.Add(new IndicatorValue(values[i].Date, prevEma));
                continue;
            }

            prevEma = (values[i].Value!.Value - prevEma!.Value) * multiplier + prevEma.Value;
            results.Add(new IndicatorValue(values[i].Date, prevEma));
        }

        return results;
    }
}
