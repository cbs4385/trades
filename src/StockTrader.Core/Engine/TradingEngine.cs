using StockTrader.Core.Models;

namespace StockTrader.Core.Engine;

public class TradingEngine
{
    private readonly Portfolio _portfolio;
    private readonly ITradingStrategy _strategy;
    private readonly decimal _commissionPerTrade;
    private readonly decimal _maxPositionPercent;

    public List<TradingSignal> SignalHistory { get; } = new();
    public List<decimal> EquityCurve { get; } = new();

    public TradingEngine(
        Portfolio portfolio,
        ITradingStrategy strategy,
        decimal commissionPerTrade = 0m,
        decimal maxPositionPercent = 0.25m)
    {
        _portfolio = portfolio;
        _strategy = strategy;
        _commissionPerTrade = commissionPerTrade;
        _maxPositionPercent = maxPositionPercent;
    }

    public SimulationResult RunBacktest(List<StockQuote> quotes)
    {
        if (quotes.Count == 0)
            throw new ArgumentException("No market data provided for backtest.");

        // Get all unique dates across all symbols, sorted
        var allDates = quotes
            .SelectMany(q => q.Prices.Select(p => p.Date))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        foreach (var date in allDates)
        {
            // Update current prices for all positions
            foreach (var quote in quotes)
            {
                var price = quote.Prices.FirstOrDefault(p => p.Date == date);
                if (price != null && _portfolio.Positions.TryGetValue(quote.Symbol, out var pos))
                    pos.CurrentPrice = price.Close;
            }

            // Evaluate strategy for each symbol
            foreach (var quote in quotes)
            {
                var pricesUpToDate = new StockQuote(
                    quote.Symbol,
                    quote.Prices.Where(p => p.Date <= date).ToList());

                if (pricesUpToDate.Prices.Count == 0) continue;

                var signals = _strategy.Evaluate(pricesUpToDate, _portfolio);
                foreach (var signal in signals)
                {
                    SignalHistory.Add(signal);
                    ExecuteSignal(signal);
                }
            }

            EquityCurve.Add(_portfolio.TotalValue);
        }

        // Close all remaining positions at last known prices
        CloseAllPositions(allDates.Last());

        return BuildResult(allDates);
    }

    private void ExecuteSignal(TradingSignal signal)
    {
        switch (signal.Type)
        {
            case SignalType.Buy:
                ExecuteBuy(signal);
                break;
            case SignalType.Sell:
                ExecuteSell(signal);
                break;
            case SignalType.Hold:
                break;
        }
    }

    private void ExecuteBuy(TradingSignal signal)
    {
        // Determine position size: use at most maxPositionPercent of portfolio value
        var maxAllocation = _portfolio.TotalValue * _maxPositionPercent;
        var availableCash = Math.Min(_portfolio.Cash - _commissionPerTrade, maxAllocation);

        if (availableCash <= 0) return;

        var quantity = (int)(availableCash / signal.Price);
        if (quantity <= 0) return;

        var totalCost = quantity * signal.Price + _commissionPerTrade;
        if (totalCost > _portfolio.Cash) return;

        _portfolio.Cash -= totalCost;

        if (_portfolio.Positions.TryGetValue(signal.Symbol, out var position))
        {
            var totalShares = position.Quantity + quantity;
            position.AverageCost = (position.CostBasis + quantity * signal.Price) / totalShares;
            position.Quantity = totalShares;
            position.CurrentPrice = signal.Price;
        }
        else
        {
            _portfolio.Positions[signal.Symbol] = new Position
            {
                Symbol = signal.Symbol,
                Quantity = quantity,
                AverageCost = signal.Price,
                CurrentPrice = signal.Price
            };
        }

        _portfolio.OrderHistory.Add(new Order(
            signal.Symbol, OrderSide.Buy, quantity, signal.Price,
            signal.Timestamp, OrderStatus.Filled));
    }

    private void ExecuteSell(TradingSignal signal)
    {
        if (!_portfolio.Positions.TryGetValue(signal.Symbol, out var position))
            return;

        if (position.Quantity <= 0) return;

        var quantity = position.Quantity;
        var proceeds = quantity * signal.Price - _commissionPerTrade;

        _portfolio.Cash += proceeds;
        _portfolio.Positions.Remove(signal.Symbol);

        _portfolio.OrderHistory.Add(new Order(
            signal.Symbol, OrderSide.Sell, quantity, signal.Price,
            signal.Timestamp, OrderStatus.Filled));
    }

    private void CloseAllPositions(DateTime date)
    {
        var symbolsToClose = _portfolio.Positions.Keys.ToList();
        foreach (var symbol in symbolsToClose)
        {
            var position = _portfolio.Positions[symbol];
            ExecuteSell(new TradingSignal(
                symbol, SignalType.Sell, position.CurrentPrice,
                date, "End of backtest - closing position"));
        }
    }

    private SimulationResult BuildResult(List<DateTime> dates)
    {
        var maxDrawdown = CalculateMaxDrawdown();

        return new SimulationResult
        {
            StrategyName = _strategy.Name,
            StartDate = dates.First(),
            EndDate = dates.Last(),
            InitialCapital = _portfolio.InitialCash,
            FinalValue = _portfolio.TotalValue,
            TotalReturn = _portfolio.TotalReturnPercent,
            TotalTrades = _portfolio.TotalTrades,
            WinRate = _portfolio.WinRate,
            MaxDrawdown = maxDrawdown,
            EquityCurve = EquityCurve.ToList(),
            OrderHistory = _portfolio.OrderHistory.ToList(),
            SignalHistory = SignalHistory.ToList()
        };
    }

    private decimal CalculateMaxDrawdown()
    {
        if (EquityCurve.Count == 0) return 0;

        decimal peak = EquityCurve[0];
        decimal maxDrawdown = 0;

        foreach (var value in EquityCurve)
        {
            if (value > peak) peak = value;
            var drawdown = (peak - value) / peak * 100m;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        }

        return maxDrawdown;
    }
}
