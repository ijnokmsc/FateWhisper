using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SilverDasher.Config;
using SilverDasher.Services;
using SilverDasher.UI.Tabs;

namespace SilverDasher.UI;

/// <summary>
/// ImGui 主窗口，包含 4 个功能 Tab。
/// </summary>
public class MainWindow : Window, IDisposable
{
    private readonly IPluginLog _log;
    private readonly PluginConfig _config;
    private readonly DataManager _dataManager;
    private readonly MqttService _mqttService;
    private readonly DutyMonitor _dutyMonitor;

    private readonly HuntTab _huntTab;
    private readonly FateTab _fateTab;
    private readonly NotificationTab _notificationTab;
    private readonly SystemTab _systemTab;
    private int _activeTab;

    // 通知日志
    private readonly List<LogEntry> _notificationLog = new();
    private const int MaxLogEntries = 200;

    private const string Prefix = "[SilverDasher]";

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
        NotificationService notificationService)
        : base($"FateWhisper {GetVersionString()}##FateWhisperMainWindow")
    {
        _log = log;
        _config = config;
        _dataManager = dataManager;
        _mqttService = mqttService;
        _dutyMonitor = dutyMonitor;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 350),
            MaximumSize = new Vector2(800, 700)
        };

        _huntTab = new HuntTab(config, dataManager, log);
        _fateTab = new FateTab(config, dataManager, log);
        _notificationTab = new NotificationTab(config, log, notificationService);
        _systemTab = new SystemTab(
            config, log, dataManager, mqttService, dutyMonitor);

        IsOpen = config.WindowVisible;
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
        if (ImGui.BeginTabBar("SilverDasherTabs"))
        {
            string[] tabNames = { "猎怪", "FATE", "通知", "系统", "日志" };
            for (var i = 0; i < tabNames.Length; i++)
            {
                if (ImGui.BeginTabItem($"{tabNames[i]}##tab{i}"))
                {
                    _activeTab = i;
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
    /// 绘制日志 Tab 内容。
    /// </summary>
    private void DrawLogTab()
    {
        if (ImGui.SmallButton("清空") && _notificationLog.Count > 0)
            _notificationLog.Clear();
        ImGui.SameLine();
        ImGui.TextDisabled($"（共 {_notificationLog.Count} 条，上限 {MaxLogEntries}）");
        ImGui.Separator();

        var availHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("LogContent", new Vector2(0, availHeight), true);
        foreach (var entry in _notificationLog)
        {
            var color = entry.Level switch
            {
                LogLevel.Success => new Vector4(0.3f, 0.9f, 0.3f, 1f),
                LogLevel.Warning => new Vector4(1f, 0.85f, 0.2f, 1f),
                LogLevel.Error   => new Vector4(0.9f, 0.3f, 0.2f, 1f),
                _                => new Vector4(0.85f, 0.85f, 0.85f, 1f)
            };
            ImGui.TextColored(color, $"[{entry.Time:HH:mm:ss}] {entry.Message}");
        }
        if (_notificationLog.Count > 0)
            ImGui.SetScrollHereY(1.0f);
        ImGui.EndChild();
    }

    /// <summary>
    /// 添加一条通知日志（线程安全）。
    /// </summary>
    public void AddLog(string message, LogLevel level = LogLevel.Info)
    {
        _notificationLog.Add(new LogEntry(DateTime.Now, message, level));
        while (_notificationLog.Count > MaxLogEntries)
            _notificationLog.RemoveAt(0);
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

    public LogEntry(DateTime time, string message, LogLevel level)
    {
        Time = time;
        Message = message;
        Level = level;
    }
}
