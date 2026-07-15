using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FateWhisper.Config;
using FateWhisper.Models;
using FateWhisper.Services;
using FateWhisper.UI.Tabs;

namespace FateWhisper.UI;

/// <summary>
/// ImGui 主窗口，包含 6 个功能 Tab。
/// </summary>
public class MainWindow : Window, IDisposable
{
    private readonly IPluginLog _log;
    private readonly PluginConfig _config;
    private readonly DataManager _dataManager;
    private readonly MqttService _mqttService;
    private readonly DutyMonitor _dutyMonitor;
    private readonly NavigationService _navigationService;
    private readonly NavigationWindow _navigationWindow;
    private readonly NavigationTestTab _navTestTab;
    private readonly DebugTab _debugTab;
    private readonly IObjectTable _objectTable;

    private readonly HuntTab _huntTab;
    private readonly FateTab _fateTab;
    private readonly NotificationTab _notificationTab;
    private readonly SystemTab _systemTab;
    private int _activeTab;

    // 通知日志（多线程读写需加锁）
    private readonly List<LogEntry> _notificationLog = new();
    private readonly object _logLock = new();
    private const int MaxLogEntries = 200;

    private const string Prefix = "[FateWhisper]";

    private static string GetVersionString()
    {
        try
        {
            var ver = typeof(Plugin).Assembly.GetName().Version;
            return $"v{ver?.Major}.{ver?.Minor}.{ver?.Build}";
        }
        catch
        {
            return "v?.?.?";
        }
    }

