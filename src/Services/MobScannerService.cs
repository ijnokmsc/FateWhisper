using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FateWhisper.Models;

namespace FateWhisper.Services;

/// <summary>
/// 猎怪扫描服务 — IObjectTable 等效 ACT 版 Negotiator.ScanMobs。
/// 每帧扫描本地可见的 IBattleNpc，匹配已知猎怪数据，
/// 检测状态变化 (Healthy/Taunted/Dying/Died) 并发布通知。
/// </summary>
public class MobScannerService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IObjectTable _objectTable;
    private readonly IClientState _clientState;
    private readonly DataManager _dataManager;
    private readonly IFramework _framework;

    private readonly string _playerWorld;
    private readonly string _datacenter;
    private readonly uint _worldId;

    private const string Prefix = "[FateWhisper]";

    // 当前追踪的猎怪: key = "{territoryId}-{bNpcNameId}-{instance}"
    private readonly ConcurrentDictionary<string, TrackedMob> _currentMobs = new();

    // 上次扫描的完整 key 集合，用于检测消失的怪物
    private HashSet<string> _lastSeenKeys = new();

    /// <summary>检测到新猎怪时触发（首次发现）</summary>
    public event Action<HuntMessage>? HuntDetected;

    /// <summary>猎怪状态变更时触发（HP 变化导致 Healthy→Taunted→Dying）</summary>
    public event Action<HuntMessage>? HuntStatusChanged;

    /// <summary>猎怪消失/死亡时触发</summary>
    public event Action<HuntMessage>? HuntVanished;

    // 扫描间隔控制: 每 6 秒扫描一次 (360 帧 @ 60fps)
    private const int ScanIntervalFrames = 360;
    private int _frameCounter;

    // 已知猎怪 BNpcNameId 集合（快速查找）
    private readonly HashSet<int> _knownHuntIds;

    public MobScannerService(
        IPluginLog log,
        IObjectTable objectTable,
        IClientState clientState,
        DataManager dataManager,
        IFramework framework,
        string playerWorld,
        string datacenter,
        uint worldId)
    {
        _log = log;
        _objectTable = objectTable;
        _clientState = clientState;
        _dataManager = dataManager;
        _framework = framework;
        _playerWorld = playerWorld;
        _datacenter = datacenter;
        _worldId = worldId;

        // 构建快速查找集
        _knownHuntIds = [];
        foreach (var key in _dataManager.Hunts.Keys)
        {
            if (int.TryParse(key, out var id))
                _knownHuntIds.Add(id);
        }

        _log.Information($"{Prefix} 猎怪扫描服务已初始化，已知 {_knownHuntIds.Count} 种猎怪");

        // 注册每帧更新
        _framework.Update += OnFrameworkUpdate;
    }

    /// <summary>
    /// 每帧回调 — 按间隔调用 ScanMobs。
    /// </summary>
    private void OnFrameworkUpdate(IFramework framework)
    {
        // 未登录不扫描
        if (!_clientState.IsLoggedIn) return;

        _frameCounter++;
        if (_frameCounter < ScanIntervalFrames) return;
        _frameCounter = 0;

        try
        {
            ScanMobs();
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 猎怪扫描异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 核心扫描方法 — 等效 ACT 版 Negotiator.ScanMobs()。
    /// 遍历 IObjectTable 中的 IBattleNpc，匹配已知猎怪，
    /// 计算 HP% 和坐标，跟踪状态变化。
    /// </summary>
    private void ScanMobs()
    {
        var currentTerritory = _clientState.TerritoryType;
        var currentInstance = _clientState.Instance;
        var currentKeys = new HashSet<string>();

        foreach (var obj in _objectTable)
        {
            // 只处理战斗 NPC
            if (obj is not IBattleNpc npc) continue;
            if (npc.NameId == 0) continue;

            var mobId = (int)npc.NameId;

            // 检查是否为已知猎怪
            if (!_knownHuntIds.Contains(mobId)) continue;

            // 构建追踪 key
            var key = $"{currentTerritory}-{mobId}-{currentInstance}";
            currentKeys.Add(key);

            // 计算 HP 百分比 (Ceil 向上取整，与 ACT 版一致)
            var hpPercent = npc.MaxHp > 0
                ? (int)Math.Ceiling((double)npc.CurrentHp / npc.MaxHp * 100.0)
                : 100;

            // 坐标转换 (ACT 版公式)
            var coords = DataManager.GamePosToTransmissionCoord(
                npc.Position.X, npc.Position.Z);

            // 获取猎怪名称
            var mobIdStr = mobId.ToString();
            var mobName = _dataManager.LookupHuntName(mobIdStr);
            var huntMobData = _dataManager.Hunts.GetValueOrDefault(mobIdStr);

            if (_currentMobs.TryGetValue(key, out var tracked))
            {
                // 已追踪 — 检查 HP 是否变化
                if (tracked.Health == hpPercent) continue;

                tracked.Health = hpPercent;
                tracked.Coordinate = coords;

                var newState = DataManager.GetHuntState(hpPercent);
                if (newState != tracked.State)
                {
                    tracked.State = newState;
                    _log.Debug($"{Prefix} 猎怪状态变化: {mobName} ({mobId}) {tracked.State}");
                }

                // 发布状态变更
                var msg = BuildHuntMessage(mobId, mobName, huntMobData, hpPercent, coords,
                    currentTerritory, currentInstance);
                HuntStatusChanged?.Invoke(msg);
            }
            else
            {
                // 新猎怪 — 首次发现
                var newMob = new TrackedMob
                {
                    MobId = mobId,
                    Health = hpPercent,
                    Coordinate = coords,
                    Instance = currentInstance,
                    TerritoryId = currentTerritory,
                    State = DataManager.GetHuntState(hpPercent)
                };
                _currentMobs[key] = newMob;

                _log.Information($"{Prefix} 发现猎怪: {mobName} ({mobId}) HP={hpPercent}% @ {currentTerritory}");

                var msg = BuildHuntMessage(mobId, mobName, huntMobData, hpPercent, coords,
                    currentTerritory, currentInstance);
                HuntDetected?.Invoke(msg);
            }
        }

        // 检测消失的猎怪（不在本次扫描中）
        foreach (var key in _lastSeenKeys)
        {
            if (!currentKeys.Contains(key) && _currentMobs.TryRemove(key, out var gone))
            {
                var mobIdStr = gone.MobId.ToString();
                var mobName = _dataManager.LookupHuntName(mobIdStr);

                _log.Information($"{Prefix} 猎怪消失: {mobName} ({gone.MobId})");

                var msg = BuildHuntMessage(gone.MobId, mobName, null, 0, null,
                    gone.TerritoryId, gone.Instance);
                msg.Health = 0;
                HuntVanished?.Invoke(msg);
            }
        }

        _lastSeenKeys = currentKeys;
    }

    /// <summary>
    /// 构造 HuntMessage 用于事件触发。
    /// </summary>
    private HuntMessage BuildHuntMessage(int mobId, string? mobName,
        Models.HuntMob? huntMobData, int hpPercent,
        Coordinate? coords, uint territory, uint instance)
    {
        return new HuntMessage
        {
            Id = mobId,
            Health = hpPercent,
            Map = territory,
            Instance = instance,
            Coordinate = coords,
            TerritoryName = _dataManager.LookupTerritoryName(territory.ToString()),
            WorldName = _playerWorld,
            Datacenter = _datacenter,
            Rank = huntMobData?.Rank ?? "",
            MobName = mobName,
            IsLocal = true,
            IsCrossDc = false,
        };
    }

    /// <summary>
    /// 重置所有追踪状态（换区时调用）。
    /// </summary>
    public void Reset()
    {
        _currentMobs.Clear();
        _lastSeenKeys.Clear();
        _log.Debug($"{Prefix} 猎怪追踪已重置");
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
        _currentMobs.Clear();
        _log.Information($"{Prefix} 猎怪扫描服务已释放");
    }

    /// <summary>
    /// 运行时猎怪追踪记录。
    /// </summary>
    private class TrackedMob
    {
        public int MobId { get; init; }
        public int Health { get; set; }
        public Coordinate? Coordinate { get; set; }
        public uint Instance { get; init; }
        public uint TerritoryId { get; init; }
        public HuntState State { get; set; } = HuntState.Healthy;
    }
}
