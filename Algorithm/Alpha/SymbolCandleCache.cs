using QuantConnect.Data.Market;
using Skender.Stock.Indicators;

namespace NetTrader.Lean.Algorithm.Alpha;

/// <summary>
/// Holds rolling 30m/1h/2h/4h candle history per symbol as Skender.Stock.Indicators Quote lists, fed by
/// LEAN TradeBarConsolidators. Minimums (210/25/15/20) mirror MLSignalService.ComputeFeatures's own
/// minimums exactly — do not lower them, they're what the model's feature warm-up requires.
///
/// TODO(Phase 0, blocking): consolidator wiring (TradeBarConsolidator + SubscriptionManager.AddConsolidator)
/// is standard LEAN Algorithm Framework practice but UNVERIFIED here — no dotnet SDK in the authoring
/// sandbox to compile/run this against a real data feed. Specifically verify: (1) that AddCrypto's
/// underlying subscription resolution (Resolution.Hour, see MultiAssetAlgorithm) is fine-grained enough
/// to consolidate down to 30m without gaps — Binance hourly bars cannot be split into two 30m bars, so
/// this likely needs a Resolution.Minute subscription for the 30m leg specifically, not Hour; (2) bar
/// timestamp/close-time semantics match "oldest-first" ordering assumed below.
/// </summary>
public sealed class SymbolCandleCache
{
    public List<Quote> Q30m { get; } = new();
    public List<Quote> Q1h { get; } = new();
    public List<Quote> Q2h { get; } = new();
    public List<Quote> Q4h { get; } = new();

    private const int MaxBars30m = 300;
    private const int MaxBars1h = 60;
    private const int MaxBars2h = 40;
    private const int MaxBars4h = 40;

    public bool HasMinimumHistory =>
        Q30m.Count >= 210 && Q1h.Count >= 25 && Q2h.Count >= 15 && Q4h.Count >= 20;

    public void OnBar30m(TradeBar bar) => Append(Q30m, bar, MaxBars30m);
    public void OnBar1h(TradeBar bar) => Append(Q1h, bar, MaxBars1h);
    public void OnBar2h(TradeBar bar) => Append(Q2h, bar, MaxBars2h);
    public void OnBar4h(TradeBar bar) => Append(Q4h, bar, MaxBars4h);

    private static void Append(List<Quote> list, TradeBar bar, int maxLen)
    {
        list.Add(new Quote
        {
            Date = bar.Time,
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Volume = bar.Volume,
        });
        if (list.Count > maxLen)
        {
            list.RemoveAt(0);
        }
    }
}
