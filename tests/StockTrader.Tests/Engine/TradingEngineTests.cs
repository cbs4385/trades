using StockTrader.Core.Engine;
using StockTrader.Core.Models;

namespace StockTrader.Tests.Engine;

public class TradingEngineTests
{
    private static List<StockPrice> GenerateUptrend(int days, decimal start = 100m)
    {
        return Enumerable.Range(0, days).Select(i =>
        {
            var close = start + i * 0.5m;
            return new StockPrice(
                DateTime.Today.AddDays(-days + i + 1),
                close - 0.2m, close + 1m, close - 1m, close, 1_000_000);
        }).ToList();
    }

    [Fact]
    public void Portfolio_InitializesCorrectly()
    {
        var portfolio = new Portfolio(100_000m);

        Assert.Equal(100_000m, portfolio.Cash);
        Assert.Equal(100_000m, portfolio.InitialCash);
        Assert.Empty(portfolio.Positions);
        Assert.Equal(100_000m, portfolio.TotalValue);
    }

    [Fact]
    public void Engine_ExecutesBuyOrder()
    {
        var portfolio = new Portfolio(10_000m);
        var strategy = new AlwaysBuyOnceStrategy();
        var engine = new TradingEngine(portfolio, strategy);

        var prices = GenerateUptrend(50);
        var quotes = new List<StockQuote> { new("TEST", prices) };

        var result = engine.RunBacktest(quotes);

        Assert.True(result.TotalTrades >= 2); // at least 1 buy + 1 closing sell
        Assert.True(result.OrderHistory.Any(o => o.Side == OrderSide.Buy));
    }

    [Fact]
    public void Engine_RespectsMaxPositionSize()
    {
        var portfolio = new Portfolio(10_000m);
        var strategy = new AlwaysBuyOnceStrategy();
        var engine = new TradingEngine(portfolio, strategy, maxPositionPercent: 0.10m);

        var prices = GenerateUptrend(50, 100m);
        var quotes = new List<StockQuote> { new("TEST", prices) };

        engine.RunBacktest(quotes);

        // First buy should use at most 10% of portfolio
        var firstBuy = portfolio.OrderHistory.First(o => o.Side == OrderSide.Buy);
        Assert.True(firstBuy.TotalValue <= 1_000m + 100m); // 10% of 10k + 1 share tolerance
    }

    [Fact]
    public void Engine_TracksEquityCurve()
    {
        var portfolio = new Portfolio(10_000m);
        var strategy = new NeverTradeStrategy();
        var engine = new TradingEngine(portfolio, strategy);

        var prices = GenerateUptrend(30);
        var quotes = new List<StockQuote> { new("TEST", prices) };

        var result = engine.RunBacktest(quotes);

        Assert.Equal(30, result.EquityCurve.Count);
        Assert.All(result.EquityCurve, v => Assert.Equal(10_000m, v)); // no trades = flat equity
    }

    [Fact]
    public void Engine_ClosesPositionsAtEndOfBacktest()
    {
        var portfolio = new Portfolio(10_000m);
        var strategy = new AlwaysBuyOnceStrategy();
        var engine = new TradingEngine(portfolio, strategy);

        var prices = GenerateUptrend(50);
        var quotes = new List<StockQuote> { new("TEST", prices) };

        engine.RunBacktest(quotes);

        Assert.Empty(portfolio.Positions); // all positions closed
        Assert.True(portfolio.Cash > 0); // cash returned
    }

    [Fact]
    public void Engine_CommissionReducesProfits()
    {
        var pricesData = GenerateUptrend(50);
        var quotes = new List<StockQuote> { new("TEST", pricesData) };

        // Run without commission
        var portfolio1 = new Portfolio(10_000m);
        var engine1 = new TradingEngine(portfolio1, new AlwaysBuyOnceStrategy(), commissionPerTrade: 0m);
        var result1 = engine1.RunBacktest(quotes);

        // Run with commission
        var portfolio2 = new Portfolio(10_000m);
        var engine2 = new TradingEngine(portfolio2, new AlwaysBuyOnceStrategy(), commissionPerTrade: 10m);
        var result2 = engine2.RunBacktest(quotes);

        Assert.True(result1.FinalValue >= result2.FinalValue,
            "Commission should reduce final value");
    }

    // Test strategies
    private class AlwaysBuyOnceStrategy : ITradingStrategy
    {
        public string Name => "AlwaysBuyOnce";
        private bool _bought;

        public List<TradingSignal> Evaluate(StockQuote quote, Portfolio portfolio)
        {
            if (!_bought && quote.Prices.Count > 10)
            {
                _bought = true;
                var price = quote.Prices.Last();
                return new List<TradingSignal>
                {
                    new(quote.Symbol, SignalType.Buy, price.Close, price.Date, "Test buy")
                };
            }
            return new List<TradingSignal>();
        }
    }

    private class NeverTradeStrategy : ITradingStrategy
    {
        public string Name => "NeverTrade";
        public List<TradingSignal> Evaluate(StockQuote quote, Portfolio portfolio) => new();
    }
}
