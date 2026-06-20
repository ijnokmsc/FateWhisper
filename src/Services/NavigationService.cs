using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using SilverDasher.Models;

namespace SilverDasher.Services;

public class NavigationService : IDisposable
{
    private readonly IPluginLog _log;
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IClientState _clientState;
    private string _playerWorldName;

    private const string Prefix = "[FateWhisper]";
    private const string IpcMoveTo = "vnavmesh.SimpleMove.PathfindAndMoveTo";
    private const string IpcCancel = "vnavmesh.SimpleMove.PathfindCancel";
    private const string IpcRunning = "vnavmesh.SimpleMove.IsRunning";

    private bool _vnavmeshAvailable;
    private bool _isNavigating;

    public event Action<bool>? NavigationStateChanged;
    public event Action<string>? StatusMessageChanged;

    public bool IsVnavmeshAvailable => _vnavmeshAvailable;
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

    // Main city aetheryte positions
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
        { 962, new Vector3(-111f, 16f, 64f) },
        { 1185, new Vector3(-24f, 37f, -573f) },
    };

    public NavigationService(
        IDalamudPluginInterface pluginInterface, IPluginLog log,
        IClientState clientState, string playerWorldName)
    {
        _pluginInterface = pluginInterface;
        _log = log;
        _clientState = clientState;
        _playerWorldName = playerWorldName;
        DetectVnavmesh();
    }

    private void DetectVnavmesh()
    {
        try
        {
            _ = _pluginInterface.GetIpcSubscriber<bool>(IpcRunning);
            _vnavmeshAvailable = true;
            _log.Information($"{Prefix} vnavmesh IPC detected, navigation available");
        }
        catch (Exception ex)
        {
            _vnavmeshAvailable = false;
            _log.Warning($"{Prefix} vnavmesh IPC not available: {ex.Message}");
        }
    }

    public string NavigateToHunt(HuntMessage msg)
    {
        if (!_vnavmeshAvailable) return SetError("vnavmesh not available");

        var currentTerritory = CurrentTerritoryType;
        var worldName = msg.WorldName ?? msg.World.ToString();
        if (!IsServerMatch(worldName))
            return NavigateToMainCity(currentTerritory, worldName);

        var targetTerritory = TryParseTerritory(msg.Territory);
        if (targetTerritory == 0) targetTerritory = currentTerritory;

        if (targetTerritory != currentTerritory)
        {
            var status = $"Hunt [{msg.Rank}] {msg.MobName} in {msg.TerritoryName}({worldName}), navigating to local aetheryte";
            NavigateToAetheryte(currentTerritory);
            StatusMessageChanged?.Invoke(status);
            return status;
        }

        var fly = CanFly(targetTerritory);
        NavigateTo(currentTerritory, Vector3.Zero, fly);

        var success = $"Navigating: [{msg.Rank}] {msg.MobName}";
        StatusMessageChanged?.Invoke(success);
        _log.Information($"{Prefix} {success}");
        return success;
    }

    public string NavigateToFate(FateMessage msg)
    {
        if (!_vnavmeshAvailable) return SetError("vnavmesh not available");

        var currentTerritory = CurrentTerritoryType;
        var worldName = msg.WorldName ?? msg.World.ToString();
        if (!IsServerMatch(worldName))
            return NavigateToMainCity(currentTerritory, worldName);

        var targetTerritory = TryParseTerritory(msg.Territory);
        if (targetTerritory == 0) targetTerritory = currentTerritory;

        if (targetTerritory != currentTerritory)
        {
            var status = $"FATE {msg.FateName} in {msg.TerritoryName}({worldName}), navigating to local aetheryte";
            NavigateToAetheryte(currentTerritory);
            StatusMessageChanged?.Invoke(status);
            return status;
        }

        var fly = CanFly(targetTerritory);
        NavigateTo(currentTerritory, Vector3.Zero, fly);

        var success = $"Navigating: FATE {msg.FateName}";
        StatusMessageChanged?.Invoke(success);
        _log.Information($"{Prefix} {success}");
        return success;
    }

    public void CancelNavigation()
    {
        if (!_vnavmeshAvailable) return;
        try
        {
            var ipc = _pluginInterface.GetIpcSubscriber<object?, object?>(IpcCancel);
            ipc.InvokeAction(null!);
            IsNavigating = false;
            StatusMessageChanged?.Invoke("Navigation stopped");
        }
        catch (Exception ex)
        {
            _log.Warning($"{Prefix} Cancel navigation failed: {ex.Message}");
        }
    }

    public bool CheckIsRunning()
    {
        if (!_vnavmeshAvailable) return false;
        try
        {
            var ipc = _pluginInterface.GetIpcSubscriber<bool>(IpcRunning);
            var running = ipc.InvokeFunc();
            IsNavigating = running;
            return running;
        }
        catch { return false; }
    }

    public void Stop()
    {
        CancelNavigation();
        IsNavigating = false;
    }

    private void NavigateTo(uint territoryType, Vector3 pos, bool fly)
    {
        try
        {
            var ipc = _pluginInterface.GetIpcSubscriber<uint, float, float, float, bool, object?>(
                IpcMoveTo);
            ipc.InvokeAction(territoryType, pos.X, pos.Y, pos.Z, fly);
            IsNavigating = true;
            _log.Information($"{Prefix} vnavmesh: territory={territoryType} pos={pos} fly={fly}");
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} vnavmesh IPC call failed: {ex.Message}");
            IsNavigating = false;
            StatusMessageChanged?.Invoke($"导航失败: {ex.Message}");
        }
    }

    private string NavigateToMainCity(uint currentTerritory, string targetWorld)
    {
        if (MainCityAetherytes.TryGetValue(currentTerritory, out var pos))
        {
            var msg = $"Server mismatch ({_playerWorldName} != {targetWorld}), use aetheryte for world visit";
            NavigateTo(currentTerritory, pos, false);
            StatusMessageChanged?.Invoke(msg);
            _log.Information($"{Prefix} {msg}");
            return msg;
        }

        var prompt = $"Target on {targetWorld} (different server), tp to main city first";
        StatusMessageChanged?.Invoke(prompt);
        NavigateToAetheryte(currentTerritory);
        return prompt;
    }

    private void NavigateToAetheryte(uint territory)
    {
        if (MainCityAetherytes.TryGetValue(territory, out var pos))
            NavigateTo(territory, pos, false);
    }

    private string SetError(string msg)
    {
        StatusMessageChanged?.Invoke(msg);
        _log.Warning($"{Prefix} {msg}");
        return msg;
    }

    private bool IsServerMatch(string targetWorld)
    {
        if (string.IsNullOrEmpty(targetWorld) || string.IsNullOrEmpty(_playerWorldName))
            return true;
        return string.Equals(_playerWorldName, targetWorld, StringComparison.OrdinalIgnoreCase);
    }

    private static uint TryParseTerritory(string s)
    {
        if (uint.TryParse(s, out var tid)) return tid;
        return 0;
    }

    private static bool CanFly(uint territoryType) => territoryType >= 397;

    public bool IsInMainCity() => MainCityAetherytes.ContainsKey(CurrentTerritoryType);

    public void Dispose()
    {
        CancelNavigation();
        _log.Information($"{Prefix} NavigationService disposed");
    }
}
