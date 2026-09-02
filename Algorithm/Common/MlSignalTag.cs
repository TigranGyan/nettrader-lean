using System.Text.Json;

namespace NetTrader.Lean.Algorithm.Common;

/// <summary>
/// LEAN's Insight has no native StopLoss/TakeProfit/RewardRisk fields, so this is carried as JSON in
/// Insight.Tag — the idiomatic way to attach extra data to an Insight (see docs/PLAN.md, "ML и Gemini
/// внутри LEAN"). Produced by MlSignalAlphaModel, consumed by FractionalKellyPortfolioConstructionModel.
/// </summary>
public sealed class MlSignalTag
{
    /// <summary>Reward:risk ratio for this specific signal (TP distance / SL distance).</summary>
    public decimal RewardRiskRatio { get; set; }

    public decimal StopLossPrice { get; set; }

    public decimal TakeProfitPrice { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static MlSignalTag? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<MlSignalTag>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
