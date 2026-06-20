using System;
using System.Threading.Tasks;
using Dalamud.Game.Gui.Toast;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using SilverDasher.Config;
using SilverDasher.Models;
using SilverDasher.UI;

namespace SilverDasher.Services;

/// <summary>
/// 通知服务 — ChatLog + 游戏 Toast + TTS 语音。
/// 支持逐状态通知开关（ACT 版兼容）。
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
    private Action<string, LogLevel>? _onLog;
    private const string Prefix = "[FateWhisper]";

    public event Action<HuntMessage>? HuntNavigationPopupRequested;
    public event Action<FateMessage>? FateNavigationPopupRequested;

    public void SetLogCallback(Action<string, LogLevel>? callback) => _onLog = callback;

    public NotificationService(
        IPluginLog log, IChatGui chatGui, IToastGui toastGui,
        INotificationManager notifManager, DataManager dataManager,
        DutyMonitor dutyMonitor, PluginConfig config)
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

    public void OnHuntBroadcast(HuntMessage message)
    {
        try
        {
            if (_config.Notification.PauseInDuty && _dutyMonitor.IsInDuty)
                return;

            // 跨大区/同大区过滤
            var rank = message.Rank ?? "";
            if (message.IsCrossDc)
            {
                if (!_config.Notification.CrossDCHunt) return;
            }
            else
            {
                if (!_config.Notification.CrossWorldHunt) return;
            }

            // 补齐名称/Rank
            var mobName = _dataManager.LookupHuntName(message.MobId);
            if (!string.IsNullOrEmpty(mobName) && message.MobId != mobName)
                message.MobName = mobName;
            if (string.IsNullOrEmpty(message.Rank))
                message.Rank = _dataManager.LookupHuntRank(message.MobId);
            if (string.IsNullOrEmpty(message.TerritoryName))
                message.TerritoryName = _dataManager.LookupTerritoryName(message.Territory);

            // 确定状态
            var state = DataManager.GetHuntState(message.Health);
            var stateName = DataManager.GetStateName(state);

            // 格式化文本
            var crossTag = message.IsCrossDc ? "[跨大区] " : "";
            var rankColor = GetRankColor(message.Rank);
            var displayWorld = message.WorldName ?? message.World.ToString();
            var text = $"{crossTag}{rankColor}[{message.Rank}] {message.MobName} {PrefixSplit} {message.TerritoryName}({displayWorld})";

            // 发送通知
            SendNotification(text, state);

            _log.Information($"{Prefix} 猎怪播报: {message}");
            _onLog?.Invoke($"{rankColor}[{message.Rank}] {message.MobName} → {message.TerritoryName}({displayWorld})", LogLevel.Info);

            if (!_dutyMonitor.IsInDuty)
                HuntNavigationPopupRequested?.Invoke(message);
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 猎怪播报异常: {ex.Message}");
        }
    }

    public void OnFateBroadcast(FateMessage message)
    {
        try
        {
            if (_config.Notification.PauseInDuty && _dutyMonitor.IsInDuty)
                return;

            if (!_config.Notification.FateEnabled) return;
            if (message.IsSpecial && !_config.Notification.FateSpecial) return;
            if (!message.IsSpecial && !_config.Notification.FateCommon) return;

            // 补齐名称
            if (string.IsNullOrEmpty(message.FateName))
                message.FateName = _dataManager.LookupFateName(message.FateId);
            if (string.IsNullOrEmpty(message.TerritoryName))
                message.TerritoryName = _dataManager.LookupTerritoryName(message.Territory);

            // FATE 状态由进度决定（ACT 版逻辑）
            var state = DataManager.GetFateState(message.Progress);
            var stateName = DataManager.GetStateName(state);

            var specialTag = message.IsSpecial ? "[特殊] " : "";
            var displayWorld = message.WorldName ?? message.World.ToString();
            var text = $"{specialTag}FATE: {message.FateName} {PrefixSplit} {message.TerritoryName}({displayWorld})";

            SendNotification(text, state);

            _log.Information($"{Prefix} FATE 播报: {message}");
            _onLog?.Invoke($"{specialTag}FATE: {message.FateName} → {message.TerritoryName}({displayWorld})", LogLevel.Info);

            if (!_dutyMonitor.IsInDuty)
                FateNavigationPopupRequested?.Invoke(message);
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} FATE 播报异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送通知 — 按状态分别检查 TTS/Toast 开关。
    /// </summary>
    private void SendNotification(string text, HuntState state)
    {
        var fullText = $"{_config.Notification.Prefix} {text}";
        var stateKey = GetStateKey(state);

        // 聊天框（无条件，不受逐状态控制）
        if (_config.Notification.ChatLogEnabled)
        {
            try { _chatGui.Print(fullText); }
            catch (Exception ex) { _log.Warning($"{Prefix} ChatLog 失败: {ex.Message}"); }
        }

        // Toast（按状态）
        if (_config.Notification.ToastEnabled &&
            _config.Notification.ToastStates.TryGetValue(stateKey, out var toastOk) && toastOk)
        {
            try
            {
                _toastGui.ShowNormal(fullText);
            }
            catch (Exception ex)
            {
                _log.Warning($"{Prefix} Toast 不可用({ex.Message})，回退聊天框");
                if (!_config.Notification.ChatLogEnabled)
                    try { _chatGui.Print($"[SD] {fullText}"); } catch { }
            }
        }

        // TTS 语音（按状态）
        if (_config.Notification.TtsEnabled &&
            _config.Notification.TtsStates.TryGetValue(stateKey, out var ttsOk) && ttsOk)
        {
            var ttsText = fullText.Replace("★", "").Replace("●", "").Replace("○", "").Replace("→", ",");
            _tts.Stop();
            _ = Task.Run(() => _tts.SpeakAsync(ttsText));
        }
    }

    public void TestToast(string text)
    {
        var fullText = $"{_config.Notification.Prefix} {text}";
        try { _toastGui.ShowNormal(fullText); }
        catch (Exception ex)
        {
            _log.Warning($"{Prefix} Toast 失败: {ex.Message}");
            try { _toastGui.ShowError(fullText); } catch { }
        }
    }

    public async Task TestTts(string text)
    {
        await _tts.SpeakAsync($"{_config.Notification.Prefix} {text}");
    }

    private static string GetStateKey(HuntState state) => state switch
    {
        HuntState.Healthy => "healthy",
        HuntState.Taunted => "taunted",
        HuntState.Dying => "dying",
        HuntState.Died => "died",
        _ => "healthy"
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
