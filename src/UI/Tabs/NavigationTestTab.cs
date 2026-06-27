using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FateWhisper.Config;
using FateWhisper.Models;
using FateWhisper.Services;

namespace FateWhisper.UI.Tabs;

/// <summary>
/// 导航测试 Tab — 手动测试 vnavmesh 导航和 Lifestream 跨服传送。
/// 支持保存自定义地点到配置文件。
/// </summary>
public class NavigationTestTab
{
    private readonly NavigationService _navigationService;
    private readonly PluginConfig _config;
    private readonly NavigationWindow _navigationWindow;
    private readonly IObjectTable _objectTable;
    private const string Prefix = "[FateWhisper]";

    // 导航测试状态
    private string _testX = "0";
    private string _testY = "0";
    private string _testZ = "0";
    private string _testTerritory = "";
    private string _testWorld = "";
    private string _testLiCommand = "";
    private string _navTestStatus = "";
    private int _selectedPreset = 0;

    // 保存地点 UI
    private string _newLocationName = "";
    private string _newLocationWorld = "";
    private string _newLocationTerritory = "";
    private string _newLocationX = "0";
    private string _newLocationY = "0";
    private string _newLocationZ = "0";
    private int _selectedSavedLocation = -1;

    // 导航预设地点
    private static readonly (string Name, uint Territory, float X, float Y, float Z)[] NavPresets = {
        ("水晶都", 819, -36f, 3f, -48f),
        ("利姆萨·罗敏萨下层甲板", 129, -73f, 17f, -548f),
        ("格里达尼亚新街", 132, 157f, 1f, 205f),
        ("乌尔达哈现世回廊", 130, 25f, 4f, -27f),
        ("伊修加德基础层", 418, 53f, 32f, -10f),
        ("黄金港", 635, -37f, 3f, 3f),
        ("图莱尤拉", 1185, -24f, 37f, -573f),
        ("水晶都(自定坐标)", 819, 0f, 0f, 0f),
    };

    public NavigationTestTab(NavigationService navigationService, PluginConfig config, NavigationWindow navigationWindow, IObjectTable objectTable)
    {
        _navigationService = navigationService;
        _config = config;
        _navigationWindow = navigationWindow;
        _objectTable = objectTable;
        _config.LoadNavTestLocations();
    }

