using Newtonsoft.Json;

namespace FateWhisper.Models;

/// <summary>
/// 世界/服务器信息模型，对应 worlds.json 中的单条记录。
/// </summary>
public class WorldInfo
{
    /// <summary>
    /// 服务器中文名称。
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// 服务器英文标签。
    /// </summary>
    [JsonProperty("name_label")]
    public string NameLabel { get; set; } = "";

    /// <summary>
    /// 所属大区（中文）。
    /// </summary>
    [JsonProperty("dc")]
    public string Dc { get; set; } = "";

    /// <summary>
    /// 所属大区（英文标签）。
    /// </summary>
    [JsonProperty("dc_label")]
    public string DcLabel { get; set; } = "";
}
