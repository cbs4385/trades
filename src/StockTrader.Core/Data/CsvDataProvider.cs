using StockTrader.Core.Models;

namespace StockTrader.Core.Data;

/// Loads market data from CSV files for offline backtesting.
/// Expected CSV format: Date,Open,High,Low,Close,Volume (with header row).
public class CsvDataProvider : IMarketDataProvider
{
    private readonly string _dataDirectory;

    public CsvDataProvider(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public Task<StockQuote> GetHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        var filePath = Path.Combine(_dataDirectory, $"{symbol}.csv");
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"No CSV data file found for {symbol} at {filePath}");

        var lines = File.ReadAllLines(filePath).Skip(1); // skip header
        var prices = new List<StockPrice>();

        foreach (var line in lines)
        {
            var parts = line.Split(',');
            if (parts.Length < 6) continue;

            var date = DateTime.Parse(parts[0]);
            if (date < startDate || date > endDate) continue;

            prices.Add(new StockPrice(
                Date: date,
                Open: decimal.Parse(parts[1]),
                High: decimal.Parse(parts[2]),
                Low: decimal.Parse(parts[3]),
                Close: decimal.Parse(parts[4]),
                Volume: long.Parse(parts[5])
            ));
        }

        return Task.FromResult(new StockQuote(symbol, prices.OrderBy(p => p.Date).ToList()));
    }

    public async Task<List<StockQuote>> GetHistoricalDataAsync(
        IEnumerable<string> symbols, DateTime startDate, DateTime endDate)
    {
        var results = new List<StockQuote>();
        foreach (var symbol in symbols)
            results.Add(await GetHistoricalDataAsync(symbol, startDate, endDate));
        return results;
    }
}
