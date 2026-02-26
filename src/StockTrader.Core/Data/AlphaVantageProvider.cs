using System.Text.Json;
using StockTrader.Core.Models;

namespace StockTrader.Core.Data;

/// Fetches historical stock data from Alpha Vantage.
/// Free tier: 25 requests/day. Get a key at https://www.alphavantage.co/support/#api-key
public class AlphaVantageProvider : IMarketDataProvider
{
    private static readonly HttpClient HttpClient = new();
    private const string BaseUrl = "https://www.alphavantage.co/query";
    private readonly string _apiKey;

    public AlphaVantageProvider(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(
                "Alpha Vantage API key is required. Get a free key at https://www.alphavantage.co/support/#api-key");
        _apiKey = apiKey;
    }

    public async Task<StockQuote> GetHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        var url = $"{BaseUrl}?function=TIME_SERIES_DAILY&symbol={symbol}&outputsize=full&apikey={_apiKey}";

        var response = await HttpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Check for error/rate limit messages
        if (root.TryGetProperty("Note", out var note))
            throw new HttpRequestException($"Alpha Vantage rate limit: {note.GetString()}");
        if (root.TryGetProperty("Error Message", out var error))
            throw new InvalidOperationException($"Alpha Vantage error for {symbol}: {error.GetString()}");

        var timeSeries = root.GetProperty("Time Series (Daily)");
        var prices = new List<StockPrice>();

        foreach (var day in timeSeries.EnumerateObject())
        {
            var date = DateTime.Parse(day.Name);
            if (date < startDate || date > endDate) continue;

            prices.Add(new StockPrice(
                Date: date,
                Open: decimal.Parse(day.Value.GetProperty("1. open").GetString()!),
                High: decimal.Parse(day.Value.GetProperty("2. high").GetString()!),
                Low: decimal.Parse(day.Value.GetProperty("3. low").GetString()!),
                Close: decimal.Parse(day.Value.GetProperty("4. close").GetString()!),
                Volume: long.Parse(day.Value.GetProperty("5. volume").GetString()!)
            ));
        }

        return new StockQuote(symbol, prices.OrderBy(p => p.Date).ToList());
    }

    public async Task<List<StockQuote>> GetHistoricalDataAsync(
        IEnumerable<string> symbols, DateTime startDate, DateTime endDate)
    {
        var quotes = new List<StockQuote>();

        foreach (var symbol in symbols)
        {
            quotes.Add(await GetHistoricalDataAsync(symbol, startDate, endDate));
            await Task.Delay(12_500); // Alpha Vantage free tier: 5 calls/min
        }

        return quotes;
    }
}
