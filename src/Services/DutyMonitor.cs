using System;
using Dalamud.Plugin.Services;

namespace FateWhisper.Services;

/// <summary>
/// 副本状态监测服务，基于 ClientState.TerritoryType 检测副本状态变化。
/// Dalamud API 15 CN 版本：IClientState 无 LocalPlayer/IsInDuty 方法。
/// </summary>
public class DutyMonitor : IDisposable
{
    private readonly IClientState _clientState;
    private readonly IPluginLog _log;
    private readonly DataManager _dataManager;
    private bool _wasInDuty;
    private const string Prefix = "[FateWhisper]";

    /// <summary>
    /// 进入副本时触发。
    /// </summary>
    public event Action? DutyEntered;

    /// <summary>
    /// 离开副本时触发。
    /// </summary>
    public event Action? DutyExited;

    /// <summary>
    /// 副本状态变更时触发（true=进入副本）。
    /// </summary>
    public event Action<bool>? DutyStateChanged;

    /// <summary>
    /// 当前是否在副本中。
    /// 依据 DataManager.IsDutyTerritory（territories.json 的 content 字段判定），
    /// 比旧的 TerritoryType >= 1000 阈值准确得多。
    /// </summary>
    public bool IsInDuty
    {
        get
        {
            try
            {
                return _dataManager.IsDutyTerritory(_clientState.TerritoryType);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 当前区域类型 ID（uint）。
    /// </summary>
    public uint TerritoryType
    {
        get
        {
            try
            {
                return _clientState.TerritoryType;
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// 获取玩家名称（通过 ClientState 的本地内容）。
    /// 注：CN 版 IClientState 不支持 LocalPlayer 属性，
    /// 玩家名称从配置获取或在其他地方设置。
    /// </summary>
    public string PlayerName { get; set; } = "";

    /// <summary>
    /// 获取玩家所在世界 ID。
    /// 注：CN 版 IClientState 不支持 LocalPlayer 属性。
    /// </summary>
    public uint HomeWorldId { get; set; }

    /// <summary>
    /// 初始化副本监测服务。
    /// </summary>
    /// <param name="clientState">Dalamud 客户端状态服务。</param>
    /// <param name="log">日志服务。</param>
    /// <param name="dataManager">静态数据管理器（提供区域副本判定）。</param>
    public DutyMonitor(IClientState clientState, IPluginLog log, DataManager dataManager)
    {
        _clientState = clientState;
        _log = log;
        _dataManager = dataManager;

        try
        {
            _wasInDuty = _dataManager.IsDutyTerritory(clientState.TerritoryType);
        }
        catch
        {
            _wasInDuty = false;
        }

        _log.Information($"{Prefix} DutyMonitor 已初始化 (TerritoryType={clientState.TerritoryType}, InDuty={_wasInDuty})");
    }

    /// <summary>
    /// 每帧检查副本状态（由 IFramework.Update 驱动）。
    /// </summary>
    public void OnFrameworkUpdate()
    {
        try
        {
            var territoryType = _clientState.TerritoryType;
            var nowInDuty = _dataManager.IsDutyTerritory(territoryType);

            if (nowInDuty != _wasInDuty)
            {
                _wasInDuty = nowInDuty;

                if (nowInDuty)
                {
                    _log.Debug($"{Prefix} 进入副本 (TerritoryType={territoryType})");
                    DutyEntered?.Invoke();
                }
                else
                {
                    _log.Debug($"{Prefix} 离开副本 (TerritoryType={territoryType})");
                    DutyExited?.Invoke();
                }

                DutyStateChanged?.Invoke(nowInDuty);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} DutyMonitor 更新异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        _log.Information($"{Prefix} DutyMonitor 已释放");
    }
}
