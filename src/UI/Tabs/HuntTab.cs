using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using SilverDasher.Config;

namespace SilverDasher.UI.Tabs;

/// <summary>
/// 猎怪订阅 Tab，管理 CW/CDC 猎怪等级和区域筛选。
/// </summary>
public class HuntTab
{
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private const string Prefix = "[SilverDasher]";

    public HuntTab(PluginConfig config, IPluginLog log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>
    /// 绘制猎怪订阅界面。
    /// </summary>
    public void Draw()
    {
        DrawCWHuntSection();
        ImGui.Separator();
        ImGui.Spacing();
        DrawCDCHuntSection();
    }

    /// <summary>
    /// 绘制同大区猎怪订阅设置。
    /// </summary>
    private void DrawCWHuntSection()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "同大区猎怪订阅");
        ImGui.Spacing();

        var cwEnabled = _config.CWHunt.Enabled;
        if (ImGui.Checkbox("启用同大区猎怪播报", ref cwEnabled))
        {
            _config.CWHunt.Enabled = cwEnabled;
            _config.Save();
        }

        if (!_config.CWHunt.Enabled)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
        }

        ImGui.Indent(16f);

        var rankB = _config.CWHunt.RankB;
        if (ImGui.Checkbox("B 级猎怪", ref rankB))
        {
            _config.CWHunt.RankB = rankB;
            _config.Save();
        }

        var rankA = _config.CWHunt.RankA;
        if (ImGui.Checkbox("A 级猎怪", ref rankA))
        {
            _config.CWHunt.RankA = rankA;
            _config.Save();
        }

        var rankS = _config.CWHunt.RankS;
        if (ImGui.Checkbox("S 级猎怪", ref rankS))
        {
            _config.CWHunt.RankS = rankS;
            _config.Save();
        }

        var rankSS = _config.CWHunt.RankSS;
        if (ImGui.Checkbox("SS 级猎怪", ref rankSS))
        {
            _config.CWHunt.RankSS = rankSS;
            _config.Save();
        }

        ImGui.Unindent(16f);

        if (!_config.CWHunt.Enabled)
        {
            ImGui.PopStyleVar();
        }
    }

    /// <summary>
    /// 绘制跨大区猎怪订阅设置。
    /// </summary>
    private void DrawCDCHuntSection()
    {
        ImGui.TextColored(new System.Numerics.Vector4(1.0f, 0.6f, 0.2f, 1.0f), "跨大区猎怪订阅");
        ImGui.Spacing();

        var cdcEnabled = _config.CDCHunt.Enabled;
        if (ImGui.Checkbox("启用跨大区猎怪播报", ref cdcEnabled))
        {
            _config.CDCHunt.Enabled = cdcEnabled;
            _config.Save();
        }

        if (!_config.CDCHunt.Enabled)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
        }

        ImGui.Indent(16f);

        var rankB = _config.CDCHunt.RankB;
        if (ImGui.Checkbox("B 级猎怪##cdc", ref rankB))
        {
            _config.CDCHunt.RankB = rankB;
            _config.Save();
        }

        var rankA = _config.CDCHunt.RankA;
        if (ImGui.Checkbox("A 级猎怪##cdc", ref rankA))
        {
            _config.CDCHunt.RankA = rankA;
            _config.Save();
        }

        var rankS = _config.CDCHunt.RankS;
        if (ImGui.Checkbox("S 级猎怪##cdc", ref rankS))
        {
            _config.CDCHunt.RankS = rankS;
            _config.Save();
        }

        var rankSS = _config.CDCHunt.RankSS;
        if (ImGui.Checkbox("SS 级猎怪##cdc", ref rankSS))
        {
            _config.CDCHunt.RankSS = rankSS;
            _config.Save();
        }

        ImGui.Unindent(16f);

        if (!_config.CDCHunt.Enabled)
        {
            ImGui.PopStyleVar();
        }
    }
}
