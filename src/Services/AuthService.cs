using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using SilverDasher.Models;

namespace SilverDasher.Services;

/// <summary>
/// 认证服务，对接 nest.garlandtools.cn/wake 获取 MQTT 会话令牌。
/// 协议：POST application/x-www-form-urlencoded → 返回 16 字符 session 令牌。
/// </summary>
public class AuthService : IDisposable
{
    private readonly IPluginLog _log;
    private const string AuthUrl = "https://nest.garlandtools.cn/wake";
    private const string Prefix = "[SilverDasher]";

    /// <summary>
    /// 当前的认证结果。
    /// </summary>
    public AuthResult? CurrentAuth { get; private set; }

    /// <summary>
    /// 是否已认证成功。
    /// </summary>
    public bool IsAuthenticated => CurrentAuth?.IsSuccess == true;

    /// <summary>
    /// 初始化认证服务。
    /// </summary>
    /// <param name="log">日志服务。</param>
    public AuthService(IPluginLog log)
    {
        _log = log;
    }

    /// <summary>
    /// 执行认证请求，获取 MQTT 会话令牌。
    /// 与 ACT 版 SilverDasher 协议一致。
    /// </summary>
    /// <param name="playerName">角色名称</param>
    /// <param name="worldId">所在服务器 World ID</param>
    /// <param name="serverName">服务器名称</param>
    /// <returns>认证结果。</returns>
    public async Task<AuthResult?> AuthenticateAsync(string? playerName = null, uint worldId = 0, string? serverName = null)
    {
        try
        {
            var name = playerName ?? "";
            var server = serverName ?? "";
            var version = 393220; // 与 ACT 版一致

            // 尝试使用 Tailor 签名（如果 Weaver.dll 可用）
            var judge = TryGetJudge();
            var seal = TryGetSeal(name, server);

            // 构造请求体（Weaver.dll 在 Dalamud 不可用，签名留空）
            var body = $"{AuthUrl}&i={judge}&n={name}&s={worldId}&v={seal}&ve={version}";
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

            _log.Information($"{Prefix} 认证 #{name} wid={worldId}");
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await httpClient.PostAsync(AuthUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _log.Error($"{Prefix} 认证失败: HTTP {response.StatusCode} - {responseBody}");
                CurrentAuth = new AuthResult();
                return CurrentAuth;
            }

            var text = responseBody.Trim();

            switch (text)
            {
                case "Banned":
                    _log.Error($"{Prefix} 认证: 已封禁");
                    CurrentAuth = new AuthResult { IsSuccess = false };
                    return CurrentAuth;
                case "Left":
                    _log.Error($"{Prefix} 认证: 永久封禁");
                    CurrentAuth = new AuthResult { IsSuccess = false };
                    return CurrentAuth;
                case "Expired":
                    _log.Error($"{Prefix} 认证: 版本过期 (ve={version})");
                    CurrentAuth = new AuthResult { IsSuccess = false };
                    return CurrentAuth;
            }

            if (text.StartsWith("Blocked"))
            {
                _log.Error($"{Prefix} 认证: 临时封禁 - {text}");
                CurrentAuth = new AuthResult { IsSuccess = false };
                return CurrentAuth;
            }

            // 成功：返回 16 字符 session 令牌
            if (text.Length == 16)
            {
                CurrentAuth = new AuthResult
                {
                    IsSuccess = true,
                    SessionToken = text,
                    PlayerName = name,
                    WorldName = server,
                    WorldId = worldId
                };
                _log.Information($"{Prefix} 认证成功: session={text[..4]}..., player={name}@{server}");
                return CurrentAuth;
            }

            _log.Warning($"{Prefix} 认证返回未知响应 ({text.Length} 字符): {text[..Math.Min(text.Length, 50)]}");
            CurrentAuth = new AuthResult();
            return CurrentAuth;
        }
        catch (TaskCanceledException)
        {
            _log.Error($"{Prefix} 认证超时");
            CurrentAuth = new AuthResult();
            return CurrentAuth;
        }
        catch (HttpRequestException ex)
        {
            _log.Error($"{Prefix} 认证网络异常: {ex.Message}");
            CurrentAuth = new AuthResult();
            return CurrentAuth;
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 认证异常: {ex.Message}");
            CurrentAuth = new AuthResult();
            return CurrentAuth;
        }
    }

    /// <summary>
    /// 使用新的玩家信息重新认证。
    /// </summary>
    public async Task<AuthResult?> ReAuthAsync(string playerName, uint worldId, string serverName)
    {
        _log.Information($"{Prefix} 正在重新认证...");
        return await AuthenticateAsync(playerName, worldId, serverName);
    }

    /// <summary>
    /// 尝试调用 Weaver.dll 获取 Judge 签名（机器指纹）。
    /// 如果 DLL 不可用，返回空字符串。
    /// </summary>
    private static string TryGetJudge()
    {
        try
        {
            return TailorHelper.Judge();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 尝试调用 Weaver.dll 获取 Seal 签名（玩家签名）。
    /// 如果 DLL 不可用，返回空字符串。
    /// </summary>
    private static string TryGetSeal(string playerName, string serverName)
    {
        try
        {
            if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(serverName))
                return "";
            return TailorHelper.Seal(playerName, serverName);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
    }
}
