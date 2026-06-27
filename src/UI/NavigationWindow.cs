using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FateWhisper.Models;
using FateWhisper.Services;

namespace FateWhisper.UI;

/// <summary>
/// 独立导航弹窗 - 收到猎怪/FATE 播报时弹出。
/// 提供「导航」「停止导航」「关闭」按钮。
/// 副本内不弹窗。
/// </summary>
public class NavigationWindow : Window, IDisposable
{
    private readonly IPluginLog _log;
    private readonly NavigationService _navigation;
    private readonly Func<bool> _isInDuty;

    private const string Prefix = "[FateWhisper]";

    // 当前弹窗关联的播报信息
    private HuntMessage? _huntTarget;
    private FateMessage? _fateTarget;
    private (string Name, string Territory, string World, uint TerritoryId, float X, float Y, float Z)? _testTarget;
    private string _statusText = "";
    private bool _isNavigating;
    private bool _serverMatch;
    private string _playerWorld = "";

    // 导航队列：导航进行中收到新请求时排队，当前导航完成后自动弹出
    private readonly Queue<Action> _navQueue = new();

    public NavigationWindow(
        IPluginLog log,
        NavigationService navigation,
        Func<bool> isInDuty)
        : base("FateWhisper 导航##FateWhisperNavWindow",
               ImGuiWindowFlags.NoCollapse)
    {
        _log = log;
        _navigation = navigation;
        _isInDuty = isInDuty;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 180),
            MaximumSize = new Vector2(500, 350)
        };

        IsOpen = false;

        _navigation.NavigationStateChanged += OnNavStateChanged;
        _navigation.StatusMessageChanged += OnStatusChanged;
    }

    /// <summary>
    /// 用猎怪播报弹出导航窗口。不在副本内才弹出。
    /// 若当前导航进行中，加入队列而非覆盖。
    /// </summary>
    public void ShowForHunt(HuntMessage msg)
    {
        if (_isInDuty())
        {
            _log.Debug($"{Prefix} NavigationWindow: 副本内, 不弹窗");
            return;
        }

        if (_isNavigating)
        {
            _navQueue.Enqueue(() => ShowForHunt(msg));
            _log.Information($"{Prefix} 导航队列+1 (Hunt: {msg.MobName}), 队列={_navQueue.Count}");
            return;
        }

        _huntTarget = msg;
        _fateTarget = null;
        _playerWorld = _navigation.PlayerWorldName;
        _serverMatch = string.Equals(
            _playerWorld, msg.WorldName ?? msg.World.ToString(), StringComparison.OrdinalIgnoreCase);
        _statusText = "";
        _isNavigating = false;

        IsOpen = true;
        BringToFront();
        _log.Information($"{Prefix} 导航弹窗已显示 (Hunt: {msg.MobName})");
    }

    /// <summary>
    /// 用 FATE 播报弹出导航窗口。不在副本内才弹出。
    /// </summary>
    public void ShowForFate(FateMessage msg)
    {
        if (_isInDuty())
        {
            _log.Debug($"{Prefix} NavigationWindow: 副本内, 不弹窗");
            return;
        }

        if (_isNavigating)
        {
            _navQueue.Enqueue(() => ShowForFate(msg));
            _log.Information($"{Prefix} 导航队列+1 (FATE: {msg.FateName}), 队列={_navQueue.Count}");
            return;
        }

        _huntTarget = null;
        _fateTarget = msg;
        _playerWorld = _navigation.PlayerWorldName;
        _serverMatch = string.Equals(
            _playerWorld, msg.WorldName ?? msg.World.ToString(), StringComparison.OrdinalIgnoreCase);
        _statusText = "";
        _isNavigating = false;

        IsOpen = true;
        BringToFront();
        _log.Information($"{Prefix} 导航弹窗已显示 (FATE: {msg.FateName})");
    }

    /// <summary>
    /// 用导航测试地点弹出导航窗口。
    /// </summary>
    public void ShowForTest(string name, string worldName, uint territory, float x, float y, float z)
    {
        var playerWorld = _navigation.PlayerWorldName;

        // 兼容旧数据：WorldName 为空时使用当前玩家世界
        if (string.IsNullOrEmpty(worldName))
        {
            worldName = playerWorld;
            _log.Warning($"{Prefix} 测试地点「{name}」缺少服务器信息，使用当前服务器: {worldName}");
        }

        _huntTarget = null;
        _fateTarget = null;
        _testTarget = (name, territory.ToString(), worldName, territory, x, y, z);
        _playerWorld = playerWorld;
        _serverMatch = string.Equals(playerWorld, worldName, StringComparison.OrdinalIgnoreCase);
        _log.Information($"{Prefix} 测试弹窗: name={name} playerWorld=[{playerWorld}] savedWorld=[{worldName}] serverMatch={_serverMatch}");
        _statusText = "";
        _isNavigating = false;

        IsOpen = true;
        BringToFront();
        _log.Information($"{Prefix} 导航测试弹窗: {name} @ {worldName} territory={territory} ({x:F1}, {y:F1}, {z:F1})");
    }

    public override void Draw()
    {
        try
        {
            DrawContent();
        }
        catch (Exception ex)
        {
            _log.Error($"{Prefix} NavigationWindow draw error: {ex.Message}");
        }
    }

    private void DrawContent()
    {
        var nav = _navigation;

        // ---- 标题行 ----
        ImGui.TextColored(
            new Vector4(0.3f, 0.8f, 1.0f, 1.0f),
            _huntTarget is not null ? "猎怪导航" : _fateTarget is not null ? "FATE 导航" : "测试导航");

        ImGui.Separator();
        ImGui.Spacing();

        // 队列信息
        if (_navQueue.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({_navQueue.Count}个排队)");
        }

        // ---- 目标信息 ----
        if (_huntTarget is not null)
        {
            var h = _huntTarget;
            var rankColor = GetRankColor(h.Rank ?? "");
            ImGui.TextWrapped($"目标: {rankColor}[{h.Rank ?? ""}] {h.MobName}");
            ImGui.TextDisabled($"位置: {h.TerritoryName} ({h.World})");
        }
        else if (_fateTarget is not null)
        {
            var f = _fateTarget;
            ImGui.TextWrapped($"目标: FATE {f.FateName}");
            ImGui.TextDisabled($"位置: {f.TerritoryName} ({f.World})");
        }
        else if (_testTarget is not null)
        {
            var t = _testTarget.Value;
            ImGui.TextWrapped($"目标: {t.Name}");
            ImGui.TextDisabled($"位置: territory={t.TerritoryId} ({t.X:F1}, {t.Y:F1}, {t.Z:F1})  [{t.World}]");
        }

        ImGui.Spacing();

        // ---- 服务器匹配信息（每帧实时刷新） ----
        var targetWorld = _huntTarget?.WorldName ?? _fateTarget?.WorldName ?? _testTarget?.World ?? _huntTarget?.World.ToString() ?? _fateTarget?.World.ToString() ?? "";
        // 优先用 NavigationService 的最新世界名（已改为直读 CurrentWorld）
        if (!string.IsNullOrEmpty(_navigation.PlayerWorldName))
            _playerWorld = _navigation.PlayerWorldName;
        var isMatch = !string.IsNullOrEmpty(_playerWorld) &&
            string.Equals(_playerWorld, targetWorld, StringComparison.OrdinalIgnoreCase);
        if (isMatch)
        {
            ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.3f, 1.0f),
                $"[同服] {_playerWorld} == {targetWorld} — 直接导航");
        }
        else
        {
            ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.2f, 1.0f),
                $"[跨服] {_playerWorld} != {targetWorld} — 导航到主城水晶");
        }

        ImGui.Spacing();

        // ---- vnavmesh 状态 ----
        if (!nav.IsVnavmeshAvailable)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f),
                "[!] vnavmesh 插件未启用，无法自动导航");
            ImGui.TextDisabled("请通过 Puni.sh 仓库安装并启用 vnavmesh");
        }

        ImGui.Spacing();

        // ---- 导航状态文本 ----
        if (!string.IsNullOrEmpty(_statusText))
        {
            var color = _isNavigating
                ? new Vector4(0.3f, 1.0f, 0.6f, 1.0f)
                : new Vector4(0.7f, 0.7f, 0.7f, 1.0f);
            ImGui.TextColored(color, _statusText);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ---- 操作按钮 ----
        var btnWidth = 100f;

        // 导航按钮
        if (nav.IsVnavmeshAvailable && !_isNavigating)
        {
            if (ImGui.Button("导航", new Vector2(btnWidth, 0)))
            {
                DoNavigate();
            }
        }
        else if (_isNavigating)
        {
            ImGui.BeginDisabled();
            ImGui.Button("导航中...", new Vector2(btnWidth, 0));
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("导航(不可用)", new Vector2(btnWidth, 0));
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        // 停止按钮
        if (_isNavigating)
        {
            if (ImGui.Button("停止导航", new Vector2(btnWidth, 0)))
            {
                nav.CancelNavigation();
                _isNavigating = false;
                _statusText = "导航已手动停止";
                // 停止后自动出队下一个
                if (_navQueue.Count > 0)
                {
                    var next = _navQueue.Dequeue();
                    next();
                }
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("停止导航", new Vector2(btnWidth, 0));
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        // 关闭按钮
        if (ImGui.Button("关闭", new Vector2(btnWidth, 0)))
        {
            if (_isNavigating)
                nav.CancelNavigation();
            _navQueue.Clear();
            IsOpen = false;
        }
    }

    private void DoNavigate()
    {
        string result;
        if (_huntTarget is not null)
            result = _navigation.NavigateToHunt(_huntTarget);
        else if (_fateTarget is not null)
            result = _navigation.NavigateToFate(_fateTarget);
        else if (_testTarget is not null)
        {
            var t = _testTarget.Value;
            result = _navigation.NavigateToTest(t.TerritoryId, new System.Numerics.Vector3(t.X, t.Y, t.Z), t.TerritoryId >= 600, t.World);
        }
        else
            return;

        _statusText = result;
        _isNavigating = _navigation.IsNavigating;
    }

    private void OnNavStateChanged(bool navigating)
    {
        _isNavigating = navigating;
        // 导航完成且队列非空 → 自动弹出下一个
        if (!navigating && _navQueue.Count > 0)
        {
            var next = _navQueue.Dequeue();
            _log.Information($"{Prefix} 导航完成，出队下一个 (剩余={_navQueue.Count})");
            next();
        }
    }

    private void OnStatusChanged(string text)
    {
        _statusText = text;
    }

    private static string GetRankColor(string rank)
    {
        return rank.ToUpperInvariant() switch
        {
            "S" => "★ ",
            "SS" => "★★ ",
            "A" => "● ",
            "B" => "○ ",
            _ => ""
        };
    }

    public void Dispose()
    {
        _navigation.NavigationStateChanged -= OnNavStateChanged;
        _navigation.StatusMessageChanged -= OnStatusChanged;
        _log.Information($"{Prefix} NavigationWindow disposed");
    }
}
