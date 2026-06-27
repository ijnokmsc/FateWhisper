using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FateWhisper.Services;

/// <summary>
/// 纯 C# 实现 Judge/Seal/Weave，不依赖 Weaver.dll。
/// 逆向自 SilverDasher.Weaver.dll (IDA Pro 分析)。
/// </summary>
public static class TailorHelper
{
    // ===== Windows API 常量 =====
    // 用 0 而非 GENERIC_READ — 非 admin 进程也能打开物理驱动器做查询
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    // IOCTL_STORAGE_QUERY_PROPERTY = CTL_CODE(0x0000002D, 0x0500, METHOD_BUFFERED, FILE_ANY_ACCESS)
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    // ===== P/Invoke (kernel32.dll — 系统标准 DLL，任何进程都能加载) =====

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize,
        IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    // ===== 结构体 =====

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public int PropertyId;   // 0 = StorageDeviceProperty
        public int QueryType;    // 0 = PropertyStandardQuery
        public int AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_DEVICE_DESCRIPTOR
    {
        public uint Version;
        public uint Size;
        public byte DeviceType;
        public byte DeviceTypeModifier;
        public byte RemovableMedia;
        public byte CommandQueueing;
        public uint VendorIdOffset;
        public uint ProductIdOffset;
        public uint ProductRevisionOffset;
        public uint SerialNumberOffset;
        public int BusType;           // STORAGE_BUS_TYPE = enum (4 bytes)
        public uint RawPropertiesLength;
    }

    // ===== 公共方法 =====

