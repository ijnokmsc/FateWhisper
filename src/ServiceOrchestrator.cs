using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FateWhisper.Commands;
using FateWhisper.Config;
using FateWhisper.Models;
using FateWhisper.Services;
using FateWhisper.UI;

namespace FateWhisper;

/// <summary>
/// 服务编排器 — 负责所有服务的实例化、事件连线、逆序释放。
/// 从 Plugin.cs 提取，消除 8 组重复的 try-catch + Task.Run lambda。
/// </summary>
internal sealed class ServiceOrchestrator : IDisposable
{
    // 配置与数据层
    private readonly PluginConfig _config;
    private readonly SubscriptionsStore _subsStore;
    private readonly DataManager _dataManager;

    // 服务层
    private readonly DutyMonitor _dutyMonitor;
    private readonly MqttService _mqttService;
    private readonly NetworkInterceptor _networkInterceptor;
    private readonly NotificationService _notificationService;
    private readonly MobScannerService _mobScannerService;
    private readonly NavigationService _navigationService;

    // 上下文管理
    private readonly PlayerContextManager _playerContext;

    // UI 层
    private readonly NavigationWindow _navigationWindow;
    private readonly MainWindow _mainWindow;
    private readonly WindowSystem _windowSystem;

    // 命令
    private readonly PluginCommands _commands;

    // 事件委托引用（确保 Dispose 能正确注销）
    private readonly Action _openMainUiHandler;
    private readonly Action _openConfigUiHandler;
    private readonly IFramework.OnUpdateDelegate _frameworkUpdateHandler;

    // 后台任务取消
    private readonly CancellationTokenSource _cts = new();

    private bool _disposed;

    private const string Prefix = "[FateWhisper]";
    private const string MqttBrokerUrl = "wss://tree.garlandtools.cn/mqtt";

    public ServiceOrchestrator()
    {
        var log = Plugin.Log;
        var pluginInterface = Plugin.PluginInterface;

        // ============================================================
        // 第1步：配置 + 订阅存储
        // ============================================================
        _config = new PluginConfig(pluginInterface);
        log.Information($"{Prefix} 插件配置已加载");

        var configDir = pluginInterface.ConfigDirectory?.FullName ?? "";
        _subsStore = new SubscriptionsStore(configDir, log);
        log.Information($"{Prefix} 订阅加载完成: hunt={_subsStore.HuntIds.Count} fate={_subsStore.FateIds.Count}");

        // ============================================================
        // 第2步：数据层
        // ============================================================
        _dataManager = new DataManager(pluginInterface, log);

        // ============================================================
        // 第3步：副本监测
        // ============================================================
        _dutyMonitor = new DutyMonitor(Plugin.ClientState, log);

        // ============================================================
        // 第4步：MQTT 服务
        // ============================================================
        var mqttUser = _config.AuthToken ?? "silverdasher";
        var mqttPass = "silverdasher";
        _mqttService = new MqttService(log, _dataManager, _config, MqttBrokerUrl, mqttUser, mqttPass);
        _mqttService.UpdatePlayerDc(_dataManager.LookupDcLabel(_config.WorldId.ToString()));

        // ============================================================
        // 第5步：网络拦截器
        // ============================================================
        _networkInterceptor = new NetworkInterceptor(
            pluginInterface, log, _dataManager, Plugin.InteropProvider,
            _dutyMonitor.PlayerName, _config.WorldName, _config.Datacenter);

        _dutyMonitor.PlayerName = _config.CharacterName;

        // ============================================================
        // 第6步：通知服务
        // ============================================================
        _notificationService = new NotificationService(
            log, Plugin.ChatGui, Plugin.ToastGui, Plugin.NotificationManager,
            _dataManager, _dutyMonitor, _config, _subsStore);

        // ============================================================
        // 第7步：猎怪扫描服务
        // ============================================================
        _mobScannerService = new MobScannerService(
            log, Plugin.ObjectTable, Plugin.ClientState, _dataManager,
            Plugin.Framework, _config.WorldName ?? "", _config.Datacenter, _config.WorldId);

        // ============================================================
        // 第8步：导航服务
        // ============================================================
        _navigationService = new NavigationService(
            pluginInterface, log, Plugin.ClientState, Plugin.Condition,
            Plugin.Framework, Plugin.GameData, _dataManager, _config,
            _config.WorldName ?? "", Plugin.ObjectTable, Plugin.ChatGui, Plugin.FateTable);

        // ============================================================
        // 第9步：玩家上下文管理器 — 快速读取世界名
        // ============================================================
        _playerContext = new PlayerContextManager(
            log, Plugin.ClientState, Plugin.ObjectTable, _dataManager,
            _config, _mqttService, _navigationService, _dutyMonitor, _mobScannerService);
        _playerContext.QuickSyncWorldName();

        // ============================================================
        // 第10步：导航窗口 + 弹窗连线
        // ============================================================
        _navigationWindow = new NavigationWindow(log, _navigationService, () => _dutyMonitor.IsInDuty);

        _notificationService.HuntNavigationPopupRequested += OnHuntNavPopup;
        _notificationService.FateNavigationPopupRequested += OnFateNavPopup;

        // ============================================================
        // 第11步：主窗口 + WindowSystem
        // ============================================================
        _mainWindow = new MainWindow(
            log, _config, _dataManager, _mqttService, _dutyMonitor,
            _notificationService, _navigationService, _navigationWindow,
            Plugin.ObjectTable, _subsStore);

        _notificationService.SetNavLogCallback(_mainWindow.AddNavigationLog);

        _windowSystem = new WindowSystem("FateWhisper_WindowSystem");
        _windowSystem.AddWindow(_mainWindow);
        _windowSystem.AddWindow(_navigationWindow);

        pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _openMainUiHandler = () => _mainWindow.IsOpen = true;
        pluginInterface.UiBuilder.OpenMainUi += _openMainUiHandler;
        _openConfigUiHandler = () => _mainWindow.IsOpen = true;
        pluginInterface.UiBuilder.OpenConfigUi += _openConfigUiHandler;

        // ============================================================
        // 第12步：命令
        // ============================================================
        _commands = new PluginCommands(Plugin.CommandManager, _mainWindow, log);

        // ============================================================
        // 第13步：框架更新 — DutyMonitor 每帧更新
        // ============================================================
        _frameworkUpdateHandler = _ => _dutyMonitor.OnFrameworkUpdate();
        Plugin.Framework.Update += _frameworkUpdateHandler;

        log.Information($"{Prefix} FateWhisper 插件初始化完成 v0.3.0.0 (基于 SilverDasher)");
    }

