using Microsoft.ML;
using NetTrader.Lean.Algorithm.Common;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Data;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Market;
using Skender.Stock.Indicators;

namespace NetTrader.Lean.Algorithm.Alpha;

/// <summary>
/// Generates Insights from the ported ML model (ModelLong.zip/ModelShort.zip, unmodified — see
/// MlFeatureBuilder.cs), gated on both a minimum reward:risk ratio and on the model's probability
/// clearing the reward:risk-implied breakeven threshold. Replaces MLSignalService.Decide()'s absolute
/// confidence floor (0.60) plus an ad-hoc noise-based dynamic gap threshold with the principled
/// p_breakeven = 1/(1+R) gate described in docs/PLAN.md — that ad-hoc formula is deliberately NOT ported,
/// it's exactly the kind of un-auditable heuristic this rewrite exists to replace.
///
/// TODO(Phase 0, blocking): none of the LEAN plumbing below (consolidator registration, AlphaModel base
/// class API) has been compiled — no dotnet SDK in the authoring sandbox. See SymbolCandleCache.cs for
/// the specific resolution/consolidation concern that most likely needs fixing first.
/// </summary>
public sealed class MlSignalAlphaModel : AlphaModel
{
    private readonly TradingOptions _options;
    private readonly TimeSpan _insightPeriod;
    private readonly string _modelsDirectory;

    private readonly Dictionary<Symbol, SymbolCandleCache> _candles = new();
    private PredictionEngine<MlInput, BinaryMlOutput>? _longEngine;
    private PredictionEngine<MlInput, BinaryMlOutput>? _shortEngine;

    private float _fearGreedIndex = 50f;
    private float _btcTrend1h;
    private float _btcRsi1h = 50f;
    private float _btcDominance = 5000f; // soft fallback, matches MLSignalService's own fallback for missing BTCDOMUSDT data

    /// <param name="modelsDirectory">
    /// TODO(Phase 0, blocking): path resolution for ModelLong.zip/ModelShort.zip is UNVERIFIED — LEAN's
    /// working directory differs between Lean CLI backtests, Docker, and cloud deployment. Confirm the
    /// actual working directory at runtime (log it) before trusting a relative path here.
    /// </param>
    public MlSignalAlphaModel(TradingOptions options, TimeSpan insightPeriod, string modelsDirectory = "models")
    {
        _options = options;
        _insightPeriod = insightPeriod;
        _modelsDirectory = modelsDirectory;
    }

    public override void OnSecuritiesChanged(QCAlgorithm algorithm, QuantConnect.Data.UniverseSelection.SecurityChanges changes)
    {
        foreach (var security in changes.AddedSecurities)
        {
            var symbol = security.Symbol;
            if (_candles.ContainsKey(symbol)) continue;

            var cache = new SymbolCandleCache();
            _candles[symbol] = cache;

            // TODO(Phase 0, blocking, see SymbolCandleCache.cs): a TradeBarConsolidator built from an
            // Hour-resolution subscription cannot produce 30m bars — this needs the underlying
            // subscription (in MultiAssetAlgorithm.AddCrypto) to be Resolution.Minute, with these four
            // consolidators (30m/1h/2h/4h) all built off the minute feed. Left as periods of the
            // *target* bar size against whatever resolution is actually subscribed, pending that fix.
            RegisterConsolidator(algorithm, symbol, TimeSpan.FromMinutes(30), cache.OnBar30m);
            RegisterConsolidator(algorithm, symbol, TimeSpan.FromHours(1), cache.OnBar1h);
            RegisterConsolidator(algorithm, symbol, TimeSpan.FromHours(2), cache.OnBar2h);
            RegisterConsolidator(algorithm, symbol, TimeSpan.FromHours(4), cache.OnBar4h);
        }

        foreach (var security in changes.RemovedSecurities)
        {
            _candles.Remove(security.Symbol);
        }
    }

    private static void RegisterConsolidator(QCAlgorithm algorithm, Symbol symbol, TimeSpan period, Action<TradeBar> onBar)
    {
        var consolidator = new TradeBarConsolidator(period);
        consolidator.DataConsolidated += (_, bar) => onBar(bar);
        algorithm.SubscriptionManager.AddConsolidator(symbol, consolidator);
    }

    private void EnsureModelsLoaded()
    {
        if (_longEngine != null && _shortEngine != null) return;

        var mlContext = new MLContext();
        var longPath = Path.Combine(_modelsDirectory, "ModelLong.zip");
        var shortPath = Path.Combine(_modelsDirectory, "ModelShort.zip");

        // Fail closed (no exception bubbled into the algorithm loop): if the model files aren't where
        // expected, this model emits nothing rather than crashing the whole algorithm — matches
        // MLSignalService.EnrichWithSignalsAsync's own `if (_predictionPool == null) return;` guard.
        if (!File.Exists(longPath) || !File.Exists(shortPath))
        {
            return;
        }

        var longModel = mlContext.Model.Load(longPath, out _);
        var shortModel = mlContext.Model.Load(shortPath, out _);
        _longEngine = mlContext.Model.CreatePredictionEngine<MlInput, BinaryMlOutput>(longModel);
        _shortEngine = mlContext.Model.CreatePredictionEngine<MlInput, BinaryMlOutput>(shortModel);
    }

