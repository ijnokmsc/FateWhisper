using System;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FateWhisper.Config;
using FateWhisper.Services;

namespace FateWhisper.UI.Tabs;

/// <summary>
/// 系统状态 Tab，显示 MQTT 连接状态、数据版本、玩家信息，并提供操作按钮。
/// </summary>
public class SystemTab
{
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private readonly DataManager _dataManager;
    private readonly MqttService _mqttService;
    private readonly DutyMonitor _dutyMonitor;
    private readonly NavigationService _navigationService;
    private const string Prefix = "[FateWhisper]";

    public SystemTab(
        PluginConfig config,
        IPluginLog log,
        DataManager dataManager,
        MqttService mqttService,
        DutyMonitor dutyMonitor,
        NavigationService navigationService)
    {
        _config = config;
        _log = log;
        _dataManager = dataManager;
        _mqttService = mqttService;
        _dutyMonitor = dutyMonitor;
        _navigationService = navigationService;
    }

    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "系统状态");
        ImGui.Spacing();

        DrawConnectionStatus();
        ImGui.Spacing();
        DrawDataStatus();
        ImGui.Spacing();
        DrawPlayerInfo();
        ImGui.Spacing();
        DrawCrossServerStatus();
        ImGui.Spacing();
        DrawActionButtons();
    }

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

        ImGui.Spacing();
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  认证服务器已废弃，MQTT 使用默认凭证连接。");
    }

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

    private void DrawPlayerInfo()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f), "玩家信息:");

        var pName = _dutyMonitor.PlayerName ?? _config.CharacterName ?? "?";
        var pWorld = _config.WorldName ?? "?";
        var pWorldId = _dutyMonitor.HomeWorldId > 0 ? _dutyMonitor.HomeWorldId : _config.WorldId;
        ImGui.Text($"  当前玩家: {pName}@{pWorld} (WorldID={pWorldId})");
        ImGui.Text($"  副本中={_dutyMonitor.IsInDuty} | 区域={_dutyMonitor.TerritoryType}");
        ImGui.Text($"  本地检测={(_config.EnableLocalDetection ? "已启用" : "已禁用（纯 MQTT 接收）")}");
    }

    private void DrawCrossServerStatus()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f), "跨服插件状态:");
        var lifeColor = _navigationService.IsLifestreamAvailable
            ? new System.Numerics.Vector4(0.0f, 1.0f, 0.0f, 1.0f)
            : new System.Numerics.Vector4(1.0f, 0.3f, 0.3f, 1.0f);
        ImGui.Text($"  Lifestream: "); ImGui.SameLine();
        ImGui.TextColored(lifeColor, _navigationService.LifestreamStatus);
    }

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

        if (ImGui.Button(_mqttService.IsConnected ? "断开重连" : "连接"))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (_mqttService.IsConnected)
                        await _mqttService.DisconnectAsync();
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
