using Newtonsoft.Json;

namespace SilverDasher.Models;

/// <summary>
/// 猎怪实体模型，对应 hunt.json / hunts.json 中的单条记录。
/// </summary>
public class HuntMob
{
    /// <summary>
    /// 多语言名称字典，键为语言代码（chs/en/ja/de/fr），值为对应名称。
    /// </summary>
    [JsonProperty("name")]
    public Dictionary<string, string> Name { get; set; } = [];

    /// <summary>
    /// 触发方式描述（仅 S 级可能有）。
    /// </summary>
    [JsonProperty("how")]
    public string? How { get; set; }

    /// <summary>
    /// 所属版本 patch 编号。
    /// </summary>
    [JsonProperty("patch")]
    public string Patch { get; set; } = "";

    /// <summary>
    /// 怪物等级。
    /// </summary>
    [JsonProperty("level")]
    public string Level { get; set; } = "";

    /// <summary>
    /// 猎怪等级（B/A/S/SS）。
    /// </summary>
    [JsonProperty("rank")]
    public string Rank { get; set; } = "";

    /// <summary>
    /// 所在区域 territory ID。
    /// </summary>
    [JsonProperty("territory")]
    public string Territory { get; set; } = "";

    /// <summary>
    /// 获取中文名称。
    /// </summary>
    [JsonIgnore]
    public string NameChs => Name.TryGetValue("chs", out var v) ? v : (Name.Count > 0 ? Name.Values.First() : "");
}
