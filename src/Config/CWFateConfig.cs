using System;
using Newtonsoft.Json;

namespace FateWhisper.Config;

/// <summary>
/// 同大区 FATE 订阅配置。
/// </summary>
[Serializable]
public class CWFateConfig
{
    /// <summary>
    /// 是否启用同大区 FATE 播报。
    /// </summary>
    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否订阅普通 FATE。
    /// </summary>
    [JsonProperty("common")]
    public bool Common { get; set; } = false;

    /// <summary>
    /// 是否订阅特殊 FATE。
    /// </summary>
    [JsonProperty("special")]
    public bool Special { get; set; } = true;

    /// <summary>
    /// 区域过滤白名单（空表示所有区域）。
    /// </summary>
    [JsonProperty("territory_filter")]
    public HashSet<string> TerritoryFilter { get; set; } = [];
}
