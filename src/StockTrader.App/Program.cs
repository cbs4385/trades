using StockTrader.Core.Data;
using StockTrader.Core.Engine;
using StockTrader.Core.Models;
using StockTrader.Core.Strategies;

namespace StockTrader.App;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════╗");
        Console.WriteLine("║          STOCK TRADING SIMULATOR                    ║");
        Console.WriteLine("║          Paper Trading & Backtesting Engine         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var config = ParseArgs(args);

        try
        {
            var result = await RunSimulation(config);
            result.PrintSummary();
            result.PrintTradeLog();
            if (result.IntradayMode)
                result.PrintPerformanceLog();
            result.PrintEquityCurve();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public static async Task<SimulationResult> RunSimulation(SimulationConfig config)
    {
        Console.WriteLine($"Strategy:    {config.StrategyName}");
        Console.WriteLine($"Symbols:     {string.Join(", ", config.Symbols)}");
        Console.WriteLine($"Period:      {config.StartDate:yyyy-MM-dd} to {config.EndDate:yyyy-MM-dd}");
        Console.WriteLine($"Capital:     {config.InitialCapital:C}");
        Console.WriteLine($"Commission:  {config.Commission:C} per trade");
        if (config.StopLossPercent > 0) Console.WriteLine($"Stop Loss:   {config.StopLossPercent}%");
        if (config.TakeProfitPercent > 0) Console.WriteLine($"Take Profit: {config.TakeProfitPercent}%");
        if (config.TrailingStopPercent > 0) Console.WriteLine($"Trail Stop:  {config.TrailingStopPercent}%");
        if (config.IntradayMode) Console.WriteLine("Mode:        INTRADAY (buy at Open, sell at Close)");
        Console.WriteLine();
        IMarketDataProvider dataProvider = config.DataSource switch
        {
            "alphavantage" => new AlphaVantageProvider(config.AlphaVantageApiKey),
            "csv" => new CsvDataProvider(config.CsvDataPath),
            _ => new YahooFinanceProvider()
        };

        Console.WriteLine($"Fetching market data via {config.DataSource}...");

        var quotes = await dataProvider.GetHistoricalDataAsync(
            config.Symbols, config.StartDate, config.EndDate);

        foreach (var quote in quotes)
            Console.WriteLine($"  {quote.Symbol}: {quote.Prices.Count} trading days loaded");

        Console.WriteLine();
        Console.WriteLine("Running backtest simulation...");

        ITradingStrategy strategy = config.StrategyName switch
        {
            "rsi" => new RsiMeanReversionStrategy(
                rsiOverbought: config.RsiOverbought,
                rsiOversold: config.RsiOversold),
            "intraday" => new IntradayMomentumStrategy(
                minimumScore: config.MinimumScore),
            _ => new SmaCrossoverStrategy(
                fastPeriod: config.FastPeriod,
                slowPeriod: config.SlowPeriod,
                macdSellBars: config.MacdSellBars)
        };

        var portfolio = new Portfolio(config.InitialCapital);
        var engine = new TradingEngine(
            portfolio, strategy,
            commissionPerTrade: config.Commission,
            maxPositionPercent: config.MaxPositionPercent,
            stopLossPercent: config.StopLossPercent,
            takeProfitPercent: config.TakeProfitPercent,
            trailingStopPercent: config.TrailingStopPercent,
            intradayMode: config.IntradayMode);

        return engine.RunBacktest(quotes);
    }

    private static SimulationConfig ParseArgs(string[] args)
    {
        var config = new SimulationConfig();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--symbols" or "-s":
                    config.Symbols = args[++i].Split(',').Select(s => s.Trim().ToUpper()).ToList();
                    break;
                case "--start":
                    config.StartDate = DateTime.Parse(args[++i]);
                    break;
                case "--end":
                    config.EndDate = DateTime.Parse(args[++i]);
                    break;
                case "--capital" or "-c":
                    config.InitialCapital = decimal.Parse(args[++i]);
                    break;
                case "--strategy":
                    config.StrategyName = args[++i].ToLower();
                    if (config.StrategyName == "intraday")
                        config.IntradayMode = true;
                    break;
                case "--fast-period":
                    config.FastPeriod = int.Parse(args[++i]);
                    break;
                case "--slow-period":
                    config.SlowPeriod = int.Parse(args[++i]);
                    break;
                case "--rsi-overbought":
                    config.RsiOverbought = decimal.Parse(args[++i]);
                    break;
                case "--rsi-oversold":
                    config.RsiOversold = decimal.Parse(args[++i]);
                    break;
                case "--commission":
                    config.Commission = decimal.Parse(args[++i]);
                    break;
                case "--max-position":
                    config.MaxPositionPercent = decimal.Parse(args[++i]);
                    break;
                case "--stop-loss":
                    config.StopLossPercent = decimal.Parse(args[++i]);
                    break;
                case "--take-profit":
                    config.TakeProfitPercent = decimal.Parse(args[++i]);
                    break;
                case "--trailing-stop":
                    config.TrailingStopPercent = decimal.Parse(args[++i]);
                    break;
                case "--macd-sell-bars":
                    config.MacdSellBars = int.Parse(args[++i]);
                    break;
                case "--min-score":
                    config.MinimumScore = decimal.Parse(args[++i]);
                    break;
                case "--data-source":
                    config.DataSource = args[++i].ToLower();
                    break;
                case "--csv-path":
                    config.CsvDataPath = args[++i];
                    break;
                case "--api-key":
                    config.AlphaVantageApiKey = args[++i];
                    break;
                case "--help" or "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        return config;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: StockTrader.App [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --symbols, -s <SYM1,SYM2>   Comma-separated stock symbols");
        Console.WriteLine("  --start <date>               Start date for backtest (default: 5 years ago)");
        Console.WriteLine("  --end <date>                 End date for backtest (default: today)");
        Console.WriteLine("  --capital, -c <amount>       Initial capital (default: $100)");
        Console.WriteLine("  --strategy <name>            Strategy: sma-crossover, rsi, intraday (default: intraday)");
        Console.WriteLine("  --fast-period <n>            Fast SMA period (default: 10)");
        Console.WriteLine("  --slow-period <n>            Slow SMA period (default: 30)");
        Console.WriteLine("  --rsi-overbought <n>         RSI overbought threshold (default: 70)");
        Console.WriteLine("  --rsi-oversold <n>           RSI oversold threshold (default: 30)");
        Console.WriteLine("  --macd-sell-bars <n>         Consecutive MACD bars before sell (default: 3)");
        Console.WriteLine("  --min-score <n>              Minimum score for intraday entry (default: 5)");
        Console.WriteLine("  --commission <amount>        Commission per trade (default: $0)");
        Console.WriteLine("  --max-position <pct>         Max position size as fraction (default: 1.0 for intraday)");
        Console.WriteLine("  --stop-loss <pct>            Stop-loss percentage, e.g. 2 for 2% (default: 2% intraday)");
        Console.WriteLine("  --take-profit <pct>          Take-profit percentage (default: off)");
        Console.WriteLine("  --trailing-stop <pct>        Trailing stop percentage from peak (default: off)");
        Console.WriteLine("  --data-source <source>       Data source: yahoo, alphavantage, csv (default: yahoo)");
        Console.WriteLine("  --api-key <key>              Alpha Vantage API key (free at alphavantage.co)");
        Console.WriteLine("  --csv-path <path>            Path to CSV data directory");
        Console.WriteLine("  --help, -h                   Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run                                                         # Intraday with $100 across 20 stocks");
        Console.WriteLine("  dotnet run -- --strategy sma-crossover --capital 100000            # Swing trading");
        Console.WriteLine("  dotnet run -- --strategy intraday --min-score 6 --stop-loss 1.5    # Stricter intraday");
    }
}

