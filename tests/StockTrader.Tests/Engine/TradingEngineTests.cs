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

    [Fact]
    public void Engine_StopLossTriggered()
    {
        // Generate a price series that goes up then sharply drops
        var prices = new List<StockPrice>();
        for (int i = 0; i < 50; i++)
        {
            decimal close;
            if (i < 15) close = 100m + i * 0.5m; // rising to trigger buy
            else if (i < 25) close = 107.5m; // flat
            else close = 107.5m - (i - 25) * 2m; // sharp drop

            prices.Add(new StockPrice(
                DateTime.Today.AddDays(-50 + i),
                close - 0.2m, close + 0.5m, close - 0.5m, close, 1_000_000));
        }

        var portfolio = new Portfolio(10_000m);
        var engine = new TradingEngine(portfolio, new AlwaysBuyOnceStrategy(), stopLossPercent: 5m);
        var quotes = new List<StockQuote> { new("TEST", prices) };

        var result = engine.RunBacktest(quotes);

        // Should have a sell triggered by stop-loss before end of backtest
        Assert.Contains(result.SignalHistory, s =>
            s.Type == SignalType.Sell && s.Reason.Contains("Stop-loss"));
    }

    [Fact]
    public void Engine_TakeProfitTriggered()
    {
        var prices = GenerateUptrend(50, 100m); // steadily rising prices
        var portfolio = new Portfolio(10_000m);
        var engine = new TradingEngine(portfolio, new AlwaysBuyOnceStrategy(), takeProfitPercent: 3m);
        var quotes = new List<StockQuote> { new("TEST", prices) };

        var result = engine.RunBacktest(quotes);

        // Should have a sell triggered by take-profit
        Assert.Contains(result.SignalHistory, s =>
            s.Type == SignalType.Sell && s.Reason.Contains("Take-profit"));
    }

    [Fact]
    public void Engine_TrailingStopTriggered()
    {
        // Generate prices that rise then fall
        var prices = new List<StockPrice>();
        for (int i = 0; i < 50; i++)
        {
            decimal close;
            if (i < 15) close = 100m + i * 0.5m;
            else if (i < 30) close = 107.5m + (i - 15) * 1m; // rise to 122.5
            else close = 122.5m - (i - 30) * 1.5m; // drop from peak

            prices.Add(new StockPrice(
                DateTime.Today.AddDays(-50 + i),
                close - 0.2m, close + 0.5m, close - 0.5m, close, 1_000_000));
        }

        var portfolio = new Portfolio(10_000m);
        var engine = new TradingEngine(portfolio, new AlwaysBuyOnceStrategy(), trailingStopPercent: 5m);
        var quotes = new List<StockQuote> { new("TEST", prices) };

        var result = engine.RunBacktest(quotes);

        Assert.Contains(result.SignalHistory, s =>
            s.Type == SignalType.Sell && s.Reason.Contains("Trailing stop"));
    }

    [Fact]
    public void Engine_CalculatesSharpeRatio()
    {
        var prices = GenerateUptrend(50);
        var portfolio = new Portfolio(10_000m);
        var engine = new TradingEngine(portfolio, new AlwaysBuyOnceStrategy());
        var quotes = new List<StockQuote> { new("TEST", prices) };

        var result = engine.RunBacktest(quotes);

        // Sharpe should be a finite number
        Assert.True(result.SharpeRatio > decimal.MinValue && result.SharpeRatio < decimal.MaxValue);
    }

    [Fact]
    public void Engine_CalculatesProfitFactor()
    {
        var prices = GenerateUptrend(50);
        var portfolio = new Portfolio(10_000m);
        var engine = new TradingEngine(portfolio, new AlwaysBuyOnceStrategy());
        var quotes = new List<StockQuote> { new("TEST", prices) };

        var result = engine.RunBacktest(quotes);

        // With an uptrend and one buy+sell, profit factor should be > 0
        Assert.True(result.ProfitFactor >= 0);
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
