using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FateWhisper.Config;
using FateWhisper.Models;

namespace FateWhisper.Services;

public class NavigationService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IFramework _framework;
    private readonly IDataManager _dataManager;
    private readonly DataManager _fateDataManager;
    private readonly IObjectTable _objectTable;
    private IFramework.OnUpdateDelegate? _frameworkHandler;
    private string _playerWorldName;
    private readonly PluginConfig _config;

    private const string Prefix = "[FateWhisper]";

    // vnavmesh IPC
    private const string IpcMoveTo = "vnavmesh.SimpleMove.PathfindAndMoveTo";
    private const string IpcPathStop = "vnavmesh.Path.Stop";
    private const string IpcNavReady = "vnavmesh.Nav.IsReady";

    // Lifestream IPC
    private const string IpcChangeWorld = "Lifestream.ChangeWorld";
    private const string IpcLifestreamIsBusy = "Lifestream.IsBusy";
    private const string IpcExecuteCommand = "Lifestream.ExecuteCommand";
    private const string IpcLifestreamAbort = "Lifestream.Abort";

    // DailyRoutines IPC（管理 FastWorldTravel 模块）
    private const string IpcDRIsModuleEnabled = "DailyRoutines.IsModuleEnabled";
    private const string IpcDRLoadModule = "DailyRoutines.LoadModule";
    private const string IpcDRUnloadModule = "DailyRoutines.UnloadModule";
    private const string ModuleFastWorldTravel = "FastWorldTravel";

    private readonly IFateTable _fateTable;
    private readonly IChatGui _chatGui;
    private bool _vnavmeshAvailable;
    private bool _lifestreamAvailable;
    private bool _dailyRoutinesAvailable;
    private bool _fastWorldTravelWasEnabled;
    private bool _fastWorldTravelNotified;
    private bool _isNavigating;

    public event Action<bool>? NavigationStateChanged;
    public event Action<string>? StatusMessageChanged;

    public bool IsVnavmeshAvailable => _vnavmeshAvailable;
    public bool IsLifestreamAvailable => _lifestreamAvailable;
    public string LifestreamStatus => _lifestreamAvailable ? "可用" : "不可用";

    public bool IsNavigating
    {
        get => _isNavigating;
        private set
        {
            if (_isNavigating != value)
            {
                _isNavigating = value;
                NavigationStateChanged?.Invoke(value);
            }
        }
    }

    public string PlayerWorldName
    {
        get => _playerWorldName;
        set => _playerWorldName = value ?? "";
    }

    public uint CurrentTerritoryType
    {
        get { try { return _clientState.TerritoryType; } catch { return 0; } }
    }

    // Main city aetheryte coordinates
    public static readonly Dictionary<uint, Vector3> MainCityAetherytes = new()
    {
        { 128, new Vector3(-73f, 17f, -548f) },
        { 129, new Vector3(-73f, 17f, -548f) },
        { 130, new Vector3(25f, 4f, -27f) },
        { 131, new Vector3(25f, 4f, -27f) },
        { 132, new Vector3(157f, 1f, 205f) },
        { 133, new Vector3(157f, 1f, 205f) },
        { 418, new Vector3(53f, 32f, -10f) },
        { 419, new Vector3(53f, 32f, -10f) },
        { 635, new Vector3(-37f, 3f, 3f) },
        { 819, new Vector3(-36f, 3f, -48f) },
        { 820, new Vector3(-46f, 4f, 52f) },
        { 962, new Vector3(-111f, 16f, 64f) },
        { 1185, new Vector3(-24f, 37f, -573f) },
        // Endwalker 区域 → Sharlayan / Radz-at-Han
        { 956, new Vector3(-205f, 42f, -176f) },   // Thavnair → Radz-at-Han
        { 957, new Vector3(-24f, 37f, -573f) },    // Mare Lamentorum → Sharlayan
        { 958, new Vector3(-24f, 37f, -573f) },    // Ultima Thule → Sharlayan
        { 959, new Vector3(-24f, 37f, -573f) },    // Elpis → Sharlayan
    };

    // Navigation step state machine
    private enum NavStep { Idle, CrossServer, TeleportToTerritory, WaitAfterTeleport, WaitForFate, MountThenNavigate }

    private NavStep _navStep;
    private string _navTargetWorld = "";
    private uint _navTargetTerritory;
    private Vector3 _navTargetPos;
    private bool _navTargetFly;
    private uint _navLastTerritory;
    private string _navTargetFateName = "";
    private string _navTargetHuntName = "";

    // Mount wait
    private bool _pendingMountNav;
    private DateTime _mountStartTime;
    private const int MountTimeoutSeconds = 8;

    // IPC retry
    private int _navRetryCount;
    private int _navCompleteFrames; // 导航完成后确认帧数（防瞬时误判）
    private const int MaxNavRetries = 60;
    private const int NavRetryFeedbackThreshold = 15; // 超过此数时向用户显示等待状态
    private DateTime _fateLookupStartTime;
    private const int FateLookupTimeoutSeconds = 30; // FATE 查找超时

    // Teleport retry
    private DateTime _teleportStartTime;
    private int _teleportRetryCount;
    private const int TeleportTimeoutSeconds = 8;
    private const int MaxTeleportRetries = 5;

    // Cross-server timeout（含等待登录/更新世界名的时间）
    private DateTime _crossServerStartTime;
    private const int CrossServerTimeoutSeconds = 90;

    // Coordinate conversion (game ↔ transmission)
    private const float CoordDivisor = 100f;
    private const float CoordOffset = 21.5f;
    private const float CoordScale = 0.02f;

    // Post-teleport wait
    private int _postTeleportWaitFrames;
    private const int PostTeleportFrameDelay = 30; // 约 0.5 秒等待位置稳定

    // ==================== Constructor / Dispose ====================

    public NavigationService(
        IDalamudPluginInterface pluginInterface, IPluginLog log,
        IClientState clientState, ICondition condition, IFramework framework,
        IDataManager dataManager, DataManager fateDataManager, PluginConfig config, string playerWorldName,
        IObjectTable objectTable, IChatGui chatGui, IFateTable fateTable)
    {
        _pluginInterface = pluginInterface;
        _log = log;
        _clientState = clientState;
        _condition = condition;
        _framework = framework;
        _dataManager = dataManager;
        _fateDataManager = fateDataManager;
        _config = config;
        _playerWorldName = playerWorldName;
        _objectTable = objectTable;
        _chatGui = chatGui;
        _fateTable = fateTable;
        DetectVnavmesh();
        DetectDailyRoutines();
        DetectLifestream(); // 跨服传送统一走 Lifestream

        _chatGui.ChatMessage += OnChatMessage;
        _frameworkHandler = OnFrameworkUpdate;
        _framework.Update += _frameworkHandler;
        _clientState.ZoneInit += OnZoneInit;
    }

    /// <summary>
    /// 重新检测所有跨服插件状态（供 UI 调用）。
    /// </summary>
    public void ReDetectPlugins()
    {
        DetectVnavmesh();
        DetectLifestream();
        _log.Information($"{Prefix} 插件重新检测完成: vnavmesh={_vnavmeshAvailable}, lifestream={_lifestreamAvailable}");
    }

    public void Dispose()
    {
        _chatGui.ChatMessage -= OnChatMessage;
        if (_frameworkHandler != null)
        {
            _framework.Update -= _frameworkHandler;
            _frameworkHandler = null;
        }
        _clientState.ZoneInit -= OnZoneInit;
        CancelNavigation();
        _log.Information($"{Prefix} NavigationService disposed");
    }

    // ==================== Plugin detection ====================

    private void DetectVnavmesh()
    {
        try
        {
            _ = _pluginInterface.GetIpcSubscriber<bool>(IpcNavReady);
            _vnavmeshAvailable = true;
            _log.Information($"{Prefix} vnavmesh IPC connected");
        }
        catch
        {
            _vnavmeshAvailable = false;
            _log.Warning($"{Prefix} vnavmesh IPC not available, please install: https://github.com/awgil/ffxiv_navmesh");
        }
    }

    private bool CheckVnavmeshReady()
    {
        if (!_vnavmeshAvailable) return false;
        try { return _pluginInterface.GetIpcSubscriber<bool>(IpcNavReady).InvokeFunc(); }
        catch { return false; }
    }

    private void DetectDailyRoutines()
    {
        try
        {
            _ = _pluginInterface.GetIpcSubscriber<string, bool?>(IpcDRIsModuleEnabled);
            _dailyRoutinesAvailable = true;
            _log.Information($"{Prefix} DailyRoutines IPC connected");
        }
        catch
        {
            _dailyRoutinesAvailable = false;
        }
    }

    private bool? IsFastWorldTravelEnabled()
    {
        try
        {
            return _pluginInterface.GetIpcSubscriber<string, bool?>(IpcDRIsModuleEnabled)
                .InvokeFunc(ModuleFastWorldTravel);
        }
        catch { return null; }
    }

    private bool EnableFastWorldTravel()
    {
        try
        {
            _pluginInterface.GetIpcSubscriber<string, bool, bool>(IpcDRLoadModule)
                .InvokeFunc(ModuleFastWorldTravel, false);
            return true;
        }
        catch { return false; }
    }

    private bool DisableFastWorldTravel()
    {
        try
        {
            _pluginInterface.GetIpcSubscriber<string, bool, bool, bool>(IpcDRUnloadModule)
                .InvokeFunc(ModuleFastWorldTravel, false, false);
            return true;
        }
        catch { return false; }
    }

    private void TryManageFastWorldTravel(bool disable)
    {
        if (!_dailyRoutinesAvailable) return;
        try
        {
            var status = IsFastWorldTravelEnabled();
            if (status == null) return;
            if (disable)
            {
                if (status == true)
                {
                    if (!_fastWorldTravelNotified)
                    {
                        _fastWorldTravelNotified = true;
                        _log.Information($"{Prefix} FastWorldTravel 已启用，正在自动临时禁用");
                    }
                    _fastWorldTravelWasEnabled = true;
                    DisableFastWorldTravel();
                }
            }
            else if (_fastWorldTravelWasEnabled)
            {
                _fastWorldTravelWasEnabled = false;
                EnableFastWorldTravel();
            }
        }
        catch { }
    }

    private void DetectLifestream()
    {
        try
        {
            _ = _pluginInterface.GetIpcSubscriber<string, bool>(IpcChangeWorld);
            _lifestreamAvailable = true;
            _log.Information($"{Prefix} Lifestream IPC connected");
        }
        catch
        {
            _lifestreamAvailable = false;
        }
    }

    public bool IsLifestreamBusy()
    {
        if (!_lifestreamAvailable) return false;
        try { return _pluginInterface.GetIpcSubscriber<bool>(IpcLifestreamIsBusy).InvokeFunc(); }
        catch { return false; }
    }

    /// <summary>
    /// 通过 Lifestream ExecuteCommand IPC 发送 /li 命令。
    /// 返回 true 表示 Lifestream 接受了命令（IsBusy 变为 true）。
    /// </summary>
    public bool LifestreamExecuteCommand(string arguments)
    {
        if (!_lifestreamAvailable) return false;
        try
        {
            // 先检查 Lifestream 是否正忙：如果忙则不接受新命令
            if (IsLifestreamBusy())
            {
                NavDebug($"Lifestream ExecuteCommand('{arguments}'): IsBusy=true (already busy), rejected");
                _log.Warning($"{Prefix} Lifestream ExecuteCommand('{arguments}') 被拒绝（Lifestream 正忙）");
                return false;
            }

            _pluginInterface.GetIpcSubscriber<string, object>(IpcExecuteCommand).InvokeAction(arguments);
            // ExecuteCommand 返回 void，通过 IsBusy 判断是否被接受
            // 等一小段时间让 Lifestream 内部状态更新
            System.Threading.Thread.Sleep(50);
            var busy = _pluginInterface.GetIpcSubscriber<bool>(IpcLifestreamIsBusy).InvokeFunc();
            NavDebug($"Lifestream ExecuteCommand('{arguments}'): IsBusy={busy}");
            if (!busy)
            {
                _log.Warning($"{Prefix} Lifestream ExecuteCommand('{arguments}') 未被接受（IsBusy=false）");
                return false;
            }
            _log.Information($"{Prefix} Lifestream /li {arguments}: accepted");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} Lifestream ExecuteCommand error: {ex.Message}");
            return false;
        }
    }

    public bool ChangeWorld(string targetWorld)
    {
        // 首选：使用 ExecuteCommand（/li 命令），Lifestream 内部自动处理同DC/跨DC
        if (_lifestreamAvailable)
        {
            var ok = LifestreamExecuteCommand(targetWorld);
            if (ok) return true;
            // ExecuteCommand 失败，尝试旧方式
            NavDebug($"ExecuteCommand failed, falling back to ChangeWorld IPC");
        }

        // 备用：旧的 ChangeWorld IPC（兼容不支持 ExecuteCommand 的旧版 Lifestream）
        if (!_lifestreamAvailable) return false;
        try
        {
            var ok = _pluginInterface.GetIpcSubscriber<string, bool>(IpcChangeWorld).InvokeFunc(targetWorld);
            _log.Information($"{Prefix} Lifestream.ChangeWorld -> {targetWorld}: {(ok ? "ok" : "fail")}");
            return ok;
        }
        catch (Exception ex) { _log.Error($"{Prefix} Lifestream ChangeWorld error: {ex.Message}"); return false; }
    }

    // Teleport via Lumina Aetheryte sheet + Telepo
    private unsafe bool TeleportToTerritory(uint territoryId)
    {
        try
        {
            var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
            if (sheet == null) return false;
            var aetheryte = sheet.FirstOrDefault(a => a.Territory.RowId == territoryId && a.IsAetheryte);
            if (aetheryte.RowId == 0) return false;
            var telepo = Telepo.Instance();
            if (telepo == null) return false;
            telepo->Teleport(aetheryte.RowId, 0);
            _log.Information($"{Prefix} Teleport -> territory={territoryId} aetheryte={aetheryte.RowId}");
            return true;
        }
        catch (Exception ex) { _log.Warning($"{Prefix} Teleport failed: {ex.Message}"); return false; }
    }

    // ==================== Public navigation API ====================

    public string NavigateToHunt(HuntMessage msg)
    {
        NavDebug($"Hunt: {msg.MobName} w={msg.WorldName} t={msg.Territory}");
        if (!_vnavmeshAvailable) return SetError("vnavmesh not available");
        CancelNavigation();
        _navTargetHuntName = msg.MobName ?? "";
        _navTargetFateName = "";
        var w = msg.WorldName ?? msg.World.ToString();
        var tt = TryParseTerritory(msg.Territory);
        var pos = CoordToGamePos(msg.Coordinate);
        if (pos == null)
        {
            _log.Warning($"{Prefix} {msg.MobName}: 缺少坐标数据，将导航到目标区域");
            _navTargetWorld = w; _navTargetTerritory = tt != 0 ? tt : CurrentTerritoryType;
            _navTargetPos = Vector3.Zero; // sentinel: no exact coords — NavigateTo handles fallback
            _navTargetFly = false;
            return StartNavChain($"[{msg.Rank}] {msg.MobName} (区域导航)");
        }
        _navTargetWorld = w; _navTargetTerritory = tt != 0 ? tt : CurrentTerritoryType;
        _navTargetPos = pos.Value; _navTargetFly = CanFly(_navTargetTerritory);
        return StartNavChain($"[{msg.Rank}] {msg.MobName}");
    }

    public string NavigateToFate(FateMessage msg)
    {
        NavDebug($"Fate: {msg.FateName} w={msg.WorldName} t={msg.Territory}");
        if (!_vnavmeshAvailable) return SetError("vnavmesh not available");
        CancelNavigation();
        _navTargetFateName = msg.FateName ?? "";
        _navTargetHuntName = "";
        var w = msg.WorldName ?? msg.World.ToString();
        var tt = TryParseTerritory(msg.Territory);
        var pos = CoordToGamePos(msg.Coordinate);
        if (pos == null)
        {
            _log.Warning($"{Prefix} {msg.FateName}: 缺少坐标数据，将导航到目标区域");
            _navTargetWorld = w; _navTargetTerritory = tt != 0 ? tt : CurrentTerritoryType;
            _navTargetPos = Vector3.Zero; // sentinel: no exact coords — NavigateTo handles fallback
            _navTargetFly = false;
            return StartNavChain($"FATE {msg.FateName} (区域导航)");
        }
        _navTargetWorld = w; _navTargetTerritory = tt != 0 ? tt : CurrentTerritoryType;
        _navTargetPos = pos.Value; _navTargetFly = CanFly(_navTargetTerritory);
        return StartNavChain($"FATE {msg.FateName}");
    }

    private string StartNavChain(string label)
    {
        IsNavigating = true; _navLastTerritory = CurrentTerritoryType;
        if (!IsServerMatch(_navTargetWorld))
        {
            var isSameDc = IsSameDatacenter(_navTargetWorld);

            // ========== 跨服 → 统一走 Lifestream（不再区分同DC/跨DC） ==========
            TryManageFastWorldTravel(true);
            if (!_lifestreamAvailable) DetectLifestream();
            if (_lifestreamAvailable)
            {
                NavDebug($"Cross-server: {_playerWorldName} → {_navTargetWorld}, using Lifestream (isSameDc={isSameDc})");
                var ok = ChangeWorld(_navTargetWorld);
                if (ok)
                {
                    _navStep = NavStep.CrossServer;
                    _crossServerStartTime = DateTime.Now;
                    StatusMessageChanged?.Invoke($"Lifestream → {_navTargetWorld}...");
                    return $"跨服中: Lifestream → {_navTargetWorld}";
                }
            }
            TryManageFastWorldTravel(false);

            // 全部失败，提示手动
            _log.Warning($"{Prefix} 跨服失败（isSameDc={isSameDc}），请手动跨服");
            StatusMessageChanged?.Invoke($"跨服失败，请手动跨服到 {_navTargetWorld}");
            _navStep = NavStep.Idle;
            IsNavigating = false;
            return $"请手动跨服到 {_navTargetWorld}";
        }
        return ContinueToTerritoryCheck(label);
    }

    private string ContinueToTerritoryCheck(string label)
    {
        NavDebug($"Territory: cur={CurrentTerritoryType} tgt={_navTargetTerritory}");
        if (_navTargetTerritory != CurrentTerritoryType)
        {
            if (TeleportToTerritory(_navTargetTerritory))
            {
                _navStep = NavStep.TeleportToTerritory;
                _teleportStartTime = DateTime.Now;
                var msg = $"Teleporting to {_navTargetTerritory}...";
                StatusMessageChanged?.Invoke(msg); NavDebug(msg); return msg;
            }
            var prompt = $"Territory {_navTargetTerritory} != {CurrentTerritoryType}, teleport manually";
            NavigateToAetheryte(CurrentTerritoryType); StatusMessageChanged?.Invoke(prompt); return prompt;
        }
        // 已到达目标区域 — FATE 导航先等待 IFateTable 定位，猎怪直接导航
        if (!string.IsNullOrEmpty(_navTargetFateName))
        {
            _navStep = NavStep.WaitForFate;
            _fateLookupStartTime = DateTime.Now;
            StatusMessageChanged?.Invoke("寻找 FATE...");
            NavDebug($"同区域到达，开始等待 IFateTable 加载目标 FATE");
            return $"寻找 FATE: {_navTargetFateName}";
        }
        TryLookupFatePosition(); // 猎怪或其他非 FATE 导航
        return ExecuteFinalNavigation(label);
    }

    private string ExecuteFinalNavigation(string label)
    {
        _navStep = NavStep.MountThenNavigate; _navRetryCount = 0;
        NavDebug($"Final nav: {label} pos={_navTargetPos} fly={_navTargetFly}");
        // 先发送初始状态消息，NavigateTo 内部会根据实际情况更新（Mounting/vnavmesh等待等）
        var s = $"Navigating: {label}"; StatusMessageChanged?.Invoke(s);
        NavigateTo(_navTargetTerritory, _navTargetPos, _navTargetFly);
        return s;
    }

    public void CancelNavigation()
    {
        _navStep = NavStep.Idle; _pendingMountNav = false;
        _navRetryCount = 0; _teleportRetryCount = 0;
        _navTargetFateName = ""; _navTargetHuntName = "";
        if (_fastWorldTravelWasEnabled)
            TryManageFastWorldTravel(false);
        if (_vnavmeshAvailable)
        {
            try { _pluginInterface.GetIpcSubscriber<object>(IpcPathStop).InvokeAction(); }
            catch (Exception ex) { _log.Warning($"{Prefix} Cancel: {ex.Message}"); }
        }
        // 中断 Lifestream 进行中的操作（避免与新导航冲突导致游戏状态异常）
        if (_lifestreamAvailable)
        {
            try { _pluginInterface.GetIpcSubscriber<object>(IpcLifestreamAbort).InvokeAction(); }
            catch (Exception ex) { _log.Warning($"{Prefix} Lifestream abort: {ex.Message}"); }
        }
        IsNavigating = false; StatusMessageChanged?.Invoke("Cancelled");
    }

    // ==================== Framework update (state machine driver) ====================

    private void OnFrameworkUpdate(IFramework framework)
    {
        switch (_navStep)
        {
            case NavStep.CrossServer:          TickCrossServer(); break;
            case NavStep.TeleportToTerritory:  TickTeleportToTerritory(); break;
            case NavStep.WaitAfterTeleport:    TickWaitAfterTeleport(); break;
            case NavStep.WaitForFate:          TickWaitForFate(); break;
            case NavStep.MountThenNavigate:    TickMountThenNavigate(); break;
        }
        
        // 导航完成检测：只在 vnavmesh 已成功启动且自主运行时检测（排除所有过渡状态）
        if (IsNavigating && _navStep == NavStep.Idle)
        {
            if (!CheckIsRunning())
            {
                _navCompleteFrames++;
                if (_navCompleteFrames > 10) // 约 0.17s 确认不是瞬时停顿
                {
                    _log.Information($"{Prefix} vnavmesh 导航完成");
                    IsNavigating = false;
                    _navCompleteFrames = 0;
                    StatusMessageChanged?.Invoke("导航完成");
                }
            }
            else
            {
                _navCompleteFrames = 0;
            }
        }
    }

    /// <summary>
    /// ZoneInit 事件：跨服完成后玩家进入新区域时触发，无需等待超时轮询。
    /// </summary>
    private void OnZoneInit(ZoneInitEventArgs e)
    {
        try
        {
            if (_navStep != NavStep.CrossServer) return;

            // 直接从游戏读取当前世界（此时 _playerWorldName 可能尚未由 Plugin.cs 更新）
            var currentWorld = _objectTable.LocalPlayer?.CurrentWorld.ValueNullable?.Name.ToString();
            if (!string.IsNullOrEmpty(currentWorld) &&
                string.Equals(currentWorld, _navTargetWorld, StringComparison.OrdinalIgnoreCase))
            {
                _log.Information($"{Prefix} ZoneInit 确认世界变更: {currentWorld}，继续导航");
                TryManageFastWorldTravel(false);
                _playerWorldName = currentWorld;
                _navStep = NavStep.Idle;
                ContinueToTerritoryCheck("post-warp");
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"{Prefix} ZoneInit 事件处理异常: {ex.Message}");
        }
    }

    private void TickCrossServer()
    {
        // Lifestream 忙时：如果世界已匹配则视为完成（Lifestream 可能卡住）
        if (IsLifestreamBusy())
        {
            if (IsServerMatch(_navTargetWorld))
            {
                _log.Information($"{Prefix} Lifestream 忙但世界已变更，继续导航");
                TryManageFastWorldTravel(false);
                _navStep = NavStep.Idle;
                ContinueToTerritoryCheck("post-warp");
            }
            return;
        }
        if (!IsServerMatch(_navTargetWorld))
        {
            if ((DateTime.Now - _crossServerStartTime).TotalSeconds > CrossServerTimeoutSeconds)
            {
                _log.Warning($"{Prefix} 跨服超时 ({CrossServerTimeoutSeconds}s)，取消");
                _navStep = NavStep.Idle;
                IsNavigating = false;
                StatusMessageChanged?.Invoke($"跨服超时，取消导航");
                TryManageFastWorldTravel(false);
            }
            return;
        }
        _navStep = NavStep.Idle;
        TryManageFastWorldTravel(false);
        ContinueToTerritoryCheck("post-warp");
    }

    private void TickTeleportToTerritory()
    {
        var ct = CurrentTerritoryType;

        // Territory changed
        if (ct != _navLastTerritory)
        {
            _navLastTerritory = ct;
            if (ct == _navTargetTerritory)
            {
                NavDebug($"Arrived at {ct}, waiting for position update");
                _navStep = NavStep.WaitAfterTeleport;
                _postTeleportWaitFrames = PostTeleportFrameDelay;
                return;
            }
            NavDebug($"Territory {ct}, waiting for {_navTargetTerritory}");
            return;
        }

        // Timeout -> retry
        if ((DateTime.Now - _teleportStartTime).TotalSeconds > TeleportTimeoutSeconds)
        {
            _teleportRetryCount++;
            if (_teleportRetryCount >= MaxTeleportRetries)
            {
                _log.Warning($"{Prefix} Teleport failed after {MaxTeleportRetries} retries");
                _navStep = NavStep.Idle; IsNavigating = false;
                StatusMessageChanged?.Invoke("Teleport failed");
                return;
            }
            NavDebug($"Teleport timeout ({_teleportRetryCount}/{MaxTeleportRetries}), retrying...");
            TeleportToTerritory(_navTargetTerritory);
            _teleportStartTime = DateTime.Now;
        }
    }

    /// <summary>
    /// 传送后等待玩家位置稳定再开始导航，避免 vnavmesh 从 (0,0,0) 开始寻路。
    /// </summary>
    private void TickWaitAfterTeleport()
    {
        if (_postTeleportWaitFrames > 0)
        {
            _postTeleportWaitFrames--;
            return;
        }
        // 传送后位置稳定，进入 FATE 查找等待阶段（或直接导航猎怪）
        if (!string.IsNullOrEmpty(_navTargetFateName))
        {
            _navStep = NavStep.WaitForFate;
            _fateLookupStartTime = DateTime.Now;
            StatusMessageChanged?.Invoke("寻找 FATE...");
            NavDebug("传送完成，开始等待 IFateTable 加载目标 FATE");
        }
        else
        {
            _navStep = NavStep.Idle;
            ExecuteFinalNavigation("post-teleport");
        }
    }

    /// <summary>
    /// 等待 IFateTable 中出现目标 FATE，获取精确坐标后再导航。
    /// 传送后 IFateTable 可能尚未加载目标 FATE，需要持续轮询直到出现或超时。
    /// </summary>
    private void TickWaitForFate()
    {
        if (string.IsNullOrEmpty(_navTargetFateName))
        {
            // 没有目标 FATE 名称（猎怪导航等），直接进入导航
            _navStep = NavStep.Idle;
            ExecuteFinalNavigation("post-teleport");
            return;
        }

        var elapsed = (DateTime.Now - _fateLookupStartTime).TotalSeconds;
        if (elapsed > FateLookupTimeoutSeconds)
        {
            NavDebug($"FATE 查找超时 ({elapsed:F1}s)，使用 MQTT/备用坐标继续导航");
            _navStep = NavStep.Idle;
            ExecuteFinalNavigation("post-teleport (FATE未定位)");
            return;
        }

        // 尝试从 IFateTable 查找目标 FATE
        try
        {
            for (var i = 0; i < _fateTable.Length; i++)
            {
                var fate = _fateTable[i];
                if (fate == null) continue;
                if (!string.Equals(fate.Name.ToString(), _navTargetFateName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // FATE 存在于当前区域（任何状态都有 Position）
                var fatePos = fate.Position;
                NavDebug($"IFateTable 找到目标 FATE '{_navTargetFateName}' pos={fatePos} state={fate.State} radius={fate.Radius}");
                _navTargetPos = fatePos;
                _navTargetFly = CanFly(_navTargetTerritory);
                _navStep = NavStep.Idle;
                ExecuteFinalNavigation("FATE 定位成功");
                return;
            }
        }
        catch (Exception ex)
        {
            NavDebug($"IFateTable lookup error: {ex.Message}");
        }

        // 每 5 秒反馈一次等待状态
        if ((int)elapsed % 5 == 0 && (int)elapsed > 0)
            StatusMessageChanged?.Invoke($"等待 FATE 加载... ({elapsed:F0}s)");
    }

    private void TickMountThenNavigate()
    {
        if (_navRetryCount > 0)
        {
            ExecuteVnavmeshNavigation(_navTargetTerritory, _navTargetPos, _navTargetFly);
            return;
        }
        if (!_pendingMountNav) return;
        if (IsMounted())
        {
            _pendingMountNav = false;
            ExecuteVnavmeshNavigation(_navTargetTerritory, _navTargetPos, _navTargetFly);
            StatusMessageChanged?.Invoke("Mounted, navigating");
        }
        else if ((DateTime.Now - _mountStartTime).TotalSeconds > MountTimeoutSeconds)
        {
            _pendingMountNav = false;
            ExecuteVnavmeshNavigation(_navTargetTerritory, _navTargetPos, false);
            StatusMessageChanged?.Invoke("Mount timeout, ground nav");
        }
    }

    // ==================== Navigation execution ====================

    private void NavigateTo(uint territoryType, Vector3 pos, bool fly)
    {
        // Sentinel: no exact coordinates — try aetheryte of target territory, or inform user
        if (pos == Vector3.Zero)
        {
            if (MainCityAetherytes.TryGetValue(territoryType, out var ap))
            {
                _log.Information($"{Prefix} 缺少精确坐标，导航到区域主城水晶 (territory={territoryType})");
                NavigateTo(territoryType, ap, false);
                return;
            }
            _log.Warning($"{Prefix} 缺少坐标且目标区域无主城水晶 (territory={territoryType}), 已到达区域");
            _navStep = NavStep.Idle;
            IsNavigating = false;
            StatusMessageChanged?.Invoke("已到达目标区域，请手动寻找目标");
            return;
        }

        // 不在此处检查 vnavmesh 就绪 — 传送后 vnavmesh 需时间加载新地图，
        // ExecuteVnavmeshNavigation 自带 _navRetryCount 重试机制（最多60次）会处理此情况
        pos = SnapToNavmeshFloor(pos);
        _navTargetPos = pos; // 保存修正后的坐标，供 TickMountThenNavigate 重试使用
        if (fly && !CanFly(CurrentTerritoryType)) { NavDebug("No flight here"); fly = false; }
        _navTargetFly = fly; // 保存修正后的飞行参数，供重试使用
        if (fly && !IsMounted())
        {
            if (TryMount()) { _pendingMountNav = true; _mountStartTime = DateTime.Now; StatusMessageChanged?.Invoke("Mounting..."); return; }
            else { NavDebug("Mount failed"); fly = false; }
        }
        ExecuteVnavmeshNavigation(territoryType, pos, fly);
    }

    private void ExecuteVnavmeshNavigation(uint territoryType, Vector3 pos, bool fly)
    {
        try
        {
            if (!CheckVnavmeshReady())
            {
                _navRetryCount++;
                NavDebug($"vnavmesh not ready ({_navRetryCount}/{MaxNavRetries})");
                if (_navRetryCount == NavRetryFeedbackThreshold)
                    StatusMessageChanged?.Invoke("正在等待 vnavmesh...");
                if (_navRetryCount >= MaxNavRetries)
                {
                    _log.Warning($"{Prefix} vnavmesh timeout"); _navStep = NavStep.Idle;
                    IsNavigating = false; StatusMessageChanged?.Invoke("vnavmesh timeout");
                }
                return;
            }
            var started = _pluginInterface.GetIpcSubscriber<Vector3, bool, bool>(IpcMoveTo).InvokeFunc(pos, fly);
            NavDebug($"vnavmesh move: pos={pos} fly={fly} started={started}");
            if (!started)
            {
                // vnavmesh 暂时无法寻路（传送后位置不稳定、navmesh 未加载等），走重试机制而非直接终止
                _navRetryCount++;
                NavDebug($"vnavmesh MoveTo failed ({_navRetryCount}/{MaxNavRetries}), retrying...");
                if (_navRetryCount >= MaxNavRetries)
                {
                    _log.Warning($"{Prefix} vnavmesh path not found after {MaxNavRetries} retries");
                    _navStep = NavStep.Idle;
                    IsNavigating = false; StatusMessageChanged?.Invoke("Path not found");
                }
                // 飞行寻路失败过半 → 降级为地面导航
                else if (fly && _navRetryCount > MaxNavRetries / 2)
                {
                    NavDebug("降级为地面导航");
                    _navTargetFly = false;
                }
                return;
            }
            _navRetryCount = 0; IsNavigating = true;
            _navStep = NavStep.Idle; // vnavmesh 已成功启动，状态机转入 Idle（不再需要 TickMountThenNavigate）
            _log.Information($"{Prefix} vnavmesh: territory={territoryType} pos={pos} fly={fly}");
        }
        catch (Exception ex)
        {
            _navRetryCount++;
            NavDebug($"vnavmesh error ({_navRetryCount}/{MaxNavRetries}): {ex.Message}");
            if (_navRetryCount == NavRetryFeedbackThreshold)
                StatusMessageChanged?.Invoke("vnavmesh 连接异常，正在重试...");
            if (_navRetryCount >= MaxNavRetries)
            {
                _log.Error($"{Prefix} vnavmesh persistent failure: {ex.Message}"); _navStep = NavStep.Idle;
                IsNavigating = false; StatusMessageChanged?.Invoke("vnavmesh unavailable");
            }
        }
    }

    // ==================== FATE position lookup ====================

    /// <summary>
    /// 到达目标区域后，尝试从 IFateTable 查找目标 FATE 的实时位置，
    /// 如果找到则用 fate.Position 替代 MQTT 坐标（更精确）。
    /// </summary>
    private void TryLookupFatePosition()
    {
        if (string.IsNullOrEmpty(_navTargetFateName)) return;

        try
        {
            for (var i = 0; i < _fateTable.Length; i++)
            {
                var fate = _fateTable[i];
                if (fate == null) continue;

                if (string.Equals(fate.Name.ToString(), _navTargetFateName, StringComparison.OrdinalIgnoreCase))
                {
                    // 接受任何状态的 FATE（传送后 FATE 可能还在准备中），只要有坐标就导航
                    // 注意：FateState 枚举值在不同版本可能不同，不做状态过滤
                    var fatePos = fate.Position;
                    NavDebug($"IFateTable 找到目标 FATE '{_navTargetFateName}' pos={fatePos} state={fate.State} radius={fate.Radius}");
                    _navTargetPos = fatePos;
                    _navTargetFly = CanFly(_navTargetTerritory);
                    return;
                }
            }
            NavDebug($"IFateTable 未找到目标 FATE '{_navTargetFateName}'（可能尚未加载到游戏内），保持原坐标");
        }
        catch (Exception ex)
        {
            NavDebug($"IFateTable lookup error: {ex.Message}");
        }
    }

    // ==================== Chat message monitoring ====================

    /// <summary>
    /// 监听系统聊天消息，在跨服导航中检测世界变更（补充 ZoneInit 的延迟检测）。
    /// </summary>
    private void OnChatMessage(IHandleableChatMessage message)
    {
        // 仅处理跨服导航中收到的系统消息
        if (_navStep != NavStep.CrossServer) return;
        if (message.LogKind != XivChatType.SystemMessage && message.LogKind != XivChatType.SystemError) return;

        var msgText = message.Message.ToString();
        // 尝试从消息中匹配世界名
        try
        {
            foreach (var (worldId, info) in _fateDataManager.Worlds)
            {
                if (msgText.Contains(info.Name))
                {
                    _playerWorldName = info.Name;
                    NavDebug($"聊天消息检测到世界: {info.Name}");
                    return;
                }
            }
        }
        catch { }
    }

    // ==================== Utilities ====================

    private Vector3? CoordToGamePos(Coordinate? coord)
    {
        if (coord == null) return null;
        // 坐标转换：传输坐标 → 游戏坐标
        // 公式：gameCoord = (transmissionCoord / CoordDivisor - CoordOffset) / CoordScale
        var pos = new Vector3(
            (coord.X / CoordDivisor - CoordOffset) / CoordScale, 0,
            (coord.Y / CoordDivisor - CoordOffset) / CoordScale);
        return SnapToNavmeshFloor(pos);
    }

    private Vector3 SnapToNavmeshFloor(Vector3 pos)
    {
        if (!_vnavmeshAvailable) return pos;
        try
        {
            var c = _pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPoint").InvokeFunc(pos, 10f, 200f);
            if (c != null && c.Value.Y > 0.01f) { pos.Y = c.Value.Y; NavDebug($"Floor: ({pos.X:F1},{pos.Z:F1}) Y={pos.Y:F1}"); }
        }
        catch { }
        return pos;
    }

    public bool CheckIsRunning()
    {
        if (!_vnavmeshAvailable) return false;
        try { return _pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").InvokeFunc(); }
        catch { return false; }
    }

    public void Stop() { CancelNavigation(); IsNavigating = false; }

    public string NavigateToTest(uint territory, Vector3 pos, bool fly, string targetWorld = "")
    {
        var cur = CurrentTerritoryType;
        if (!string.IsNullOrEmpty(targetWorld) && !IsServerMatch(targetWorld))
        {
            // 跨服测试：通过 StartNavChain 处理服务器变更
            CancelNavigation();
            _navTargetWorld = targetWorld;
            _navTargetTerritory = territory;
            _navTargetPos = pos;
            _navTargetFly = fly;
            _navLastTerritory = cur;
            return StartNavChain($"Test: {targetWorld} territory={territory}");
        }
        if (territory == cur) { _navStep = NavStep.MountThenNavigate; _navRetryCount = 0; NavigateTo(territory, pos, fly); return IsNavigating ? $"Nav: ({pos.X:F1},{pos.Y:F1},{pos.Z:F1}) fly={fly}" : "Failed"; }
        CancelNavigation();
        _navTargetWorld = string.IsNullOrEmpty(targetWorld) ? _playerWorldName : targetWorld;
        _navTargetTerritory = territory; _navTargetPos = pos; _navTargetFly = fly; _navLastTerritory = cur;
        if (TeleportToTerritory(territory))
        {
            _navStep = NavStep.TeleportToTerritory; _teleportStartTime = DateTime.Now; IsNavigating = true;
            var m = $"Teleporting to {territory}..."; StatusMessageChanged?.Invoke(m); return m;
        }
        return $"Territory {territory} != {cur}, teleport first";
    }

    private void NavigateToAetheryte(uint territory)
    {
        if (MainCityAetherytes.TryGetValue(territory, out var pos)) NavigateTo(territory, pos, false);
    }

    private bool IsServerMatch(string w)
    {
        if (string.IsNullOrEmpty(w)) return true;
        
        // 直接读游戏当前世界 + _playerWorldName（Plugin.cs OnZoneInit更新），任一匹配即通过
        var playerWorld = _playerWorldName;
        try
        {
            var cw = _objectTable.LocalPlayer?.CurrentWorld.ValueNullable?.Name.ToString();
            if (!string.IsNullOrEmpty(cw)) playerWorld = cw;
        }
        catch { }
        
        if (string.IsNullOrEmpty(playerWorld) && !string.IsNullOrEmpty(_config.WorldName))
            playerWorld = _config.WorldName;
        return string.IsNullOrEmpty(playerWorld) || string.Equals(playerWorld, w, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 根据世界中文名查找所属大区中文名（如 "萌芽池" → "陆行鸟"）。
    /// </summary>
    private string LookupDcByWorldName(string worldName)
    {
        if (string.IsNullOrEmpty(worldName)) return "";
        try
        {
            foreach (var (worldId, info) in _fateDataManager.Worlds)
            {
                if (string.Equals(info.Name, worldName, StringComparison.OrdinalIgnoreCase))
                    return info.Dc ?? "";
            }
        }
        catch { }
        return "";
    }

    /// <summary>
    /// 查询目标世界的 DC 名称（供 UI 测试用）。
    /// </summary>
    public string LookupDcByWorldNameForTest(string worldName) => LookupDcByWorldName(worldName);

    /// <summary>
    /// 判断目标世界是否与玩家在同一大区。
    /// </summary>
    private bool IsSameDatacenter(string targetWorldName)
    {
        if (string.IsNullOrEmpty(targetWorldName)) return true;

        try
        {
            // 用当前世界名查 DC（而非 HomeWorldId），因为玩家可能在访问其他 DC
            var playerWorldName = !string.IsNullOrEmpty(_playerWorldName) ? _playerWorldName : _config.WorldName;
            var playerDc = LookupDcByWorldName(playerWorldName);
            var targetDc = LookupDcByWorldName(targetWorldName);

            NavDebug($"DC check: player={playerDc} (world={playerWorldName}), target={targetDc} (world={targetWorldName})");

            if (string.IsNullOrEmpty(playerDc) || string.IsNullOrEmpty(targetDc))
            {
                // 无法判断时，保守处理：当作不同DC
                NavDebug("DC check: unable to determine, treating as cross-DC");
                return false;
            }

            return string.Equals(playerDc, targetDc, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            NavDebug($"DC check error: {ex.Message}, treating as cross-DC");
            return false;
        }
    }

    private static uint TryParseTerritory(string s) => uint.TryParse(s, out var t) ? t : 0;
    private bool CanFly(uint territoryTypeId)
    {
        try
        {
            var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
            if (sheet.TryGetRow(territoryTypeId, out var row))
                return row.TerritoryIntendedUse.RowId >= 41;
        }
        catch { }
        return false;
    }
    public bool IsInMainCity() => MainCityAetherytes.ContainsKey(CurrentTerritoryType);

    private bool IsMounted() { try { return _condition[ConditionFlag.Mounted]; } catch { return false; } }

    private bool TryMount()
    {
        try
        {
            if (_condition[ConditionFlag.InCombat] || _condition[ConditionFlag.BoundByDuty]) return false;
            if (_condition[ConditionFlag.Mounted] || _condition[ConditionFlag.Mounting]) return true;
            unsafe { return ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9); }
        }
        catch (Exception ex) { NavDebug($"Mount: {ex.Message}"); return false; }
    }

    private void NavDebug(string m) { if (_config.Debug.Navigation) _log.Information($"{Prefix} [Nav] {m}"); }
    private string SetError(string m) { StatusMessageChanged?.Invoke(m); _log.Warning($"{Prefix} {m}"); return m; }
}
