namespace StockTrader.Core.Models;

public enum SignalType { Buy, Sell, Hold }

public record TradingSignal(
    string Symbol,
    SignalType Type,
    decimal Price,
    DateTime Timestamp,
    string Reason,
    decimal Score = 0m);
