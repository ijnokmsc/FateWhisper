namespace SilverDasher.Models;

/// <summary>
/// 猎怪/FATE 状态枚举，对齐 ACT 版 SilverDasher。
/// </summary>
public enum HuntState
{
    /// <summary>健康/未被攻击 (HP=100%) / FATE 刚开始</summary>
    Healthy,

    /// <summary>已被开怪 (HP > 95%) / FATE 进度 < 20%</summary>
    Taunted,

    /// <summary>被暴打中 (HP > 0%) / FATE 进行中</summary>
    Dying,

    /// <summary>已死亡或结束</summary>
    Died,

    /// <summary>未知</summary>
    Unknown
}
