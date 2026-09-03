namespace NetTrader.Lean.Algorithm.Common;

/// <summary>
/// Risk-layer config. Values ported from nettrader/NetTrader.Domain/Options/TradingOptions.cs where
/// a direct equivalent existed; new fields (KellyFraction, MinRewardRiskRatio, EntryProbabilityMargin)
/// are new for this rewrite — see docs/PLAN.md, "Риск-слой как компоненты LEAN".
/// </summary>
public sealed class TradingOptions
{
    /// <summary>Half-Kelly by default — full Kelly is too volatile for live capital; see docs/PLAN.md.</summary>
    public decimal KellyFraction { get; set; } = 0.5m;

    /// <summary>Hard floor on take-profit/stop-loss distance ratio. A signal offering less is never traded,
    /// regardless of model confidence.</summary>
    public decimal MinRewardRiskRatio { get; set; } = 1.5m;

    /// <summary>Added on top of the breakeven probability (1/(1+R)) before a signal is allowed to trade,
    /// so the edge survives fees/slippage estimation error.</summary>
    public decimal EntryProbabilityMargin { get; set; } = 0.05m;

    public decimal MaxMarginUsagePercent { get; set; } = 0.8m;

    public int MaxOpenPositions { get; set; } = 3;

    public decimal DailyDrawdownPausePercent { get; set; } = 10m;

    public decimal EmergencyStopBalancePercent { get; set; } = 0.5m;

    public int DefaultLeverage { get; set; } = 10;
}
