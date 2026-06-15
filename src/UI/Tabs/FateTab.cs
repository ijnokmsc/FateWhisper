using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using SilverDasher.Config;

namespace SilverDasher.UI.Tabs;

/// <summary>
/// FATE 订阅 Tab，管理 FATE 类型和区域筛选。
/// </summary>
public class FateTab
{
    private readonly PluginConfig _config;
    private readonly IPluginLog _log;
    private const string Prefix = "[SilverDasher]";

    public FateTab(PluginConfig config, IPluginLog log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>
    /// 绘制 FATE 订阅界面。
    /// </summary>
    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "FATE 播报订阅");

        ImGui.Spacing();

        var fateEnabled = _config.CWFate.Enabled;
        if (ImGui.Checkbox("启用 FATE 播报", ref fateEnabled))
        {
            _config.CWFate.Enabled = fateEnabled;
            _config.Save();
        }

        ImGui.Spacing();

        if (!_config.CWFate.Enabled)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
        }

        ImGui.Indent(16f);

        var common = _config.CWFate.Common;
        if (ImGui.Checkbox("普通 FATE", ref common))
        {
            _config.CWFate.Common = common;
            _config.Save();
        }

        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  普通 FATE 数量巨大，启用会频繁触发通知。");

        ImGui.Spacing();

        var special = _config.CWFate.Special;
        if (ImGui.Checkbox("特殊 FATE", ref special))
        {
            _config.CWFate.Special = special;
            _config.Save();
        }

        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "  特殊 FATE 包括博兹雅、优雷卡等独立副本的专属 FATE。");

        ImGui.Unindent(16f);

        if (!_config.CWFate.Enabled)
        {
            ImGui.PopStyleVar();
        }

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1.0f),
            "提示：您在本地区域触发的 FATE 会自动上报到同大区播报网络。");
    }
}
