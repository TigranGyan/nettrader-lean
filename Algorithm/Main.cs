using NetTrader.Lean.Algorithm.Alpha;
using NetTrader.Lean.Algorithm.Common;
using NetTrader.Lean.Algorithm.PortfolioConstruction;
using NetTrader.Lean.Algorithm.RiskManagement;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;

namespace NetTrader.Lean.Algorithm;

/// <summary>
/// Phase 0 entry point (docs/PLAN.md): crypto symbols only, wired for a clean backtest smoke-test of
/// Insight generation.
/// </summary>
public class Main : QCAlgorithm
{
    private const bool UseKellySizing = true;

    public override void Initialize()
    {
        var options = new TradingOptions();

        SetAccountCurrency("USDT");
        SetStartDate(2023, 1, 1);
        SetEndDate(2024, 1, 1);
        SetCash(100000);

        SetBenchmark(x => 0);

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
