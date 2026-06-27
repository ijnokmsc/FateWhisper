using System;
using Newtonsoft.Json;

namespace FateWhisper.Config;

/// <summary>
/// 同大区猎怪订阅配置。
/// </summary>
[Serializable]
public class CWHuntConfig
{
    /// <summary>
    /// 是否启用同大区猎怪播报。
    /// </summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否订阅 B 级猎怪。
    /// </summary>
    [JsonProperty("rank_b")]
    public bool RankB { get; set; } = false;

    /// <summary>
    /// 是否订阅 A 级猎怪。
    /// </summary>
    [JsonProperty("rank_a")]
    public bool RankA { get; set; } = true;

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
    /// 区域过滤白名单（空表示所有区域）。
    /// </summary>
    [JsonProperty("territory_filter")]
    public HashSet<string> TerritoryFilter { get; set; } = [];

    /// <summary>
    /// 检查指定等级的猎怪是否应该播报。
    /// </summary>
    public bool IsRankEnabled(string rank)
    {
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
