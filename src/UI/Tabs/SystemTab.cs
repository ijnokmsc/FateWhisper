using System;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using SilverDasher.Config;
using SilverDasher.Services;

namespace SilverDasher.UI.Tabs;

/// <summary>
/// 系统状态 Tab，显示 MQTT 连接状态、认证状态、数据版本，并提供操作按钮。
/// </summary>
public class SystemTab
{
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private readonly DataManager _dataManager;
    private readonly AuthService _authService;
    private readonly MqttService _mqttService;
    private readonly DutyMonitor _dutyMonitor;
    private const string Prefix = "[SilverDasher]";

    public SystemTab(
        PluginConfig config,
        IPluginLog log,
        DataManager dataManager,
        AuthService authService,
        MqttService mqttService,
        DutyMonitor dutyMonitor)
    {
        _config = config;
        _log = log;
        _dataManager = dataManager;
        _authService = authService;
        _mqttService = mqttService;
        _dutyMonitor = dutyMonitor;
    }

    /// <summary>
    /// 绘制系统状态界面。
    /// </summary>
    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "系统状态");
        ImGui.Spacing();

        DrawConnectionStatus();
        ImGui.Spacing();
        DrawAuthStatus();
        ImGui.Spacing();
        DrawDataStatus();
        ImGui.Spacing();
        DrawPlayerInfo();
        ImGui.Spacing();
        DrawActionButtons();
    }

    /// <summary>
    /// 绘制 MQTT 连接状态。
    /// </summary>
    private void DrawConnectionStatus()
    {
        var isConnected = _mqttService.IsConnected;
        var color = isConnected
            ? new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f)
            : new System.Numerics.Vector4(1.0f, 0.3f, 0.3f, 1.0f);
        var status = isConnected ? "已连接" : "未连接";

        ImGui.Text("MQTT 状态: ");
        ImGui.SameLine();
        ImGui.TextColored(color, status);
        ImGui.SameLine();
        ImGui.Text($"| Broker: wss://tree.garlandtools.cn/mqtt");
    }

    /// <summary>
    /// 绘制认证状态。
    /// </summary>
    private void DrawAuthStatus()
    {
        var isAuth = _authService.IsAuthenticated;
        var color = isAuth
            ? new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f)
            : new System.Numerics.Vector4(1.0f, 0.6f, 0.2f, 1.0f);
        var status = isAuth ? "已认证" : "未认证";

        ImGui.Text("认证状态: ");
        ImGui.SameLine();
        ImGui.TextColored(color, status);

        if (isAuth && _authService.CurrentAuth is not null)
        {
            ImGui.SameLine();
            ImGui.Text($"| 角色: {_authService.CurrentAuth.PlayerName}@{_authService.CurrentAuth.WorldName}");
        }
    }

    /// <summary>
    /// 绘制数据版本信息。
    /// </summary>
    private void DrawDataStatus()
    {
        ImGui.Text("静态数据: ");
        ImGui.SameLine();

        var parts = new System.Collections.Generic.List<string>();
        foreach (var (key, version) in _dataManager.LocalVersions)
        {
            parts.Add($"{key}=v{version}");
        }

        ImGui.Text(string.Join(", ", parts));
    }

    /// <summary>
    /// 绘制玩家信息及配置输入。
    /// </summary>
    private void DrawPlayerInfo()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f), "玩家信息（认证必需）:");
        ImGui.Spacing();

        // 角色名称
        var charName = _config.CharacterName ?? "";
        ImGui.Text("角色名: ");
        ImGui.SameLine();
        if (ImGui.InputText("##charName", ref charName, 64))
        {
            _config.CharacterName = charName;
            _config.Save();
        }

        // World ID
        var worldIdStr = _config.WorldId.ToString();
        ImGui.Text("世界 ID: ");
        ImGui.SameLine();
        if (ImGui.InputText("##worldId", ref worldIdStr, 10, ImGuiInputTextFlags.CharsDecimal))
        {
            if (uint.TryParse(worldIdStr, out var wid))
            {
                _config.WorldId = wid;
                _config.Save();
            }
        }

        // 服务器名称
        var worldName = _config.WorldName ?? "";
        ImGui.Text("服务器名:");
        ImGui.SameLine();
        if (ImGui.InputText("##worldName", ref worldName, 32))
        {
            _config.WorldName = worldName;
            _config.Save();
        }

        ImGui.Spacing();
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "World ID 可从 garlandtools.cn 查得（如拉诺西亚=26、幻影群岛=27）");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 当前检测到的玩家信息
        var pName = _dutyMonitor.PlayerName ?? _config.CharacterName ?? "";
        var pWorld = _config.WorldName ?? "";
        var pWorldId = _dutyMonitor.HomeWorldId > 0 ? _dutyMonitor.HomeWorldId : _config.WorldId;
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 1.0f, 0.6f, 1.0f),
            $"当前玩家: {pName}@{pWorld} (WorldID={pWorldId})");
        ImGui.SameLine();
        ImGui.Text($"| 副本中={_dutyMonitor.IsInDuty}, 区域={_dutyMonitor.TerritoryType}");
    }

    /// <summary>
    /// 绘制操作按钮。
    /// </summary>
    private void DrawActionButtons()
    {
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("检查数据更新"))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _dataManager.CheckRemoteUpdatesAsync();
                    _log.Information($"{Prefix} 用户手动检查数据更新完成");
                }
                catch (Exception ex)
                {
                    _log.Error($"{Prefix} 手动检查更新失败: {ex.Message}");
                }
            });
        }

        ImGui.SameLine();

        if (ImGui.Button("重新认证"))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _authService.ReAuthAsync(
                        _dutyMonitor.PlayerName ?? "",
                        _dutyMonitor.HomeWorldId,
                        _config.WorldName ?? "");
                    if (result?.IsSuccess == true)
                    {
                        _config.AuthToken = result.SessionToken;
                        _config.CharacterName = result.PlayerName;
                        _config.WorldName = result.WorldName;
                        _config.Save();
                        _log.Information($"{Prefix} 重新认证成功");
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"{Prefix} 重新认证失败: {ex.Message}");
                }
            });
        }

        ImGui.SameLine();

        if (ImGui.Button(_mqttService.IsConnected ? "断开重连" : "连接"))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_mqttService.IsConnected)
                    {
                        await _mqttService.DisconnectAsync();
                    }

                    await _mqttService.ConnectAsync();
                }
                catch (Exception ex)
                {
                    _log.Error($"{Prefix} 连接操作失败: {ex.Message}");
                }
            });
        }
    }
}
