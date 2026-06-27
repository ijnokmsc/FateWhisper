using Newtonsoft.Json;

namespace FateWhisper.Models;

/// <summary>
/// 导航测试保存的地点，包含服务器、区域和坐标信息。
/// 持久化到独立文件 FateWhisper_navtest.json。
/// </summary>
public class NavTestLocation
{
    [JsonProperty("name")]
    public string Name { get; set; } = "";

    [JsonProperty("world")]
    public string WorldName { get; set; } = "";

    [JsonProperty("territory")]
    public uint Territory { get; set; }

    [JsonProperty("x")]
    public float X { get; set; }

    [JsonProperty("y")]
    public float Y { get; set; }

    [JsonProperty("z")]
    public float Z { get; set; }
}
