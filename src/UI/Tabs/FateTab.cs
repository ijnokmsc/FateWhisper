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
/// FATE 订阅 Tab — ACT 版风格树形勾选管理。
/// 按版本(Patch)→区域(地图)→FATE 树形展示。
/// </summary>
public class FateTab
{
    private readonly PluginConfig _config;
    private readonly DataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly SubscriptionsStore _subs;

    private CheckTreeNode? _fateRoot;
    private CheckTreeNode? _specialFateRoot;
    private readonly Dictionary<string, CheckTreeNode> _nodeLookup = [];
    private readonly Dictionary<string, string> _leafParentMap = [];

    public FateTab(PluginConfig config, DataManager dataManager, IPluginLog log, SubscriptionsStore subsStore)
    {
        _config = config;
        _dataManager = dataManager;
        _log = log;
        _subs = subsStore;
    }

    public void Draw()
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.3f, 0.8f, 1.0f, 1.0f), "FATE 订阅管理");
        ImGui.Spacing();

        if (_fateRoot is null)
            BuildTree();

        ImGui.Text($"已订阅 {_subs.FateIds.Count} 个 FATE");
        ImGui.Spacing();

        if (ImGui.Button("重建树"))
            BuildTree();
        ImGui.Spacing();

        // 普通 FATE 树
        if (_fateRoot is not null)
            _fateRoot.Draw();
        else
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.3f, 0.3f, 1.0f),
                "FATE 数据为空。");

        // 特殊 FATE 树（独立节点，同级展示）
        if (_specialFateRoot is not null)
        {
            ImGui.Spacing();
            _specialFateRoot.Draw();
        }
    }

    private void BuildTree()
    {
        if (_dataManager.Fates.Count == 0) return;

        _nodeLookup.Clear();
        _leafParentMap.Clear();
        var subs = _subs.FateIds;

        // 普通 FATE：按 Patch → 地图 → FATE
        var root = new CheckTreeNode("全部 FATE", "fate-all");
        _nodeLookup["fate-all"] = root;
        root.OnCheckChanged = (id, checked_) => OnFateToggled(id, checked_);
        BuildRegularFateTree(root, subs);
        root.ValidateChildStatus();
        _fateRoot = root;

        // 特殊 FATE：从 spfates.json 分组树（ACT 版风格，与"全部 FATE"同级）
        if (_dataManager.SpecialFateGroups.Count > 0)
        {
            var spRoot = new CheckTreeNode("全部特殊 FATE", "fate-allsp-0");
            _nodeLookup["fate-allsp-0"] = spRoot;
            spRoot.OnCheckChanged = (id, checked_) => OnFateToggled(id, checked_);

            foreach (var group in _dataManager.SpecialFateGroups)
            {
                var groupNode = BuildFateGroupNode(group, subs);
                spRoot.Add(groupNode);
            }

            spRoot.ValidateChildStatus();
            _specialFateRoot = spRoot;
        }
    }

    /// <summary>
    /// 构建普通 FATE 的版本→地图树。
    /// </summary>
    private void BuildRegularFateTree(CheckTreeNode root, IReadOnlyList<int> subs)
    {
        var byPatch = new Dictionary<int, List<KeyValuePair<string, Models.FateInfo>>>();
        foreach (var kv in _dataManager.Fates)
        {
            if (int.TryParse(kv.Key, out var fateId))
            {
                var patch = kv.Value.Patch;
                if (!byPatch.ContainsKey(patch))
                    byPatch[patch] = [];
                byPatch[patch].Add(kv);
            }
        }

        foreach (var (patch, fates) in byPatch.OrderBy(kv => kv.Key))
        {
            var patchName = GetPatchName(patch);
            var patchNode = new CheckTreeNode(patchName, $"fate-patch-{patch}");
            _nodeLookup[$"fate-patch-{patch}"] = patchNode;
            patchNode.OnCheckChanged = (id, checked_) => OnFateToggled(id, checked_);

            var byMap = fates.GroupBy(f => f.Value.Territory)
                .OrderBy(g => g.Key);

            foreach (var mapGroup in byMap)
            {
                var mapId = mapGroup.Key.ToString();
                var mapName = GetMapName(mapId);
                var mapNode = new CheckTreeNode(mapName, $"fate-map-{mapId}");
                _nodeLookup[$"fate-map-{mapId}"] = mapNode;
                mapNode.OnCheckChanged = (id, checked_) => OnFateToggled(id, checked_);

                foreach (var kv in mapGroup.OrderBy(f => f.Value.NameChs))
                {
                    var fateId = int.Parse(kv.Key);
                    var displayName = kv.Value.NameChs;
                    var nodeId = $"fate-id-{kv.Key}";
                    var fateNode = new CheckTreeNode(displayName, nodeId);
                    _nodeLookup[nodeId] = fateNode;
                    _leafParentMap[nodeId] = $"fate-map-{mapId}";
                    fateNode.OnCheckChanged = (id, checked_) => OnFateToggled(id, checked_);

                if (subs.Contains(fateId))
                    fateNode.IsChecked = true;

                mapNode.Add(fateNode);
            }

            // 计算地图节点状态（子节点已全部添加）
            mapNode.ValidateChildStatus();
            patchNode.Add(mapNode);
            }

            root.Add(patchNode);
        }
    }

    /// <summary>
    /// 递归构建特殊 FATE 分组节点（对应 ACT 版 BuildFateGroupNode）。
    /// </summary>
    private CheckTreeNode BuildFateGroupNode(Models.HuntGroup group, IReadOnlyList<int> subs)
    {
        var nodeId = $"fate-group-{group.Group}";
        var node = new CheckTreeNode(group.Name, nodeId);
        _nodeLookup[nodeId] = node;
        node.OnCheckChanged = (id, checked_) => OnFateToggled(id, checked_);

        // 递归子分组
        if (group.SubGroups is not null)
        {
            foreach (var sub in group.SubGroups)
            {
                var childNode = BuildFateGroupNode(sub, subs);
                node.Add(childNode);
            }
        }

        // 叶子项 — 对应 FATE ID
        if (group.Items is not null)
        {
            foreach (var itemId in group.Items)
            {
                if (!int.TryParse(itemId, out var fateId)) continue;

                var fateName = _dataManager.LookupFateName(itemId);
                var leafNode = new CheckTreeNode(fateName, $"fate-id-{itemId}");
                _nodeLookup[$"fate-id-{itemId}"] = leafNode;
                _leafParentMap[$"fate-id-{itemId}"] = nodeId;
                leafNode.OnCheckChanged = (id, checked_) => OnFateToggled(id, checked_);

                if (subs.Contains(fateId))
                    leafNode.IsChecked = true;

                node.Add(leafNode);
            }
        }

        // 自底向上计算状态（子节点已全部添加）
        node.ValidateChildStatus();
        return node;
    }

    private void OnFateToggled(string id, bool isChecked)
    {
        try
        {
            var parts = id.Split('-');
            if (parts.Length < 3 || parts[0] != "fate" || parts[1] != "id") return;

            var fateId = int.Parse(parts[2]);
            if (isChecked)
                _subs.AddFate(fateId);
            else
                _subs.RemoveFate(fateId);

            // 就地更新节点视觉状态（不重建整个树）
            if (_nodeLookup.TryGetValue(id, out var node))
            {
                node.IsChecked = isChecked;
                if (_leafParentMap.TryGetValue(id, out var parentId) &&
                    _nodeLookup.TryGetValue(parentId, out var parentNode))
                {
                    parentNode.ValidateChildStatus();
                }
            }

            _log.Debug($"[FateWhisper] FATE 订阅变更: {id}={(isChecked ? "✓" : "✗")} 总数={_subs.FateIds.Count}");
        }
        catch (Exception ex)
        {
            _log.Error($"[FateWhisper] FATE 订阅保存失败: {ex.Message}");
        }
    }

    private string GetPatchName(int patch)
    {
        var patchInfo = _dataManager.Patches.FirstOrDefault(p => p.Code == patch);
        return patchInfo is not null && !string.IsNullOrEmpty(patchInfo.NameChs)
            ? patchInfo.NameChs
            : $"Patch {patch}";
    }

    private string GetMapName(string territoryId)
    {
        var name = _dataManager.LookupTerritoryName(territoryId);
        return name != $"Zone {territoryId}" ? name : $"区域{territoryId}";
    }
}
