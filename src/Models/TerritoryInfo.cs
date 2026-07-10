using Newtonsoft.Json;

namespace FateWhisper.Models;

/// <summary>
/// 区域/地图信息模型，对应 territories.json 中的单条记录。
/// </summary>
public class TerritoryInfo
{
    /// <summary>
    /// 区域名称（英文）。
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 所属地区/大区域名称。
    /// </summary>
    [JsonProperty("region")]
    public string Region { get; set; } = "";

    /// <summary>
    /// 关联的内容查找器 ID（ContentFinderCondition）。
    /// 对应游戏 TerritoryType 表的 ContentFinderCondition 字段。
    /// 非 0 表示此区域是副本/实例内容（用于副本内静音判定）。
    /// </summary>
    [JsonProperty("content")]
    public int Content { get; set; }
}
