using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game.Gui.Toast;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using FateWhisper.Config;
using FateWhisper.Models;
using FateWhisper.UI;

namespace FateWhisper.Services;

/// <summary>
/// 通知服务 — ChatLog + 游戏 Toast + TTS 语音。
/// 支持逐状态通知开关、订阅列表过滤、去重（ACT 版兼容）。
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
    private readonly SubscriptionsStore _subs;
    private readonly TtsService _tts;
    // 导航日志回调：只在推送到导航面板时触发，携带格式化文本和原始消息
    private Action<string, HuntMessage?, FateMessage?>? _onNavLog;
    private const string Prefix = "[FateWhisper]";

    // 状态跟踪表: key="{type}-{id}" → 上次通知的状态
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HuntState> _trackedStates = new();

    public event Action<HuntMessage>? HuntNavigationPopupRequested;
    public event Action<FateMessage>? FateNavigationPopupRequested;

    /// <summary>
    /// 设置导航日志回调。当消息推送到导航面板时触发。
    /// </summary>
    public void SetNavLogCallback(Action<string, HuntMessage?, FateMessage?>? callback) => _onNavLog = callback;

    public NotificationService(
        IPluginLog log, IChatGui chatGui, IToastGui toastGui,
        INotificationManager notifManager, DataManager dataManager,
        DutyMonitor dutyMonitor, PluginConfig config, SubscriptionsStore subsStore)
    {
        _log = log;
        _chatGui = chatGui;
        _toastGui = toastGui;
        _notifManager = notifManager;
        _dataManager = dataManager;
        _dutyMonitor = dutyMonitor;
        _config = config;
        _subs = subsStore;
        _tts = new TtsService();
    }

    /// <summary>
    /// MQTT 猎怪消息处理管线：
    /// 大区过滤 → 订阅过滤 → 去重 → 副本过滤 → 通知 + 导航弹窗
    /// </summary>
    public void OnHuntBroadcast(HuntMessage message)
    {
        try
        {
            int mobId = message.Id;

            // ═══ 第1层：大区接收选项过滤 ═══
            if (message.IsCrossDc)
            { if (!_config.Notification.CrossDCHunt) return; }
            else
            { if (!_config.Notification.CrossWorldHunt) return; }

            // ═══ 第2层：订阅管理过滤 ═══
            if (mobId > 0 && !_subs.IsHuntSubscribed(mobId))
                return;

            // ═══ 第3层：重复消息过滤（状态机去重） ═══
            var state = DataManager.GetHuntState(message.Health);
            var trackKey = $"hunt-{mobId}";
            if (_trackedStates.TryGetValue(trackKey, out var lastState))
            {
                if (state == lastState) { _log.Debug($"{Prefix} [去重] {trackKey} 状态未变 ({state})，跳过"); return; }
                _trackedStates[trackKey] = state;
            }
            else { _trackedStates[trackKey] = state; }
            if (state == HuntState.Died) _trackedStates.TryRemove(trackKey, out _);

            // ═══ 第4层：副本状态过滤 ═══
            var isInDuty = _dutyMonitor.IsInDuty;

            // 补齐名称/Rank
            var mobName = _dataManager.LookupHuntName(message.MobId);
            if (!string.IsNullOrEmpty(mobName) && message.MobId != mobName)
                message.MobName = mobName;
            if (string.IsNullOrEmpty(message.Rank))
                message.Rank = _dataManager.LookupHuntRank(message.MobId);
            if (string.IsNullOrEmpty(message.TerritoryName))
                message.TerritoryName = _dataManager.LookupTerritoryName(message.Territory);
            if (string.IsNullOrEmpty(message.WorldName))
                message.WorldName = _dataManager.LookupWorldName(message.World.ToString());

            // 格式化文本
            var crossTag = message.IsCrossDc ? "[跨大区] " : "";
            var rankColor = GetRankColor(message.Rank);
            var displayWorld = message.WorldName ?? message.World.ToString();
            var stateTag = GetStateTag(state);
            var text = $"{crossTag}{rankColor}{stateTag}[{message.Rank}] {message.MobName} {PrefixSplit} {message.TerritoryName}({displayWorld})";

            SendNotification(_config.Notification.HuntPrefix, text, state);
            _log.Information($"{Prefix} 猎怪: {text}");

            // 导航弹窗始终触发（不受副本限制）
            HuntNavigationPopupRequested?.Invoke(message);
            _onNavLog?.Invoke(text, message, null);
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 猎怪播报异常: {ex.Message}");
        }
    }

    /// <summary>
    /// MQTT FATE 消息处理管线：
    /// 大区过滤 → 类型/订阅过滤 → 去重 → 副本过滤 → 通知 + 导航弹窗
    /// </summary>
    public void OnFateBroadcast(FateMessage message)
    {
        try
        {
            int fateId = message.Id;

            // ═══ 第1层：大区接收选项过滤 ═══
            if (message.IsCrossDc)
            { if (!_config.Notification.CrossDCHunt) return; }
            else
            { if (!_config.Notification.CrossWorldHunt) return; }

            // ═══ 第2层：FATE 类型 + 订阅管理过滤 ═══
            if (!_config.Notification.FateEnabled) return;
            if (message.IsSpecial && !_config.Notification.FateSpecial) return;
            if (!message.IsSpecial && !_config.Notification.FateCommon) return;
            if (fateId > 0 && !_subs.IsFateSubscribed(fateId))
                return;

            // ═══ 第3层：重复消息过滤（状态机去重） ═══
            var state = DataManager.GetFateState(message.Progress);
            var trackKey = $"fate-{fateId}";
            if (_trackedStates.TryGetValue(trackKey, out var lastState))
            {
                if (state == lastState) { _log.Debug($"{Prefix} [去重] {trackKey} 状态未变 ({state})，跳过"); return; }
                _trackedStates[trackKey] = state;
            }
            else { _trackedStates[trackKey] = state; }
            if (state == HuntState.Died) _trackedStates.TryRemove(trackKey, out _);

            // ═══ 第4层：副本状态过滤 ═══
            var isInDuty = _dutyMonitor.IsInDuty;

            // 补齐名称
            if (string.IsNullOrEmpty(message.FateName))
                message.FateName = _dataManager.LookupFateName(message.FateId);
            if (string.IsNullOrEmpty(message.TerritoryName))
                message.TerritoryName = _dataManager.LookupTerritoryName(message.Territory);
            if (string.IsNullOrEmpty(message.WorldName))
                message.WorldName = _dataManager.LookupWorldName(message.World.ToString());

            var specialTag = message.IsSpecial ? "[特殊] " : "";
            var displayWorld = message.WorldName ?? message.World.ToString();
            var stateTag = GetStateTag(state);
            var text = $"{specialTag}{stateTag}FATE: {message.FateName} {PrefixSplit} {message.TerritoryName}({displayWorld})";

            SendNotification(_config.Notification.FatePrefix, text, state);
            _log.Information($"{Prefix} {text}");

            // 导航弹窗始终触发（不受副本限制）
            FateNavigationPopupRequested?.Invoke(message);
            _onNavLog?.Invoke(text, null, message);
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} FATE 播报异常: {ex.Message}");
        }
    }

    private void SendNotification(string typePrefix, string text, HuntState state)
    {
        var fullText = $"{typePrefix} {text}";
        var stateKey = GetStateKey(state);
        var isInDuty = _dutyMonitor.IsInDuty;

        // ChatLog：副本内根据 MuteNotificationInDuty 决定
        if (_config.Notification.ChatLogEnabled &&
            !(isInDuty && _config.Notification.MuteNotificationInDuty))
        {
            try { _chatGui.Print(fullText); }
            catch (Exception ex) { _log.Warning($"{Prefix} ChatLog 失败: {ex.Message}"); }
        }

        // Toast：副本内根据 MuteNotificationInDuty 决定
        if (_config.Notification.ToastEnabled &&
            !(isInDuty && _config.Notification.MuteNotificationInDuty) &&
            _config.Notification.ToastStates.TryGetValue(stateKey, out var toastOk) && toastOk)
        {
            try { _toastGui.ShowNormal(fullText); }
            catch (Exception ex)
            {
                _log.Warning($"{Prefix} Toast 不可用({ex.Message})");
                if (!_config.Notification.ChatLogEnabled)
                    try { _chatGui.Print($"[SD] {fullText}"); } catch { }
            }
        }

        // TTS：副本内根据 MuteTtsInDuty 决定
        if (_config.Notification.TtsEnabled &&
            !(isInDuty && _config.Notification.MuteTtsInDuty) &&
            _config.Notification.TtsStates.TryGetValue(stateKey, out var ttsOk) && ttsOk)
        {
            var ttsText = fullText.Replace("★", "").Replace("●", "").Replace("○", "").Replace("→", ",");
            _tts.Stop();
            _ = Task.Run(() => _tts.SpeakAsync(ttsText));
        }
    }

    public void TestToast(string text)
    {
        var fullText = $"{_config.Notification.HuntPrefix} {text}";
        try { _toastGui.ShowNormal(fullText); }
        catch (Exception ex)
        {
            _log.Warning($"{Prefix} Toast 失败: {ex.Message}");
            try { _toastGui.ShowError(fullText); } catch { }
        }
    }

    public async Task TestTts(string text)
    {
        await _tts.SpeakAsync($"{_config.Notification.HuntPrefix} {text}");
    }

    private static string GetStateKey(HuntState state) => state switch
    {
        HuntState.Healthy => "healthy",
        HuntState.Taunted => "taunted",
        HuntState.Dying => "dying",
        HuntState.Died => "died",
        _ => "healthy"
    };

    private static string GetStateTag(HuntState state) => state switch
    {
        HuntState.Healthy => "🟢",
        HuntState.Taunted => "🟡",
        HuntState.Dying   => "🟠",
        HuntState.Died    => "💀",
        _ => ""
    };

    private static string GetRankColor(string? rank)
    {
        return rank?.ToUpperInvariant() switch
        {
            "S" => "★ ",
            "SS" => "★★ ",
            "A" => "● ",
            "B" => "○ ",
            _ => ""
        };
    }

    private const string PrefixSplit = "→";

    public void Dispose()
    {
        _tts.Dispose();
        _log.Information($"{Prefix} 通知服务已释放");
    }
}
