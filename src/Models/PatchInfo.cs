using Newtonsoft.Json;

namespace FateWhisper.Models;

/// <summary>
/// 版本信息模型，对应 patches.json 中的单条记录。
/// </summary>
public class PatchInfo
{
    /// <summary>
    /// 版本编号。
    /// </summary>
    [JsonProperty("code")]
    public int Code { get; set; }

    /// <summary>
    /// 多语言名称字典。
    /// </summary>
    [JsonProperty("name")]
    public Dictionary<string, string> Name { get; set; } = [];

    /// <summary>
    /// 获取中文版本名。
    /// </summary>
    [JsonIgnore]
    public string NameChs => Name.TryGetValue("chs", out var v) ? v : "";
}
