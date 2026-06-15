using System;
using Dalamud.Game.Gui.Toast;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using SilverDasher.Config;
using SilverDasher.Models;

namespace SilverDasher.Services;

/// <summary>
/// 通知服务 — ChatLog + 游戏 Toast + ImGui 通知 + 系统蜂鸣。
/// </summary>
public class NotificationService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IChatGui _chatGui;
    private readonly IToastGui _toastGui;
    private readonly INotificationManager _notifManager;
    private readonly DataManager _dataManager;
    private readonly DutyMonitor _dutyMonitor;
    private readonly PluginConfig _config;
    private readonly TtsService _tts;
    private const string Prefix = "[SilverDasher]";

    public NotificationService(
        IPluginLog log,
        IChatGui chatGui,
        IToastGui toastGui,
        INotificationManager notifManager,
        DataManager dataManager,
        DutyMonitor dutyMonitor,
        PluginConfig config)
    {
        _log = log;
        _chatGui = chatGui;
        _toastGui = toastGui;
        _notifManager = notifManager;
        _dataManager = dataManager;
        _dutyMonitor = dutyMonitor;
        _config = config;
        _tts = new TtsService();
    }

    /// <summary>
    /// 处理远程猎怪播报事件。
    /// </summary>
    /// <param name="message">猎怪消息。</param>
    public void OnHuntBroadcast(HuntMessage message)
    {
        try
        {
            // 检查副本暂停
            if (_config.Notification.PauseInDuty && _dutyMonitor.IsInDuty)
            {
                _log.Debug($"{Prefix} 在副本中，暂停猎怪播报: {message}");
                return;
            }

            // 根据来源判断使用 CW 还是 CDC 配置
            if (message.IsCrossDc)
            {
                if (!_config.CDCHunt.Enabled || !_config.CDCHunt.IsRankEnabled(message.Rank))
                {
                    _log.Debug($"{Prefix} 跨大区猎怪未订阅 {message.Rank} 级，跳过: {message}");
                    return;
                }
            }
            else
            {
                if (!_config.CWHunt.Enabled || !_config.CWHunt.IsRankEnabled(message.Rank))
                {
                    _log.Debug($"{Prefix} 同大区猎怪未订阅 {message.Rank} 级，跳过: {message}");
                    return;
                }
            }

            // 翻译名称
            var mobName = _dataManager.LookupHuntName(message.MobId);
            var territoryName = _dataManager.LookupTerritoryName(message.Territory);
            var worldName = _dataManager.LookupWorldName(message.World);

            if (!string.IsNullOrEmpty(mobName) && message.MobId != mobName)
                message.MobName = mobName;
            if (!string.IsNullOrEmpty(territoryName))
                message.TerritoryName = territoryName;
            if (!string.IsNullOrEmpty(worldName))
                message.World = worldName;

            // 构造通知文本
            var crossTag = message.IsCrossDc ? "[跨大区] " : "";
            var rankColor = GetRankColor(message.Rank);
            var text = $"{crossTag}{rankColor}[{message.Rank}] {message.MobName} {PrefixSplit} {territoryName}({message.World})";

            // 输出通知
            SendNotification(text);

            _log.Information($"{Prefix} 猎怪播报: {message}");
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 处理猎怪播报异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 处理远程 FATE 播报事件。
    /// </summary>
    /// <param name="message">FATE 消息。</param>
    public void OnFateBroadcast(FateMessage message)
    {
        try
        {
            if (_config.Notification.PauseInDuty && _dutyMonitor.IsInDuty)
            {
                _log.Debug($"{Prefix} 在副本中，暂停 FATE 播报: {message}");
                return;
            }

            if (!_config.CWFate.Enabled)
            {
                _log.Debug($"{Prefix} FATE 播报未启用，跳过: {message}");
                return;
            }

            // 根据 FATE 类型检查订阅
            if (message.IsSpecial && !_config.CWFate.Special)
            {
                _log.Debug($"{Prefix} 特殊 FATE 未订阅，跳过: {message}");
                return;
            }
            if (!message.IsSpecial && !_config.CWFate.Common)
            {
                _log.Debug($"{Prefix} 普通 FATE 未订阅，跳过: {message}");
                return;
            }

            // 翻译名称
            var fateName = _dataManager.LookupFateName(message.FateId);
            var territoryName = _dataManager.LookupTerritoryName(message.Territory);
            var worldName = _dataManager.LookupWorldName(message.World);

            if (!string.IsNullOrEmpty(fateName) && message.FateId != fateName)
                message.FateName = fateName;
            if (!string.IsNullOrEmpty(territoryName))
                message.TerritoryName = territoryName;
            if (!string.IsNullOrEmpty(worldName))
                message.World = worldName;

            var specialTag = message.IsSpecial ? "[特殊] " : "";
            var text = $"{specialTag}FATE: {message.FateName} {PrefixSplit} {territoryName}({message.World})";

            SendNotification(text);

            _log.Information($"{Prefix} FATE 播报: {message}");
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 处理 FATE 播报异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送通知 — ChatLog + 系统音效 + TTS（按配置）。
    /// </summary>
    private void SendNotification(string text)
    {
        var fullText = $"{_config.Notification.Prefix} {text}";

        // 聊天框
        if (_config.Notification.ChatLogEnabled)
        {
            try { _chatGui.Print(fullText); }
            catch (Exception ex) { _log.Warning($"{Prefix} ChatLog 失败: {ex.Message}"); }
        }

        // 游戏原生 Toast
        if (_config.Notification.ToastEnabled)
        {
            try
            {
                _toastGui.ShowNormal(fullText);
                _log.Debug($"{Prefix} Toast 已发送");
            }
            catch (Exception ex)
            {
                _log.Warning($"{Prefix} Toast 不可用({ex.Message})，回退聊天框");
                if (!_config.Notification.ChatLogEnabled)
                    try { _chatGui.Print($"[SD] {fullText}"); }
                    catch { }
            }
        }

        // TTS 语音播报
        if (_config.Notification.TtsEnabled)
        {
            var ttsText = fullText.Replace("★","").Replace("●","").Replace("○","").Replace("→", ",");
            _tts.Stop();
            _ = Task.Run(() => _tts.SpeakAsync(ttsText));
        }
    }

    public void TestToast(string text)
    {
        var fullText = $"{_config.Notification.Prefix} {text}";
        // 尝试不带 Options 的简化调用
        try
        {
            _toastGui.ShowNormal(fullText);
            _log.Information($"{Prefix} Toast(plain) 已发送");
        }
        catch (Exception ex)
        {
            _log.Warning($"{Prefix} Toast(plain) 失败: {ex.Message}");
            try
            {
                _toastGui.ShowError(fullText);
                _log.Information($"{Prefix} Toast(error) 已发送");
            }
            catch (Exception ex2)
            {
                _log.Warning($"{Prefix} Toast(error) 也失败: {ex2.Message}");
                try { _chatGui.Print($"[ToastTest] {fullText}"); }
                catch { }
            }
        }
    }

    public async Task TestTts(string text)
    {
        await _tts.SpeakAsync($"{_config.Notification.Prefix} {text}");
    }

    /// <summary>
    /// 获取猎怪等级对应的显示颜色标记。
    /// </summary>
    private static string GetRankColor(string rank)
    {
        return rank.ToUpperInvariant() switch
        {
            "S" => "★ ",
            "SS" => "★★ ",
            "A" => "● ",
            "B" => "○ ",
            _ => ""
        };
    }

    /// <summary>
    /// 分隔符（用于文本格式化）。
    /// </summary>
    private const string PrefixSplit = "→";

    /// <summary>
    /// 清理资源。
    /// </summary>
    public void Dispose()
    {
        _tts.Dispose();
        _log.Information($"{Prefix} 通知服务已释放");
    }
}
