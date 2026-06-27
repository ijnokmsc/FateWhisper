using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FateWhisper.Models;

namespace FateWhisper.Services;

/// <summary>
/// 认证服务，对接 nest.garlandtools.cn/wake 获取 MQTT 会话令牌。
/// 协议：POST application/x-www-form-urlencoded → 返回 16 字符 session 令牌。
/// </summary>
public class AuthService : IDisposable
{
    private readonly IPluginLog _log;
    private const string AuthUrl = "https://nest.garlandtools.cn/wake";
    private const string Prefix = "[FateWhisper]";

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
            var version = DataStore.AuthVersion; // 0x60004 = 393220

            // 纯 C# 实现的 Judge/Seal（不依赖 Weaver.dll）
            var judge = TailorHelper.Judge();
            var seal = string.IsNullOrEmpty(name) || string.IsNullOrEmpty(server)
                ? ""
                : TailorHelper.Seal(server, name);  // ACT 版顺序: server 在前, name 在后

            var judgePreview = judge.Length <= 20 ? judge : judge[..20];
            var sealPreview = seal.Length <= 20 ? seal : seal[..20];
            _log.Information($"{Prefix} Judge={judgePreview} Seal={sealPreview}... #{name} wid={worldId}");

            // 请求体：完整 URL 开头，无 URL 编码 — 与 ACT 版完全一致
            // ACT 源码: encoding.GetBytes(url + "&i=" + Judge() + "&n=" + name + "&s=" + serverID + "&v=" + Seal(server, name) + "&ve=" + Version)
            var body = $"{AuthUrl}&i={judge}&n={name}&s={worldId}&v={seal}&ve={version}";
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await httpClient.PostAsync(AuthUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                _log.Error($"{Prefix} 认证失败: HTTP {response.StatusCode} - {responseBody}");

                // 403 Forbidden — 非网络问题，服务器端拒绝（版本过期/协议变更等）
                if (statusCode == 403)
                {
                    _log.Error($"{Prefix} 认证服务器返回 403，这可能是因为：(1) 版本号 {DataStore.AuthVersion} 已过期，" +
                        "(2) 认证协议已变更。MQTT 将使用默认凭证运行。");
                    CurrentAuth = new AuthResult { IsSuccess = false };
                    return CurrentAuth;
                }

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
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
    }
}
