namespace SilverDasher.Models;

/// <summary>
/// 认证结果模型，与 ACT 版 SilverDasher 协议一致。
/// 认证成功时返回 16 字符 session 令牌（非 JSON）。
/// </summary>
public class AuthResult
{
    /// <summary>
    /// 是否认证成功。
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 16 字符 session 令牌（成功时）。
    /// </summary>
    public string SessionToken { get; set; } = "";

    /// <summary>
    /// 角色名称。
    /// </summary>
    public string PlayerName { get; set; } = "";

    /// <summary>
    /// 所在服务器名称。
    /// </summary>
    public string WorldName { get; set; } = "";

    /// <summary>
    /// 所在服务器 World ID。
    /// </summary>
    public uint WorldId { get; set; }
}
