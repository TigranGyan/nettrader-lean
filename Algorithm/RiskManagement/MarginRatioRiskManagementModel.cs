using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Data.UniverseSelection;

namespace NetTrader.Lean.Algorithm.RiskManagement;

/// <summary>
/// Blocks/reduces new targets as margin usage approaches a configured ceiling. This existed nowhere
/// in nettrader — the old code only checked "is there enough free balance for this trade's margin",
/// never how close the whole book is to a liquidation-triggering margin ratio. See docs/PLAN.md.
/// </summary>
public sealed class MarginRatioRiskManagementModel : RiskManagementModel
{
    private readonly decimal _maxMarginRatio;

    public MarginRatioRiskManagementModel(decimal maxMarginRatio = 0.8m)
    {
        _maxMarginRatio = maxMarginRatio;
    }

    public override IEnumerable<IPortfolioTarget> ManageRisk(QCAlgorithm algorithm, IPortfolioTarget[] targets)
    {
        var totalPortfolioValue = algorithm.Portfolio.TotalPortfolioValue;
        if (totalPortfolioValue <= 0)
        {
            return targets;
        }

        var currentMarginRatio = algorithm.Portfolio.TotalMarginUsed / totalPortfolioValue;
        if (currentMarginRatio < _maxMarginRatio)
        {
            return targets;
        }

        // Already at or past the margin ceiling from existing positions — refuse every new/increased
        // target rather than letting the portfolio construction model push the book closer to
        // liquidation. Existing positions are left alone (this model does not force-close anything;
        // that is DailyDrawdownRiskManagementModel's job at a harder threshold).
        algorithm.Log($"MarginRatioRiskManagementModel: blocking new targets, margin ratio {currentMarginRatio:P1} >= ceiling {_maxMarginRatio:P1}");
        return Array.Empty<IPortfolioTarget>();
    }
}
