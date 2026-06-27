using Newtonsoft.Json;

namespace FateWhisper.Models;

/// <summary>
/// FATE 消息模型，对齐 ACT 版 SilverDasher MQTT 协议。
/// JSON 序列化使用缩写字段名，C# 层面提供兼容性属性。
/// </summary>
public class FateMessage : Message
{
    /// <summary>FATE 进度 (0~100)</summary>
    [JsonProperty("p")]
    public int Progress { get; set; }

    /// <summary>剩余时间字符串</summary>
    [JsonProperty("lt")]
    public string? LeftTime { get; set; }

    // ===== 兼容属性（供 UI/服务层使用） =====

    /// <summary>FATE ID（字符串形式）</summary>
    [JsonIgnore]
    public string FateId => Id.ToString();

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

    /// <summary>FATE 名称（本地存储）</summary>
    [JsonIgnore]
    public string? FateName { get; set; }

    /// <summary>FATE 类型: "common" / "special"</summary>
    [JsonIgnore]
    public string? Type_ { get; set; }

    /// <summary>大区名称</summary>
    [JsonIgnore]
    public string? Datacenter { get; set; }

    /// <summary>是否为跨大区消息</summary>
    [JsonIgnore]
    public bool IsCrossDc { get; set; }

    /// <summary>是否为特殊 FATE</summary>
    [JsonIgnore]
    public bool IsSpecial { get; set; }

    /// <summary>是否为本地检测（非 MQTT 接收）</summary>
    [JsonIgnore]
    public bool IsLocal { get; set; }

    /// <summary>事件类型: "start" / "end" / "progress"</summary>
    [JsonIgnore]
    public string? EventType { get; set; }

    public FateMessage()
    {
        Type = "fate";
    }
}
