namespace StockTrader.Core.Models;

public enum OrderSide { Buy, Sell }
public enum OrderStatus { Pending, Filled, Cancelled }

public record Order(
    string Symbol,
    OrderSide Side,
    int Quantity,
    decimal Price,
    DateTime Timestamp,
    OrderStatus Status = OrderStatus.Pending)
{
    public decimal TotalValue => Price * Quantity;
}
