using System;
using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using FateWhisper.UI;

namespace FateWhisper.Commands;

/// <summary>
/// 插件命令管理，注册 /fw /fatewhisper 命令（向下兼容 /sd）。
/// </summary>
public class PluginCommands : IDisposable
{
    private readonly ICommandManager _commandManager;
    private readonly MainWindow _mainWindow;
    private readonly IPluginLog _log;
    private const string MainCommand = "/fw";
    private const string AliasCommand = "/fatewhisper";
    private const string LegacyCommand = "/sd";
    private const string Prefix = "[FateWhisper]";

    public PluginCommands(
        ICommandManager commandManager,
        MainWindow mainWindow,
        IPluginLog log)
    {
        _commandManager = commandManager;
        _mainWindow = mainWindow;
        _log = log;

        var handler = new CommandInfo(OnCommand)
        {
            HelpMessage = "打开/关闭 FateWhisper 设置窗口（基于 SilverDasher）",
            ShowInHelp = true
        };

        _commandManager.AddHandler(MainCommand, handler);
        _commandManager.AddHandler(AliasCommand, handler);
        _commandManager.AddHandler(LegacyCommand, handler); // 向下兼容

        _log.Information($"{Prefix} 命令已注册: {MainCommand}, {AliasCommand} (兼容: {LegacyCommand})");
    }

    private void OnCommand(string command, string args)
    {
        try
        {
            _mainWindow.IsOpen = !_mainWindow.IsOpen;
            if (_mainWindow.IsOpen) _mainWindow.Open();
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} 命令处理异常: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(MainCommand);
        _commandManager.RemoveHandler(AliasCommand);
        _commandManager.RemoveHandler(LegacyCommand);
    }
}
