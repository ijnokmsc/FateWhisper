using Newtonsoft.Json;

namespace SilverDasher.Models;

/// <summary>
/// FATE 播报消息模型，用于 MQTT 传输和本地通知。
/// </summary>
public class FateMessage
{
    [JsonProperty("fate_id")]
    public string FateId { get; set; } = "";

    [JsonProperty("fate_name")]
    public string FateName { get; set; } = "";

    [JsonProperty("world")]
    public string World { get; set; } = "";

    [JsonProperty("territory")]
    public string Territory { get; set; } = "";

    [JsonProperty("territory_name")]
    public string TerritoryName { get; set; } = "";

    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("datacenter")]
    public string Datacenter { get; set; } = "";

    [JsonProperty("is_special")]
    public bool IsSpecial { get; set; }

    [JsonProperty("event_type")]
    public string EventType { get; set; } = "start";

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
        var specialTag = IsSpecial ? "[特殊]" : "";
        return $"{specialTag}FATE: {FateName} @ {TerritoryName}({World}) {TimeString}";
    }
}
