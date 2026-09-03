namespace NetTrader.Lean.Algorithm.Common;

/// <summary>
/// Ported from nettrader/NetTrader.Domain/Constants/SymbolConfig.cs — keep in sync by hand until
/// NetTrader.Domain itself is referenced directly (it has zero EF/ASP dependencies, so a direct
/// project reference is possible later instead of duplicating this list).
/// </summary>
public static class SymbolConfig
{
    public enum CorrelationGroup
    {
        Bitcoin,
        Layer1,
        Payment,
        Meme,
        Exchange
    }

    public static readonly IReadOnlyList<string> AllowedSymbols = new[]
    {
        "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "BNBUSDT", "AVAXUSDT"
    };

    public static readonly IReadOnlyDictionary<string, CorrelationGroup> CoinGroups =
        new Dictionary<string, CorrelationGroup>
        {
            ["BTCUSDT"] = CorrelationGroup.Bitcoin,
            ["ETHUSDT"] = CorrelationGroup.Layer1,
            ["SOLUSDT"] = CorrelationGroup.Layer1,
            ["AVAXUSDT"] = CorrelationGroup.Layer1,
            ["XRPUSDT"] = CorrelationGroup.Payment,
            ["DOGEUSDT"] = CorrelationGroup.Meme,
            ["BNBUSDT"] = CorrelationGroup.Exchange,
        };
}
