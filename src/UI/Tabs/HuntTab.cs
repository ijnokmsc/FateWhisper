using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FateWhisper.Config;
using FateWhisper.Services;
using FateWhisper.UI.Widgets;

namespace FateWhisper.UI.Tabs;

/// <summary>
/// 猎怪订阅 Tab — ACT 版风格树形勾选管理。
/// 按版本(Patch)→猎怪ID 树形展示，勾选即订阅。
/// </summary>
public class HuntTab
{
    private readonly PluginConfig _config;
    private readonly DataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly SubscriptionsStore _subs;

    private CheckTreeNode? _huntRoot;
    private readonly Dictionary<string, CheckTreeNode> _nodeLookup = [];

    public HuntTab(PluginConfig config, DataManager dataManager, IPluginLog log, SubscriptionsStore subsStore)
    {
        _config = config;
        _dataManager = dataManager;
        _log = log;
        _subs = subsStore;
    }

    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "狩猎订阅管理");
        ImGui.Spacing();

        if (_huntRoot is null)
            BuildTree();

        if (_huntRoot is not null)
        {
            ImGui.Text($"已订阅 {_subs.HuntIds.Count} 只猎怪");
            ImGui.Spacing();

            if (ImGui.Button("收起全部"))
            {
                // Can't collapse ImGui tree nodes programmatically
            }
            ImGui.SameLine();
            if (ImGui.Button("重建树"))
                BuildTree();
            ImGui.Spacing();

            _huntRoot.Draw();
        }
        else
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1.0f),
                "猎怪数据为空。请检查 data/hunts.json 是否存在。");
        }
    }

    private void BuildTree()
    {
        if (_dataManager.Hunts.Count == 0) return;

        _nodeLookup.Clear();
        var subs = _subs.HuntIds;
        var root = new CheckTreeNode("全部狩猎", "hunt-all");
        _nodeLookup["hunt-all"] = root;
        root.OnCheckChanged = (id, checked_) => OnHuntToggled(id, checked_);

        // 按 Patch 分组
        var byPatch = new Dictionary<int, List<KeyValuePair<string, Models.HuntMob>>>();
        foreach (var kv in _dataManager.Hunts)
        {
            if (int.TryParse(kv.Key, out var mobId) &&
                int.TryParse(kv.Value.Patch, out var patch))
            {
                if (!byPatch.ContainsKey(patch))
                    byPatch[patch] = [];
                byPatch[patch].Add(kv);
            }
        }

        foreach (var (patch, mobs) in byPatch.OrderBy(kv => kv.Key))
        {
            var patchName = GetPatchName(patch);
            var patchNode = new CheckTreeNode(patchName, $"hunt-patch-{patch}");
            _nodeLookup[$"hunt-patch-{patch}"] = patchNode;
            patchNode.OnCheckChanged = (id, checked_) => OnHuntToggled(id, checked_);

            foreach (var kv in mobs.OrderBy(m => m.Value.NameChs))
            {
                var mob = kv.Value;
                var mobId = int.Parse(kv.Key);
                var displayName = $"{GetTerritoryName(mob.Territory)} [{mob.Rank}] {mob.NameChs}";
                var nodeId = $"hunt-id-{kv.Key}";
                var mobNode = new CheckTreeNode(displayName, nodeId);
                _nodeLookup[nodeId] = mobNode;
                mobNode.OnCheckChanged = (id, checked_) => OnHuntToggled(id, checked_);

                // 恢复已订阅状态
                if (subs.Contains(mobId))
                    mobNode.IsChecked = true;

                patchNode.Add(mobNode);
            }

            root.Add(patchNode);
        }

        // 验证各级状态
        foreach (var node in root.Nodes)
            node.ValidateChildStatus();

        _huntRoot = root;
    }

    private void OnHuntToggled(string id, bool isChecked)
    {
        try
        {
            var parts = id.Split('-');
            if (parts.Length < 3 || parts[0] != "hunt" || parts[1] != "id") return;

            var mobId = int.Parse(parts[2]);
            if (isChecked)
                _subs.AddHunt(mobId);
            else
                _subs.RemoveHunt(mobId);

            // 就地更新节点视觉状态
            if (_nodeLookup.TryGetValue(id, out var node))
            {
                node.IsChecked = isChecked;
            }
            // 重算父节点 → 根节点
            var patchId = $"hunt-patch-{GetMobPatch(mobId)}";
            if (_nodeLookup.TryGetValue(patchId, out var patchNode))
            {
                patchNode.ValidateChildStatus();
                _log.Debug($"[FateWhisper] 猎怪父节点更新: {patchNode.Name} IsChecked={patchNode.IsChecked}");
            }
            // 确保根节点也更新
            if (_nodeLookup.TryGetValue("hunt-all", out var rootNode))
            {
                rootNode.ValidateChildStatus();
            }

            _log.Debug($"[FateWhisper] 猎怪订阅变更: {id}={(isChecked ? "✓" : "✗")} 总数={_subs.HuntIds.Count}");
        }
        catch (Exception ex)
        {
            _log.Error($"[FateWhisper] 猎怪订阅保存失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据猎怪 ID 查找其所属的 Patch 编号。
    /// </summary>
    private int GetMobPatch(int mobId)
    {
        if (_dataManager.Hunts.TryGetValue(mobId.ToString(), out var mob) &&
            int.TryParse(mob.Patch, out var p))
            return p;
        return 0;
    }

    private string GetPatchName(int patch)
    {
        var patchInfo = _dataManager.Patches.FirstOrDefault(p => p.Code == patch);
        return patchInfo is not null && !string.IsNullOrEmpty(patchInfo.NameChs)
            ? patchInfo.NameChs
            : $"Patch {patch}";
    }

    private string GetTerritoryName(string territoryId)
    {
        var name = _dataManager.LookupTerritoryName(territoryId);
        return name != $"Zone {territoryId}" ? name : $"区域{territoryId}";
    }
}