    /// <summary>
    /// 连线所有服务间事件，并启动玩家上下文 + 远程数据检查。
    /// 在构造函数完成后调用，确保所有服务已就绪。
    /// </summary>
    public void Initialize()
    {
        // ============================================================
        // 事件连线 — MQTT 远程接收 → 通知服务
        // ============================================================
        _mqttService.HuntReceived += OnHuntBroadcast;
        _mqttService.FateReceived += OnFateBroadcast;

        // ============================================================
        // 事件连线 — MobScanner 猎怪扫描 → 通知 + MQTT 发布
        // ============================================================
        _mobScannerService.HuntDetected += OnHuntDetectedAndPublish;
        _mobScannerService.HuntStatusChanged += OnHuntBroadcast;
        _mobScannerService.HuntVanished += OnHuntBroadcast;

        // ============================================================
        // 事件连线 — 网络拦截本地检测 → 通知 + MQTT 发布
        // ============================================================
        _networkInterceptor.HuntDetected += OnHuntDetectedAndPublish;
        _networkInterceptor.FateDetected += OnFateDetectedAndPublish;

        // ============================================================
        // 启动 — 玩家上下文初始化（ClientState 事件订阅 + MQTT 连接）
        // ============================================================
        _playerContext.Initialize();

        // ============================================================
        // 启动 — 异步远程数据更新检查
        // ============================================================
        _ = Task.Run(async () =>
        {
            try { await _dataManager.CheckRemoteUpdatesAsync(); }
            catch (Exception ex) { Plugin.Log.Warning($"{Prefix} 远程更新失败: {ex.Message}"); }
        }, _cts.Token);

        Plugin.Log.Information($"{Prefix} 启动完成 (若角色已登录将立即连接 MQTT)");
    }

    // ============================================================
    // 事件处理器 — 命名方法替代 8 组重复的 inline lambda
    // ============================================================

    /// <summary>MQTT 远程猎怪播报 → 通知服务</summary>
    private void OnHuntBroadcast(HuntMessage msg)
    {
        try { _notificationService.OnHuntBroadcast(msg); }
        catch (Exception ex) { Plugin.Log.Error($"{Prefix} HuntReceived 处理异常: {ex.Message}"); }
    }

