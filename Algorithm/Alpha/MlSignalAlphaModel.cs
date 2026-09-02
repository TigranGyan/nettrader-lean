using NetTrader.Lean.Algorithm.Common;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data;

namespace NetTrader.Lean.Algorithm.Alpha;

/// <summary>
/// Generates Insights from the ML model, gated on both a minimum reward:risk ratio and on the model's
/// probability clearing the reward:risk-implied breakeven threshold — the two checks that did not exist
/// anywhere in nettrader's code (only a prompt-only ratio suggestion, and an absolute confidence floor
/// unrelated to the R:R actually offered). See docs/PLAN.md, "Риск-слой как компоненты LEAN".
///
/// TODO(Phase 0, blocking — the actual signal, not just the gate): this class does NOT yet compute the
/// 30-feature vector or load ModelLong.zip/ModelShort.zip. That logic must be PORTED, not rewritten,
/// from nettrader/NetTrader.Infrastructure/MLServices/MLSignalService.cs (ComputeFeatures, lines
/// ~135-336) and nettrader/NetTrader.Application/Services/IndicatorEnrichmentService.cs (the 30m/1h/2h/4h
/// indicator pipeline the features are built from) — reimplementing the indicator math from scratch here
/// risks a silent mismatch against what the model was actually trained on, which is exactly the class of
/// bug this whole rewrite exists to eliminate. GetPLongPShort below is a stub returning (0, 0) — replace
/// its body with the ported logic before this model can emit anything real.
///
/// TODO(Phase 0, blocking): verify AlphaModel base class / Update signature against the pinned LEAN
/// version — not compiled in the authoring sandbox (no dotnet SDK there).
/// </summary>
public sealed class MlSignalAlphaModel : AlphaModel
{
    private readonly TradingOptions _options;
    private readonly TimeSpan _insightPeriod;

    public MlSignalAlphaModel(TradingOptions options, TimeSpan insightPeriod)
    {
        _options = options;
        _insightPeriod = insightPeriod;
    }

    public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
    {
        var insights = new List<Insight>();

        foreach (var symbolStr in SymbolConfig.AllowedSymbols)
        {
            var symbol = algorithm.Securities.Keys.FirstOrDefault(s => s.Value == symbolStr);
            if (symbol is null || !data.Bars.ContainsKey(symbol))
            {
                continue;
            }

            var (pLong, pShort) = GetPLongPShort(algorithm, symbol);
            if (pLong <= 0 && pShort <= 0)
            {
                continue; // model not wired up yet (see class-level TODO), or genuinely no signal.
            }

            var direction = pLong > pShort ? InsightDirection.Up : InsightDirection.Down;
            var p = (decimal)Math.Max(pLong, pShort);

            var (rewardRiskRatio, stopLoss, takeProfit) = ComputeStructuralSlTp(algorithm, symbol, direction);
            if (rewardRiskRatio < _options.MinRewardRiskRatio)
            {
                continue; // hard R:R floor — never traded regardless of confidence.
            }

            // p_breakeven = 1 / (1 + R): the minimum win probability at which this R:R breaks even
            // before costs. Trading only above breakeven + margin is the fix for nettrader's absolute,
            // R:R-blind confidence threshold (minRequiredConfidence=0.60 in the old MLSignalService).
            var pBreakeven = 1m / (1m + rewardRiskRatio);
            if (p < pBreakeven + _options.EntryProbabilityMargin)
            {
                continue;
            }

            var tag = new MlSignalTag
            {
                RewardRiskRatio = rewardRiskRatio,
                StopLossPrice = stopLoss,
                TakeProfitPrice = takeProfit,
            }.ToJson();

            insights.Add(Insight.Price(symbol, _insightPeriod, direction, confidence: (double)p, tag: tag));
        }

        return insights;
    }

    /// <summary>
    /// STUB. Must be replaced with the ported ComputeFeatures + PredictionEngine("ModelLong"/"ModelShort")
    /// logic from nettrader's MLSignalService.cs. Returns (0, 0) so this model is inert (emits nothing)
    /// until that porting work is done — deliberately fails closed, not open.
    /// </summary>
    private static (double pLong, double pShort) GetPLongPShort(QCAlgorithm algorithm, Symbol symbol)
    {
        return (0d, 0d);
    }

    /// <summary>
    /// STUB. Must be replaced with ATR/volume-profile-based SL/TP sized to match the ML model's own
    /// training label (+3%/-2% triple barrier per docs/PLAN.md), not nettrader's mismatched fallback
    /// (2%/1.5%, GridMathCalculator's fallbackSlPct/fallbackTpPct) which under-targets the model's own
    /// profit label. Returns a 0 ratio so the R:R gate above fails closed until this is implemented.
    /// </summary>
    private static (decimal rewardRiskRatio, decimal stopLoss, decimal takeProfit) ComputeStructuralSlTp(
        QCAlgorithm algorithm, Symbol symbol, InsightDirection direction)
    {
        return (0m, 0m, 0m);
    }
}
