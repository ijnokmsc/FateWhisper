using System;
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
    private readonly AuthService _authService;
    private readonly MqttService _mqttService;
    private readonly DutyMonitor _dutyMonitor;

    private readonly HuntTab _huntTab;
    private readonly FateTab _fateTab;
    private readonly NotificationTab _notificationTab;
    private readonly SystemTab _systemTab;
    private int _activeTab;

    private const string Prefix = "[SilverDasher]";

    /// <summary>
    /// 初始化主窗口及所有子 Tab。
    /// </summary>
    public MainWindow(
        IPluginLog log,
        PluginConfig config,
        DataManager dataManager,
        AuthService authService,
        MqttService mqttService,
        DutyMonitor dutyMonitor,
        NotificationService notificationService)
        : base("SilverDasher##SilverDasherMainWindow")
    {
        _log = log;
        _config = config;
        _dataManager = dataManager;
        _authService = authService;
        _mqttService = mqttService;
        _dutyMonitor = dutyMonitor;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 350),
            MaximumSize = new Vector2(800, 700)
        };

        _huntTab = new HuntTab(config, log);
        _fateTab = new FateTab(config, log);
        _notificationTab = new NotificationTab(config, log, notificationService);
        _systemTab = new SystemTab(
            config, log, dataManager, authService, mqttService, dutyMonitor);

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
            string[] tabNames = { "猎怪", "FATE", "通知", "系统" };
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
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        _log.Information($"{Prefix} 主窗口已释放");
    }
}
