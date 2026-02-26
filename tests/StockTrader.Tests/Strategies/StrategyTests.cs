using StockTrader.Core.Models;
using StockTrader.Core.Strategies;

namespace StockTrader.Tests.Strategies;

public class StrategyTests
{
    private static List<StockPrice> GeneratePricesWithCrossover(int days)
    {
        // Generate prices that start declining then start rising (to create a golden cross)
        var prices = new List<StockPrice>();
        for (int i = 0; i < days; i++)
        {
            decimal close;
            if (i < days / 2)
                close = 100m - i * 0.3m; // downtrend
            else
                close = 100m - (days / 2) * 0.3m + (i - days / 2) * 0.8m; // uptrend

            prices.Add(new StockPrice(
                DateTime.Today.AddDays(-days + i + 1),
                close - 0.2m, close + 1m, close - 1m, close, 1_000_000));
        }
        return prices;
    }

    [Fact]
    public void SmaCrossover_ReturnsEmptyForInsufficientData()
    {
        var strategy = new SmaCrossoverStrategy(fastPeriod: 10, slowPeriod: 30);
        var prices = Enumerable.Range(0, 15).Select(i => new StockPrice(
            DateTime.Today.AddDays(-15 + i), 100m, 101m, 99m, 100m, 1_000_000)).ToList();

        var portfolio = new Portfolio(10_000m);
        var signals = strategy.Evaluate(new StockQuote("TEST", prices), portfolio);

        Assert.Empty(signals);
    }

    [Fact]
    public void SmaCrossover_GeneratesBuyOnGoldenCross()
    {
        var strategy = new SmaCrossoverStrategy(fastPeriod: 5, slowPeriod: 15);
        var prices = GeneratePricesWithCrossover(100);

        var portfolio = new Portfolio(100_000m);

        // Feed prices incrementally to find a buy signal
        var signals = new List<TradingSignal>();
        for (int i = 20; i <= prices.Count; i++)
        {
            var subset = prices.Take(i).ToList();
            var quote = new StockQuote("TEST", subset);
            var result = strategy.Evaluate(quote, portfolio);
            signals.AddRange(result);
            if (signals.Any(s => s.Type == SignalType.Buy)) break;
        }

        Assert.Contains(signals, s => s.Type == SignalType.Buy);
    }

    [Fact]
    public void RsiMeanReversion_BuysOnOversold()
    {
        var strategy = new RsiMeanReversionStrategy(rsiPeriod: 14, rsiOversold: 30m, rsiOverbought: 70m);

        // Generate a sharp decline to trigger oversold RSI
        var prices = new List<StockPrice>();
        for (int i = 0; i < 50; i++)
        {
            var close = i < 30 ? 100m : 100m - (i - 30) * 3m;
            prices.Add(new StockPrice(
                DateTime.Today.AddDays(-50 + i),
                close - 0.2m, close + 0.5m, close - 0.5m, close, 1_000_000));
        }

        var portfolio = new Portfolio(100_000m);
        var signals = new List<TradingSignal>();

        for (int i = 20; i <= prices.Count; i++)
        {
            var subset = prices.Take(i).ToList();
            var quote = new StockQuote("TEST", subset);
            signals.AddRange(strategy.Evaluate(quote, portfolio));
            if (signals.Any(s => s.Type == SignalType.Buy)) break;
        }

        Assert.Contains(signals, s => s.Type == SignalType.Buy);
        Assert.Contains("oversold", signals.First(s => s.Type == SignalType.Buy).Reason);
    }

    [Fact]
    public void SmaCrossover_NameIncludesPeriods()
    {
        var strategy = new SmaCrossoverStrategy(fastPeriod: 10, slowPeriod: 30);
        Assert.Contains("10", strategy.Name);
        Assert.Contains("30", strategy.Name);
    }
}
