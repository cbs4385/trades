using StockTrader.Core.Models;

namespace StockTrader.Core.Engine;

public class SimulationResult
{
    public string StrategyName { get; init; } = string.Empty;
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public decimal InitialCapital { get; init; }
    public decimal FinalValue { get; init; }
    public decimal TotalReturn { get; init; }
    public int TotalTrades { get; init; }
    public decimal WinRate { get; init; }
    public decimal MaxDrawdown { get; init; }
    public decimal SharpeRatio { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public List<decimal> EquityCurve { get; init; } = new();
    public List<Order> OrderHistory { get; init; } = new();
    public List<TradingSignal> SignalHistory { get; init; } = new();
    public List<(DateTime Date, decimal PnL, string Symbol)> DailyTrades { get; init; } = new();
    public bool IntradayMode { get; init; }

    public decimal AnnualizedReturn
    {
        get
        {
            var years = (decimal)(EndDate - StartDate).TotalDays / 365.25m;
            if (years <= 0) return 0;
            var totalReturnFraction = TotalReturn / 100m;
            return ((decimal)Math.Pow((double)(1m + totalReturnFraction), (double)(1m / years)) - 1m) * 100m;
        }
    }

    private const int BoxWidth = 60;
    private static string BoxLine(string content)
    {
        var inner = BoxWidth - 4;
        if (content.Length > inner)
            content = content[..inner];
        return $"║ {content.PadRight(inner)} ║";
    }
    private static string BoxTop => $"╔{new string('═', BoxWidth - 2)}╗";
    private static string BoxMid => $"╠{new string('═', BoxWidth - 2)}╣";
    private static string BoxBot => $"╚{new string('═', BoxWidth - 2)}╝";

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine(BoxTop);
        Console.WriteLine(BoxLine("          BACKTEST SIMULATION RESULTS"));
        Console.WriteLine(BoxMid);
        Console.WriteLine(BoxLine($"Strategy:     {StrategyName}"));
        Console.WriteLine(BoxLine($"Period:       {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}"));
        Console.WriteLine(BoxLine($"Initial:      {InitialCapital,14:C}"));
        Console.WriteLine(BoxLine($"Final:        {FinalValue,14:C}"));
        Console.WriteLine(BoxMid);
        Console.WriteLine(BoxLine("RETURNS"));
        Console.WriteLine(BoxLine($"  Total Return:      {TotalReturn,10:F2}%"));
        Console.WriteLine(BoxLine($"  Annualized Return: {AnnualizedReturn,10:F2}%"));
        Console.WriteLine(BoxMid);
        Console.WriteLine(BoxLine("RISK"));
        Console.WriteLine(BoxLine($"  Max Drawdown:      {MaxDrawdown,10:F2}%"));
        Console.WriteLine(BoxLine($"  Sharpe Ratio:      {SharpeRatio,10:F2}"));
        Console.WriteLine(BoxMid);
        Console.WriteLine(BoxLine("TRADES"));
        Console.WriteLine(BoxLine($"  Total Trades:      {TotalTrades,10}"));
        Console.WriteLine(BoxLine($"  Win Rate:          {WinRate,10:F1}%"));
        Console.WriteLine(BoxLine($"  Profit Factor:     {ProfitFactor,10:F2}"));
        Console.WriteLine(BoxLine($"  Avg Win:           {AverageWin,10:C}"));
        Console.WriteLine(BoxLine($"  Avg Loss:          {AverageLoss,10:C}"));
        Console.WriteLine(BoxBot);
    }

