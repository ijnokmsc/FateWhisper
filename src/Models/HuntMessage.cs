using Newtonsoft.Json;

namespace FateWhisper.Models;

/// <summary>
/// 猎怪消息模型，对齐 ACT 版 SilverDasher MQTT 协议。
/// JSON 序列化使用缩写字段名，C# 层面提供兼容性属性。
/// </summary>
public class HuntMessage : Message
{
    /// <summary>血量百分比 (100=满血, 0=死亡)</summary>
    [JsonProperty("hp")]
    public int Health { get; set; } = 100;

    // ===== 兼容属性（供 UI/服务层使用） =====

    /// <summary>怪物 ID（字符串形式，兼容旧代码，可读写）</summary>
    [JsonIgnore]
    public string MobId
    {
        get => Id.ToString();
        set
        {
            if (int.TryParse(value, out var id))
                Id = id;
        }
    }

    /// <summary>区域 ID（字符串形式，兼容旧代码）</summary>
    [JsonIgnore]
    public string Territory
    {
        get => Map.ToString();
        set
        {
            if (ushort.TryParse(value, out var tid))
                Map = tid;
        }
    }

    /// <summary>怪物名称（本地存储，不序列化到 MQTT）</summary>
    [JsonIgnore]
    public string? MobName { get; set; }

    /// <summary>猎怪等级 (B/A/S/SS)</summary>
    [JsonIgnore]
    public string? Rank { get; set; }

    /// <summary>大区名称</summary>
    [JsonIgnore]
    public string? Datacenter { get; set; }

    /// <summary>是否跨大区</summary>
    [JsonIgnore]
    public bool IsCrossDc { get; set; }

    /// <summary>是否为本地检测（非 MQTT 接收）</summary>
    [JsonIgnore]
    public bool IsLocal { get; set; }

    public HuntMessage()
    {
        Type = "hunt";
    }
}
