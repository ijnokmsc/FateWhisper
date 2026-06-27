using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FateWhisper.Config;
using FateWhisper.Models;
using FateWhisper.Services;

namespace FateWhisper.UI.Tabs;

/// <summary>
/// 调试 Tab — 调试开关 + 实时调试信息。
/// </summary>
public class DebugTab
{
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private readonly MqttService _mqttService;
    private readonly NavigationService _navigationService;
    private readonly NotificationService _notificationService;

    // 调试日志缓冲（仅本 Tab 显示，不持久化）
    private readonly List<string> _debugLog = new();
    private const int MaxDebugLogEntries = 100;
    private bool _autoScroll = true;

    public DebugTab(
        PluginConfig config,
        IPluginLog log,
        MqttService mqttService,
        NavigationService navigationService,
        NotificationService notificationService)
    {
        _config = config;
        _log = log;
        _mqttService = mqttService;
        _navigationService = navigationService;
        _notificationService = notificationService;
    }

    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.7f, 0.3f, 1.0f), "调试开关");
        ImGui.SameLine();
        ImGui.TextDisabled("（开关状态持久保存）");
        ImGui.Spacing();

        DrawSwitches();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawNavigationDiagnostics();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawDebugLog();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawTestButtons();
    }

    /// <summary>
    /// 绘制调试开关 checkbox。
    /// </summary>
    private void DrawSwitches()
    {
        var dbg = _config.Debug;

        var mqttMsg = dbg.MqttMessages;
        if (DrawCheckbox("MQTT 消息##dbg_mqtt", ref mqttMsg,
            "开启后，MQTT 收到的每条消息都会输出到 Dalamud 日志"))
        {
            dbg.MqttMessages = mqttMsg;
            _config.Save();
            _log.Information($"[FateWhisper] 调试开关 MQTT消息 = {dbg.MqttMessages}");
        }

        var huntTrig = dbg.HuntTriggers;
        if (DrawCheckbox("猎怪播报触发##dbg_hunt", ref huntTrig,
            "开启后，猎怪播报触发时输出详细信息（ID/HP/地图/服务器）"))
        {
            dbg.HuntTriggers = huntTrig;
            _config.Save();
            _log.Information($"[FateWhisper] 调试开关 猎怪触发 = {dbg.HuntTriggers}");
        }

        var fateTrig = dbg.FateTriggers;
        if (DrawCheckbox("FATE 播报触发##dbg_fate", ref fateTrig,
            "开启后，FATE 播报触发时输出详细信息（ID/进度/地图/服务器）"))
        {
            dbg.FateTriggers = fateTrig;
            _config.Save();
            _log.Information($"[FateWhisper] 调试开关 FATE触发 = {dbg.FateTriggers}");
        }

        var nav = dbg.Navigation;
        if (DrawCheckbox("导航诊断##dbg_nav", ref nav,
            "开启后，导航决策全过程输出诊断日志（传送判断、区域匹配、路径查找等）"))
        {
            dbg.Navigation = nav;
            _config.Save();
            _log.Information($"[FateWhisper] 调试开关 导航诊断 = {dbg.Navigation}");
        }
    }

    /// <summary>
    /// 绘制单个 checkbox，变更时返回 true（由调用方负责写回属性并保存配置）。
    /// </summary>
    private bool DrawCheckbox(string label, ref bool value, string tooltip)
    {
        var changed = ImGui.Checkbox(label, ref value);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        return changed;
    }

    /// <summary>
    /// 绘制导航诊断面板 — 实时显示导航状态和传送决策依据。
    /// </summary>
    private void DrawNavigationDiagnostics()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "导航诊断");
        ImGui.Spacing();

        // 插件状态
        ImGui.Text("vnavmesh: ");
        ImGui.SameLine();
        var vnavOk = _navigationService.IsVnavmeshAvailable;
        ImGui.TextColored(
            vnavOk ? new System.Numerics.Vector4(0.3f, 1f, 0.3f, 1f) : new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f),
            vnavOk ? "已连接" : "未安装");

        ImGui.SameLine();
        ImGui.Text("| Lifestream: ");
        ImGui.SameLine();
        var lifeOk = _navigationService.IsLifestreamAvailable;
        ImGui.TextColored(
            lifeOk ? new System.Numerics.Vector4(0.3f, 1f, 0.3f, 1f) : new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f),
            lifeOk ? "已连接" : "未安装");

        ImGui.SameLine();
        ImGui.Text("| DCTraveler: ");
        ImGui.SameLine();
        var dcOk = _navigationService.IsDCTravelerAvailable;
        ImGui.TextColored(
            dcOk ? new System.Numerics.Vector4(0.3f, 1f, 0.3f, 1f) : new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1f),
            dcOk ? "已连接" : "未安装");

        if (dcOk)
        {
            ImGui.SameLine();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.3f, 1f), "⚠ 国服可能崩溃");
        }

        ImGui.Spacing();

        // 运行状态
        ImGui.Text($"当前区域: {_navigationService.CurrentTerritoryType}");
        ImGui.Text($"玩家世界: {_navigationService.PlayerWorldName}");
        ImGui.Text($"导航中: {_navigationService.IsNavigating}");

        if (_navigationService.IsLifestreamAvailable)
        {
            ImGui.SameLine();
            ImGui.Text($"| Lifestream 忙碌: {_navigationService.IsLifestreamBusy()}");
        }

        ImGui.Spacing();

        // 跨服配置
        ImGui.Separator();
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "跨服配置");
        ImGui.Spacing();

        var useDcTraveler = _config.CrossServer.UseDCTraveler;
        if (ImGui.Checkbox("允许使用 DCTraveler 跨DC（⚠️ 国服可能崩溃）", ref useDcTraveler))
        {
            _config.CrossServer.UseDCTraveler = useDcTraveler;
            _config.Save();
            _log.Information($"[FateWhisper] 跨服配置 UseDCTraveler = {useDcTraveler}");
        }

        var preferLifestream = _config.CrossServer.PreferLifestream;
        if (ImGui.Checkbox("优先使用 Lifestream", ref preferLifestream))
        {
            _config.CrossServer.PreferLifestream = preferLifestream;
            _config.Save();
            _log.Information($"[FateWhisper] 跨服配置 PreferLifestream = {preferLifestream}");
        }

        ImGui.Spacing();

        if (ImGui.Button("重新检测插件"))
        {
            _navigationService.ReDetectPlugins();
            _log.Information("[FateWhisper] 用户触发插件重新检测");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // 传送诊断说明
        ImGui.TextDisabled("传送决策逻辑:");
        ImGui.BulletText("同DC跨服 → Lifestream /li <世界名>（安全，不受 DCTraveler 影响）");
        ImGui.BulletText("跨DC → DCTraveler（优先，需手动开启）；失败回退 Lifestream");
        ImGui.BulletText("全部失败 → 提示手动跨服");

        if (!lifeOk && !dcOk)
        {
            ImGui.Spacing();
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.3f, 1f),
                "⚠ Lifestream 和 DCTraveler 均未安装，跨服导航将提示手动操作");
        }
    }

    /// <summary>
    /// 绘制调试日志区域。
    /// </summary>
    private void DrawDebugLog()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "调试日志");
        ImGui.SameLine();

        if (ImGui.SmallButton("清空"))
            _debugLog.Clear();

        ImGui.SameLine();
        ImGui.Checkbox("自动滚动##dbg_autoscroll", ref _autoScroll);

        ImGui.SameLine();
        if (ImGui.SmallButton("测试推送"))
        {
            _debugLog.Add($"[{DateTime.Now:HH:mm:ss}] 测试日志条目 #{_debugLog.Count + 1}");
        }

        ImGui.Separator();

        var availHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("DebugLogContent", new System.Numerics.Vector2(0, availHeight), true);

        foreach (var line in _debugLog)
        {
            ImGui.TextUnformatted(line);
        }

        if (_autoScroll && _debugLog.Count > 0)
            ImGui.SetScrollHereY(1.0f);

        ImGui.EndChild();
    }

    /// <summary>
    /// 添加一条调试日志（供外部服务调用）。
    /// </summary>
    public void AddDebugLog(string message)
    {
        _debugLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (_debugLog.Count > MaxDebugLogEntries)
            _debugLog.RemoveAt(0);
    }

    /// <summary>
    /// 测试按钮区域 — 用于手动测试通知各渠道是否正常。
    /// </summary>
    private void DrawTestButtons()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 1.0f, 0.6f, 1.0f), "通知测试");
        ImGui.Spacing();

        if (ImGui.Button("测试聊天框"))
        {
            _notificationService.OnHuntBroadcast(new HuntMessage
            {
                Id = 0, Rank = "S", MobName = "S级恶名精英·测试怪",
                TerritoryName = "拉诺西亚", WorldName = "萌芽池",
                Health = 100,
            });
        }

        ImGui.SameLine();

        if (ImGui.Button("测试 Toast"))
        {
            _notificationService.TestToast("S级恶名精英出现在拉诺西亚·萌芽池！");
        }

        ImGui.SameLine();

        if (ImGui.Button("测试 TTS"))
        {
            _ = _notificationService.TestTts("S级恶名精英出现在萌芽池");
        }
    }
}
