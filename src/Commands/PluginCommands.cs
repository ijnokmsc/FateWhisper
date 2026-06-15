using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using SilverDasher.UI;

namespace SilverDasher.Commands;

/// <summary>
/// 插件命令管理，注册 /sd 命令。
/// </summary>
public class PluginCommands : IDisposable
{
    private readonly ICommandManager _commandManager;
    private readonly MainWindow _mainWindow;
    private readonly IPluginLog _log;
    private const string CommandName = "/sd";
    private const string Prefix = "[SilverDasher]";

    /// <summary>
    /// 初始化命令管理并注册 /sd 命令。
    /// </summary>
    /// <param name="commandManager">Dalamud 命令管理器。</param>
    /// <param name="mainWindow">主窗口实例。</param>
    /// <param name="log">日志服务。</param>
    public PluginCommands(
        ICommandManager commandManager,
        MainWindow mainWindow,
        IPluginLog log)
    {
        _commandManager = commandManager;
        _mainWindow = mainWindow;
        _log = log;

        _commandManager.AddHandler(
            CommandName,
            new CommandInfo(OnCommand)
            {
                HelpMessage = "打开/关闭 SilverDasher 设置窗口",
                ShowInHelp = true
            });

        _log.Information($"{Prefix} 命令已注册: {CommandName}");
    }

    /// <summary>
    /// /sd 命令处理：切换主窗口可见性。
    /// </summary>
    private void OnCommand(string command, string args)
    {
        try
        {
            _mainWindow.IsOpen = !_mainWindow.IsOpen;

            if (_mainWindow.IsOpen)
            {
                _mainWindow.Open();
                _log.Information($"{Prefix} 通过 /sd 命令打开设置窗口");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 命令处理异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 注销命令并释放资源。
    /// </summary>
    public void Dispose()
    {
        _commandManager.RemoveHandler(CommandName);
        _log.Information($"{Prefix} 命令已注销: {CommandName}");
    }
}
