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
        Console.WriteLine();
        Console.WriteLine("Fetching market data from Yahoo Finance...");

        IMarketDataProvider dataProvider = config.DataSource switch
        {
            "csv" => new CsvDataProvider(config.CsvDataPath),
            _ => new YahooFinanceProvider()
        };

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
            _ => new SmaCrossoverStrategy(
                fastPeriod: config.FastPeriod,
                slowPeriod: config.SlowPeriod)
        };

        var portfolio = new Portfolio(config.InitialCapital);
        var engine = new TradingEngine(
            portfolio, strategy,
            commissionPerTrade: config.Commission,
            maxPositionPercent: config.MaxPositionPercent);

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
                case "--data-source":
                    config.DataSource = args[++i].ToLower();
                    break;
                case "--csv-path":
                    config.CsvDataPath = args[++i];
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
        Console.WriteLine("  --symbols, -s <SYM1,SYM2>   Comma-separated stock symbols (default: AAPL,MSFT,GOOGL)");
        Console.WriteLine("  --start <date>               Start date for backtest (default: 2 years ago)");
        Console.WriteLine("  --end <date>                 End date for backtest (default: today)");
        Console.WriteLine("  --capital, -c <amount>       Initial capital (default: $100,000)");
        Console.WriteLine("  --strategy <name>            Strategy: sma-crossover, rsi (default: sma-crossover)");
        Console.WriteLine("  --fast-period <n>            Fast SMA period (default: 10)");
        Console.WriteLine("  --slow-period <n>            Slow SMA period (default: 30)");
        Console.WriteLine("  --rsi-overbought <n>         RSI overbought threshold (default: 70)");
        Console.WriteLine("  --rsi-oversold <n>           RSI oversold threshold (default: 30)");
        Console.WriteLine("  --commission <amount>        Commission per trade (default: $0)");
        Console.WriteLine("  --max-position <pct>         Max position size as fraction (default: 0.25)");
        Console.WriteLine("  --data-source <source>       Data source: yahoo, csv (default: yahoo)");
        Console.WriteLine("  --csv-path <path>            Path to CSV data directory");
        Console.WriteLine("  --help, -h                   Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- --symbols AAPL,MSFT --capital 50000 --strategy sma-crossover");
        Console.WriteLine("  dotnet run -- --symbols TSLA --strategy rsi --rsi-oversold 25 --rsi-overbought 75");
    }
}

public class SimulationConfig
{
    public List<string> Symbols { get; set; } = new() { "AAPL", "MSFT", "GOOGL" };
    public DateTime StartDate { get; set; } = DateTime.Now.AddYears(-2);
    public DateTime EndDate { get; set; } = DateTime.Now;
    public decimal InitialCapital { get; set; } = 100_000m;
    public string StrategyName { get; set; } = "sma-crossover";
    public int FastPeriod { get; set; } = 10;
    public int SlowPeriod { get; set; } = 30;
    public decimal RsiOverbought { get; set; } = 70m;
    public decimal RsiOversold { get; set; } = 30m;
    public decimal Commission { get; set; } = 0m;
    public decimal MaxPositionPercent { get; set; } = 0.25m;
    public string DataSource { get; set; } = "yahoo";
    public string CsvDataPath { get; set; } = "./data";
}
