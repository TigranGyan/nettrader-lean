using NetTrader.Lean.Algorithm.Alpha;
using NetTrader.Lean.Algorithm.Common;
using NetTrader.Lean.Algorithm.PortfolioConstruction;
using NetTrader.Lean.Algorithm.RiskManagement;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Algorithm.Framework.Selection;

namespace NetTrader.Lean.Algorithm;

/// <summary>
/// Phase 0 entry point (docs/PLAN.md): crypto symbols only, wired for a clean backtest smoke-test of
/// Insight generation. Deliberately defaults to the BUILT-IN InsightWeightingPortfolioConstructionModel,
/// not FractionalKellyPortfolioConstructionModel yet — per the plan's Phase 0 gate, sizing/risk models
/// with real money math should only be switched on after Insight generation itself is validated against
/// the historically-logged behavior of the old MLSignalService.Decide(). Flip UseKellySizing to true once
/// that gate is cleared (and once MlSignalAlphaModel's stubs are replaced with the ported model logic —
/// until then this algorithm will not trade at all, by design: GetPLongPShort/ComputeStructuralSlTp
/// return zeros, so every candidate insight is filtered out before construction).
///
/// TODO(Phase 0, blocking): none of this has been compiled — no dotnet SDK in the authoring sandbox.
/// `dotnet restore && dotnet build` first, fix whatever the pinned LEAN version's actual API surface
/// disagrees with here (class names below are believed correct for recent LEAN releases but unverified).
/// </summary>
public sealed class MultiAssetAlgorithm : QCAlgorithm
{
    private const bool UseKellySizing = false;

    public override void Initialize()
    {
        var options = new TradingOptions();

        // TODO(Phase 0): confirm start/end dates and starting cash before any backtest run —
        // placeholders here, not a considered choice.
        SetStartDate(2024, 1, 1);
        SetEndDate(2025, 1, 1);
        SetCash(100000);

        foreach (var symbol in SymbolConfig.AllowedSymbols)
        {
            AddCrypto(symbol, Resolution.Hour, Market.Binance);
        }

        var insightPeriod = TimeSpan.FromHours(4);
        SetAlpha(new MlSignalAlphaModel(options, insightPeriod));

        SetPortfolioConstruction(UseKellySizing
            ? new FractionalKellyPortfolioConstructionModel(options)
            : new QuantConnect.Algorithm.Framework.Portfolio.InsightWeightingPortfolioConstructionModel());

        SetRiskManagement(new CompositeRiskManagementModel(
            new CorrelationGroupExposureRiskManagementModel(),
            new MarginRatioRiskManagementModel(options.MaxMarginUsagePercent),
            new DailyDrawdownRiskManagementModel(options.DailyDrawdownPausePercent, options.EmergencyStopBalancePercent, (decimal)Portfolio.TotalPortfolioValue)
        ));

        SetExecution(new ImmediateExecutionModel());
    }
}
