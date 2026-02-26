namespace StockTrader.Core.Indicators;

public record IndicatorValue(DateTime Date, decimal? Value);

public record MacdResult(
    DateTime Date,
    decimal? MacdLine,
    decimal? SignalLine,
    decimal? Histogram);