    public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
    {
        EnsureModelsLoaded();
        if (_longEngine is null || _shortEngine is null)
        {
            yield break; // models not found — fail closed, see EnsureModelsLoaded.
        }

        RefreshBtcContext();

        foreach (var symbolStr in SymbolConfig.AllowedSymbols)
        {
            var symbol = algorithm.Securities.Keys.FirstOrDefault(s => s.Value == symbolStr);
            if (symbol is null || !_candles.TryGetValue(symbol, out var cache) || !cache.HasMinimumHistory)
            {
                continue;
            }

            var input = MlFeatureBuilder.ComputeFeatures(
                cache.Q30m, cache.Q1h, cache.Q2h, cache.Q4h,
                _fearGreedIndex, _btcTrend1h, _btcRsi1h, _btcDominance);
            if (input is null) continue;

            float pLong, pShort;
            try
            {
                pLong = _longEngine.Predict(input).Probability;
                pShort = _shortEngine.Predict(input).Probability;
            }
            catch (Exception ex)
            {
                algorithm.Log($"MlSignalAlphaModel: prediction failed for {symbol}: {ex.Message}");
                continue;
            }

            // HOLD if neither direction is dominant enough to bother computing SL/TP for.
            var probHold = 1f - (pLong + pShort);
            if (probHold > 0.5f) continue;

            var direction = pLong > pShort ? InsightDirection.Up : InsightDirection.Down;
            var p = (decimal)Math.Max(pLong, pShort);

            var (rewardRiskRatio, stopLoss, takeProfit) = ComputeStructuralSlTp(algorithm, symbol, direction, cache);
            if (rewardRiskRatio < _options.MinRewardRiskRatio)
            {
                continue; // hard R:R floor — never traded regardless of confidence.
            }

            // p_breakeven = 1/(1+R): minimum win probability at which this R:R breaks even before costs.
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

            yield return Insight.Price(symbol, _insightPeriod, direction, confidence: (double)p, tag: tag);
        }
    }

    private void RefreshBtcContext()
    {
        // TODO(Phase 0): port PrefetchBtcContextAsync properly — needs its own SymbolCandleCache entry
        // for BTCUSDT (added automatically once BTCUSDT is one of the traded symbols, which it is per
        // SymbolConfig.AllowedSymbols) and a BTCDOMUSDT feed for BtcDominance, which LEAN's Binance data
        // source may not carry at all (it's a Binance-specific dominance index instrument, not a regular
        // spot/futures pair) — confirm this before assuming BtcDominance is ever anything but the
        // fallback value below.
        if (_candles.TryGetValue(QuantConnect.Symbol.Create("BTCUSDT", SecurityType.Crypto, Market.Binance), out var btcCache)
            && btcCache.Q1h.Count >= 15)
        {
            var closed1h = btcCache.Q1h;
            _btcRsi1h = MlFeatureBuilder.GetLastRsi(closed1h);
            int n = closed1h.Count;
            _btcTrend1h = n >= 11 && (decimal)closed1h[n - 11].Close > 0
                ? (float)(((decimal)closed1h[n - 1].Close - (decimal)closed1h[n - 11].Close) / (decimal)closed1h[n - 11].Close * 100m)
                : 0f;
        }
        // _fearGreedIndex and _btcDominance: TODO — need a data source wired in (MacroAnalyzer's
        // equivalent doesn't exist yet in this repo); left at neutral/fallback defaults (50 / 5000)
        // until ported, matching MLSignalService's own soft-fallback values.
    }

    /// <summary>
    /// SIMPLIFIED vs. the original: nettrader's GridMathCalculator.CalculateDynamicSlTpByVolumeProfile
    /// builds a 100-bin volume profile and finds high-volume liquidity nodes to place SL/TP against —
    /// that full algorithm was NOT ported here (real effort, not just risk math, and lower priority than
    /// getting the ML signal itself right first). This uses a simpler ATR-scaled distance instead, sized
    /// to the model's own training label (+3% long profit / -2% long loss triple-barrier per
    /// docs/PLAN.md — nettrader's own fallback of 2%/1.5% under-targets that label, which this corrects).
    /// TODO: port the real volume-profile method, or validate in Phase 0 backtest that this simplification
    /// is good enough before trusting it with capital either way.
    /// </summary>
    private static (decimal rewardRiskRatio, decimal stopLoss, decimal takeProfit) ComputeStructuralSlTp(
        QCAlgorithm algorithm, Symbol symbol, InsightDirection direction, SymbolCandleCache cache)
    {
        if (cache.Q30m.Count == 0) return (0m, 0m, 0m);

        var atrLast = cache.Q30m.GetAtr(14).LastOrDefault(x => x.Atr.HasValue);
        var lastClose = (decimal)cache.Q30m[^1].Close;
        if (atrLast?.Atr is null || lastClose <= 0) return (0m, 0m, 0m);

        var atrPercent = (decimal)atrLast.Atr.Value / lastClose;

        // Base target matches the model's training label; ATR scales it for the instrument's current
        // volatility instead of using a fixed percent for every symbol/regime.
        const decimal baseSlPercent = 0.02m; // -2%, matches ModelLong.zip's loss-barrier label
        const decimal baseTpPercent = 0.03m; // +3%, matches ModelLong.zip's profit-barrier label
        var atrScale = Math.Clamp(atrPercent / 0.01m, 0.5m, 2.5m); // normalize against a ~1% ATR baseline

        var slPercent = baseSlPercent * atrScale;
        var tpPercent = baseTpPercent * atrScale;

        var stopLoss = direction == InsightDirection.Up ? lastClose * (1 - slPercent) : lastClose * (1 + slPercent);
        var takeProfit = direction == InsightDirection.Up ? lastClose * (1 + tpPercent) : lastClose * (1 - tpPercent);
        var rewardRiskRatio = slPercent > 0 ? tpPercent / slPercent : 0m;

        return (rewardRiskRatio, stopLoss, takeProfit);
    }
}
