using System;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FateWhisper.Commands;
using FateWhisper.Config;
using FateWhisper.Services;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using FateWhisper.UI;

namespace FateWhisper;

/// <summary>
/// FateWhisper 插件入口，实现 IDalamudPlugin 接口。
/// Dalamud API 15 使用 [PluginService] 静态属性注入替代构造函数注入。
/// 框架通过 Source Generator 自动填充标记了 [PluginService] 的属性。
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    // API 15: 通过 [PluginService] 静态属性注入 Dalamud 核心服务
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    public static IPluginLog? SharedLog => Log;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider InteropProvider { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IDataManager GameData { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    private readonly PluginConfig _config;
    private readonly SubscriptionsStore _subsStore;
    private readonly DataManager _dataManager;
    private readonly MqttService _mqttService;
    private readonly DutyMonitor _dutyMonitor;
    private readonly NetworkInterceptor _networkInterceptor;
    private readonly NotificationService _notificationService;
    private readonly MobScannerService _mobScannerService;
    private readonly NavigationService _navigationService;
    private readonly MainWindow _mainWindow;
    private readonly NavigationWindow _navigationWindow;
    private readonly WindowSystem _windowSystem;
    private readonly PluginCommands _commands;
    private readonly Action _openMainUiHandler;
    private readonly Action _openConfigUiHandler;
    private readonly IFramework.OnUpdateDelegate _frameworkUpdateHandler = null!;
    private readonly CancellationTokenSource _cts = new();
    private bool _mqttLoggedIn; // 标记是否已登录过（用于检测世界变更后重连）

    private const string Prefix = "[FateWhisper]";
    private const string MqttBrokerUrl = "wss://tree.garlandtools.cn/mqtt";

    /// <summary>
    /// 无参构造函数，Dalamud API 15 通过 [PluginService] 注入依赖。
    /// 在构造函数执行时，所有 [PluginService] 属性已被框架填充。
    /// 按照架构设计的分层顺序创建所有服务并连线事件。
    /// </summary>
    public Plugin()
    {
        // ============================================================
        // 第1步：加载配置
        // ============================================================
        _config = new PluginConfig(PluginInterface);
        Log.Information($"{Prefix} 插件配置已加载");

        // 订阅存储（独立文件，绕过 Dalamud 序列化）
        var configDir = PluginInterface.ConfigDirectory?.FullName ?? "";
        _subsStore = new SubscriptionsStore(configDir, Log);
        Log.Information($"{Prefix} 订阅加载完成: hunt={_subsStore.HuntIds.Count} fate={_subsStore.FateIds.Count}");

        // ============================================================
        // 第2步：数据层 — DataManager
        // ============================================================
        _dataManager = new DataManager(PluginInterface, Log);

        // ============================================================
        // 第3步：副本监测 — DutyMonitor
        // ============================================================
        _dutyMonitor = new DutyMonitor(ClientState, Log);

        // ============================================================
        // 第5步：MQTT 服务 — MqttService
        // ============================================================
        var mqttUser = _config.AuthToken ?? "silverdasher";
        var mqttPass = "silverdasher";
        _mqttService = new MqttService(Log, _dataManager, _config, MqttBrokerUrl, mqttUser, mqttPass);
        _mqttService.UpdatePlayerDc(_dataManager.LookupDcLabel(_config.WorldId.ToString()));

        // ============================================================
        // 第6步：网络拦截器 — NetworkInterceptor
        // ============================================================
        _networkInterceptor = new NetworkInterceptor(
            PluginInterface,
            Log,
            _dataManager,
            InteropProvider,
            _dutyMonitor.PlayerName,
            _config.WorldName,
            _config.Datacenter);

        // 同步玩家名称到 DutyMonitor
        _dutyMonitor.PlayerName = _config.CharacterName;

        // ============================================================
        // 第7步：通知服务 — NotificationService
        // ============================================================
        _notificationService = new NotificationService(
            Log,
            ChatGui,
            ToastGui,
            NotificationManager,
            _dataManager,
            _dutyMonitor,
            _config,
            _subsStore);

        // ============================================================
        // 第7.3步：猎怪扫描服务 — MobScannerService（IObjectTable 扫描）
        // ============================================================
        _mobScannerService = new MobScannerService(
            Log,
            ObjectTable,
            ClientState,
            _dataManager,
            Framework,
            _config.WorldName ?? "",
            _config.Datacenter,
            _config.WorldId);

        // ============================================================
        // 第7.5步：导航服务 — NavigationService + NavigationWindow
        // ============================================================
        _navigationService = new NavigationService(
            PluginInterface,
            Log,
            ClientState,
            Condition,
            Framework,
            GameData,
            _dataManager,
            _config,
            _config.WorldName ?? "",
            ObjectTable);

        // 立即从游戏读取玩家世界（不等 OnZoneInit），确保跨服判断准确
        try
        {
            if (ClientState.IsLoggedIn && ObjectTable.LocalPlayer is { } lp)
            {
                // 使用 CurrentWorld（当前服），过渡期为 null 时跳过
                var cw = lp.CurrentWorld.ValueNullable;
                var worldName = cw?.Name.ToString();
                if (!string.IsNullOrEmpty(worldName))
                {
                    _navigationService.PlayerWorldName = worldName;
                    _config.WorldName = worldName;
                }
            }
        }
        catch (Exception ex) { Log.Warning($"{Prefix} 读取玩家世界失败: {ex.Message}"); }

        _navigationWindow = new NavigationWindow(
            Log,
            _navigationService,
            () => _dutyMonitor.IsInDuty);

        // 连线：NotificationService 播报 → NavigationWindow 弹窗（封送到主线程）
        _notificationService.HuntNavigationPopupRequested += msg =>
        {
            try { Framework.RunOnFrameworkThread(() => _navigationWindow.ShowForHunt(msg)); }
            catch (Exception ex) { Log.Error($"{Prefix} 导航弹窗(猎怪)异常: {ex.Message}"); }
        };
        _notificationService.FateNavigationPopupRequested += msg =>
        {
            try { Framework.RunOnFrameworkThread(() => _navigationWindow.ShowForFate(msg)); }
            catch (Exception ex) { Log.Error($"{Prefix} 导航弹窗(FATE)异常: {ex.Message}"); }
        };

        // ============================================================
        // 第8步：UI 层 — MainWindow + WindowSystem
        // ============================================================
        _mainWindow = new MainWindow(
            Log,
            _config,
            _dataManager,
            _mqttService,
            _dutyMonitor,
            _notificationService,
            _navigationService,
            _navigationWindow,
            ObjectTable,
            _subsStore);

        // 将 MainWindow 的导航日志回调注入 NotificationService
        _notificationService.SetNavLogCallback(_mainWindow.AddNavigationLog);

        _windowSystem = new WindowSystem("FateWhisper_WindowSystem");
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_navigationWindow);

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _openMainUiHandler = () => _mainWindow.IsOpen = true;
        PluginInterface.UiBuilder.OpenMainUi += _openMainUiHandler;
        _openConfigUiHandler = () => _mainWindow.IsOpen = true;
        PluginInterface.UiBuilder.OpenConfigUi += _openConfigUiHandler;

        // ============================================================
        // 第9步：命令 — /sd
        // ============================================================
        _commands = new PluginCommands(CommandManager, _mainWindow, Log);

        // ============================================================
        // 第10步：事件连线 — 连接各服务之间的数据流
        // ============================================================

        // MQTT 远程接收 → 通知服务
        _mqttService.HuntReceived += msg =>
        {
            try { _notificationService.OnHuntBroadcast(msg); }
            catch (Exception ex) { Log.Error($"{Prefix} HuntReceived 处理异常: {ex.Message}"); }
        };
        _mqttService.FateReceived += msg =>
        {
            try { _notificationService.OnFateBroadcast(msg); }
            catch (Exception ex) { Log.Error($"{Prefix} FateReceived 处理异常: {ex.Message}"); }
        };

        // MobScanner 猎怪扫描检测 → 通知服务 + MQTT 发布
        _mobScannerService.HuntDetected += msg =>
        {
            try
            {
                _notificationService.OnHuntBroadcast(msg);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _mqttService.PublishHuntAsync(
                            msg, _config.Datacenter, _config.WorldName, msg.Rank ?? "");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"{Prefix} 发布猎怪检测失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"{Prefix} HuntDetected 处理异常: {ex.Message}");
            }
        };
        _mobScannerService.HuntStatusChanged += msg =>
        {
            try { _notificationService.OnHuntBroadcast(msg); }
            catch (Exception ex) { Log.Error($"{Prefix} HuntStatusChanged 处理异常: {ex.Message}"); }
        };
        _mobScannerService.HuntVanished += msg =>
        {
            try { _notificationService.OnHuntBroadcast(msg); }
            catch (Exception ex) { Log.Error($"{Prefix} HuntVanished 处理异常: {ex.Message}"); }
        };

        // 网络拦截本地检测 → MQTT 发布 + 通知服务
        _networkInterceptor.HuntDetected += msg =>
        {
            try
            {
                _notificationService.OnHuntBroadcast(msg);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _mqttService.PublishHuntAsync(
                            msg, _config.Datacenter, _config.WorldName, msg.Rank);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"{Prefix} 发布本地猎怪失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"{Prefix} HuntDetected 处理异常: {ex.Message}");
            }
        };

        _networkInterceptor.FateDetected += msg =>
        {
            try
            {
                _notificationService.OnFateBroadcast(msg);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _mqttService.PublishFateAsync(
                            msg, _config.Datacenter, _config.WorldName, msg.Type_ ?? "unknown");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"{Prefix} 发布本地 FATE 失败: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"{Prefix} FateDetected 处理异常: {ex.Message}");
            }
        };

        // 副本状态监测 — 每帧更新（存储委托引用，确保 Dispose 能正确注销）
        _frameworkUpdateHandler = _ => _dutyMonitor.OnFrameworkUpdate();
        Framework.Update += _frameworkUpdateHandler;

        // 换区时重置猎怪追踪（与 ACT 版 zoneInit 处理一致）
        ClientState.ZoneInit += OnZoneInit;

        // 角色登录后连接 MQTT（此时已有完整的玩家服务器信息）
        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;

        // ============================================================
        // 第11步：启动流程 — 异步数据更新（MQTT 连接延迟到 OnLogin）
        // ============================================================
        _ = StartAsync();

        Log.Information($"{Prefix} 订阅加载完成: hunt={_subsStore.HuntIds.Count} fate={_subsStore.FateIds.Count}");
        Log.Information($"{Prefix} FateWhisper 插件初始化完成 v0.3.0.0 (基于 SilverDasher)");
    }

    /// <summary>
    /// 换区时重置猎怪追踪（ACT 版 ZI 等效）。
    /// </summary>
    private void OnZoneInit(ZoneInitEventArgs e)
    {
        _mobScannerService.Reset();

        // 从游戏中获取玩家世界信息，用于跨大区判断
        try
        {
            if (ClientState.IsLoggedIn && ObjectTable.LocalPlayer is { } lp)
            {
                var homeWorldId = lp.HomeWorld.RowId;
                var homeWorldName = lp.HomeWorld.Value.Name.ToString();
                // 仅当 CurrentWorld 可用时更新玩家世界名（过渡期为 null 时不回退到 HomeWorld）
                var currentWorld = lp.CurrentWorld.ValueNullable;
                var currentWorldName = currentWorld?.Name.ToString();
                var playerName = lp.Name.TextValue;

                // 检测世界变更（用于 MQTT 重连和跨大区判断更新）
                var worldChanged = _config.WorldId != 0 && _config.WorldId != homeWorldId;

                _config.CharacterName = playerName;
                _config.WorldId = homeWorldId;
                if (currentWorldName != null)
                {
                    _config.WorldName = currentWorldName;
                    _navigationService.PlayerWorldName = currentWorldName ?? "";
                }
                _config.Datacenter = _dataManager.LookupDatacenter(homeWorldId.ToString()) ?? "";
                _config.Save();

                var dcLabel = _dataManager.LookupDcLabel(homeWorldId.ToString());
                _mqttService.UpdatePlayerDc(dcLabel);

                _dutyMonitor.PlayerName = playerName;
                _dutyMonitor.HomeWorldId = homeWorldId;
                if (currentWorldName != null)
                    _navigationService.PlayerWorldName = currentWorldName;

                // 世界变更 → 断开旧 MQTT 连接，以新服务器信息重新连接
                if (worldChanged && _mqttLoggedIn)
                {
                    Log.Information($"{Prefix} 世界变更: {_config.WorldId} → {homeWorldId}，重新连接 MQTT");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _mqttService.DisconnectAsync();
                            await _mqttService.ConnectAsync();
                        }
                        catch (Exception ex) { Log.Error($"{Prefix} MQTT 重连异常: {ex.Message}"); }
                    });
                }

                Log.Information($"{Prefix} 玩家信息更新: {playerName}@{currentWorldName} (worldId={homeWorldId}, dc={dcLabel})");
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"{Prefix} 获取玩家信息异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 异步启动流程：认证 → MQTT 连接 → 数据远程检查。
    /// </summary>
    private async Task StartAsync()
    {
        // 检测角色是否已登录（插件重载等场景，Login 事件不会触发）
        if (ClientState.IsLoggedIn) TryConnectMqttIfLoggedIn();

        // 异步检查远程数据更新
        _ = Task.Run(async () =>
        {
            try { await _dataManager.CheckRemoteUpdatesAsync(); }
            catch (Exception ex) { Log.Warning($"{Prefix} 远程更新失败: {ex.Message}"); }
        }, _cts.Token);

        Log.Information($"{Prefix} 启动完成 (若角色已登录将立即连接 MQTT)");
    }

    /// <summary>
    /// 检测角色是否已登录且服务器信息可用，若是则连接 MQTT。
    /// 验证角色名和服务器名非空后再连接，避免数据未就绪时错误连接。
    /// </summary>
    private void TryConnectMqttIfLoggedIn()
    {
        try
        {
            if (ClientState.IsLoggedIn && ObjectTable.LocalPlayer is { } lp)
            {
                var homeWorldId = lp.HomeWorld.RowId;
                var cw = lp.CurrentWorld.ValueNullable;
                var worldName = cw?.Name.ToString();
                if (string.IsNullOrEmpty(worldName)) return;
                var playerName = lp.Name.TextValue;

                if (!string.IsNullOrEmpty(worldName) && !string.IsNullOrEmpty(playerName))
                {
                    _config.CharacterName = playerName;
                    _config.WorldId = homeWorldId;
                    _config.WorldName = worldName;
                    _config.Datacenter = _dataManager.LookupDatacenter(homeWorldId.ToString()) ?? "";
                    _config.Save();

                    var dcLabel = _dataManager.LookupDcLabel(homeWorldId.ToString());
                    _mqttService.UpdatePlayerDc(dcLabel);

                    _navigationService.PlayerWorldName = worldName;
                    _mqttLoggedIn = true;

                    Log.Information($"{Prefix} 角色已登录: {playerName}@{worldName}，立即连接 MQTT");
                    _ = Task.Run(async () =>
                    {
                        try { await _mqttService.ConnectAsync(); }
                        catch (Exception ex) { Log.Error($"{Prefix} MQTT 连接异常: {ex.Message}"); }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"{Prefix} 检测角色登录状态失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 角色登录后连接 MQTT，此时已有完整的玩家服务器信息。
    /// </summary>
    private void OnLogin()
    {
        _mqttLoggedIn = true;
        _ = Task.Run(async () =>
        {
            try { await _mqttService.ConnectAsync(); }
            catch (Exception ex) { Log.Error($"{Prefix} MQTT 连接异常: {ex.Message}"); }
        });
    }

    /// <summary>
    /// 角色登出时断开 MQTT。
    /// </summary>
    private void OnLogout(int type, int code)
    {
        _mqttLoggedIn = false;
        _ = Task.Run(async () =>
        {
            try { await _mqttService.DisconnectAsync(); }
            catch (Exception ex) { Log.Warning($"{Prefix} MQTT 断开异常: {ex.Message}"); }
        });
    }

    /// <summary>
    /// 释放所有资源，按逆序销毁服务。
    /// </summary>
    public void Dispose()
    {
        // 先停止所有后台任务
        _cts.Cancel();
        _cts.Dispose();

        // 逆序释放所有服务
        try { _commands.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 命令释放异常: {ex.Message}"); }

        try
        {
            PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
            PluginInterface.UiBuilder.OpenMainUi -= _openMainUiHandler;
            PluginInterface.UiBuilder.OpenConfigUi -= _openConfigUiHandler;
        }
        catch (Exception ex) { Log.Error($"{Prefix} UI 注销异常: {ex.Message}"); }

        try { _mainWindow.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 主窗口释放异常: {ex.Message}"); }

        try { _navigationWindow.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 导航窗口释放异常: {ex.Message}"); }

        try { _notificationService.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 通知服务释放异常: {ex.Message}"); }

        try { _mobScannerService.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 猎怪扫描服务释放异常: {ex.Message}"); }

        try { ClientState.ZoneInit -= OnZoneInit; }
        catch (Exception ex) { Log.Error($"{Prefix} ZoneInit 注销异常: {ex.Message}"); }

        try { ClientState.Login -= OnLogin; }
        catch (Exception ex) { Log.Error($"{Prefix} Login 注销异常: {ex.Message}"); }

        try { ClientState.Logout -= OnLogout; }
        catch (Exception ex) { Log.Error($"{Prefix} Logout 注销异常: {ex.Message}"); }

        try { _navigationService.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 导航服务释放异常: {ex.Message}"); }

        try { _networkInterceptor.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 网络拦截器释放异常: {ex.Message}"); }

        try { Framework.Update -= _frameworkUpdateHandler; }
        catch (Exception ex) { Log.Error($"{Prefix} Framework 注销异常: {ex.Message}"); }

        try { _mqttService.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} MQTT 服务释放异常: {ex.Message}"); }

        try { _dutyMonitor.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} DutyMonitor 释放异常: {ex.Message}"); }

        try { _dataManager.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} DataManager 释放异常: {ex.Message}"); }

        Log.Information($"{Prefix} FateWhisper 插件已卸载");
    }
}
