using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SilverDasher.Config;

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

    [JsonProperty("pause_in_duty")]
    public bool PauseInDuty { get; set; } = true;

    [JsonProperty("prefix")]
    public string Prefix { get; set; } = "[SD]";

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
}
