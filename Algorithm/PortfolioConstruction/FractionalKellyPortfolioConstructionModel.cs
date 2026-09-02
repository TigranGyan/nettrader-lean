using NetTrader.Lean.Algorithm.Common;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;

namespace NetTrader.Lean.Algorithm.PortfolioConstruction;

/// <summary>
/// Position sizing by fractional Kelly instead of a fixed percent-of-balance (the bug this replaces:
/// nettrader's GeminiAdvisor.CalcSmartInvestment used a flat 20% regardless of signal confidence — see
/// docs/PLAN.md, "Риск-слой как компоненты LEAN"). Not LEAN's built-in InsightWeightingPortfolioConstructionModel:
/// that only normalizes by Insight.Confidence across the active insight set, with no concept of the
/// asymmetric loss-adjusted Kelly formula or a per-signal reward:risk ratio.
///
/// f = p - (1-p)/R,  clipped to [0, fCap], scaled by KellyFraction (half-Kelly by default) and by
/// available margin (MaxMarginUsagePercent). p = Insight.Confidence (the model's win probability for
/// this direction). R = MlSignalTag.RewardRiskRatio (produced by MlSignalAlphaModel alongside the insight).
///
/// TODO(Phase 0, blocking): verify this against the actual PortfolioConstructionModel base class API
/// for the LEAN version pinned in NetTrader.Lean.Algorithm.csproj — the CreateTargets signature and
/// PortfolioTarget.Percent helper are stable across recent LEAN releases but were not compiled/verified
/// in the sandbox that authored this file (no dotnet SDK available there).
/// </summary>
public sealed class FractionalKellyPortfolioConstructionModel : QuantConnect.Algorithm.Framework.Portfolio.PortfolioConstructionModel
{
    private readonly TradingOptions _options;

    public FractionalKellyPortfolioConstructionModel(TradingOptions options)
    {
        _options = options;
    }

    public override List<IPortfolioTarget> CreateTargets(QCAlgorithm algorithm, Insight[] insights)
    {
        var targets = new List<IPortfolioTarget>();

        // Hard cap: never let a single Kelly-sized position exceed this fraction of the portfolio,
        // even if the formula alone would suggest more (Kelly is a growth-optimal formula, not a
        // risk-of-ruin-safe one on its own without a cap — the whole point of using a *fraction* of it).
        const decimal fCap = 0.4m;

        foreach (var insight in insights)
        {
            var tag = MlSignalTag.FromJson(insight.Tag);
            if (tag is null || tag.RewardRiskRatio <= 0)
            {
                // No sizing info attached — do not guess a position size for it.
                continue;
            }

            var p = (decimal)insight.Confidence.GetValueOrDefault();
            var r = tag.RewardRiskRatio;

            // f = p - (1-p)/R  — the Kelly criterion for an asymmetric binary bet.
            var kellyFull = p - (1 - p) / r;
            if (kellyFull <= 0)
            {
                // Model claims an edge that Kelly says is not actually profitable net of the R:R offered.
                // This should already have been filtered by MlSignalAlphaModel's entry gate — if it
                // reaches here, treat it as a signal that should not have been emitted, not a sizing bug.
                continue;
            }

            var fraction = Math.Min(kellyFull * _options.KellyFraction, fCap);

            // Per-position cap: never let one position alone claim more than an equal share of the
            // total margin budget across the configured max concurrent positions. This is a simple,
            // explainable cap — not a portfolio-level optimization — deliberately, since the whole
            // point of this rewrite is a risk layer whose math is auditable (see docs/PLAN.md).
            var marginCapFraction = _options.MaxMarginUsagePercent / Math.Max(1, _options.MaxOpenPositions);
            fraction = Math.Min(fraction, marginCapFraction);

            var direction = insight.Direction == InsightDirection.Down ? -1m : 1m;
            targets.Add(QuantConnect.Algorithm.Framework.Portfolio.PortfolioTarget.Percent(algorithm, insight.Symbol, direction * fraction));
        }

        return targets;
    }
}
