using Newtonsoft.Json;

namespace FateWhisper.Models;

/// <summary>
/// FATE 信息模型，对应 fates.json 中的单条记录。
/// </summary>
public class FateInfo
{
    /// <summary>
    /// 多语言名称字典。
    /// </summary>
    [JsonProperty("name")]
    public Dictionary<string, string> Name { get; set; } = [];

    /// <summary>
    /// FATE 等级。
    /// </summary>
    [JsonProperty("level")]
    public int Level { get; set; }

    /// <summary>
    /// 所属版本 patch 编号。
    /// </summary>
    [JsonProperty("patch")]
    public int Patch { get; set; }

    /// <summary>
    /// 所在区域 territory ID。
    /// </summary>
    [JsonProperty("territory")]
    public int Territory { get; set; }

    /// <summary>
    /// 获取中文名称。
    /// </summary>
    [JsonIgnore]
    public string NameChs => Name.TryGetValue("chs", out var v) ? v : (Name.Count > 0 ? Name.Values.First() : "");
}
