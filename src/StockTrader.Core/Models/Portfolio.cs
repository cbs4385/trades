namespace StockTrader.Core.Models;

public class Position
{
    public string Symbol { get; init; } = string.Empty;
    public int Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public decimal CurrentPrice { get; set; }

    public decimal MarketValue => Quantity * CurrentPrice;
    public decimal CostBasis => Quantity * AverageCost;
    public decimal UnrealizedPnL => MarketValue - CostBasis;
    public decimal UnrealizedPnLPercent => CostBasis != 0 ? (UnrealizedPnL / CostBasis) * 100m : 0m;
}

public class Portfolio
{
    public decimal InitialCash { get; }
    public decimal Cash { get; set; }
    public Dictionary<string, Position> Positions { get; } = new();
    public List<Order> OrderHistory { get; } = new();

    public Portfolio(decimal initialCash)
    {
        InitialCash = initialCash;
        Cash = initialCash;
    }

    public decimal TotalMarketValue => Positions.Values.Sum(p => p.MarketValue);
    public decimal TotalValue => Cash + TotalMarketValue;
    public decimal TotalPnL => TotalValue - InitialCash;
    public decimal TotalReturnPercent => (TotalPnL / InitialCash) * 100m;

    public int TotalTrades => OrderHistory.Count(o => o.Status == OrderStatus.Filled);
    public int WinningTrades => OrderHistory
        .Where(o => o is { Status: OrderStatus.Filled, Side: OrderSide.Sell })
        .Count(SellWasProfitable);

    public decimal WinRate => TotalTrades > 0
        ? (decimal)WinningTrades / OrderHistory.Count(o => o is { Status: OrderStatus.Filled, Side: OrderSide.Sell }) * 100m
        : 0m;

    private bool SellWasProfitable(Order sellOrder)
    {
        // Find the matching buy orders to calculate if this sell was profitable
        var buys = OrderHistory
            .Where(o => o is { Status: OrderStatus.Filled, Side: OrderSide.Buy }
                        && o.Symbol == sellOrder.Symbol
                        && o.Timestamp < sellOrder.Timestamp)
            .ToList();

        if (buys.Count == 0) return false;

        var avgBuyPrice = buys.Sum(b => b.TotalValue) / buys.Sum(b => b.Quantity);
        return sellOrder.Price > avgBuyPrice;
    }
}
