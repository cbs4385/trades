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

    public void PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║             BACKTEST SIMULATION RESULTS                 ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Strategy:          {StrategyName,-36}  ║");
        Console.WriteLine($"║  Period:            {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}          ║");
        Console.WriteLine($"║  Initial Capital:   {InitialCapital,14:C}                    ║");
        Console.WriteLine($"║  Final Value:       {FinalValue,14:C}                    ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  RETURNS                                               ║");
        Console.WriteLine($"║    Total Return:      {TotalReturn,10:F2}%                      ║");
        Console.WriteLine($"║    Annualized Return: {AnnualizedReturn,10:F2}%                      ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  RISK                                                  ║");
        Console.WriteLine($"║    Max Drawdown:      {MaxDrawdown,10:F2}%                      ║");
        Console.WriteLine($"║    Sharpe Ratio:      {SharpeRatio,10:F2}                       ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  TRADES                                                ║");
        Console.WriteLine($"║    Total Trades:      {TotalTrades,10}                       ║");
        Console.WriteLine($"║    Win Rate:          {WinRate,10:F1}%                      ║");
        Console.WriteLine($"║    Profit Factor:     {ProfitFactor,10:F2}                       ║");
        Console.WriteLine($"║    Avg Win:           {AverageWin,10:C}                    ║");
        Console.WriteLine($"║    Avg Loss:          {AverageLoss,10:C}                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
    }

    public void PrintTradeLog()
    {
        Console.WriteLine();
        Console.WriteLine("Trade History:");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");
        Console.WriteLine($"{"Date",-12} {"Action",-6} {"Symbol",-8} {"Qty",6} {"Price",10} {"Total",12}");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");

        foreach (var order in OrderHistory.Where(o => o.Status == OrderStatus.Filled))
        {
            Console.WriteLine(
                $"{order.Timestamp:yyyy-MM-dd}  {order.Side,-6} {order.Symbol,-8} {order.Quantity,6} {order.Price,10:F2} {order.TotalValue,12:F2}");
        }
    }

    public void PrintEquityCurve(int width = 50)
    {
        if (EquityCurve.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("Equity Curve:");
        Console.WriteLine("─────────────────────────────────────────────────────────────────");

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
