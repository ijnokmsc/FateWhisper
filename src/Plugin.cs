using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Fates;

namespace FateWhisper;

/// <summary>
/// FateWhisper 插件入口，实现 IDalamudPlugin 接口。
/// Dalamud API 15 使用 [PluginService] 静态属性注入替代构造函数注入。
/// 框架通过 Source Generator 自动填充标记了 [PluginService] 的属性。
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    // API 15: 通过 [PluginService] 静态属性注入 Dalamud 核心服务
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    public static IPluginLog? SharedLog => Log;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider InteropProvider { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IDataManager GameData { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] internal static IFateTable FateTable { get; private set; } = null!;

    private readonly ServiceOrchestrator _orchestrator;

    /// <summary>
    /// 无参构造函数，Dalamud API 15 通过 [PluginService] 注入依赖。
    /// 在构造函数执行时，所有 [PluginService] 属性已被框架填充。
    /// 职责仅限：创建 ServiceOrchestrator → 初始化。
    /// </summary>
    public Plugin()
    {
        _orchestrator = new ServiceOrchestrator();
        _orchestrator.Initialize();
    }

    /// <summary>
    /// 释放所有资源，委托给 ServiceOrchestrator 逆序释放。
    /// </summary>
    public void Dispose() => _orchestrator.Dispose();
}
