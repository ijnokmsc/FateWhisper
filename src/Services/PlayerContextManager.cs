using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Plugin.Services;
using FateWhisper.Config;

namespace FateWhisper.Services;

/// <summary>
/// 玩家上下文管理器 — 统一负责玩家信息同步和 MQTT 连接生命周期。
/// 消除原 Plugin.cs 中 3 处重复的玩家信息读取逻辑（构造函数 / OnZoneInit / TryConnectMqttIfLoggedIn）。
/// </summary>
internal sealed class PlayerContextManager : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly DataManager _dataManager;
    private readonly PluginConfig _config;
    private readonly MqttService _mqttService;
    private readonly NavigationService _navigationService;
    private readonly DutyMonitor _dutyMonitor;
    private readonly MobScannerService _mobScannerService;

    private bool _mqttLoggedIn;
    private bool _disposed;

    private const string Prefix = "[FateWhisper]";

    public PlayerContextManager(
        IPluginLog log,
        IClientState clientState,
        IObjectTable objectTable,
        DataManager dataManager,
        PluginConfig config,
        MqttService mqttService,
        NavigationService navigationService,
        DutyMonitor dutyMonitor,
        MobScannerService mobScannerService)
    {
        _log = log;
        _clientState = clientState;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _config = config;
        _mqttService = mqttService;
        _navigationService = navigationService;
        _dutyMonitor = dutyMonitor;
        _mobScannerService = mobScannerService;
    }

    /// <summary>
    /// 快速读取玩家当前世界名（不保存配置、不连接 MQTT）。
    /// 在服务创建后立即调用，确保导航服务有正确的世界名。
    /// </summary>
    public void QuickSyncWorldName()
    {
        try
        {
            if (_clientState.IsLoggedIn && _objectTable.LocalPlayer is { } lp)
            {
                var cw = lp.CurrentWorld.ValueNullable;
                var worldName = cw?.Name.ToString();
                if (!string.IsNullOrEmpty(worldName))
                {
                    _navigationService.PlayerWorldName = worldName;
                    _config.WorldName = worldName;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"{Prefix} 读取玩家世界失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 订阅 ClientState 事件并尝试初始 MQTT 连接。
    /// </summary>
    public void Initialize()
    {
        _clientState.ZoneInit += OnZoneInit;
        _clientState.Login += OnLogin;
        _clientState.Logout += OnLogout;

        // 插件重载等场景，Login 事件不会触发，需主动检测
        TryConnectMqttIfLoggedIn();
    }

    /// <summary>
    /// 从游戏读取玩家信息并同步到配置和所有服务。
    /// 合并原 OnZoneInit / TryConnectMqttIfLoggedIn 中重复的同步逻辑。
    /// </summary>
    /// <returns>同步结果，或 null 如果玩家未登录。</returns>
    private PlayerSyncResult? ReadAndSyncPlayerInfo()
    {
        if (!_clientState.IsLoggedIn || _objectTable.LocalPlayer is not { } lp)
            return null;

        var homeWorldId = lp.HomeWorld.RowId;
        var currentWorld = lp.CurrentWorld.ValueNullable;
        var currentWorldName = currentWorld?.Name.ToString();
        var playerName = lp.Name.TextValue;

        var oldWorldId = _config.WorldId;
        var worldChanged = oldWorldId != 0 && oldWorldId != homeWorldId;

        _config.CharacterName = playerName;
        _config.WorldId = homeWorldId;
        if (currentWorldName != null)
        {
            _config.WorldName = currentWorldName;
            _navigationService.PlayerWorldName = currentWorldName;
        }
        _config.Datacenter = _dataManager.LookupDatacenter(homeWorldId.ToString()) ?? "";
        _config.Save();

        var dcLabel = _dataManager.LookupDcLabel(homeWorldId.ToString()) ?? "";
        _mqttService.UpdatePlayerDc(dcLabel);

        _dutyMonitor.PlayerName = playerName;
        _dutyMonitor.HomeWorldId = homeWorldId;

        return new PlayerSyncResult(playerName, homeWorldId, currentWorldName, dcLabel, worldChanged, oldWorldId);
    }

    /// <summary>
    /// 检测角色是否已登录且服务器信息可用，若是则同步信息并连接 MQTT。
    /// </summary>
    public void TryConnectMqttIfLoggedIn()
    {
        try
        {
            // 预检：CurrentWorld 名必须可用（过渡期可能为 null）
            if (!_clientState.IsLoggedIn || _objectTable.LocalPlayer is not { } lp)
                return;
            var cw = lp.CurrentWorld.ValueNullable;
            var worldName = cw?.Name.ToString();
            if (string.IsNullOrEmpty(worldName))
                return;

            var info = ReadAndSyncPlayerInfo();
            if (info is not { } result)
                return;

            _mqttLoggedIn = true;
            _log.Information($"{Prefix} 角色已登录: {result.Name}@{result.CurrentWorldName}，立即连接 MQTT");
            ConnectMqttAsync();
        }
        catch (Exception ex)
        {
            _log.Warning($"{Prefix} 检测角色登录状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 换区时重置猎怪追踪并同步玩家信息（ACT 版 ZI 等效）。
    /// </summary>
    private void OnZoneInit(ZoneInitEventArgs e)
    {
        _mobScannerService.Reset();

        try
        {
            var info = ReadAndSyncPlayerInfo();
            if (info is not { } result)
                return;

            if (result.WorldChanged && _mqttLoggedIn)
            {
                _log.Information($"{Prefix} 世界变更: {result.OldWorldId} → {result.HomeWorldId}，重新连接 MQTT");
                ReconnectMqttAsync();
            }

            _log.Information($"{Prefix} 玩家信息更新: {result.Name}@{result.CurrentWorldName} (worldId={result.HomeWorldId}, dc={result.DcLabel})");
        }
        catch (Exception ex)
        {
            _log.Warning($"{Prefix} 获取玩家信息异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 角色登录后连接 MQTT。
    /// </summary>
    private void OnLogin()
    {
        _mqttLoggedIn = true;
        ConnectMqttAsync();
    }

    /// <summary>
    /// 角色登出时断开 MQTT。
    /// </summary>
    private void OnLogout(int type, int code)
    {
        _mqttLoggedIn = false;
        DisconnectMqttAsync();
    }

    private void ConnectMqttAsync()
    {
        _ = Task.Run(async () =>
        {
            try { await _mqttService.ConnectAsync(); }
            catch (Exception ex) { _log.Error($"{Prefix} MQTT 连接异常: {ex.Message}"); }
        });
    }

    private void ReconnectMqttAsync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _mqttService.DisconnectAsync();
                await _mqttService.ConnectAsync();
            }
            catch (Exception ex) { _log.Error($"{Prefix} MQTT 重连异常: {ex.Message}"); }
        });
    }

    private void DisconnectMqttAsync()
    {
        _ = Task.Run(async () =>
        {
            try { await _mqttService.DisconnectAsync(); }
            catch (Exception ex) { _log.Warning($"{Prefix} MQTT 断开异常: {ex.Message}"); }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _clientState.ZoneInit -= OnZoneInit; }
        catch (Exception ex) { _log.Error($"{Prefix} ZoneInit 注销异常: {ex.Message}"); }

        try { _clientState.Login -= OnLogin; }
        catch (Exception ex) { _log.Error($"{Prefix} Login 注销异常: {ex.Message}"); }

        try { _clientState.Logout -= OnLogout; }
        catch (Exception ex) { _log.Error($"{Prefix} Logout 注销异常: {ex.Message}"); }
    }

    /// <summary>
    /// 玩家信息同步结果。
    /// </summary>
    private record struct PlayerSyncResult(
        string Name,
        uint HomeWorldId,
        string? CurrentWorldName,
        string DcLabel,
        bool WorldChanged,
        uint OldWorldId);
}
