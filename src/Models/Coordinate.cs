using Newtonsoft.Json;

namespace FateWhisper.Models;

/// <summary>
/// 坐标模型，与 ACT 版 SilverDasher 协议一致。
/// 游戏坐标 (float) → 整数传输: x_int = (gameCoord * 0.02 + 21.5) * 100
/// 显示: displayX = x_int / 100
///
/// 轴映射：
///   X → 游戏 X（东-西）
///   Y → 游戏 Z（北-南）
/// 注意：不包含游戏 Y（高度），传给 vnavmesh 时需要查询网格地板高度。
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
