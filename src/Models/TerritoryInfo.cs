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
}
