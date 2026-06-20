using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SilverDasher.Models;
using SilverDasher.Services;

namespace SilverDasher.UI;

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

    private const string Prefix = "[SilverDasher]";

    // 当前弹窗关联的播报信息
    private HuntMessage? _huntTarget;
    private FateMessage? _fateTarget;
    private string _statusText = "";
    private bool _isNavigating;
    private bool _serverMatch;
    private string _playerWorld = "";

    public NavigationWindow(
        IPluginLog log,
        NavigationService navigation,
        Func<bool> isInDuty)
        : base("SilverDasher 导航##SilverDasherNavWindow",
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
    /// </summary>
    public void ShowForHunt(HuntMessage msg)
    {
        if (_isInDuty())
        {
            _log.Debug($"{Prefix} NavigationWindow: 副本内, 不弹窗");
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
            _huntTarget is not null ? "猎怪导航" : "FATE 导航");

        ImGui.Separator();
        ImGui.Spacing();

        // ---- 目标信息 ----
        if (_huntTarget is not null)
        {
            var h = _huntTarget;
            var rankColor = GetRankColor(h.Rank ?? "");
            ImGui.TextWrapped($"目标: {rankColor}[{h.Rank}] {h.MobName}");
            ImGui.TextDisabled($"位置: {h.TerritoryName} ({h.World})");
        }
        else if (_fateTarget is not null)
        {
            var f = _fateTarget;
            ImGui.TextWrapped($"目标: FATE {f.FateName}");
            ImGui.TextDisabled($"位置: {f.TerritoryName} ({f.World})");
        }

        ImGui.Spacing();

        // ---- 服务器匹配信息 ----
        var targetWorld = _huntTarget?.WorldName ?? _huntTarget?.World.ToString() ?? _fateTarget?.WorldName ?? _fateTarget?.World.ToString() ?? "";
        if (_serverMatch)
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
        var availWidth = ImGui.GetContentRegionAvail().X;
        var spacing = (availWidth - btnWidth * 3) / 2;

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

        ImGui.SameLine(btnWidth + spacing);

        // 停止按钮
        if (_isNavigating)
        {
            if (ImGui.Button("停止导航", new Vector2(btnWidth, 0)))
            {
                nav.CancelNavigation();
                _isNavigating = false;
                _statusText = "导航已手动停止";
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("停止导航", new Vector2(btnWidth, 0));
            ImGui.EndDisabled();
        }

        ImGui.SameLine(btnWidth * 2 + spacing * 2);

        // 关闭按钮
        if (ImGui.Button("关闭", new Vector2(btnWidth, 0)))
        {
            if (_isNavigating)
                nav.CancelNavigation();
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
        else
            return;

        _statusText = result;
        _isNavigating = _navigation.IsNavigating;
    }

    private void OnNavStateChanged(bool navigating)
    {
        _isNavigating = navigating;
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
