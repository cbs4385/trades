using StockTrader.Core.Models;

namespace StockTrader.Core.Engine;

public interface ITradingStrategy
{
    string Name { get; }
    List<TradingSignal> Evaluate(StockQuote quote, Portfolio portfolio);
}
