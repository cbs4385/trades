using System.Text.Json;
using StockTrader.Core.Models;

namespace StockTrader.Core.Data;

public class YahooFinanceProvider : IMarketDataProvider
{
    private static readonly HttpClient HttpClient;
    private const int MaxRetries = 3;
    private const string BaseUrl = "https://query1.finance.yahoo.com/v8/finance/chart";

    static YahooFinanceProvider()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        HttpClient = new HttpClient(handler);
        HttpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        HttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<StockQuote> GetHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        var period1 = new DateTimeOffset(startDate).ToUnixTimeSeconds();
        var period2 = new DateTimeOffset(endDate).ToUnixTimeSeconds();
        var url = $"{BaseUrl}/{symbol}?period1={period1}&period2={period2}&interval=1d&includeAdjustedClose=true";

        string json = await FetchWithRetryAsync(url);
        var prices = ParseChartResponse(json, symbol);

        return new StockQuote(symbol, prices);
    }

    public async Task<List<StockQuote>> GetHistoricalDataAsync(
        IEnumerable<string> symbols, DateTime startDate, DateTime endDate)
    {
        var quotes = new List<StockQuote>();

        // Fetch sequentially with a delay between requests to avoid rate limiting
        foreach (var symbol in symbols)
        {
            quotes.Add(await GetHistoricalDataAsync(symbol, startDate, endDate));
            await Task.Delay(500); // 500ms pause between symbols
        }

        return quotes;
    }

    private static async Task<string> FetchWithRetryAsync(string url)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await HttpClient.GetAsync(url);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt == MaxRetries)
                        throw new HttpRequestException($"Yahoo Finance rate limit exceeded after {MaxRetries + 1} attempts.");

                    var delay = (int)Math.Pow(2, attempt + 1) * 1000; // 2s, 4s, 8s
                    Console.WriteLine($"  Rate limited, waiting {delay / 1000}s before retry...");
                    await Task.Delay(delay);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException) when (attempt < MaxRetries)
            {
                var delay = (int)Math.Pow(2, attempt + 1) * 1000;
                Console.WriteLine($"  Request failed, retrying in {delay / 1000}s...");
                await Task.Delay(delay);
            }
        }

        throw new HttpRequestException($"Failed to fetch data after {MaxRetries + 1} attempts.");
    }

    private static List<StockPrice> ParseChartResponse(string json, string symbol)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var chart = root.GetProperty("chart");
        var error = chart.GetProperty("error");
        if (error.ValueKind != JsonValueKind.Null)
        {
            var errorMsg = error.GetProperty("description").GetString();
            throw new InvalidOperationException($"Yahoo Finance error for {symbol}: {errorMsg}");
        }

        var result = chart.GetProperty("result")[0];
        var timestamps = result.GetProperty("timestamp");
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];

        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.GetProperty("volume");

        var prices = new List<StockPrice>();

        for (int i = 0; i < timestamps.GetArrayLength(); i++)
        {
            // Skip entries where any OHLC value is null (happens on some trading days)
            if (opens[i].ValueKind == JsonValueKind.Null ||
                highs[i].ValueKind == JsonValueKind.Null ||
                lows[i].ValueKind == JsonValueKind.Null ||
                closes[i].ValueKind == JsonValueKind.Null)
                continue;

            var timestamp = timestamps[i].GetInt64();
            var date = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime.Date;

            prices.Add(new StockPrice(
                Date: date,
                Open: (decimal)opens[i].GetDouble(),
                High: (decimal)highs[i].GetDouble(),
                Low: (decimal)lows[i].GetDouble(),
                Close: (decimal)closes[i].GetDouble(),
                Volume: volumes[i].ValueKind == JsonValueKind.Null ? 0 : volumes[i].GetInt64()
            ));
        }

        return prices.OrderBy(p => p.Date).ToList();
    }
}
