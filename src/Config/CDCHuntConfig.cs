using System;
using Newtonsoft.Json;

namespace FateWhisper.Config;

/// <summary>
/// 跨大区猎怪订阅配置。
/// </summary>
[Serializable]
public class CDCHuntConfig
{
    /// <summary>
    /// 是否启用跨大区猎怪播报。
    /// </summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 是否订阅 B 级猎怪。
    /// </summary>
    [JsonProperty("rank_b")]
    public bool RankB { get; set; } = false;

    /// <summary>
    /// 是否订阅 A 级猎怪。
    /// </summary>
    [JsonProperty("rank_a")]
    public bool RankA { get; set; } = false;

    /// <summary>
    /// 是否订阅 S 级猎怪。
    /// </summary>
    [JsonProperty("rank_s")]
    public bool RankS { get; set; } = true;

    /// <summary>
    /// 是否订阅 SS 级猎怪。
    /// </summary>
    [JsonProperty("rank_ss")]
    public bool RankSS { get; set; } = true;

    /// <summary>
    /// 检查指定等级的猎怪是否应该播报。
    /// </summary>
    public bool IsRankEnabled(string rank)
    {
        if (!Enabled) return false;
        return rank.ToUpperInvariant() switch
        {
            "B" => RankB,
            "A" => RankA,
            "S" => RankS,
            "SS" => RankSS,
            _ => false
        };
    }
}
