using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FateWhisper.Config;
using FateWhisper.Models;
using FateWhisper.Services;

namespace FateWhisper.UI.Tabs;

/// <summary>
/// 通知设置 Tab — ACT 版风格。含逐状态通知开关 + 大区接收选项。
/// </summary>
public class NotificationTab
{
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private readonly NotificationService _notification;
    private const string Prefix = "[FateWhisper]";

    private static readonly (string Key, string Label)[] StateLabels =
    [
        ("healthy", "健康（发现）"),
        ("taunted", "已开怪"),
        ("dying",   "被暴打中"),
        ("died",    "死亡"),
    ];

    public NotificationTab(PluginConfig config, IPluginLog log, NotificationService notification)
    {
        _config = config;
        _log = log;
        _notification = notification;
    }

    public void Draw()
    {
        DrawNotificationChannels();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawPerStateToggle();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawReceptionOptions();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawMiscSettings();
    }

    private void DrawNotificationChannels()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "通知渠道");
        ImGui.Spacing();

        var chat = _config.Notification.ChatLogEnabled;
        if (ImGui.Checkbox("游戏聊天框通知", ref chat))
        { _config.Notification.ChatLogEnabled = chat; _config.Save(); }

        var toast = _config.Notification.ToastEnabled;
        if (ImGui.Checkbox("游戏内 Toast 通知", ref toast))
        { _config.Notification.ToastEnabled = toast; _config.Save(); }
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  屏幕右下角弹出提示。");

        var tts = _config.Notification.TtsEnabled;
        if (ImGui.Checkbox("TTS 语音播报", ref tts))
        { _config.Notification.TtsEnabled = tts; _config.Save(); }
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  微软晓晓中文语音朗读。");
    }

    private void DrawPerStateToggle()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 1.0f, 0.6f, 1.0f), "逐状态通知（ACT 版风格）");
        ImGui.Spacing();

        // 表头
        ImGui.Columns(3, "stateCols", false);
        ImGui.SetColumnWidth(0, 120);
        ImGui.SetColumnWidth(1, 60);
        ImGui.SetColumnWidth(2, 60);
        ImGui.Text("状态"); ImGui.NextColumn();
        ImGui.Text("TTS"); ImGui.NextColumn();
        ImGui.Text("Toast"); ImGui.NextColumn();
        ImGui.Separator();

        foreach (var (key, label) in StateLabels)
        {
            ImGui.Text(label); ImGui.NextColumn();

            var ttsVal = _config.Notification.TtsStates.GetValueOrDefault(key, false);
            if (ImGui.Checkbox($"##tts_{key}", ref ttsVal))
            { _config.Notification.TtsStates[key] = ttsVal; _config.Save(); }
            ImGui.NextColumn();

            var toastVal = _config.Notification.ToastStates.GetValueOrDefault(key, false);
            if (ImGui.Checkbox($"##toast_{key}", ref toastVal))
            { _config.Notification.ToastStates[key] = toastVal; _config.Save(); }
            ImGui.NextColumn();
        }

        ImGui.Columns(1);
    }

    private void DrawReceptionOptions()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.6f, 0.2f, 1.0f), "大区接收选项");
        ImGui.Spacing();

        var cwHunt = _config.Notification.CrossWorldHunt;
        if (ImGui.Checkbox("同大区猎怪播报", ref cwHunt))
        { _config.Notification.CrossWorldHunt = cwHunt; _config.Save(); }

        var cdcHunt = _config.Notification.CrossDCHunt;
        if (ImGui.Checkbox("跨大区猎怪播报", ref cdcHunt))
        { _config.Notification.CrossDCHunt = cdcHunt; _config.Save(); }

        ImGui.Spacing();

        var fate = _config.Notification.FateEnabled;
        if (ImGui.Checkbox("FATE 播报", ref fate))
        { _config.Notification.FateEnabled = fate; _config.Save(); }

        if (_config.Notification.FateEnabled)
        {
            ImGui.Indent(16f);
            var common = _config.Notification.FateCommon;
            if (ImGui.Checkbox("普通 FATE", ref common))
            { _config.Notification.FateCommon = common; _config.Save(); }

            var special = _config.Notification.FateSpecial;
            if (ImGui.Checkbox("特殊 FATE", ref special))
            { _config.Notification.FateSpecial = special; _config.Save(); }
            ImGui.Unindent(16f);
        }
    }

    private void DrawMiscSettings()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f), "其他设置");
        ImGui.Spacing();

        var muteTts = _config.Notification.MuteTtsInDuty;
        if (ImGui.Checkbox("副本内不播报语音", ref muteTts))
        { _config.Notification.MuteTtsInDuty = muteTts; _config.Save(); }

        var muteNotif = _config.Notification.MuteNotificationInDuty;
        if (ImGui.Checkbox("副本内不显示通知", ref muteNotif))
        { _config.Notification.MuteNotificationInDuty = muteNotif; _config.Save(); }

        ImGui.TextDisabled("导航弹窗始终可用（不受上述选项影响）");

        ImGui.Spacing();

        var huntPrefix = _config.Notification.HuntPrefix;
        ImGui.Text("猎怪通知前缀:");
        ImGui.SameLine();
        if (ImGui.InputText("##hunt_prefix", ref huntPrefix, 64))
        { _config.Notification.HuntPrefix = huntPrefix; _config.Save(); }

        var fatePrefix = _config.Notification.FatePrefix;
        ImGui.Text("FATE 通知前缀:");
        ImGui.SameLine();
        if (ImGui.InputText("##fate_prefix", ref fatePrefix, 64))
        { _config.Notification.FatePrefix = fatePrefix; _config.Save(); }
    }

}
