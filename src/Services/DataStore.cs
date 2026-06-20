using System.IO;

namespace SilverDasher.Services;

/// <summary>
/// SilverDasher 数据常量存储，对齐 ACT 版 DataStorage。
/// </summary>
public static class DataStore
{
    /// <summary>认证版本号 = 0x60004</summary>
    public const int AuthVersion = 0x60004;

    /// <summary>可用 nest 服务器列表</summary>
    private static readonly string[] Nests = ["garlandtools.cn", "silverdasher.com", ""];

    /// <summary>当前选择的 nest 索引</summary>
    public const int SelectedNest = 0;

    /// <summary>认证服务器 URL</summary>
    public static string NestUrl => $"https://nest.{Nests[SelectedNest]}/";

    /// <summary>MQTT WebSocket URL</summary>
    public static string MqttUrl => $"wss://tree.{Nests[SelectedNest]}/mqtt";
}
