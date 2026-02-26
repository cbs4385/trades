using StockTrader.Core.Models;

namespace StockTrader.Core.Data;

public interface IMarketDataProvider
{
    Task<StockQuote> GetHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate);
    Task<List<StockQuote>> GetHistoricalDataAsync(IEnumerable<string> symbols, DateTime startDate, DateTime endDate);
}
