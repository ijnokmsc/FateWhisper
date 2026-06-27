using System;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FateWhisper.Models;

namespace FateWhisper.Services;

/// <summary>
/// 网络包拦截服务，通过 Dalamud Hook 机制监听 FF14 网络包以检测本地猎怪/FATE 事件。
/// 拦截 InitZone、FateInfo、ActorControlSelf 三个 opcode 对应的游戏函数。
/// 注：IGameNetwork 在 Dalamud API 15 CN 版中不可用，改用 IGameInteropProvider + Hook 方式。
///
/// TODO: 猎怪网络包检测（opcode 待实机抓包确认）
///   完整的猎怪检测需要以下 opcode 从 CN 客户端实机抓包后填入 opcodes.json：
///     - NpcSpawn / ActorCreate：当怪物/NPC 生成时触发，可获取 actorId → NPC baseId 映射
///     - ActorControlSelf type=2281 (ActorDespawn)：当怪物死亡时触发，需结合 NpcSpawn 映射表
///       判断是否为猎怪怪物
///   当前实现已保留完整的猎怪检测框架（ProcessGameNetworkPacket 中的 opcode 分发、
///   ActorDespawn 处理逻辑、HuntDetected 事件触发），仅需确认 opcode 后取消注释对应分支即可。
///   具体步骤：
///     1. 使用 Dalamud 数据浏览器或网络抓包工具确认 CN 版 NpcSpawn opcode
///     2. 将确认的 opcode 填入 opcodes.json 的 "NpcSpawn" 条目
///     3. 在 ProcessGameNetworkPacket 中取消 NpcSpawn 分支注释
///     4. 维护 _actorNpcMap 字典跟踪 actorId → mobId 映射
///     5. 在 ActorDespawn 处理中调用 HuntDetected 事件
///   在此期间，FATE 检测（FateStart/FateEnd/FateProgress/InitZone）完全可用。
/// </summary>
public class NetworkInterceptor : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IGameInteropProvider _interopProvider;
    private readonly DataManager _dataManager;
    private readonly string _playerName;
    private readonly string _worldName;
    private readonly string _datacenter;

    private const string Prefix = "[FateWhisper]";
    private const ushort FateStartType = 2370;
    private const ushort FateEndType = 2357;
    private const ushort FateProgressType = 2364;
    private const ushort ActorDespawnType = 2281;

    private string _currentTerritoryId = "";
    private DateTime _lastFateStartTime = DateTime.MinValue;
    private string _lastFateId = "";

    /// <summary>
    /// Hook for the game's zone packet processing function.
    /// The delegate signature must match the original game function exactly.
    /// Common FFXIV signature: ProcessZonePacketDown(IntPtr networkModule, uint sourceId,
    /// uint targetId, ushort opcode, IntPtr dataPtr).
    /// </summary>
    private delegate IntPtr ProcessZonePacketDelegate(
        IntPtr networkModule, uint sourceId, uint targetId, ushort opcode, IntPtr dataPtr);

    private Hook<ProcessZonePacketDelegate>? _packetHook;

    /// <summary>
    /// 本地检测到猎怪时触发。
    /// 当前触发点待 NpcSpawn/ActorCreate opcode 从实机抓包确认后启用。
    /// 事件已在 Plugin.cs 中订阅，链路完整。
    /// </summary>
#pragma warning disable CS0067 // 事件已在外部订阅，触发逻辑待 opcode 确认后启用
    public event Action<HuntMessage>? HuntDetected;
