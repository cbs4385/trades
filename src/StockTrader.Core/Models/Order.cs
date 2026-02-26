namespace StockTrader.Core.Models;

public enum OrderSide { Buy, Sell }
public enum OrderStatus { Pending, Filled, Cancelled }

public record Order(
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    DateTime Timestamp,
    OrderStatus Status = OrderStatus.Pending)
{
    public decimal TotalValue => Price * Quantity;
}
