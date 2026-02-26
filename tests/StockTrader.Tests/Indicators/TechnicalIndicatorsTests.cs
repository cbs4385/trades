using StockTrader.Core.Indicators;
using StockTrader.Core.Models;

namespace StockTrader.Tests.Indicators;

public class TechnicalIndicatorsTests
{
    private static List<StockPrice> GeneratePrices(decimal[] closes)
    {
        return closes.Select((c, i) => new StockPrice(
            Date: DateTime.Today.AddDays(-closes.Length + i + 1),
            Open: c - 0.5m,
            High: c + 1m,
            Low: c - 1m,
            Close: c,
            Volume: 1_000_000
        )).ToList();
    }

    [Fact]
    public void SMA_CalculatesCorrectAverage()
    {
        var prices = GeneratePrices(new[] { 10m, 20m, 30m, 40m, 50m });
        var sma = TechnicalIndicators.SMA(prices, 3);

        Assert.Equal(5, sma.Count);
        Assert.Null(sma[0].Value); // not enough data
        Assert.Null(sma[1].Value);
        Assert.Equal(20m, sma[2].Value); // (10+20+30)/3
        Assert.Equal(30m, sma[3].Value); // (20+30+40)/3
        Assert.Equal(40m, sma[4].Value); // (30+40+50)/3
    }

    [Fact]
    public void SMA_ReturnsNullForInsufficientData()
    {
        var prices = GeneratePrices(new[] { 10m, 20m });
        var sma = TechnicalIndicators.SMA(prices, 5);

        Assert.All(sma, v => Assert.Null(v.Value));
    }

    [Fact]
    public void EMA_FirstValueIsSma()
    {
        var prices = GeneratePrices(new[] { 10m, 20m, 30m, 40m, 50m });
        var ema = TechnicalIndicators.EMA(prices, 3);

        // First EMA = SMA(3) = (10+20+30)/3 = 20
        Assert.Equal(20m, ema[2].Value);
    }

    [Fact]
    public void EMA_AppliesMultiplierCorrectly()
    {
        var prices = GeneratePrices(new[] { 10m, 20m, 30m, 40m });
        var ema = TechnicalIndicators.EMA(prices, 3);

        // period=3, multiplier = 2/(3+1) = 0.5
        // EMA[2] = SMA = 20
        // EMA[3] = (40 - 20) * 0.5 + 20 = 30
        Assert.Equal(30m, ema[3].Value);
    }

    [Fact]
    public void RSI_ReturnsValuesInRange()
    {
        // Generate a price series with ups and downs
        var closes = new decimal[30];
        for (int i = 0; i < 30; i++)
            closes[i] = 100m + (i % 3 == 0 ? 5m : -2m) * (i / 3);

        var prices = GeneratePrices(closes);
        var rsi = TechnicalIndicators.RSI(prices, 14);

        foreach (var value in rsi.Where(v => v.Value.HasValue))
        {
            Assert.InRange(value.Value!.Value, 0m, 100m);
        }
    }

    [Fact]
    public void RSI_AllGainsReturnsHigh()
    {
        // Steadily increasing prices should produce RSI near 100
        var closes = Enumerable.Range(1, 30).Select(i => (decimal)i * 10).ToArray();
        var prices = GeneratePrices(closes);
        var rsi = TechnicalIndicators.RSI(prices, 14);

        var lastRsi = rsi.Last().Value;
        Assert.NotNull(lastRsi);
        Assert.True(lastRsi > 90m, $"Expected RSI > 90 for all gains, got {lastRsi}");
    }

    [Fact]
    public void MACD_ReturnsCorrectStructure()
    {
        var closes = Enumerable.Range(1, 50).Select(i => 100m + (decimal)Math.Sin(i * 0.3) * 20).ToArray();
        var prices = GeneratePrices(closes);
        var macd = TechnicalIndicators.MACD(prices);

        Assert.Equal(50, macd.Count);

        // First values should be null (not enough data)
        Assert.Null(macd[0].MacdLine);

        // Later values should have all components
        var last = macd.Last();
        Assert.NotNull(last.MacdLine);
    }

    [Fact]
    public void BollingerBands_UpperAboveMiddleAboveLower()
    {
        var closes = Enumerable.Range(1, 30)
            .Select(i => 100m + (decimal)Math.Sin(i * 0.5) * 10)
            .ToArray();
        var prices = GeneratePrices(closes);
        var bb = TechnicalIndicators.BollingerBands(prices, 20);

        foreach (var band in bb.Where(b => b.Upper.HasValue))
        {
            Assert.True(band.Upper > band.Middle);
            Assert.True(band.Middle > band.Lower);
        }
    }
}
