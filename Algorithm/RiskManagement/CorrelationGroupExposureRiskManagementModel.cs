using NetTrader.Lean.Algorithm.Common;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Data.UniverseSelection;

namespace NetTrader.Lean.Algorithm.RiskManagement;

/// <summary>
/// Caps total exposure per correlation group (see SymbolConfig.CoinGroups) instead of relying on an AI
/// prompt instruction ("Max 1 per correlation group") that was never actually enforced in code — see
/// docs/PLAN.md. LEAN has no built-in notion of arbitrary user-defined correlation groups, so this is
/// new code, not a port.
/// </summary>
public sealed class CorrelationGroupExposureRiskManagementModel : RiskManagementModel
{
    private readonly decimal _maxGroupExposurePercent;

    public CorrelationGroupExposureRiskManagementModel(decimal maxGroupExposurePercent = 0.4m)
    {
        _maxGroupExposurePercent = maxGroupExposurePercent;
    }

    public override IEnumerable<IPortfolioTarget> ManageRisk(QCAlgorithm algorithm, IPortfolioTarget[] targets)
    {
        var groupTotals = new Dictionary<SymbolConfig.CorrelationGroup, decimal>();
        var result = new List<IPortfolioTarget>();
        var totalPortfolioValue = algorithm.Portfolio.TotalPortfolioValue;

        foreach (var target in targets)
        {
            var symbolKey = target.Symbol.Value;
            if (!SymbolConfig.CoinGroups.TryGetValue(symbolKey, out var group) || totalPortfolioValue <= 0)
            {
                // Not one of the whitelisted symbols this model knows about, or no portfolio value to
                // compute a fraction against — pass through unchanged.
                result.Add(target);
                continue;
            }

            var price = algorithm.Securities[target.Symbol].Price;
            var requestedFraction = Math.Abs(target.Quantity) * price / totalPortfolioValue;
            var sign = Math.Sign(target.Quantity);

            var currentGroupExposure = groupTotals.GetValueOrDefault(group, 0m);
            var allowedRemaining = Math.Max(0, _maxGroupExposurePercent - currentGroupExposure);

            if (requestedFraction > allowedRemaining)
            {
                // Scale down rather than drop entirely, and preserve direction.
                result.Add(QuantConnect.Algorithm.Framework.Portfolio.PortfolioTarget.Percent(algorithm, target.Symbol, allowedRemaining * sign));
                groupTotals[group] = currentGroupExposure + allowedRemaining;
            }
            else
            {
                result.Add(target);
                groupTotals[group] = currentGroupExposure + requestedFraction;
            }
        }

        return result;
    }
}
