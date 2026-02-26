using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using StockTrader.Core.Models;

namespace StockTrader.Core.Data;

public class YahooFinanceProvider : IMarketDataProvider
{
    private const int MaxRetries = 3;

    private static readonly CookieContainer CookieJar = new();
    private static readonly HttpClient HttpClient;
    private static string? _crumb;
    private static readonly SemaphoreSlim CrumbLock = new(1, 1);

    static YahooFinanceProvider()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            CookieContainer = CookieJar,
            UseCookies = true
        };
        HttpClient = new HttpClient(handler);
        HttpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        HttpClient.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.8,*/*;q=0.7");
    }

    public async Task<StockQuote> GetHistoricalDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        var period1 = new DateTimeOffset(startDate).ToUnixTimeSeconds();
        var period2 = new DateTimeOffset(endDate).ToUnixTimeSeconds();

        // Try v8 chart API first (often works without crumb), then fall back to v7 with crumb
        var strategies = new List<Func<Task<string>>>
        {
            () => FetchV8ChartAsync(symbol, period1, period2),
            () => FetchV7DownloadAsync(symbol, period1, period2)
        };

        foreach (var strategy in strategies)
        {
            try
            {
                var json = await strategy();
                return ParseResponse(json, symbol);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {symbol}: attempt failed ({ex.Message}), trying next method...");
            }
        }

        throw new HttpRequestException(
            $"All Yahoo Finance methods failed for {symbol}. Consider using --data-source alphavantage or --data-source csv");
    }

    public async Task<List<StockQuote>> GetHistoricalDataAsync(
        IEnumerable<string> symbols, DateTime startDate, DateTime endDate)
    {
        var quotes = new List<StockQuote>();

        foreach (var symbol in symbols)
        {
            quotes.Add(await GetHistoricalDataAsync(symbol, startDate, endDate));
            await Task.Delay(1000); // 1s pause between symbols
        }

        return quotes;
    }

    private static async Task<string> FetchV8ChartAsync(string symbol, long period1, long period2)
    {
        // Try query2 first (less rate-limited), then query1
        string[] hosts = { "query2.finance.yahoo.com", "query1.finance.yahoo.com" };

        foreach (var host in hosts)
        {
            var url = $"https://{host}/v8/finance/chart/{symbol}?period1={period1}&period2={period2}&interval=1d";

            try
            {
                return await FetchWithRetryAsync(url);
            }
            catch
            {
                // Try next host
            }
        }

        throw new HttpRequestException("v8 chart API failed on all hosts.");
    }

    private static async Task<string> FetchV7DownloadAsync(string symbol, long period1, long period2)
    {
        var crumb = await GetCrumbAsync();

        string[] hosts = { "query2.finance.yahoo.com", "query1.finance.yahoo.com" };

        foreach (var host in hosts)
        {
            var url = $"https://{host}/v7/finance/download/{symbol}" +
                      $"?period1={period1}&period2={period2}&interval=1d&events=history&crumb={Uri.EscapeDataString(crumb)}";

            try
            {
                return await FetchWithRetryAsync(url);
            }
            catch
            {
                // Try next host
            }
        }

        throw new HttpRequestException("v7 download API failed on all hosts.");
    }

    private static async Task<string> GetCrumbAsync()
    {
        await CrumbLock.WaitAsync();
        try
        {
            if (_crumb != null) return _crumb;

            // Step 1: Visit Yahoo Finance to get cookies
            var homeResponse = await HttpClient.GetAsync("https://finance.yahoo.com/quote/AAPL/");
            homeResponse.EnsureSuccessStatusCode();

            // Step 2: Fetch crumb using the cookies
            var crumbResponse = await HttpClient.GetAsync("https://query2.finance.yahoo.com/v1/test/getcrumb");
            crumbResponse.EnsureSuccessStatusCode();
            _crumb = await crumbResponse.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(_crumb))
                throw new InvalidOperationException("Failed to obtain Yahoo Finance crumb.");

            return _crumb;
        }
        finally
        {
            CrumbLock.Release();
        }
    }

    private static async Task<string> FetchWithRetryAsync(string url)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var response = await HttpClient.GetAsync(url);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (attempt == MaxRetries)
                    throw new HttpRequestException("Rate limited after retries.");

                var delay = (int)Math.Pow(2, attempt + 1) * 1000;
                Console.WriteLine($"  Rate limited, waiting {delay / 1000}s...");
                await Task.Delay(delay);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        throw new HttpRequestException("Failed after retries.");
    }

    private static StockQuote ParseResponse(string content, string symbol)
    {
        // Try JSON (v8 chart API) first, then CSV (v7 download)
        content = content.Trim();
        if (content.StartsWith('{'))
            return ParseChartJson(content, symbol);
        else
            return ParseCsvResponse(content, symbol);
    }

    private static StockQuote ParseChartJson(string json, string symbol)
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

        if (!result.TryGetProperty("timestamp", out var timestamps))
            throw new InvalidOperationException($"No data returned for {symbol}.");

        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var opens = quote.GetProperty("open");
        var highs = quote.GetProperty("high");
        var lows = quote.GetProperty("low");
        var closes = quote.GetProperty("close");
        var volumes = quote.GetProperty("volume");

        var prices = new List<StockPrice>();

        for (int i = 0; i < timestamps.GetArrayLength(); i++)
        {
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

        return new StockQuote(symbol, prices.OrderBy(p => p.Date).ToList());
    }

    private static StockQuote ParseCsvResponse(string csv, string symbol)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1); // skip header
        var prices = new List<StockPrice>();

        foreach (var line in lines)
        {
            var parts = line.Split(',');
            if (parts.Length < 6) continue;
            if (parts.Any(p => p.Trim() == "null")) continue;

            prices.Add(new StockPrice(
                Date: DateTime.Parse(parts[0]),
                Open: decimal.Parse(parts[1]),
                High: decimal.Parse(parts[2]),
                Low: decimal.Parse(parts[3]),
                Close: decimal.Parse(parts[4]),
                Volume: long.Parse(parts[5])
            ));
        }

        return new StockQuote(symbol, prices.OrderBy(p => p.Date).ToList());
    }
}
