using Newtonsoft.Json;

namespace SilverDasher.Models;

/// <summary>
/// 猎怪播报消息模型，用于 MQTT 传输和本地通知。
/// </summary>
public class HuntMessage
{
    [JsonProperty("mob_id")]
    public string MobId { get; set; } = "";

    [JsonProperty("mob_name")]
    public string MobName { get; set; } = "";

    [JsonProperty("world")]
    public string World { get; set; } = "";

    [JsonProperty("territory")]
    public string Territory { get; set; } = "";

    [JsonProperty("territory_name")]
    public string TerritoryName { get; set; } = "";

    [JsonProperty("rank")]
    public string Rank { get; set; } = "";

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("datacenter")]
    public string Datacenter { get; set; } = "";

    [JsonProperty("is_cross_dc")]
    public bool IsCrossDc { get; set; }

    [JsonProperty("instance")]
    public int Instance { get; set; }

    /// <summary>
    /// 是否为本地检测（非 MQTT 接收）。
    /// </summary>
    [JsonIgnore]
    public bool IsLocal { get; set; }

    /// <summary>
    /// 获取格式化的时间字符串。
    /// </summary>
    public string TimeString => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).ToLocalTime().ToString("HH:mm:ss");

    public override string ToString()
    {
        var crossTag = IsCrossDc ? "[跨大区]" : "";
        return $"{crossTag}[{Rank}] {MobName} @ {TerritoryName}({World}) {TimeString}";
    }
}
