using Newtonsoft.Json;

namespace SilverDasher.Models;

/// <summary>
/// 猎怪分组模型，对应 sphunts.json 中的分组结构。
/// 支持嵌套子分组和猎怪 ID 列表。
/// </summary>
public class HuntGroup
{
    /// <summary>
    /// 分组标识符。
    /// </summary>
    [JsonProperty("group")]
    public string Group { get; set; } = "";

    /// <summary>
    /// 分组显示名称。
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 子分组列表。
    /// </summary>
    [JsonProperty("subGroups")]
    public List<HuntGroup>? SubGroups { get; set; }

    /// <summary>
    /// 该分组包含的猎怪 ID 列表（对应 hunt.json 中的 key）。
    /// </summary>
    [JsonProperty("items")]
    public List<string>? Items { get; set; }
}