    public void PrintTradeLog()
    {
        Console.WriteLine();
        Console.WriteLine("Trade History:");
        Console.WriteLine("──────────────────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"{"Date",-12} {"Action",-6} {"Symbol",-8} {"Qty",9} {"Price",10} {"Total",12} {"P&L",10} {"Reason"}");
        Console.WriteLine("──────────────────────────────────────────────────────────────────────────────────");

        var lastBuyPrice = new Dictionary<string, decimal>();

        foreach (var order in OrderHistory.Where(o => o.Status == OrderStatus.Filled))
        {
            string pnlStr = "";
            string reason = "";

            if (order.Side == OrderSide.Buy)
            {
                lastBuyPrice[order.Symbol] = order.Price;
            }
            else if (order.Side == OrderSide.Sell && lastBuyPrice.TryGetValue(order.Symbol, out var buyPrice))
            {
                var pnl = (order.Price - buyPrice) * order.Quantity;
                var indicator = pnl >= 0 ? "+" : "";
                pnlStr = $"{indicator}{pnl:F2}";
                pnlStr = $"{pnlStr,10}";

                var signal = SignalHistory
                    .FirstOrDefault(s => s.Symbol == order.Symbol
                                        && s.Timestamp == order.Timestamp
                                        && s.Type == SignalType.Sell);
                if (signal != null)
                {
                    reason = signal.Reason;
                    if (reason.Length > 35) reason = reason[..35] + "...";
                }
            }

            // Show integer quantities without decimals, fractional with 2 decimals
            var qtyStr = order.Quantity == Math.Floor(order.Quantity)
                ? $"{order.Quantity,9:F0}"
                : $"{order.Quantity,9:F4}";
            Console.WriteLine(
                $"{order.Timestamp:yyyy-MM-dd}  {order.Side,-6} {order.Symbol,-8} {qtyStr} {order.Price,10:F2} {order.TotalValue,12:F2} {pnlStr,10} {reason}");
        }
    }

    public void PrintPerformanceLog()
    {
        if (DailyTrades.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                    PERFORMANCE LOG");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        // Monthly breakdown
        var monthlyGroups = DailyTrades
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Monthly Returns:");
        Console.WriteLine("─────────────────────────────────────────────────────");
        Console.WriteLine($"  {"Month",-10} {"P&L",10} {"Return",9} {"Trades",7} {"W/L",8} {"Win%",7}");
        Console.WriteLine("─────────────────────────────────────────────────────");

        // Track running equity for monthly return calculation
        var runningEquity = InitialCapital;

        foreach (var month in monthlyGroups)
        {
            var monthPnL = month.Sum(t => t.PnL);
            var monthReturn = runningEquity > 0 ? monthPnL / runningEquity * 100m : 0m;
            var wins = month.Count(t => t.PnL > 0);
            var losses = month.Count(t => t.PnL <= 0);
            var totalTrades = month.Count();
            var winPct = totalTrades > 0 ? (decimal)wins / totalTrades * 100m : 0m;

            var sign = monthPnL >= 0 ? "+" : "";
            Console.WriteLine(
                $"  {month.Key.Year}-{month.Key.Month:D2}    {sign}{monthPnL,9:F2} {sign}{monthReturn,7:F1}%  {totalTrades,5}  {wins}W/{losses}L  {winPct,5:F1}%");

            runningEquity += monthPnL;
        }

        // Yearly breakdown
        var yearlyGroups = DailyTrades
            .GroupBy(t => t.Date.Year)
            .OrderBy(g => g.Key)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Yearly Summary:");
        Console.WriteLine("─────────────────────────────────────────────────────");

        var yearRunning = InitialCapital;
        foreach (var year in yearlyGroups)
        {
            var yearPnL = year.Sum(t => t.PnL);
            var yearReturn = yearRunning > 0 ? yearPnL / yearRunning * 100m : 0m;
            var wins = year.Count(t => t.PnL > 0);
            var losses = year.Count(t => t.PnL <= 0);
            var sign = yearPnL >= 0 ? "+" : "";
            Console.WriteLine(
                $"  {year.Key}       {sign}{yearPnL,9:F2} {sign}{yearReturn,7:F1}%   [{wins}W / {losses}L]");
            yearRunning += yearPnL;
        }

        // Statistics
        Console.WriteLine();
        Console.WriteLine("Statistics:");
        Console.WriteLine("─────────────────────────────────────────────────────");

        var bestTrade = DailyTrades.MaxBy(t => t.PnL);
        var worstTrade = DailyTrades.MinBy(t => t.PnL);

        if (bestTrade.Symbol != null)
            Console.WriteLine($"  Best Day:       +${bestTrade.PnL:F2} ({bestTrade.Date:yyyy-MM-dd}, {bestTrade.Symbol})");
        if (worstTrade.Symbol != null)
            Console.WriteLine($"  Worst Day:      -${Math.Abs(worstTrade.PnL):F2} ({worstTrade.Date:yyyy-MM-dd}, {worstTrade.Symbol})");

        // Streaks
        int maxWinStreak = 0, maxLossStreak = 0;
        int currentWinStreak = 0, currentLossStreak = 0;
        foreach (var trade in DailyTrades)
        {
            if (trade.PnL > 0)
            {
                currentWinStreak++;
                currentLossStreak = 0;
                maxWinStreak = Math.Max(maxWinStreak, currentWinStreak);
            }
            else
            {
                currentLossStreak++;
                currentWinStreak = 0;
                maxLossStreak = Math.Max(maxLossStreak, currentLossStreak);
            }
        }

        Console.WriteLine($"  Max Win Streak:  {maxWinStreak} days");
        Console.WriteLine($"  Max Loss Streak: {maxLossStreak} days");
        Console.WriteLine($"  Days Traded:     {DailyTrades.Count}");

        var totalDays = (EndDate - StartDate).Days;
        var tradingDays = (int)(totalDays * 252.0 / 365.25);
        var daysSkipped = tradingDays - DailyTrades.Count;
        if (daysSkipped > 0)
            Console.WriteLine($"  Days Skipped:    {daysSkipped} (no signal above threshold)");

        var avgDailyPnL = DailyTrades.Average(t => t.PnL);
        var sign2 = avgDailyPnL >= 0 ? "+" : "";
        Console.WriteLine($"  Avg Daily P&L:   {sign2}${avgDailyPnL:F4}");

        // Per-symbol breakdown
        var symbolGroups = DailyTrades
            .GroupBy(t => t.Symbol)
            .OrderByDescending(g => g.Sum(t => t.PnL))
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Per-Symbol Breakdown:");
        Console.WriteLine("─────────────────────────────────────────────────────");
        Console.WriteLine($"  {"Symbol",-8} {"Total P&L",12} {"Trades",7} {"Win%",7} {"Avg P&L",10}");
        Console.WriteLine("─────────────────────────────────────────────────────");

        foreach (var grp in symbolGroups)
        {
            var pnl = grp.Sum(t => t.PnL);
            var count = grp.Count();
            var w = grp.Count(t => t.PnL > 0);
            var wp = count > 0 ? (decimal)w / count * 100m : 0m;
            var avg = grp.Average(t => t.PnL);
            var s = pnl >= 0 ? "+" : "";
            Console.WriteLine($"  {grp.Key,-8} {s}{pnl,11:F2} {count,5}   {wp,5:F1}%  {s}{avg,8:F4}");
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
    }

    public void PrintEquityCurve(int width = 50)
    {
        if (EquityCurve.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("Equity Curve:");
        Console.WriteLine("──────────────────────────────────────────────────────────────────────────────────");

        var min = EquityCurve.Min();
        var max = EquityCurve.Max();
        var range = max - min;

        if (range == 0) return;

        // Sample points to fit the display width
        var step = Math.Max(1, EquityCurve.Count / 20);

        for (int i = 0; i < EquityCurve.Count; i += step)
        {
            var value = EquityCurve[i];
            var barLength = (int)((value - min) / range * width);
            var bar = new string('█', barLength);
            Console.WriteLine($"  {value,10:F2} |{bar}");
        }
    }
}
