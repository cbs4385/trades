using StockTrader.Core.Models;

namespace StockTrader.Core.Engine;

public class TradingEngine
{
    private readonly Portfolio _portfolio;
    private readonly ITradingStrategy _strategy;
    private readonly decimal _commissionPerTrade;
    private readonly decimal _maxPositionPercent;
    private readonly decimal _stopLossPercent;
    private readonly decimal _takeProfitPercent;
    private readonly decimal _trailingStopPercent;

    // Track highest price since entry for trailing stops
    private readonly Dictionary<string, decimal> _highWaterMark = new();

    public List<TradingSignal> SignalHistory { get; } = new();
    public List<decimal> EquityCurve { get; } = new();

    public TradingEngine(
        Portfolio portfolio,
        ITradingStrategy strategy,
        decimal commissionPerTrade = 0m,
        decimal maxPositionPercent = 0.25m,
        decimal stopLossPercent = 0m,
        decimal takeProfitPercent = 0m,
        decimal trailingStopPercent = 0m)
    {
        _portfolio = portfolio;
        _strategy = strategy;
        _commissionPerTrade = commissionPerTrade;
        _maxPositionPercent = maxPositionPercent;
        _stopLossPercent = stopLossPercent;
        _takeProfitPercent = takeProfitPercent;
        _trailingStopPercent = trailingStopPercent;
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
            // Build today's price lookup for intraday stop simulation
            var todayPrices = new Dictionary<string, StockPrice>();

            // Update current prices and trailing stop high-water marks
            foreach (var quote in quotes)
            {
                var price = quote.Prices.FirstOrDefault(p => p.Date == date);
                if (price != null)
                {
                    todayPrices[quote.Symbol] = price;

                    if (_portfolio.Positions.TryGetValue(quote.Symbol, out var pos))
                    {
                        pos.CurrentPrice = price.Close;

                        // Track high-water mark using daily high for trailing stops
                        if (_trailingStopPercent > 0)
                        {
                            if (!_highWaterMark.TryGetValue(quote.Symbol, out var hwm) || price.High > hwm)
                                _highWaterMark[quote.Symbol] = price.High;
                        }
                    }
                }
            }

            // Check stop-loss, take-profit, and trailing stop before strategy evaluation
            // Pass today's price data so stops can use intraday low/high
            CheckRiskManagement(date, todayPrices);

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

    private void CheckRiskManagement(DateTime date, Dictionary<string, StockPrice> todayPrices)
    {
        // (symbol, reason, executionPrice) - execution price may differ from close
        // when a stop is triggered intraday at the exact stop level
        var symbolsToSell = new List<(string symbol, string reason, decimal execPrice)>();

        foreach (var (symbol, position) in _portfolio.Positions)
        {
            todayPrices.TryGetValue(symbol, out var todayPrice);

            // Stop-loss: check if today's LOW breached the stop level
            if (_stopLossPercent > 0)
            {
                var stopPrice = position.AverageCost * (1m - _stopLossPercent / 100m);
                var low = todayPrice?.Low ?? position.CurrentPrice;

                if (low <= stopPrice)
                {
                    // Execute at the stop price (simulating a stop order fill)
                    var pnlPercent = (stopPrice - position.AverageCost) / position.AverageCost * 100m;
                    symbolsToSell.Add((symbol,
                        $"Stop-loss triggered ({pnlPercent:F1}% loss)",
                        stopPrice));
                    continue;
                }
            }

            // Take-profit: check if today's HIGH reached the target
            if (_takeProfitPercent > 0)
            {
                var targetPrice = position.AverageCost * (1m + _takeProfitPercent / 100m);
                var high = todayPrice?.High ?? position.CurrentPrice;

                if (high >= targetPrice)
                {
                    var pnlPercent = (targetPrice - position.AverageCost) / position.AverageCost * 100m;
                    symbolsToSell.Add((symbol,
                        $"Take-profit triggered ({pnlPercent:F1}% gain)",
                        targetPrice));
                    continue;
                }
            }

            // Trailing stop: check if today's LOW dropped enough from the high-water mark
            if (_trailingStopPercent > 0 && _highWaterMark.TryGetValue(symbol, out var hwm) && hwm > 0)
            {
                var trailStopPrice = hwm * (1m - _trailingStopPercent / 100m);
                var low = todayPrice?.Low ?? position.CurrentPrice;

                if (low <= trailStopPrice)
                {
                    var dropFromPeak = _trailingStopPercent; // executed at exact stop level
                    symbolsToSell.Add((symbol,
                        $"Trailing stop triggered ({dropFromPeak:F1}% drop from peak ${hwm:F2})",
                        trailStopPrice));
                }
            }
        }

        foreach (var (symbol, reason, execPrice) in symbolsToSell)
        {
            var signal = new TradingSignal(symbol, SignalType.Sell, execPrice, date, reason);
            SignalHistory.Add(signal);
            ExecuteSell(signal);
            _highWaterMark.Remove(symbol);
        }
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

        // Initialize high-water mark for trailing stops
        if (_trailingStopPercent > 0)
            _highWaterMark[signal.Symbol] = signal.Price;

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
        _highWaterMark.Remove(signal.Symbol);

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
        var (sharpeRatio, profitFactor, avgWin, avgLoss) = CalculateAdvancedMetrics();

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
            SharpeRatio = sharpeRatio,
            ProfitFactor = profitFactor,
            AverageWin = avgWin,
            AverageLoss = avgLoss,
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

    private (decimal sharpe, decimal profitFactor, decimal avgWin, decimal avgLoss) CalculateAdvancedMetrics()
    {
        // Calculate per-trade P&L for each round trip (buy+sell pair)
        var tradePnLs = new List<decimal>();
        var sells = _portfolio.OrderHistory
            .Where(o => o is { Status: OrderStatus.Filled, Side: OrderSide.Sell })
            .ToList();

        foreach (var sell in sells)
        {
            // Find the most recent buy for this symbol before this sell
            var matchingBuy = _portfolio.OrderHistory
                .Where(o => o is { Status: OrderStatus.Filled, Side: OrderSide.Buy }
                            && o.Symbol == sell.Symbol
                            && o.Timestamp <= sell.Timestamp)
                .OrderByDescending(o => o.Timestamp)
                .FirstOrDefault();

            if (matchingBuy != null)
            {
                var pnl = (sell.Price - matchingBuy.Price) * sell.Quantity;
                tradePnLs.Add(pnl);
            }
        }

        if (tradePnLs.Count == 0)
            return (0, 0, 0, 0);

        var wins = tradePnLs.Where(p => p > 0).ToList();
        var losses = tradePnLs.Where(p => p < 0).ToList();

        var avgWin = wins.Count > 0 ? wins.Average() : 0;
        var avgLoss = losses.Count > 0 ? losses.Average() : 0;

        // Profit factor = gross wins / gross losses
        var grossWins = wins.Sum();
        var grossLosses = Math.Abs(losses.Sum());
        var profitFactor = grossLosses > 0 ? grossWins / grossLosses : grossWins > 0 ? decimal.MaxValue : 0;

        // Sharpe ratio (annualized, using daily equity curve returns)
        decimal sharpe = 0;
        if (EquityCurve.Count > 1)
        {
            var dailyReturns = new List<double>();
            for (int i = 1; i < EquityCurve.Count; i++)
            {
                if (EquityCurve[i - 1] != 0)
                    dailyReturns.Add((double)(EquityCurve[i] / EquityCurve[i - 1] - 1m));
            }

            if (dailyReturns.Count > 1)
            {
                var avgReturn = dailyReturns.Average();
                var stdDev = Math.Sqrt(dailyReturns.Sum(r => (r - avgReturn) * (r - avgReturn)) / (dailyReturns.Count - 1));

                if (stdDev > 0)
                    sharpe = (decimal)(avgReturn / stdDev * Math.Sqrt(252)); // annualized
            }
        }

        return (sharpe, profitFactor, avgWin, avgLoss);
    }
}