    /// <summary>MQTT 远程 FATE 播报 → 通知服务</summary>
    private void OnFateBroadcast(FateMessage msg)
    {
        try { _notificationService.OnFateBroadcast(msg); }
        catch (Exception ex) { Plugin.Log.Error($"{Prefix} FateReceived 处理异常: {ex.Message}"); }
    }

    /// <summary>猎怪检测（MobScanner / NetworkInterceptor）→ 通知 + MQTT 发布</summary>
    private void OnHuntDetectedAndPublish(HuntMessage msg)
    {
        try
        {
            _notificationService.OnHuntBroadcast(msg);
            _ = Task.Run(async () =>
            {
                try { await _mqttService.PublishHuntAsync(msg, _config.Datacenter, _config.WorldName, msg.Rank ?? ""); }
                catch (Exception ex) { Plugin.Log.Error($"{Prefix} 发布猎怪检测失败: {ex.Message}"); }
            });
        }
        catch (Exception ex) { Plugin.Log.Error($"{Prefix} HuntDetected 处理异常: {ex.Message}"); }
    }

    /// <summary>FATE 检测（NetworkInterceptor）→ 通知 + MQTT 发布</summary>
    private void OnFateDetectedAndPublish(FateMessage msg)
    {
        try
        {
            _notificationService.OnFateBroadcast(msg);
            _ = Task.Run(async () =>
            {
                try { await _mqttService.PublishFateAsync(msg, _config.Datacenter, _config.WorldName, msg.Type_ ?? "unknown"); }
                catch (Exception ex) { Plugin.Log.Error($"{Prefix} 发布本地 FATE 失败: {ex.Message}"); }
            });
        }
        catch (Exception ex) { Plugin.Log.Error($"{Prefix} FateDetected 处理异常: {ex.Message}"); }
    }

    /// <summary>猎怪导航弹窗 → 封送到主线程</summary>
    private void OnHuntNavPopup(HuntMessage msg)
    {
        try { Plugin.Framework.RunOnFrameworkThread(() => _navigationWindow.ShowForHunt(msg)); }
        catch (Exception ex) { Plugin.Log.Error($"{Prefix} 导航弹窗(猎怪)异常: {ex.Message}"); }
    }

    /// <summary>FATE 导航弹窗 → 封送到主线程</summary>
    private void OnFateNavPopup(FateMessage msg)
    {
        try { Plugin.Framework.RunOnFrameworkThread(() => _navigationWindow.ShowForFate(msg)); }
        catch (Exception ex) { Plugin.Log.Error($"{Prefix} 导航弹窗(FATE)异常: {ex.Message}"); }
    }

    /// <summary>
    /// 逆序释放所有服务。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var log = Plugin.Log;

        // 先停止所有后台任务
        try { _cts.Cancel(); } catch { }
        try { _cts.Dispose(); } catch { }

        // 逆序释放
        SafeDispose(_commands, "命令", log);
        SafeDispose(_playerContext, "玩家上下文", log);

        // UI 事件注销
        try
        {
            Plugin.PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
            Plugin.PluginInterface.UiBuilder.OpenMainUi -= _openMainUiHandler;
            Plugin.PluginInterface.UiBuilder.OpenConfigUi -= _openConfigUiHandler;
        }
        catch (Exception ex) { log.Error($"{Prefix} UI 注销异常: {ex.Message}"); }

        SafeDispose(_mainWindow, "主窗口", log);
        SafeDispose(_navigationWindow, "导航窗口", log);
        SafeDispose(_notificationService, "通知服务", log);
        SafeDispose(_mobScannerService, "猎怪扫描服务", log);
        SafeDispose(_navigationService, "导航服务", log);
        SafeDispose(_networkInterceptor, "网络拦截器", log);

        try { Plugin.Framework.Update -= _frameworkUpdateHandler; }
        catch (Exception ex) { log.Error($"{Prefix} Framework 注销异常: {ex.Message}"); }

        SafeDispose(_mqttService, "MQTT 服务", log);
        SafeDispose(_dutyMonitor, "DutyMonitor", log);
        SafeDispose(_dataManager, "DataManager", log);

        log.Information($"{Prefix} FateWhisper 插件已卸载");
    }

    private static void SafeDispose(IDisposable? obj, string label, IPluginLog log)
    {
        if (obj == null) return;
        try { obj.Dispose(); }
        catch (Exception ex) { log.Error($"{Prefix} {label}释放异常: {ex.Message}"); }
    }
}
