using Newtonsoft.Json;
using SilverDasher.Services;

namespace SilverDasher.Models;

/// <summary>
/// ACT 协议消息基类。
/// 字段名缩写对齐 ACT 版 SilverDasher MQTT 协议。
/// 提供兼容性辅助属性供 UI/服务层使用。
/// </summary>
public abstract class Message
{
    /// <summary>消息类型: "hunt" / "fate"</summary>
    [JsonProperty("t")]
    public string Type { get; set; } = "";

    /// <summary>协议版本号 (0x60004 = 393220)</summary>
    [JsonProperty("v")]
    public int Version => DataStore.AuthVersion;

    /// <summary>怪物/FATE ID (int)</summary>
    [JsonProperty("id")]
    public int Id { get; set; }

    /// <summary>副本实例编号</summary>
    [JsonProperty("i")]
    public uint Instance { get; set; }

    /// <summary>世界 ID (uint)</summary>
    [JsonProperty("w")]
    public uint World { get; set; }

    // ===== 兼容属性 =====

    /// <summary>世界名称（字符串形式，不序列化到 MQTT，由消费者维护）</summary>
    [JsonIgnore]
    public string? WorldName { get; set; }

    /// <summary>区域/地图 territory ID (uint)</summary>
    [JsonProperty("m")]
    public uint Map { get; set; }

    /// <summary>区域名称（字符串形式，兼容旧代码，由消费者维护）</summary>
    [JsonIgnore]
    public string? TerritoryName { get; set; }

    /// <summary>坐标</summary>
    [JsonProperty("c")]
    public Coordinate? Coordinate { get; set; }

    /// <summary>时间戳 (Unix 毫秒)</summary>
    [JsonProperty("ts")]
    public string Timestamp { get; set; } = "";

    protected Message()
    {
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
    }
}
