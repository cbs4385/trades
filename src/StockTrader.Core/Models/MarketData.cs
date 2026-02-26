namespace StockTrader.Core.Models;

public record StockPrice(
    DateTime Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume)
{
    /// Typical price used by many indicators.
    public decimal TypicalPrice => (High + Low + Close) / 3m;
}

public record StockQuote(
    string Symbol,
    List<StockPrice> Prices)
{
    public StockPrice? LatestPrice => Prices.Count > 0 ? Prices[^1] : null;
}