public class SimulationConfig
{
    // Default: 20 liquid stocks across sectors for diversified intraday scanning
    public List<string> Symbols { get; set; } = new()
    {
        "AAPL", "MSFT", "GOOGL", "AMZN", "META",   // Big Tech
        "NVDA", "AMD", "TSLA",                        // High-beta tech
        "JPM", "BAC", "GS",                           // Financials
        "JNJ", "UNH", "PFE",                          // Healthcare
        "WMT", "KO", "DIS",                           // Consumer
        "XOM", "CVX",                                  // Energy
        "CAT"                                          // Industrial
    };
    public DateTime StartDate { get; set; } = DateTime.Now.AddYears(-5);
    public DateTime EndDate { get; set; } = DateTime.Now;
    public decimal InitialCapital { get; set; } = 100m;
    public string StrategyName { get; set; } = "intraday";
    public bool IntradayMode { get; set; } = true;
    public int FastPeriod { get; set; } = 10;
    public int SlowPeriod { get; set; } = 30;
    public decimal RsiOverbought { get; set; } = 70m;
    public decimal RsiOversold { get; set; } = 30m;
    public int MacdSellBars { get; set; } = 3;
    public decimal MinimumScore { get; set; } = 5m;
    public decimal Commission { get; set; } = 0m;
    public decimal MaxPositionPercent { get; set; } = 1.0m;  // Intraday: use 100% (one position at a time)
    public decimal StopLossPercent { get; set; } = 2m;       // 2% intraday stop-loss
    public decimal TakeProfitPercent { get; set; } = 0m;
    public decimal TrailingStopPercent { get; set; } = 0m;
    public string DataSource { get; set; } = "yahoo";
    public string CsvDataPath { get; set; } = "./data";
    public string AlphaVantageApiKey { get; set; } = string.Empty;
}
