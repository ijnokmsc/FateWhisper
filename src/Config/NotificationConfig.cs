using System;
using Newtonsoft.Json;

namespace SilverDasher.Config;

/// <summary>
/// 通知配置，控制通知渠道开关。
/// </summary>
[Serializable]
public class NotificationConfig
{
    /// <summary>
    /// 是否启用系统 Toast 通知。
    /// </summary>
    [JsonProperty("toast_enabled")]
    public bool ToastEnabled { get; set; } = true;

    /// <summary>
    /// 是否启用聊天框通知。
    /// </summary>
    [JsonProperty("chat_log_enabled")]
    public bool ChatLogEnabled { get; set; } = true;

    /// <summary>
    /// 是否启用系统提示音效。
    /// </summary>
    [JsonProperty("sound_enabled")]
    public bool SoundEnabled { get; set; } = false;

    /// <summary>
    /// 是否启用 TTS 语音播报。
    /// </summary>
    [JsonProperty("tts_enabled")]
    public bool TtsEnabled { get; set; } = true;

    /// <summary>
    /// 是否在副本内暂停通知。
    /// </summary>
    [JsonProperty("pause_in_duty")]
    public bool PauseInDuty { get; set; } = true;

    /// <summary>
    /// 通知消息前缀。
    /// </summary>
    [JsonProperty("prefix")]
    public string Prefix { get; set; } = "[SilverDasher]";
}
