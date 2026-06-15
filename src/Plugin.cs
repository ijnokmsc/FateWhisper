using System;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SilverDasher.Commands;
using SilverDasher.Config;
using SilverDasher.Services;
using SilverDasher.UI;

namespace SilverDasher;

/// <summary>
/// SilverDasher 插件入口，实现 IDalamudPlugin 接口。
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
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider InteropProvider { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    private readonly PluginConfig _config;
    private readonly DataManager _dataManager;
    private readonly AuthService _authService;
    private readonly MqttService _mqttService;
    private readonly DutyMonitor _dutyMonitor;
    private readonly NetworkInterceptor _networkInterceptor;
    private readonly NotificationService _notificationService;
    private readonly MainWindow _mainWindow;
    private readonly WindowSystem _windowSystem;
    private readonly PluginCommands _commands;
    private readonly Action _openMainUiHandler;
    private readonly Action _openConfigUiHandler;
    private readonly IFramework.OnUpdateDelegate _frameworkUpdateHandler = null!;
    private readonly CancellationTokenSource _cts = new();

    private const string Prefix = "[SilverDasher]";
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

        // ============================================================
        // 第2步：数据层 — DataManager
        // ============================================================
        _dataManager = new DataManager(PluginInterface, Log);

        // ============================================================
        // 第3步：认证服务 — AuthService
        // ============================================================
        _authService = new AuthService(Log);

        // ============================================================
        // 第4步：副本监测 — DutyMonitor
        // ============================================================
        _dutyMonitor = new DutyMonitor(ClientState, Log);

        // ============================================================
        // 第5步：MQTT 服务 — MqttService
        // ============================================================
        var mqttUser = _config.AuthToken ?? "silverdasher";
        var mqttPass = "silverdasher";
        _mqttService = new MqttService(Log, MqttBrokerUrl, mqttUser, mqttPass);

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
            _config);

        // ============================================================
        // 第8步：UI 层 — MainWindow + WindowSystem
        // ============================================================
        _mainWindow = new MainWindow(
            Log,
            _config,
            _dataManager,
            _authService,
            _mqttService,
            _dutyMonitor,
            _notificationService);

        _windowSystem = new WindowSystem("SilverDasher_WindowSystem");
        _windowSystem.AddWindow(_mainWindow);

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
                            msg, _config.Datacenter, _config.WorldName, msg.Type);
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

        // ============================================================
        // 第11步：启动流程 — 认证 → MQTT 连接 → 数据更新
        // ============================================================
        _ = StartAsync();

        Log.Information($"{Prefix} SilverDasher 插件初始化完成 v0.1.0.0");
    }

    /// <summary>
    /// 异步启动流程：认证 → MQTT 连接 → 数据远程检查。
    /// </summary>
    private async Task StartAsync()
    {
        // 后台持续尝试认证（不阻塞 MQTT 连接）
        var authCt = _cts.Token;
        _ = Task.Run(async () =>
        {
            var retry = 0;
            const int maxRetry = 3;
            while (!authCt.IsCancellationRequested && retry < maxRetry)
            {
                try
                {
                    var pn = ""; uint wid = 0; var sn = "";
                    await Framework.RunOnTick(() =>
                    {
                        if (ClientState.IsLoggedIn && ObjectTable.LocalPlayer is { } lp)
                        { pn = lp.Name.TextValue; wid = lp.HomeWorld.RowId; sn = lp.HomeWorld.Value.Name.ToString(); }
                    });
                    if (string.IsNullOrEmpty(pn)) { pn = _config.CharacterName; wid = _config.WorldId; sn = _config.WorldName; }
                    if (string.IsNullOrEmpty(pn) || wid == 0) { await Task.Delay(10000, authCt); continue; }

                    _config.CharacterName = pn; _config.WorldId = wid; _config.WorldName = sn;
                    _config.Save(); _dutyMonitor.PlayerName = pn; _dutyMonitor.HomeWorldId = wid;

                    retry++;
                    Log.Information($"{Prefix} 认证尝试 #{retry}/{maxRetry}: {pn} wid={wid}");
                    var r = await _authService.AuthenticateAsync(pn, wid, sn);
                    if (r?.IsSuccess == true)
                    {
                        _config.AuthToken = r.SessionToken; _config.Save();
                        _mqttService.UpdateCredentials(r.SessionToken, "silverdasher");
                        Log.Information($"{Prefix} 认证成功");
                        if (_mqttService.IsConnected) { await _mqttService.DisconnectAsync(); await _mqttService.ConnectAsync(); }
                        break;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log.Warning($"{Prefix} 认证异常: {ex.Message}"); }

                if (authCt.IsCancellationRequested || retry >= maxRetry) break;
                await Task.Delay(30000, authCt);
            }
            Log.Information($"{Prefix} 认证已退出 (共尝试 {retry}/{maxRetry} 次)");
        }, authCt);

        // MQTT 先以默认凭证连接（不等待认证）
        try { await _mqttService.ConnectAsync(); }
        catch (Exception ex) { Log.Error($"{Prefix} MQTT 连接异常: {ex.Message}"); }

        // 异步检查远程数据更新
        _ = Task.Run(async () =>
        {
            try { await _dataManager.CheckRemoteUpdatesAsync(); }
            catch (Exception ex) { Log.Warning($"{Prefix} 远程更新失败: {ex.Message}"); }
        }, _cts.Token);
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

        try { _notificationService.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 通知服务释放异常: {ex.Message}"); }

        try { _networkInterceptor.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 网络拦截器释放异常: {ex.Message}"); }

        try { Framework.Update -= _frameworkUpdateHandler; }
        catch (Exception ex) { Log.Error($"{Prefix} Framework 注销异常: {ex.Message}"); }

        try { _mqttService.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} MQTT 服务释放异常: {ex.Message}"); }

        try { _dutyMonitor.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} DutyMonitor 释放异常: {ex.Message}"); }

        try { _authService.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} 认证服务释放异常: {ex.Message}"); }

        try { _dataManager.Dispose(); }
        catch (Exception ex) { Log.Error($"{Prefix} DataManager 释放异常: {ex.Message}"); }

        Log.Information($"{Prefix} SilverDasher 插件已卸载");
    }
}
