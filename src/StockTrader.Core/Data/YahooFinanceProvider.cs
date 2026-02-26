using StockTrader.Core.Models;
using YahooFinanceApi;

namespace StockTrader.Core.Data;

public class YahooFinanceProvider : IMarketDataProvider
{
    public async Task<StockQuote> GetHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        var history = await Yahoo.GetHistoricalAsync(symbol, startDate, endDate, Period.Daily);

        var prices = history.Select(candle => new StockPrice(
            Date: candle.DateTime,
            Open: (decimal)candle.Open,
            High: (decimal)candle.High,
            Low: (decimal)candle.Low,
            Close: (decimal)candle.Close,
            Volume: (long)candle.Volume
        )).OrderBy(p => p.Date).ToList();

        return new StockQuote(symbol, prices);
    }

    public async Task<List<StockQuote>> GetHistoricalDataAsync(
        IEnumerable<string> symbols, DateTime startDate, DateTime endDate)
    {
        var tasks = symbols.Select(s => GetHistoricalDataAsync(s, startDate, endDate));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }
}
