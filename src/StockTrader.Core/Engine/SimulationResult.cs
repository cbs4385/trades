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
        // Pad or truncate to fit within box borders: "║  {content}  ║"
        var inner = BoxWidth - 4; // 2 for "║ " on each side
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
        Console.WriteLine($"{"Date",-12} {"Action",-6} {"Symbol",-8} {"Qty",6} {"Price",10} {"Total",12} {"P&L",10} {"Reason"}");
        Console.WriteLine("──────────────────────────────────────────────────────────────────────────────────");

        // Match sells to their most recent buy to show per-trade P&L
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
                var pnlPct = (order.Price - buyPrice) / buyPrice * 100m;
                pnlStr = $"{pnl,+10:F0}";
                // Color indicator
                var indicator = pnl >= 0 ? "+" : "";
                pnlStr = $"{indicator}{pnl:F0}";
                pnlStr = $"{pnlStr,10}";

                // Find the sell reason from signal history
                var signal = SignalHistory
                    .FirstOrDefault(s => s.Symbol == order.Symbol
                                        && s.Timestamp == order.Timestamp
                                        && s.Type == SignalType.Sell);
                if (signal != null)
                {
                    // Shorten the reason for display
                    reason = signal.Reason;
                    if (reason.Length > 35) reason = reason[..35] + "...";
                }
            }

            Console.WriteLine(
                $"{order.Timestamp:yyyy-MM-dd}  {order.Side,-6} {order.Symbol,-8} {order.Quantity,6} {order.Price,10:F2} {order.TotalValue,12:F2} {pnlStr,10} {reason}");
        }
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
            Console.WriteLine($"  {value,10:F0} |{bar}");
        }
    }
}
