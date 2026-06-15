using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using SilverDasher.Config;
using SilverDasher.Services;

namespace SilverDasher.UI.Tabs;

/// <summary>
/// 通知设置 Tab，管理通知渠道开关 + 测试按钮。
/// </summary>
public class NotificationTab
{
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private readonly NotificationService _notification;
    private const string Prefix = "[SilverDasher]";

    public NotificationTab(PluginConfig config, IPluginLog log, NotificationService notification)
    {
        _config = config;
        _log = log;
        _notification = notification;
    }

    /// <summary>
    /// 绘制通知设置界面。
    /// </summary>
    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "通知设置");
        ImGui.Spacing();

        // 游戏 Toast 通知
        var toastEnabled = _config.Notification.ToastEnabled;
        if (ImGui.Checkbox("游戏内 Toast 通知", ref toastEnabled))
        {
            _config.Notification.ToastEnabled = toastEnabled;
            _config.Save();
        }
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  使用 FF14 原生 Toast 提示框（屏幕右下角弹出）。");

        ImGui.Spacing();

        // 游戏 Toast 通知
        var chatEnabled = _config.Notification.ChatLogEnabled;
        if (ImGui.Checkbox("游戏聊天框通知", ref chatEnabled))
        {
            _config.Notification.ChatLogEnabled = chatEnabled;
            _config.Save();
        }
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  在游戏聊天窗口中打印播报消息。");

        ImGui.Spacing();

        // 系统提示音
        var soundEnabled = _config.Notification.SoundEnabled;
        if (ImGui.Checkbox("系统提示音效（默认关闭）", ref soundEnabled))
        {
            _config.Notification.SoundEnabled = soundEnabled;
            _config.Save();
        }
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  收到播报时播放 Windows 系统提示音。");

        ImGui.Spacing();

        // TTS 语音播报
        var ttsEnabled = _config.Notification.TtsEnabled;
        if (ImGui.Checkbox("TTS 语音播报", ref ttsEnabled))
        {
            _config.Notification.TtsEnabled = ttsEnabled;
            _config.Save();
        }
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  使用系统语音引擎朗读播报内容。首次使用建议先测试。");

        ImGui.Spacing();

        // 副本暂停
        var pauseInDuty = _config.Notification.PauseInDuty;
        if (ImGui.Checkbox("副本内暂停通知", ref pauseInDuty))
        {
            _config.Notification.PauseInDuty = pauseInDuty;
            _config.Save();
        }
        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  进入副本后自动暂停所有播报通知，退出副本后恢复。");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 通知前缀
        ImGui.Text("通知消息前缀:");
        var prefix = _config.Notification.Prefix;
        if (ImGui.InputText("##prefix", ref prefix, 64))
        {
            _config.Notification.Prefix = prefix;
            _config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === 测试按钮 ===
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 1.0f, 0.6f, 1.0f), "测试");
        ImGui.Spacing();

        if (ImGui.Button("测试聊天框通知"))
        {
            _notification.OnHuntBroadcast(new Models.HuntMessage
            {
                MobId = "0",
                MobName = "S级恶名精英·测试怪",
                Rank = "S",
                Territory = "128",
                World = "萌芽池",
                IsCrossDc = false
            });
        }

        ImGui.SameLine();

        if (ImGui.Button("测试游戏 Toast"))
        {
            _notification.TestToast("S级恶名精英出现在拉诺西亚·萌芽池！");
            _log.Information($"{Prefix} 用户测试了游戏 Toast");
        }

        ImGui.SameLine();

        if (ImGui.Button("测试 TTS 语音"))
        {
            _ = _notification.TestTts("S级恶名精英出现在萌芽池，请注意查看");
            _log.Information($"{Prefix} 用户测试了 TTS 语音");
        }
    }
}