    /// <summary>
    /// 初始化主窗口及所有子 Tab。
    /// </summary>
    public MainWindow(
        IPluginLog log,
        PluginConfig config,
        DataManager dataManager,
        MqttService mqttService,
        DutyMonitor dutyMonitor,
        NotificationService notificationService,
        NavigationService navigationService,
        NavigationWindow navigationWindow,
        IObjectTable objectTable,
        SubscriptionsStore subsStore)
        : base($"FateWhisper {GetVersionString()}##FateWhisperMainWindow")
    {
        _log = log;
        _config = config;
        _dataManager = dataManager;
        _mqttService = mqttService;
        _dutyMonitor = dutyMonitor;
        _navigationService = navigationService;
        _navigationWindow = navigationWindow;
        _objectTable = objectTable;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 350),
            MaximumSize = new Vector2(800, 700)
        };

        _huntTab = new HuntTab(config, dataManager, log, subsStore);
        _fateTab = new FateTab(config, dataManager, log, subsStore);
        _notificationTab = new NotificationTab(config, log, notificationService);
        _systemTab = new SystemTab(
            config, log, dataManager, mqttService, dutyMonitor, _navigationService);
        _navTestTab = new NavigationTestTab(_navigationService, _config, _navigationWindow, _objectTable);
        _debugTab = new DebugTab(config, log, _mqttService, _navigationService, notificationService);

        // 启动时不根据持久化配置自动打开主窗口：用户明确要求插件加载后主窗口保持关闭，
        // 只能通过 /fw 命令或插件「设置」按钮手动打开。若磁盘配置残留 WindowVisible=true，
        // 这里直接忽略，避免每次加载都弹出。
        IsOpen = false;
        _activeTab = Math.Clamp(config.ActiveTab, 0, 6);
        _log.Information($"{Prefix} 主窗口已初始化");
    }

    /// <summary>
    /// 绘制主窗口内容（Tab 布局）。
    /// </summary>
    public override void Draw()
    {
        try
        {
            DrawTabBar();
            DrawActiveTab();
            UpdateWindowVisibility();
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 主窗口绘制异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 绘制 Tab 栏。
    /// </summary>
    private void DrawTabBar()
    {
        if (ImGui.BeginTabBar("FateWhisperTabs"))
        {
            string[] tabNames = { "猎怪", "FATE", "通知", "系统", "导航测试", "调试", "日志" };
            for (var i = 0; i < tabNames.Length; i++)
            {
                if (ImGui.BeginTabItem($"{tabNames[i]}##tab{i}"))
                {
                    _activeTab = i;
                    _config.ActiveTab = i;
                    _config.Save();
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
    }

    /// <summary>
    /// 根据选中的 Tab 绘制对应内容。
    /// </summary>
    private void DrawActiveTab()
    {
        switch (_activeTab)
        {
            case 0:
                _huntTab.Draw();
                break;
            case 1:
                _fateTab.Draw();
                break;
            case 2:
                _notificationTab.Draw();
                break;
            case 3:
                _systemTab.Draw();
                break;
            case 4:
                _navTestTab.Draw();
                break;
            case 5:
                _debugTab.Draw();
                break;
            case 6:
                DrawLogTab();
                break;
            default:
                _activeTab = 0;
                _huntTab.Draw();
                break;
        }
    }

    /// <summary>
    /// 同步窗口可见性到配置。
    /// </summary>
    private void UpdateWindowVisibility()
    {
        if (_config.WindowVisible != IsOpen)
        {
            _config.WindowVisible = IsOpen;
            _config.Save();
        }
    }

    /// <summary>
    /// 打开窗口。
    /// </summary>
    public void Open()
    {
        IsOpen = true;
        _config.WindowVisible = true;
        _config.Save();
    }

    /// <summary>
    /// 绘制日志 Tab 内容。仅记录推送到导航面板的消息，双击可重新弹出导航窗口。
    /// </summary>
    private void DrawLogTab()
    {
        if (ImGui.SmallButton("清空"))
        {
            lock (_logLock) _notificationLog.Clear();
        }
        ImGui.SameLine();

        int logCount;
        List<LogEntry> snapshot;
        lock (_logLock)
        {
            logCount = _notificationLog.Count;
            snapshot = new List<LogEntry>(_notificationLog);
        }

        ImGui.TextDisabled($"（共 {logCount} 条，双击可重新打开导航）");
        ImGui.Separator();

        var availHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("LogContent", new Vector2(0, availHeight), true);

        for (var i = 0; i < snapshot.Count; i++)
        {
            var entry = snapshot[i];
            var color = entry.Level switch
            {
                LogLevel.Success => new Vector4(0.3f, 0.9f, 0.3f, 1f),
                LogLevel.Warning => new Vector4(1f, 0.85f, 0.2f, 1f),
                LogLevel.Error   => new Vector4(0.9f, 0.3f, 0.2f, 1f),
                _                => new Vector4(0.85f, 0.85f, 0.85f, 1f)
            };

            // 导航目标条目用高亮色
            if (entry.HasNavigationTarget)
            {
                color = new Vector4(0.3f, 0.85f, 1.0f, 1f);
            }

            ImGui.PushID(i);
            var stateLabel = GetLogStateLabel(entry);
            var displayText = string.IsNullOrEmpty(stateLabel) ? entry.Message : $"{entry.Message} [状态: {stateLabel}]";
            ImGui.TextColored(color, $"[{entry.Time:HH:mm:ss}] {displayText}");

            // 双击检测
            if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && entry.HasNavigationTarget)
            {
                if (entry.HuntMessage is not null)
                    _navigationWindow.ShowForHunt(entry.HuntMessage);
                else if (entry.FateMessage is not null)
                    _navigationWindow.ShowForFate(entry.FateMessage);
            }

            // 悬停提示
            if (entry.HasNavigationTarget && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("双击重新打开导航窗口");
            }
            ImGui.PopID();
        }

        if (logCount > 0)
            ImGui.SetScrollHereY(1.0f);
        ImGui.EndChild();
    }

    /// <summary>
    /// 添加一条导航日志（线程安全）。仅在消息推送到导航面板时调用。
    /// </summary>
    public void AddNavigationLog(string message, HuntMessage? huntMsg = null, FateMessage? fateMsg = null)
    {
        lock (_logLock)
        {
            _notificationLog.Add(new LogEntry(DateTime.Now, message, LogLevel.Info, huntMsg, fateMsg));
            while (_notificationLog.Count > MaxLogEntries)
                _notificationLog.RemoveAt(0);
        }
    }

    /// <summary>
    /// 根据日志条目携带的猎怪 / FATE 消息推算中文状态名
    /// （健康 / 已开怪 / 被暴打中 / 死亡）。无导航目标（普通信息）返回空串。
    /// </summary>
    private static string GetLogStateLabel(LogEntry entry)
    {
        HuntState state;
        if (entry.HuntMessage is not null)
            state = DataManager.GetHuntState(entry.HuntMessage.Health);
        else if (entry.FateMessage is not null)
            state = DataManager.GetFateState(entry.FateMessage.Progress);
        else
            return "";
        return DataManager.GetStateName(state);
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        _log.Information($"{Prefix} 主窗口已释放");
    }
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class LogEntry
{
    public DateTime Time { get; }
    public string Message { get; }
    public LogLevel Level { get; }
    public HuntMessage? HuntMessage { get; }
    public FateMessage? FateMessage { get; }

    public bool HasNavigationTarget => HuntMessage is not null || FateMessage is not null;

    public LogEntry(DateTime time, string message, LogLevel level,
        HuntMessage? huntMsg = null, FateMessage? fateMsg = null)
    {
        Time = time;
        Message = message;
        Level = level;
        HuntMessage = huntMsg;
        FateMessage = fateMsg;
    }
}
