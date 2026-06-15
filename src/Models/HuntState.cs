using Newtonsoft.Json;

namespace SilverDasher.Models;

/// <summary>
/// 猎怪状态模型，用于跟踪猎怪的存活/死亡/冷却状态。
/// </summary>
public class HuntState
{
    /// <summary>
    /// 猎怪 ID。
    /// </summary>
    [JsonProperty("mob_id")]
    public string MobId { get; set; } = "";

    /// <summary>
    /// 是否存活。
    /// </summary>
    [JsonProperty("is_alive")]
    public bool IsAlive { get; set; } = true;

    /// <summary>
    /// 上次死亡时间戳（Unix 毫秒）。
    /// </summary>
    [JsonProperty("last_kill_time")]
    public long LastKillTime { get; set; }

    /// <summary>
    /// 冷却时间（分钟）。
    /// </summary>
    [JsonProperty("cooldown_minutes")]
    public int CooldownMinutes { get; set; } = 120;

    /// <summary>
    /// 上次检测时间戳（Unix 毫秒）。
    /// </summary>
    [JsonProperty("last_seen_time")]
    public long LastSeenTime { get; set; }

    /// <summary>
    /// 检查冷却是否已过。
    /// </summary>
    public bool IsCooldownOver => !IsAlive &&
        LastKillTime > 0 &&
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - LastKillTime > CooldownMinutes * 60 * 1000;
}