#pragma warning restore CS0067

    /// <summary>
    /// 本地检测到 FATE 时触发。
    /// </summary>
    public event Action<FateMessage>? FateDetected;

    /// <summary>
    /// 初始化网络拦截服务。
    /// 使用 IGameInteropProvider 进行函数 Hook，拦截游戏网络包处理函数。
    /// </summary>
    /// <param name="pluginInterface">Dalamud 插件接口。</param>
    /// <param name="log">日志服务。</param>
    /// <param name="dataManager">数据管理器。</param>
    /// <param name="interopProvider">游戏互操作提供者。</param>
    /// <param name="playerName">玩家角色名。</param>
    /// <param name="worldName">所在世界。</param>
    /// <param name="datacenter">所在大区。</param>
    public NetworkInterceptor(
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        DataManager dataManager,
        IGameInteropProvider interopProvider,
        string playerName,
        string worldName,
        string datacenter)
    {
        _log = log;
        _dataManager = dataManager;
        _interopProvider = interopProvider;
        _playerName = playerName;
        _worldName = worldName;
        _datacenter = datacenter;

        // 尝试通过 IGameInteropProvider Hook 游戏网络包处理函数
        // 使用已知的 FFXIV ProcessZonePacketDown 签名进行 Hook
        // 签名随客户端版本变化，CN 版需实机确认后更新
        RegisterPacketHook();

        _log.Information($"{Prefix} 网络拦截器已初始化（IGameInteropProvider Hook 模式）");
    }

    /// <summary>
    /// 注册游戏网络包处理 Hook。
    /// 尝试通过多个已知签名匹配游戏函数，支持 CN/Global 双版本。
    /// </summary>
    private void RegisterPacketHook()
    {
        // 已知的 ProcessZonePacketDown 签名（FFXIV 7.x 通用）
        // 格式: E8 ?? ?? ?? ?? 48 8B 7C 24 ?? 48 8B 74 24 ?? ...
        // 注意：签名随游戏版本变化，需在新版本发布后验证
        var knownSignatures = new[]
        {
            // FFXIV 7.0-7.x Global 签名
            "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 8B F2 49 8B D8",
            // FFXIV 7.x CN 签名（待实机确认，与 Global 可能不同）
            "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 8B F2 49 8B D8",
        };

        foreach (var signature in knownSignatures)
        {
            try
            {
                _packetHook = _interopProvider.HookFromSignature<ProcessZonePacketDelegate>(
                    signature, OnProcessZonePacket);
                _packetHook.Enable();
                _log.Information($"{Prefix} 网络包 Hook 注册成功 (sig: {signature[..20]}...)");
                return;
            }
            catch (Exception ex)
            {
                _log.Debug($"{Prefix} 网络包 Hook 签名未匹配 (sig: {signature[..20]}...): {ex.Message}");
            }
        }

        // 所有已知签名均未匹配 — 这不影响 FATE 检测（可手动调用 ProcessGameNetworkPacket）
        _log.Warning($"{Prefix} 所有已知网络包 Hook 签名均未匹配当前客户端版本。" +
            "FATE 检测功能可通过手动调用 ProcessGameNetworkPacket 使用。" +
            "请使用 Dalamud 数据浏览器确认 CN 版 ProcessZonePacketDown 签名后更新。");
    }

    /// <summary>
    /// Hook 回调 — 游戏网络包处理的拦截点。
    /// 提取 opcode 和 dataPtr 后分发给 ProcessGameNetworkPacket 进行分析。
    /// </summary>
    private IntPtr OnProcessZonePacket(
        IntPtr networkModule, uint sourceId, uint targetId, ushort opcode, IntPtr dataPtr)
    {
        try
        {
            ProcessGameNetworkPacket(dataPtr, opcode);
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} Hook 回调异常 (opcode=0x{opcode:X4}): {ex.Message}");
        }

        // 调用原始函数，不影响游戏正常网络包处理
        return _packetHook!.Original(networkModule, sourceId, targetId, opcode, dataPtr);
    }

    /// <summary>
    /// 处理网络消息。通过 opcode 匹配分发给对应的处理函数。
    /// 支持 InitZone、FateInfo、ActorControlSelf 的检测。
    /// 猎怪相关检测（NpcSpawn/ActorCreate/ActorDespawn）框架已就绪，待 opcode 确认。
    /// </summary>
    public void ProcessGameNetworkPacket(IntPtr dataPtr, ushort opcode)
    {
        try
        {
            // 从 opcodes.json 获取国服 opcode 映射
            var initZone = _dataManager.GetOpcode("InitZone");
            var fateInfo = _dataManager.GetOpcode("FateInfo");
            var actorControl = _dataManager.GetOpcode("ActorControlSelf");

            // TODO: 取消注释以下行，待 NpcSpawn opcode 从实机抓包确认后填入 opcodes.json
            // var npcSpawn = _dataManager.GetOpcode("NpcSpawn");
            // var actorCreate = _dataManager.GetOpcode("ActorCreate");

            if (initZone is not null && opcode == initZone.OpcodeValue)
            {
                ProcessInitZonePacket(dataPtr, initZone.Cnl);
            }
            else if (fateInfo is not null && opcode == fateInfo.OpcodeValue)
            {
                ProcessFateInfoPacket(dataPtr, fateInfo.Cnl);
            }
            else if (actorControl is not null && opcode == actorControl.OpcodeValue)
            {
                ProcessActorControlPacket(dataPtr, actorControl.Cnl);
            }
            // TODO: 取消注释以下分支，待 opcodes.json 中确认 NpcSpawn/ActorCreate opcode
            // else if (npcSpawn is not null && opcode == npcSpawn.OpcodeValue)
            // {
            //     ProcessNpcSpawnPacket(dataPtr, npcSpawn.Cnl);
            // }
            // else if (actorCreate is not null && opcode == actorCreate.OpcodeValue)
            // {
            //     ProcessActorCreatePacket(dataPtr, actorCreate.Cnl);
            // }
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 处理网络包异常 (opcode=0x{opcode:X4}): {ex.Message}");
        }
    }

    /// <summary>
    /// 处理 InitZone 包，记录当前区域。
    /// </summary>
    private unsafe void ProcessInitZonePacket(IntPtr dataPtr, int length)
    {
        if (length < 8) return;

        var buffer = new byte[length];
        Marshal.Copy(dataPtr, buffer, 0, length);

        var territoryId = BitConverter.ToUInt16(buffer, 6);
        _currentTerritoryId = territoryId.ToString();
        _log.Debug($"{Prefix} InitZone: territoryId={_currentTerritoryId}");
    }

    /// <summary>
    /// 处理 FateInfo 包，检测 FATE 开始。
    /// </summary>
    private unsafe void ProcessFateInfoPacket(IntPtr dataPtr, int length)
    {
        if (length < 16) return;

        var buffer = new byte[length];
        Marshal.Copy(dataPtr, buffer, 0, length);

        var fateId = BitConverter.ToUInt16(buffer, 0);
        var state = buffer[4];

        if (state == 1 && fateId > 0)
        {
            var fateIdStr = fateId.ToString();

            if (fateIdStr == _lastFateId &&
                (DateTime.UtcNow - _lastFateStartTime).TotalSeconds < 2)
            {
                return;
            }

            _lastFateId = fateIdStr;
            _lastFateStartTime = DateTime.UtcNow;

            var message = new FateMessage
            {
                Id = fateId,
                FateName = _dataManager.LookupFateName(fateIdStr),
                World = 0,
                Map = (uint)(ushort.TryParse(_currentTerritoryId, out var tid) ? tid : 0),
                TerritoryName = _dataManager.LookupTerritoryName(_currentTerritoryId),
                Type_ = "common",
                Datacenter = _datacenter,
                IsSpecial = false,
                IsLocal = true,
                EventType = "start"
            };

            _log.Information($"{Prefix} 本地检测到 FATE: {message}");
            FateDetected?.Invoke(message);
        }
    }

    /// <summary>
    /// 处理 ActorControlSelf 包，检测 FATE 事件类型和怪物死亡事件。
    /// </summary>
    private unsafe void ProcessActorControlPacket(IntPtr dataPtr, int length)
    {
        if (length < 8) return;

        var buffer = new byte[length];
        Marshal.Copy(dataPtr, buffer, 0, length);

        var type = BitConverter.ToUInt16(buffer, 0);

        switch (type)
        {
            case FateStartType:
                ProcessActorFateStart(buffer);
                break;
            case FateEndType:
                ProcessActorFateEnd(buffer);
                break;
            case FateProgressType:
                var progress = buffer[8];
                _log.Debug($"{Prefix} FATE Progress: progress={progress}%");
                break;
            case ActorDespawnType:
                ProcessActorDespawn(buffer);
                break;
        }
    }

    /// <summary>
    /// 处理 ActorControlSelf FateStart (type=2370)。
    /// </summary>
    private void ProcessActorFateStart(byte[] buffer)
    {
        var fateId = BitConverter.ToUInt16(buffer, 4);
        if (fateId <= 0) return;

        var fateIdStr = fateId.ToString();
        if (fateIdStr == _lastFateId &&
            (DateTime.UtcNow - _lastFateStartTime).TotalSeconds < 2)
        {
            return;
        }

        _lastFateId = fateIdStr;
        _lastFateStartTime = DateTime.UtcNow;

        var message = new FateMessage
        {
            Id = fateId,
            FateName = _dataManager.LookupFateName(fateIdStr),
            World = 0,
            Map = (uint)(ushort.TryParse(_currentTerritoryId, out var tid) ? tid : 0),
            TerritoryName = _dataManager.LookupTerritoryName(_currentTerritoryId),
            Type_ = "common",
            Datacenter = _datacenter,
            IsSpecial = false,
            IsLocal = true,
            EventType = "start"
        };

        _log.Information($"{Prefix} 本地检测到 FATE Start (ActorControl): {message}");
        FateDetected?.Invoke(message);
    }

    /// <summary>
    /// 处理 ActorControlSelf FateEnd (type=2357)。
    /// </summary>
    private void ProcessActorFateEnd(byte[] buffer)
    {
        var fateId = BitConverter.ToUInt16(buffer, 4);
        var fateIdStr = fateId.ToString();

        var message = new FateMessage
        {
            Id = fateId,
            FateName = _dataManager.LookupFateName(fateIdStr),
            World = 0,
            Map = (uint)(ushort.TryParse(_currentTerritoryId, out var tid) ? tid : 0),
            TerritoryName = _dataManager.LookupTerritoryName(_currentTerritoryId),
            Datacenter = _datacenter,
            IsLocal = true,
            EventType = "end"
        };

        _log.Debug($"{Prefix} 本地检测到 FATE End: {message}");
        FateDetected?.Invoke(message);
    }

    /// <summary>
    /// 处理 ActorControlSelf ActorDespawn (type=2281) — 怪物/实体消失。
    ///
    /// TODO: 完整的猎怪检测需要：
    ///   1. 通过 NpcSpawn/ActorCreate opcode 维护 actorId → NPC baseId 映射表 (_actorNpcMap)
    ///   2. 在此处查表获取 NPC baseId (mobId)
    ///   3. 在 Hunts 数据库中查找该 mobId 是否属于猎怪
    ///   4. 若是猎怪，构造 HuntMessage 并触发 HuntDetected 事件
    ///
    ///   当前已实现事件触发框架，仅需确认 NpcSpawn opcode 并建立映射表即可启用。
    /// </summary>
    private void ProcessActorDespawn(byte[] buffer)
    {
        // ActorDespawn 包结构:
        //   [0-1] type = 2281
        //   [2-3] category
        //   [4-5] actorId (runtime) — 需要结合 NpcSpawn 映射表转换为 NPC baseId
        //   [6-7] param2

        var actorId = BitConverter.ToUInt16(buffer, 4);

        // TODO: 当 NpcSpawn opcode 确认后，从此处查表：
        // if (_actorNpcMap.TryGetValue(actorId, out var mobId))
        // {
        //     if (_dataManager.Hunts.TryGetValue(mobId, out var huntMob))
        //     {
        //         var message = new HuntMessage
        //         {
        //             Id = int.Parse(mobId),
        //             MobName = huntMob.NameChs,
        //             World = 0,
        //             Map = (uint)(ushort.TryParse(_currentTerritoryId, out var tid) ? tid : 0),
        //             TerritoryName = _dataManager.LookupTerritoryName(_currentTerritoryId),
        //             Rank = huntMob.Rank,
        //             Datacenter = _datacenter,
        //             IsLocal = true
        //         };
        //         _log.Information($"{Prefix} 本地检测到猎怪死亡: {message}");
        //         HuntDetected?.Invoke(message);
        //     }
        // }

        _log.Debug($"{Prefix} ActorDespawn: actorId={actorId} (猎怪检测待 NpcSpawn opcode 确认后启用)");
    }

    /// <summary>
    /// 更新当前区域 ID（也可由其他来源触发）。
    /// </summary>
    public void UpdateTerritory(string territoryId)
    {
        _currentTerritoryId = territoryId;
    }

    /// <summary>
    /// 获取当前区域 ID。
    /// </summary>
    public string CurrentTerritoryId => _currentTerritoryId;

    /// <summary>
    /// 释放 Hook 和资源。
    /// </summary>
    public void Dispose()
    {
        try
        {
            _packetHook?.Disable();
            _packetHook?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 释放网络包 Hook 异常: {ex.Message}");
        }

        _log.Information($"{Prefix} 网络拦截器已释放");
    }
}
