using Skender.Stock.Indicators;

namespace NetTrader.Lean.Algorithm.Alpha;

/// <summary>
/// Ported from nettrader/NetTrader.Infrastructure/MLServices/MLSignalService.cs (ComputeFeatures,
/// PrefetchBtcContextAsync, GetLastRsi — original lines ~103-336). The arithmetic below is an EXACT
/// port, deliberately not "improved" or reimplemented against LEAN's native indicator classes: the whole
/// point (see docs/PLAN.md) is that the model was trained against these exact Skender.Stock.Indicators
/// calls over these exact lookback windows, and reimplementing against a different indicator library
/// risks a silent mismatch that would quietly degrade or invalidate the model's real (if modest) edge.
/// Skender.Stock.Indicators is a plain NuGet package — using it here inside a LEAN algorithm needs no
/// bridge, it's the same library, called the same way, just fed candles sourced from LEAN instead of
/// nettrader's IMarketDataProvider.
///
/// This class is intentionally split from the LEAN-specific plumbing (candle history retrieval — see
/// MlSignalAlphaModel's TODOs) so the feature math itself can be reviewed/tested against the original
/// independent of whether the LEAN data wiring is right yet.
///
/// TODO(Phase 0, blocking): not compiled anywhere (no dotnet SDK in the authoring sandbox) — in
/// particular, verify Skender.Stock.Indicators' current API still exposes GetAdx/GetRsi/GetStochRsi/
/// GetMacd/GetAtr/GetEma/GetBollingerBands/GetKeltner with this exact shape (version pinned in the
/// .csproj is a placeholder, not verified against nettrader's actual referenced version — check
/// nettrader's own .csproj for the exact version it uses and match it here for certainty of identical
/// behavior, since even a minor version bump of an indicator library can change warm-up behavior).
/// </summary>
public static class MlFeatureBuilder
{
    /// <summary>
    /// Same signature/logic as MLSignalService.ComputeFeatures, but takes candle lists directly instead
    /// of reading MarketData.KlinesCache — the LEAN-side caller is responsible for keeping q30m/q1h/q2h/q4h
    /// current (at least 210/25/15/20 bars respectively, oldest-first, matching the original's minimums).
    /// btcTrend1h/btcRsi1h/btcDominance are precomputed once per cycle by the caller (see
    /// MLSignalService.PrefetchBtcContextAsync for the original per-cycle-not-per-symbol rationale —
    /// BTC context is shared across all symbols in one Update() call, not recomputed per symbol).
    /// </summary>
    public static MlInput? ComputeFeatures(
        List<Quote> q30m, List<Quote> q1h, List<Quote> q2h, List<Quote> q4h,
        float fearGreedIndex, float btcTrend1h, float btcRsi1h, float btcDominance)
    {
        if (q30m.Count < 210 || q1h.Count < 25 || q2h.Count < 15 || q4h.Count < 20)
        {
            return null;
        }

        // ── ADX(14) on 30m ──
        var adxLast = q30m.GetAdx(14).LastOrDefault(x => x.Adx.HasValue);
        float adx30m = adxLast != null ? (float)adxLast.Adx!.Value : 25f;

        // ── RSI(14) on 30m / 1h / 2h / 4h ──
        float rsi30m = GetLastRsi(q30m);
        float rsi1h = GetLastRsi(q1h);
        float rsi2h = GetLastRsi(q2h);
        float rsi4h = GetLastRsi(q4h);

        // ── StochRSI(14,14,3,1) on 30m ──
        float stochRsi30m = 50f;
        try
        {
            var sr = q30m.GetStochRsi(14, 14, 3, 1).LastOrDefault(x => x.StochRsi.HasValue);
            if (sr != null) stochRsi30m = (float)sr.StochRsi!.Value;
        }
        catch { /* fallback */ }

        var last30m = q30m[^1];
        decimal close30m = (decimal)last30m.Close;

        // ── MACD(12,26,9) histogram on 30m — normalized by price ──
        var macdLast = q30m.GetMacd(12, 26, 9).LastOrDefault(x => x.Histogram.HasValue);
        float macdRaw = macdLast != null ? (float)macdLast.Histogram!.Value : 0f;
        float macdNorm30m = close30m > 0 ? (float)(macdRaw / (double)close30m * 10000.0) : 0f;

        // ── ATR(14) / close × 100 on 30m ──
        var atrLast = q30m.GetAtr(14).LastOrDefault(x => x.Atr.HasValue);
        float atrPct30m = (atrLast != null && close30m > 0)
            ? (float)(atrLast.Atr!.Value / (double)close30m * 100.0) : 1f;

        // ── EMA(50) and EMA(200) distance on 30m ──
        var ema50Last = q30m.GetEma(50).LastOrDefault(x => x.Ema.HasValue);
        var ema200Last = q30m.GetEma(200).LastOrDefault(x => x.Ema.HasValue);
        float distEma50 = (ema50Last != null && close30m > 0)
            ? (float)(((double)close30m - ema50Last.Ema!.Value) / ema50Last.Ema!.Value * 100.0) : 0f;
        float distEma200 = (ema200Last != null && close30m > 0)
            ? (float)(((double)close30m - ema200Last.Ema!.Value) / ema200Last.Ema!.Value * 100.0) : 0f;

        // ── Bollinger Bands Width on 30m ──
        float bbandsWidth = 2f;
        try
        {
            var bb = q30m.GetBollingerBands(20, 2).LastOrDefault(x => x.Sma.HasValue && x.UpperBand.HasValue);
            if (bb != null && bb.Sma!.Value > 0)
                bbandsWidth = (float)((bb.UpperBand!.Value - bb.LowerBand!.Value) / bb.Sma!.Value * 100.0);
        }
        catch { /* fallback */ }

        // ── Volatility24h = (max_high − min_low over last 48 × 30m) / close × 100 ──
        var win48 = q30m.TakeLast(48).ToList();
        decimal maxH = (decimal)win48.Max(x => x.High);
        decimal minL = (decimal)win48.Min(x => x.Low);
        decimal close = close30m;
        float vol24h = close > 0 ? (float)((maxH - minL) / close * 100m) : 2f;

        // ── PositionInRange24h ──
        float posInRange = (maxH - minL) > 0 ? (float)((close - minL) / (maxH - minL)) : 0.5f;

        // ── VolumeSpike = current volume / avg volume(48) ──
        decimal sumVol = 0;
        foreach (var k in win48) sumVol += (decimal)k.Volume;
        decimal avgVol = sumVol / 48;
        float volumeSpike = (avgVol > 0) ? (float)((decimal)last30m.Volume / avgVol) : 1f;

        // ── VWAP Distance (rolling 48 bars) ──
        decimal sumVolPrice = 0, sumVol2 = 0;
        foreach (var k in win48)
        {
            decimal tp = ((decimal)k.High + (decimal)k.Low + (decimal)k.Close) / 3m;
            sumVolPrice += tp * (decimal)k.Volume;
            sumVol2 += (decimal)k.Volume;
        }
        float vwapDist = 0f;
        if (sumVol2 > 0 && close > 0)
        {
            decimal vwap = sumVolPrice / sumVol2;
            vwapDist = (float)((close - vwap) / vwap * 100m);
        }

        // ── ConsecutiveCandles (last bar direction streak) ──
        float consecCandles;
        {
            bool lastGreen = (decimal)last30m.Close > (decimal)last30m.Open;
            int streak = lastGreen ? 1 : -1;
            for (int i = q30m.Count - 2; i >= 0; i--)
            {
                bool green = (decimal)q30m[i].Close > (decimal)q30m[i].Open;
                if (green == lastGreen) streak += lastGreen ? 1 : -1;
                else break;
            }
            consecCandles = streak;
        }

        // ── Trend_30m/1h/2h/4h: 10-period momentum ──
        float trend30m = Trend(q30m);
        float trend1h = Trend(q1h);
        float trend2h = Trend(q2h);
        float trend4h = Trend(q4h);

        // ── Candle body/wick proportions (last 30m bar) ──
        decimal range = (decimal)last30m.High - (decimal)last30m.Low;
        decimal body = Math.Abs((decimal)last30m.Close - (decimal)last30m.Open);
        decimal upperWick = (decimal)last30m.High - Math.Max((decimal)last30m.Open, (decimal)last30m.Close);
        decimal lowerWick = Math.Min((decimal)last30m.Open, (decimal)last30m.Close) - (decimal)last30m.Low;
        float bodyPct = range > 0 ? (float)(body / range) : 0f;
        float upperWickPct = range > 0 ? (float)(upperWick / range) : 0f;
        float lowerWickPct = range > 0 ? (float)(lowerWick / range) : 0f;

        // ── 1H Squeeze (Bollinger vs Keltner) ──
        float isSqueezing1h = 0f;
        try
        {
            var bb1 = q1h.GetBollingerBands(20, 2).LastOrDefault(x => x.UpperBand.HasValue);
            var kc1 = q1h.GetKeltner(20, 1.5).LastOrDefault(x => x.UpperBand.HasValue);
            if (bb1 != null && kc1 != null && bb1.UpperBand < kc1.UpperBand && bb1.LowerBand > kc1.LowerBand)
                isSqueezing1h = 1f;
        }
        catch { /* fallback */ }

        // ── 4H Features (VolumeRatio, DistEma200) ──
        float volRatio4h = 1f;
        float distEma200_4h = 0f;
        try
        {
            if (q4h.Count >= 21)
            {
                var vols4h = q4h.TakeLast(21).ToList();
                decimal avgV = 0;
                for (int k = 0; k < 20; k++) avgV += (decimal)vols4h[k].Volume;
                avgV /= 20m;
                if (avgV > 0) volRatio4h = (float)((decimal)vols4h[^1].Volume / avgV);
            }
            var ema200_4h = q4h.GetEma(200).LastOrDefault(x => x.Ema.HasValue);
            if (ema200_4h != null && (decimal)q4h[^1].Close > 0)
                distEma200_4h = (float)(((double)(decimal)q4h[^1].Close - ema200_4h.Ema!.Value) / ema200_4h.Ema!.Value * 100.0);
        }
        catch { /* fallback */ }

        return new MlInput
        {
            Adx_30m = adx30m,
            Volatility24h = vol24h,
            Trend_30m = trend30m,
            Trend_1h = trend1h,
            Trend_2h = trend2h,
            Rsi_30m = rsi30m,
            StochRsi_30m = stochRsi30m,
            Rsi_1h = rsi1h,
            Rsi_2h = rsi2h,
            PositionInRange24h = posInRange,
            MacdNorm_30m = macdNorm30m,
            AtrPercent_30m = atrPct30m,
            BBandsWidth_30m = bbandsWidth,
            VolumeSpike = volumeSpike,
            VwapDist_30m = vwapDist,
            ConsecCandles_30m = consecCandles,
            DistEma50_30m = distEma50,
            DistEma200_30m = distEma200,
            BtcTrend_1h = btcTrend1h,
            BtcRsi_1h = btcRsi1h,
            BodyPct_30m = bodyPct,
            UpperWickPct_30m = upperWickPct,
            LowerWickPct_30m = lowerWickPct,
            HourOfDay = last30m.Date.Hour, // match training: candle open hour, not wall-clock hour
            Trend_4h = trend4h,
            Rsi_4h = rsi4h,
            FearGreedIndex = fearGreedIndex,
            BtcDominance = btcDominance,
            IsSqueezing_1h = isSqueezing1h,
            VolumeRatio_4h = volRatio4h,
            DistEma200_4h = distEma200_4h,
        };
    }

    private static float Trend(List<Quote> q)
    {
        int n = q.Count;
        if (n < 11 || (decimal)q[n - 11].Close <= 0) return 0f;
        return (float)(((decimal)q[n - 1].Close - (decimal)q[n - 11].Close) / (decimal)q[n - 11].Close * 100m);
    }

    public static float GetLastRsi(List<Quote> quotes)
    {
        try
        {
            var last = quotes.GetRsi(14).LastOrDefault(x => x.Rsi.HasValue);
            return last != null ? (float)last.Rsi!.Value : 50f;
        }
        catch
        {
            return 50f;
        }
    }
}