    public void Draw()
    {
        ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), "导航测试");
        ImGui.Spacing();

        // --- 插件状态 ---
        DrawPluginStatus();

        // --- 预设地点选择 ---
        DrawPresetSelector();

        // --- 手动坐标输入 ---
        DrawCoordinateInput();

        // --- 导航按钮 ---
        DrawNavButtons();

        // --- 保存地点区域 ---
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawSavedLocations();

        // --- Lifestream 跨服传送测试 ---
        DrawLifestreamTest();

        // --- 状态文本 ---
        DrawStatusText();
    }

    private void DrawPluginStatus()
    {
        var vnavOk = _navigationService.IsVnavmeshAvailable;
        var lifeOk = _navigationService.IsLifestreamAvailable;
        ImGui.Text("vnavmesh: ");
        ImGui.SameLine();
        ImGui.TextColored(
            vnavOk ? new Vector4(0.3f, 1f, 0.3f, 1f) : new Vector4(1f, 0.3f, 0.3f, 1f),
            vnavOk ? "已连接" : "未安装");
        ImGui.SameLine();
        ImGui.Text("| Lifestream: ");
        ImGui.SameLine();
        ImGui.TextColored(
            lifeOk ? new Vector4(0.3f, 1f, 0.3f, 1f) : new Vector4(1f, 0.3f, 0.3f, 1f),
            lifeOk ? "已连接" : "未安装");

        ImGui.Spacing();
        ImGui.TextDisabled($"当前区域: {_navigationService.CurrentTerritoryType} | 导航中: {_navigationService.IsNavigating}");
        ImGui.Spacing();
    }

    private void DrawPresetSelector()
    {
        ImGui.Text("预设地点:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("##navPreset", ref _selectedPreset,
            NavPresets.Select(p => p.Name).ToArray(), NavPresets.Length))
        {
            var preset = NavPresets[_selectedPreset];
            _testTerritory = preset.Territory.ToString();
            _testX = preset.X.ToString("F1");
            _testY = preset.Y.ToString("F1");
            _testZ = preset.Z.ToString("F1");
        }
        ImGui.Spacing();
    }

    private void DrawCoordinateInput()
    {
        ImGui.Text("手动坐标:");
        ImGui.Spacing();

        float inputWidth = 80f;
        ImGui.Text("Territory:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.InputText("##testTerr", ref _testTerritory, 10);

        ImGui.SameLine();
        ImGui.Text("X:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.InputText("##testX", ref _testX, 10);

        ImGui.SameLine();
        ImGui.Text("Y:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.InputText("##testY", ref _testY, 10);

        ImGui.SameLine();
        ImGui.Text("Z:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(inputWidth);
        ImGui.InputText("##testZ", ref _testZ, 10);
        ImGui.Spacing();
    }

    private void DrawNavButtons()
    {
        if (ImGui.Button("开始导航", new Vector2(100, 0)))
        {
            TryStartNavigation();
        }

        ImGui.SameLine();

        if (_navigationService.IsNavigating)
        {
            if (ImGui.Button("停止导航", new Vector2(100, 0)))
            {
                _navigationService.CancelNavigation();
                _navTestStatus = "导航已停止";
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("停止导航", new Vector2(100, 0));
            ImGui.EndDisabled();
        }

        ImGui.SameLine();

        if (ImGui.Button("检查状态", new Vector2(100, 0)))
        {
            var running = _navigationService.CheckIsRunning();
            _navTestStatus = $"vnavmesh.Path.IsRunning = {running}";
        }
    }

    private void DrawSavedLocations()
    {
        ImGui.TextColored(new Vector4(0.3f, 1.0f, 0.6f, 1.0f), "保存的地点");
        ImGui.Spacing();

        // ---- 输入新地点（一行内: 名称 服务器 Terr X Y Z） ----
        float iw = 68f;
        ImGui.SetNextItemWidth(100);
        ImGui.InputText("名称##locName", ref _newLocationName, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        ImGui.InputText("服务器##locWorld", ref _newLocationWorld, 30);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(iw);
        ImGui.InputText("Terr##locTerr", ref _newLocationTerritory, 10);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(iw);
        ImGui.InputText("X##locX", ref _newLocationX, 10);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(iw);
        ImGui.InputText("Y##locY", ref _newLocationY, 10);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(iw);
        ImGui.InputText("Z##locZ", ref _newLocationZ, 10);

        ImGui.Spacing();

        if (ImGui.Button("从输入保存", new Vector2(100, 0)))
            SaveFromInputs();

        ImGui.SameLine();

        if (ImGui.Button("保存当前", new Vector2(100, 0)))
            SaveCurrentLocation();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // ---- 已保存地点列表 ----
        var locations = _config.NavTestLocations;
        if (locations.Count > 0)
        {
            ImGui.Text($"已保存 {locations.Count} 个地点");
            ImGui.Spacing();

            var names = locations.Select((l, i) => $"[{i + 1}] {l.Name} ({l.WorldName})").ToArray();
            if (_selectedSavedLocation >= locations.Count)
                _selectedSavedLocation = -1;

            ImGui.SetNextItemWidth(220);
            if (ImGui.Combo("##savedLocs", ref _selectedSavedLocation, names, names.Length))
            {
                // 选择后自动填入坐标
                if (_selectedSavedLocation >= 0 && _selectedSavedLocation < locations.Count)
                {
                    var loc = locations[_selectedSavedLocation];
                    _testTerritory = loc.Territory.ToString();
                    _testX = loc.X.ToString("F1");
                    _testY = loc.Y.ToString("F1");
                    _testZ = loc.Z.ToString("F1");
                    _testWorld = loc.WorldName;
                }
            }

            ImGui.SameLine();

            if (_selectedSavedLocation >= 0 && _selectedSavedLocation < locations.Count)
            {
                if (ImGui.Button("导航", new Vector2(60, 0)))
                {
                    var loc = locations[_selectedSavedLocation];
                    _navigationWindow.ShowForTest(loc.Name, loc.WorldName, loc.Territory, loc.X, loc.Y, loc.Z);
                    _navTestStatus = $"测试导航: {loc.Name}";
                }

                ImGui.SameLine();

                if (ImGui.Button("删除", new Vector2(60, 0)))
                {
                    locations.RemoveAt(_selectedSavedLocation);
                    _selectedSavedLocation = -1;
                    _config.SaveNavTestLocations();
                    _navTestStatus = "地点已删除";
                }
            }
        }
        else
        {
            ImGui.TextDisabled("暂无保存的地点，使用上方输入框添加");
        }
    }

    private void DrawLifestreamTest()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_navigationService.IsLifestreamAvailable)
        {
            ImGui.Text("跨服传送测试 (Lifestream /li):");
            ImGui.Spacing();

            ImGui.SetNextItemWidth(150);
            ImGui.InputText("服务器##testWorld", ref _testWorld, 30);
            ImGui.SameLine();

            if (ImGui.Button("/li 传送", new Vector2(100, 0)))
            {
                if (!string.IsNullOrWhiteSpace(_testWorld))
                {
                    var ok = _navigationService.ChangeWorld(_testWorld.Trim());
                    _navTestStatus = $"/li {_testWorld}: {(ok ? "已接受" : "失败")}";
                }
            }
            ImGui.SameLine();

            if (!string.IsNullOrWhiteSpace(_testWorld))
            {
                var dc = _navigationService.LookupDcByWorldNameForTest(_testWorld.Trim());
                ImGui.TextDisabled($"  DC: {(string.IsNullOrEmpty(dc) ? "未知" : dc)}");
            }
            ImGui.SameLine();

            ImGui.SetNextItemWidth(150);
            ImGui.InputText("/li 命令##testLiCmd", ref _testLiCommand, 60);
            ImGui.SameLine();

            if (ImGui.Button("执行", new Vector2(60, 0)))
            {
                if (!string.IsNullOrWhiteSpace(_testLiCommand))
                {
                    var ok = _navigationService.LifestreamExecuteCommand(_testLiCommand.Trim());
                    _navTestStatus = $"/li {_testLiCommand}: {(ok ? "已接受" : "失败")}";
                }
            }
            ImGui.SameLine();

            if (ImGui.Button("忙碌?", new Vector2(80, 0)))
            {
                _navTestStatus = $"Lifestream.IsBusy = {_navigationService.IsLifestreamBusy()}";
            }

            ImGui.TextDisabled("提示: /li 命令支持复合语法，如 \"萌芽池, tp 利特林\"");
        }
        else
        {
            ImGui.TextDisabled("Lifestream 未安装或已被禁用，跨服功能不可用");
            ImGui.TextDisabled("如需跨服导航，请安装 Lifestream 或使用 DCTraveler");
        }
    }

    private void DrawStatusText()
    {
        ImGui.Spacing();
        if (!string.IsNullOrEmpty(_navTestStatus))
        {
            ImGui.Separator();
            ImGui.Spacing();
            var color = _navigationService.IsNavigating
                ? new Vector4(0.3f, 1f, 0.6f, 1f)
                : new Vector4(0.7f, 0.7f, 0.7f, 1f);
            ImGui.TextColored(color, _navTestStatus);
        }
    }

    /// <summary>
    /// 从当前输入框保存新地点。
    /// </summary>
    private void SaveFromInputs()
    {
        if (string.IsNullOrWhiteSpace(_newLocationName))
        { _navTestStatus = "请输入地点名称"; return; }
        if (!uint.TryParse(_newLocationTerritory, out var territory))
        { _navTestStatus = "Territory ID 格式错误"; return; }
        if (!float.TryParse(_newLocationX, out var x) ||
            !float.TryParse(_newLocationY, out var y) ||
            !float.TryParse(_newLocationZ, out var z))
        { _navTestStatus = "坐标格式错误"; return; }

        var loc = new NavTestLocation
        {
            Name = _newLocationName,
            WorldName = string.IsNullOrWhiteSpace(_newLocationWorld) ? _navigationService.PlayerWorldName : _newLocationWorld,
            Territory = territory,
            X = x, Y = y, Z = z
        };
        _config.NavTestLocations.Add(loc);
        _config.SaveNavTestLocations();
        _navTestStatus = $"已保存: {loc.Name}";
    }

    /// <summary>
    /// 保存当前游戏中的位置到列表。
    /// </summary>
    private void SaveCurrentLocation()
    {
        var player = _objectTable.LocalPlayer;
        if (player == null)
        { _navTestStatus = "无法获取角色信息"; return; }

        var pos = player.Position;
        var worldName = _navigationService.PlayerWorldName;
        var territory = _navigationService.CurrentTerritoryType;

        // 未填名称时自动生成
        var name = !string.IsNullOrWhiteSpace(_newLocationName)
            ? _newLocationName
            : $"地点_{_config.NavTestLocations.Count + 1}";

        var loc = new NavTestLocation
        {
            Name = name,
            WorldName = worldName,
            Territory = territory,
            X = pos.X, Y = pos.Y, Z = pos.Z
        };
        _config.NavTestLocations.Add(loc);
        _config.SaveNavTestLocations();
        _newLocationName = "";
        _navTestStatus = $"已保存: {loc.Name} @ ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})";
    }

    /// <summary>
    /// 尝试启动导航，解析输入的坐标并调用 NavigationService.NavigateToTest。
    /// </summary>
    private void TryStartNavigation()
    {
        if (!_navigationService.IsVnavmeshAvailable)
        {
            _navTestStatus = "vnavmesh 不可用，请先安装";
            return;
        }

        if (!uint.TryParse(_testTerritory, out var territory))
        {
            _navTestStatus = "Territory ID 格式错误";
            return;
        }

        if (!float.TryParse(_testX, out var x) ||
            !float.TryParse(_testY, out var y) ||
            !float.TryParse(_testZ, out var z))
        {
            _navTestStatus = "坐标格式错误，请输入数字";
            return;
        }

        var pos = new Vector3(x, y, z);
        var fly = territory >= 600;
        _navTestStatus = _navigationService.NavigateToTest(territory, pos, fly);
    }
}