    /// <summary>
    /// 生成机器指纹（Judge）。
    /// 读取物理硬盘序列号，与 Weaver.dll 原始实现一致。
    /// 三级 fallback：IOCTL(0-access) → PowerShell WMI → wmic 命令行。
    /// </summary>
    public static string Judge()
    {
        // 方法1: CreateFileW + DeviceIoControl（0 access，非 admin 可用）
        for (int i = 0; i < 4; i++)
        {
            var path = $"\\\\.\\PhysicalDrive{i}";
            // dwDesiredAccess=0 — 只查询不读写，非 admin 进程也能打开
            var handle = CreateFileW(path, 0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle == INVALID_HANDLE_VALUE || handle == IntPtr.Zero)
                continue;

            try
            {
                var serial = GetStorageSerialNumber(handle);
                if (!string.IsNullOrWhiteSpace(serial))
                    return serial.Trim();
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        // 方法2: PowerShell Get-PhysicalDisk（不需要 admin）
        var psSerial = GetSerialViaPowerShell();
        if (!string.IsNullOrWhiteSpace(psSerial) && psSerial != "failed")
            return psSerial.Trim();

        // 方法3: wmic 命令行（兼容旧版 Windows）
        var wmicSerial = GetSerialViaWmic();
        if (!string.IsNullOrWhiteSpace(wmicSerial) && wmicSerial != "failed")
            return wmicSerial.Trim();

        return "failed";
    }

    /// <summary>
    /// 生成玩家签名（Seal）。
    /// 与 Weaver.dll 原始实现完全一致（IDA Pro + ILSpy 双重验证）：
    /// 1. 三路字节混合（模数索引）: result[i] = (a[i%lenA] + b[i%lenB] + judge[i%lenJ]) / 3
    /// 2. 若结果为 0，替换为 0xCA(202) 防止 null 终止符
    /// 3. Base64 编码混合后的字节
    /// 4. Trim 并过滤 '/' 和 '\\' 字符
    /// </summary>
    public static string Seal(string a, string b)
    {
        var judge = Judge();
        var judgeStr = string.IsNullOrEmpty(judge) ? "failed" : judge;

        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        var bytesJ = Encoding.UTF8.GetBytes(judgeStr);

        // 防止空字符串导致模数除零
        if (bytesA.Length == 0) bytesA = new byte[] { 0 };
        if (bytesB.Length == 0) bytesB = new byte[] { 0 };
        if (bytesJ.Length == 0) bytesJ = new byte[] { 0 };

        int maxLen = Math.Max(bytesA.Length, Math.Max(bytesB.Length, bytesJ.Length));
        var result = new byte[maxLen];

        for (int i = 0; i < maxLen; i++)
        {
            // 模数索引 — 与 Weaver.dll 一致 (v9 % v3, v9 % v4, v9 % lenJ)
            int va = bytesA[i % bytesA.Length];
            int vb = bytesB[i % bytesB.Length];
            int vj = bytesJ[i % bytesJ.Length];

            int val = (va + vb + vj) / 3; // 整数除法（截断）
            if (val == 0) val = 0xCA;     // 202 — 防止 null 终止符
            result[i] = (byte)val;
        }

        // Base64 编码 — 与 Weaver.dll 内部 Base64Encode 步骤一致
        // 标准 Base64: A-Za-z0-9+/ 带 = 填充
        var text = Convert.ToBase64String(result).Trim();

        // 过滤 '/' 和 '\\' 字符 — 与 Weaver.dll 一致
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch != '/' && ch != '\\')
                sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Weave 编码（认证流程未使用，保留完整性）。
    /// 拼接 b+Judge，逐字节加 a[i%lenA]，超 127 加 1，然后前后交织。
    /// </summary>
    public static string Weave(string a, string b)
    {
        var judge = Judge();
        var judgeStr = string.IsNullOrEmpty(judge) ? "failed" : judge;
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        var bytesJ = Encoding.UTF8.GetBytes(judgeStr);

        // 拼接 b + judge
        var concat = new byte[bytesB.Length + bytesJ.Length];
        Array.Copy(bytesB, 0, concat, 0, bytesB.Length);
        Array.Copy(bytesJ, 0, concat, bytesB.Length, bytesJ.Length);

        // result[i] = concat[i] + a[i % lenA]; 若 sum > 127 则 +1
        var result = new byte[concat.Length];
        for (int i = 0; i < concat.Length; i++)
        {
            int val = concat[i] + bytesA[i % bytesA.Length];
            if (val > 127) val += 1;
            result[i] = (byte)(val & 0xFF);
        }

        // 前后交织: front[0], back[-1], front[1], back[-2]...
        var interleaved = new byte[result.Length];
        int front = 0, back = result.Length - 1;
        for (int i = 0; i < result.Length; i++)
        {
            interleaved[i] = (i % 2 == 0) ? result[front++] : result[back--];
        }

        return Encoding.Latin1.GetString(interleaved).Trim();
    }

    // ===== 私有方法 =====

    /// <summary>
    /// 通过 IOCTL_STORAGE_QUERY_PROPERTY 读取硬盘序列号。
    /// </summary>
    private static string GetStorageSerialNumber(IntPtr handle)
    {
        var query = new STORAGE_PROPERTY_QUERY
        {
            PropertyId = 0, // StorageDeviceProperty
            QueryType = 0   // PropertyStandardQuery
        };

        int querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
        IntPtr inBuffer = Marshal.AllocHGlobal(querySize);
        IntPtr outBuffer = Marshal.AllocHGlobal(4096);

        try
        {
            Marshal.StructureToPtr(query, inBuffer, false);

            if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                inBuffer, (uint)querySize, outBuffer, 4096, out _, IntPtr.Zero))
                return "";

            var desc = Marshal.PtrToStructure<STORAGE_DEVICE_DESCRIPTOR>(outBuffer);
            if (desc.SerialNumberOffset == 0)
                return "";

            // 序列号是 ANSI 字符串，偏移量相对于输出缓冲区
            IntPtr serialPtr = IntPtr.Add(outBuffer, (int)desc.SerialNumberOffset);
            return Marshal.PtrToStringAnsi(serialPtr) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(inBuffer);
            Marshal.FreeHGlobal(outBuffer);
        }
    }

    /// <summary>
    /// 通过 PowerShell Get-PhysicalDisk 读取硬盘序列号（不需要 admin 权限）。
    /// </summary>
    private static string GetSerialViaPowerShell()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"(Get-PhysicalDisk | Select-Object -First 1 -ExpandProperty SerialNumber)\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "";
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            return output;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 通过 wmic 命令行读取硬盘序列号（兼容旧版 Windows）。
    /// </summary>
    private static string GetSerialViaWmic()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wmic",
                Arguments = "diskdrive get SerialNumber",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return "";
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            // wmic 输出格式: "SerialNumber\n WD-XXXXX\n"
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length >= 2)
                return lines[1].Trim();
            return "";
        }
        catch
        {
            return "";
        }
    }
}
