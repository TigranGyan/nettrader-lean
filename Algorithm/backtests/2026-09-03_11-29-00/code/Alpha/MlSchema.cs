using Microsoft.ML.Data;

namespace NetTrader.Lean.Algorithm.Alpha;

/// <summary>
/// EXACT copy of nettrader/NetTrader.Infrastructure/MLServices/MLSignalService.cs's MlInput/BinaryMlOutput
/// (field names, order, and the Label/Weight schema fields ML.NET's binary pipeline expects). Do NOT
/// rename or reorder fields here without re-checking against the trained ModelLong.zip/ModelShort.zip —
/// these field names are baked into the model's feature-extraction pipeline at training time.
/// </summary>
public class MlInput
{
    public float Adx_30m { get; set; }
    public float Volatility24h { get; set; }
    public float Trend_30m { get; set; }
    public float Trend_1h { get; set; }
    public float Trend_2h { get; set; }
    public float Rsi_30m { get; set; }
    public float StochRsi_30m { get; set; }
    public float Rsi_1h { get; set; }
    public float Rsi_2h { get; set; }
    public float PositionInRange24h { get; set; }
    public float MacdNorm_30m { get; set; }
    public float AtrPercent_30m { get; set; }
    public float BBandsWidth_30m { get; set; }
    public float VolumeSpike { get; set; }
    public float VwapDist_30m { get; set; }
    public float ConsecCandles_30m { get; set; }
    public float DistEma50_30m { get; set; }
    public float DistEma200_30m { get; set; }
    public float BtcTrend_1h { get; set; }
    public float BtcRsi_1h { get; set; }
    public float BodyPct_30m { get; set; }
    public float UpperWickPct_30m { get; set; }
    public float LowerWickPct_30m { get; set; }
    public float HourOfDay { get; set; }
    public float Trend_4h { get; set; }
    public float Rsi_4h { get; set; }
    public float FearGreedIndex { get; set; }
    public float BtcDominance { get; set; }
    public float IsSqueezing_1h { get; set; }
    public float VolumeRatio_4h { get; set; }
    public float DistEma200_4h { get; set; }

    // Required by the binary pipeline schema — ignored during prediction.
    [ColumnName("Label")] public bool Label { get; set; } = false;
    public float Weight { get; set; } = 1f;
}

public class BinaryMlOutput
{
    [ColumnName("PredictedLabel")] public bool PredictedLabel { get; set; }
    [ColumnName("Probability")] public float Probability { get; set; }
    [ColumnName("Score")] public float Score { get; set; }
}
