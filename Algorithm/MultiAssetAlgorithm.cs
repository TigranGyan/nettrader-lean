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
/// Insight generation.
/// </summary>
public sealed class MultiAssetAlgorithm : QCAlgorithm
{
    private const bool UseKellySizing = false;

    public override void Initialize()
    {
        var options = new TradingOptions();

        SetStartDate(2024, 1, 1);
        SetEndDate(2025, 1, 1);
        SetCash(100000);

        // Resolution.Minute, not Hour: MlSignalAlphaModel's SymbolCandleCache builds 30m/1h/2h/4h bars
        // via TradeBarConsolidator from this feed (see SymbolCandleCache.cs's TODO) — an Hour-resolution
        // subscription cannot be split into 30m bars. That consolidator wiring is still unverified by an
        // actual build/backtest (see README.md's "что не готово" list).
        foreach (var symbol in SymbolConfig.AllowedSymbols)
        {
            AddCrypto(symbol, Resolution.Minute, Market.Binance);
        }

        var insightPeriod = TimeSpan.FromHours(4);
        SetAlpha(new MlSignalAlphaModel(options, insightPeriod));

        SetPortfolioConstruction(UseKellySizing
            ? new FractionalKellyPortfolioConstructionModel(options)
            : new EqualWeightingPortfolioConstructionModel());

        SetRiskManagement(new CompositeRiskManagementModel(
            new CorrelationGroupExposureRiskManagementModel(),
            new MarginRatioRiskManagementModel(options.MaxMarginUsagePercent),
            new DailyDrawdownRiskManagementModel(options.DailyDrawdownPausePercent, options.EmergencyStopBalancePercent, (decimal)Portfolio.TotalPortfolioValue)
        ));

        SetExecution(new ImmediateExecutionModel());
    }
}
