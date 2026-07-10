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

    // 状态跟踪表: key="{type}-{id}" → (上次通知的状态, 上次通知时间)
    // 改为元组以支持"首报锁定 + 冷却窗口"去重（多源异步交错时避免重复播报）。
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (HuntState State, DateTime NotifiedAt)> _trackedStates = new();

    /// <summary>
    /// 非关键状态（Healthy/Taunted/Dying）的冷却窗口（秒）。
    /// 同一 {type}-{id} 在窗口内的状态变化将被合并，只保留首次通知。
    /// 关键状态 Died（击杀/完成）不受冷却限制，确保不漏报。
    /// </summary>
    private const int NonCriticalCooldownSeconds = 30;

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
            if (_trackedStates.TryGetValue(trackKey, out var last))
            {
                // 同状态：始终跳过（原始去重）
                if (state == last.State) { _log.Debug($"{Prefix} [去重] {trackKey} 状态未变 ({state})，跳过"); return; }

                // 非关键状态在冷却窗口内的变化：合并跳过（多源异步交错时防重复播报）
                if (state != HuntState.Died &&
                    (DateTime.UtcNow - last.NotifiedAt).TotalSeconds < NonCriticalCooldownSeconds)
                {
                    _log.Debug($"{Prefix} [去重] {trackKey} 冷却期内状态变化 ({last.State}→{state})，跳过");
                    return;
                }

                _trackedStates[trackKey] = (state, DateTime.UtcNow);
            }
            else { _trackedStates[trackKey] = (state, DateTime.UtcNow); }
            // 注意：Died 时不移除键，否则 HuntVanished(同样映射为 Died) 会在约 6 秒后
            // 再次入队并重复播报击杀通知。保留键可让 Vanished 命中 state==lastState 被去重吞掉；
            // 重生时 Detected@Healthy 因 Healthy!=Died 仍会正常重新通知。

            // 副本静音逻辑统一在 SendNotification 中按 Mute*InDuty 处理
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
            if (_trackedStates.TryGetValue(trackKey, out var last))
            {
                // 同状态：始终跳过（原始去重）
                if (state == last.State) { _log.Debug($"{Prefix} [去重] {trackKey} 状态未变 ({state})，跳过"); return; }

                // 非关键状态在冷却窗口内的变化：合并跳过（多源异步交错时防重复播报）
                if (state != HuntState.Died &&
                    (DateTime.UtcNow - last.NotifiedAt).TotalSeconds < NonCriticalCooldownSeconds)
                {
                    _log.Debug($"{Prefix} [去重] {trackKey} 冷却期内状态变化 ({last.State}→{state})，跳过");
                    return;
                }

                _trackedStates[trackKey] = (state, DateTime.UtcNow);
            }
            else { _trackedStates[trackKey] = (state, DateTime.UtcNow); }
            // 注意：Died 时不移除键，否则 HuntVanished(同样映射为 Died) 会在约 6 秒后
            // 再次入队并重复播报击杀通知。保留键可让 Vanished 命中 state==lastState 被去重吞掉；
            // 重生时 Detected@Healthy 因 Healthy!=Died 仍会正常重新通知。

            // 副本静音逻辑统一在 SendNotification 中按 Mute*InDuty 处理
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

        // TTS：副本内根据 MuteTtsInDuty 决定（串行播放，避免设备争用导致静音）
        if (_config.Notification.TtsEnabled &&
            !(isInDuty && _config.Notification.MuteTtsInDuty) &&
            _config.Notification.TtsStates.TryGetValue(stateKey, out var ttsOk) && ttsOk)
        {
            var ttsText = fullText.Replace("★", "").Replace("●", "").Replace("○", "").Replace("→", ",");
            _tts.SpeakAsync(ttsText);
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
