using Newtonsoft.Json;

namespace SilverDasher.Models;

/// <summary>
/// 坐标模型，与 ACT 版 SilverDasher 协议一致。
/// 游戏坐标 (float) → 整数传输: x_int = (gameX * 0.02 + 21.5) * 100
/// 显示: displayX = x_int / 100
/// </summary>
public class Coordinate
{
    [JsonProperty("x")]
    public int X { get; set; }

    [JsonProperty("y")]
    public int Y { get; set; }

    [JsonIgnore]
    public string DisplayX => (X / 100f).ToString("0.##");

    [JsonIgnore]
    public string DisplayY => (Y / 100f).ToString("0.##");

    public override string ToString() => $"({DisplayX}, {DisplayY})";
}
