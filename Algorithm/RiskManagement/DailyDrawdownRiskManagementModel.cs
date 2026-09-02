using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;

namespace NetTrader.Lean.Algorithm.RiskManagement;

/// <summary>
/// Ports nettrader's TradingOptions.DailyDrawdownPausePercent / EmergencyStopBalancePercent concept:
/// pause new entries past a daily-loss threshold, force-flat everything past an emergency threshold.
/// LEAN ships MaximumDrawdownPercentPortfolio for the "liquidate everything" case; this model adds the
/// softer "pause new entries" behavior nettrader had, which has no built-in LEAN equivalent.
///
/// TODO(Phase 0, blocking): verify against the pinned LEAN version's IRiskManagementModel signature and
/// Portfolio.TotalPortfolioValue semantics — not compiled in the authoring sandbox (no dotnet SDK there).
/// TODO: this tracks the day's starting equity in memory; confirm this survives LEAN's own day-rollover
/// and live-deployment restart semantics before relying on it, rather than assuming it does.
/// </summary>
public sealed class DailyDrawdownRiskManagementModel : IRiskManagementModel
{
    private readonly decimal _dailyDrawdownPausePercent;
    private readonly decimal _emergencyStopBalancePercent;
    private DateTime _currentDay;
    private decimal _dayStartValue;
    private readonly decimal _initialCapital;

    public DailyDrawdownRiskManagementModel(decimal dailyDrawdownPausePercent, decimal emergencyStopBalancePercent, decimal initialCapital)
    {
        _dailyDrawdownPausePercent = dailyDrawdownPausePercent;
        _emergencyStopBalancePercent = emergencyStopBalancePercent;
        _initialCapital = initialCapital;
    }

    public IEnumerable<IPortfolioTarget> ManageRisk(QCAlgorithm algorithm, IPortfolioTarget[] targets)
    {
        var now = algorithm.Time.Date;
        var currentValue = algorithm.Portfolio.TotalPortfolioValue;

        if (_currentDay != now)
        {
            _currentDay = now;
            _dayStartValue = currentValue;
        }

        // Emergency stop: balance has fallen below a fraction of the account's initial capital —
        // liquidate everything, do not merely pause. Mirrors nettrader's EmergencyStopBalancePercent.
        if (_initialCapital > 0 && currentValue < _initialCapital * _emergencyStopBalancePercent)
        {
            algorithm.Log($"DailyDrawdownRiskManagementModel: EMERGENCY STOP, equity {currentValue:C} below {_emergencyStopBalancePercent:P0} of initial capital {_initialCapital:C}");
            return algorithm.Portfolio.Values
                .Where(h => h.Invested)
                .Select(h => (IPortfolioTarget)QuantConnect.Algorithm.Framework.Portfolio.PortfolioTarget.Percent(algorithm, h.Symbol, 0m));
        }

        // Daily drawdown pause: today's loss exceeds the configured threshold — pause new entries,
        // but do not force-close existing positions (they may still be within their own planned SL/TP).
        if (_dayStartValue > 0)
        {
            var dailyLossPercent = (_dayStartValue - currentValue) / _dayStartValue * 100m;
            if (dailyLossPercent >= _dailyDrawdownPausePercent)
            {
                algorithm.Log($"DailyDrawdownRiskManagementModel: pausing new entries, daily loss {dailyLossPercent:F1}% >= threshold {_dailyDrawdownPausePercent:F1}%");
                return targets.Where(t => algorithm.Portfolio[t.Symbol].Invested && Math.Abs(t.Quantity) <= Math.Abs(algorithm.Portfolio[t.Symbol].Quantity));
            }
        }

        return targets;
    }
}
