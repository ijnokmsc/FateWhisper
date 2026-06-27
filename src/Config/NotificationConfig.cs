using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FateWhisper.Config;

/// <summary>
/// 通知配置，控制通知渠道和逐状态开关。
/// ACT 版兼容：TTSNotifications / ToastNotifications 的 Dictionary{HuntState, bool}。
/// </summary>
[Serializable]
public class NotificationConfig
{
    [JsonProperty("toast_enabled")]
    public bool ToastEnabled { get; set; } = true;

    [JsonProperty("chat_log_enabled")]
    public bool ChatLogEnabled { get; set; } = true;

    [JsonProperty("tts_enabled")]
    public bool TtsEnabled { get; set; } = true;

    /// <summary>副本内不播报 TTS 语音</summary>
    [JsonProperty("mute_tts_in_duty")]
    public bool MuteTtsInDuty { get; set; } = true;

    /// <summary>副本内不显示 Toast + ChatLog 通知（导航弹窗仍可触发）</summary>
    [JsonProperty("mute_notification_in_duty")]
    public bool MuteNotificationInDuty { get; set; } = false;

    /// <summary>旧版 pause_in_duty（仅反序列化兼容，不再序列化）</summary>
    [JsonProperty("pause_in_duty")]
    internal bool PauseInDutyLegacy { get; set; } = true;

    /// <summary>禁止序列化 PauseInDutyLegacy（Newtonsoft ShouldSerialize 模式）</summary>
    public bool ShouldSerializePauseInDutyLegacy() => false;

    [JsonProperty("hunt_prefix")]
    public string HuntPrefix { get; set; } = "猎怪";

    [JsonProperty("fate_prefix")]
    public string FatePrefix { get; set; } = "fate";

    // ===== 逐状态通知开关（ACT 版兼容） =====

    /// <summary>各状态是否播报 TTS</summary>
    [JsonProperty("tts_states")]
    public Dictionary<string, bool> TtsStates { get; set; } = new()
    {
        ["healthy"] = true,
        ["taunted"] = false,
        ["dying"] = false,
        ["died"] = true,
    };

    /// <summary>各状态是否显示 Toast</summary>
    [JsonProperty("toast_states")]
    public Dictionary<string, bool> ToastStates { get; set; } = new()
    {
        ["healthy"] = true,
        ["taunted"] = false,
        ["dying"] = false,
        ["died"] = true,
    };

    /// <summary>是否启用同大区狩猎接收</summary>
    [JsonProperty("cw_hunt_enabled")]
    public bool CrossWorldHunt { get; set; } = true;

    /// <summary>是否启用跨大区狩猎接收</summary>
    [JsonProperty("cdc_hunt_enabled")]
    public bool CrossDCHunt { get; set; } = false;

    /// <summary>是否启用 FATE 接收</summary>
    [JsonProperty("fate_enabled")]
    public bool FateEnabled { get; set; } = true;

    /// <summary>是否接收特殊 FATE</summary>
    [JsonProperty("fate_special")]
    public bool FateSpecial { get; set; } = true;

    /// <summary>是否接收普通 FATE</summary>
    [JsonProperty("fate_common")]
    public bool FateCommon { get; set; } = false;

    /// <summary>
    /// 从已保存配置复制所有属性（含旧版字段迁移）。
    /// </summary>
    public void CopyFrom(NotificationConfig saved)
    {
        if (saved is null) return;

        ToastEnabled = saved.ToastEnabled;
        ChatLogEnabled = saved.ChatLogEnabled;
        TtsEnabled = saved.TtsEnabled;
        MuteTtsInDuty = saved.MuteTtsInDuty;
        MuteNotificationInDuty = saved.MuteNotificationInDuty;
        HuntPrefix = saved.HuntPrefix ?? "猎怪";
        FatePrefix = saved.FatePrefix ?? "fate";
        TtsStates = saved.TtsStates ?? new Dictionary<string, bool>();
        ToastStates = saved.ToastStates ?? new Dictionary<string, bool>();
        CrossWorldHunt = saved.CrossWorldHunt;
        CrossDCHunt = saved.CrossDCHunt;
        FateEnabled = saved.FateEnabled;
        FateSpecial = saved.FateSpecial;
        FateCommon = saved.FateCommon;

        // 向后兼容：旧版 PauseInDuty=false → 用户曾明确关闭副本内所有暂停
        // 迁移为同时关闭语音和通知的副本内限制
        if (!saved.PauseInDutyLegacy)
        {
            MuteTtsInDuty = false;
            MuteNotificationInDuty = false;
        }
    }
}
