using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SilverDasher.Services;

/// <summary>
/// P/Invoke 封装 SilverDasher.Weaver.dll 的加密函数。
/// 与 ACT 版 Tailor 类完全对应。
/// </summary>
public static class TailorHelper
{
    [DllImport("SilverDasher.Weaver.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern IntPtr Weave(ref byte a, ref byte b);

    [DllImport("SilverDasher.Weaver.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern IntPtr Seal(ref byte a, ref byte b);

    [DllImport("SilverDasher.Weaver.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, EntryPoint = "Judge")]
    private static extern IntPtr JudgeRaw();

    static TailorHelper()
    {
        // Weaver.dll 在 Dalamud 进程中初始化失败 (0x8007045A)
        // 跳过加载，认证时使用空签名（不影响 MQTT 订阅）
    }

    /// <summary>
    /// 生成机器指纹（Judge）。
    /// 对应 ACT 版 Tailor.Judge()。
    /// </summary>
    public static string Judge()
    {
        var ptr = JudgeRaw();
        return Marshal.PtrToStringAnsi(ptr)?.Trim() ?? "";
    }

    /// <summary>
    /// 对两个字符串进行 Weave 编码。
    /// 对应 ACT 版 Tailor.Weave(a, b)。
    /// </summary>
    public static string Weave(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        var ptr = Weave(ref bytesA[0], ref bytesB[0]);
        return Marshal.PtrToStringAnsi(ptr)?.Trim() ?? "";
    }

    /// <summary>
    /// 生成玩家签名（Seal）。
    /// 对应 ACT 版 Tailor.Seal(serverName, playerName)。
    /// </summary>
    public static string Seal(string serverName, string playerName)
    {
        var bytesA = Encoding.UTF8.GetBytes(serverName);
        var bytesB = Encoding.UTF8.GetBytes(playerName);
        var ptr = Seal(ref bytesA[0], ref bytesB[0]);
        return Marshal.PtrToStringAnsi(ptr)?.Trim() ?? "";
    }
}
