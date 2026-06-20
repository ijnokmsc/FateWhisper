using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace SilverDasher.UI.Widgets;

/// <summary>
/// 可勾选树节点，对齐 ACT 版 CheckTreeNode。
/// 支持三级状态：true=全选, false=全不选, null=部分选。
/// 通过回调通知外部订阅变更。
/// </summary>
public class CheckTreeNode
{
    private CheckTreeNode? _parent;
    private readonly List<CheckTreeNode> _children = [];
    private readonly List<CheckTreeNode> _related = [];

    public string Name { get; }
    public string Id { get; }

    /// <summary>子节点列表</summary>
    public IReadOnlyList<CheckTreeNode> Nodes => _children.AsReadOnly();

    /// <summary>true=全选, false=全不选, null=部分选</summary>
    public bool? IsChecked { get; set; } = false;

    /// <summary>勾选状态变更回调 (nodeId, isChecked)</summary>
    public Action<string, bool>? OnCheckChanged { get; set; }

    public CheckTreeNode(string name, string id)
    {
        Name = name;
        Id = id;
    }

    public void Add(CheckTreeNode node)
    {
        if (_children.Contains(node) || node._parent == this) return;
        node._parent = this;
        _children.Add(node);
    }

    public void Draw()
    {
        ImGui.PushID(Id);

        if (_children.Count > 0)
        {
            // 有子节点 — 显示复选框 + Tree 折叠
            var isChecked = IsChecked ?? false;
            var isPartial = IsChecked is null;

            if (isPartial)
            {
                ImGui.PushStyleColor(ImGuiCol.CheckMark, 0xFF888888);
            }

            var changed = ImGui.Checkbox($"##chk_{Id}", ref isChecked);

            if (isPartial)
            {
                ImGui.PopStyleColor();
            }

            ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
            var open = ImGui.TreeNodeEx(Name);

            if (changed) OnCheckedChanged(isChecked);

            if (open)
            {
                foreach (var child in _children)
                    child.Draw();
                ImGui.TreePop();
            }
        }
        else
        {
            // 叶节点 — 普通复选框
            var isChecked = IsChecked ?? false;
            if (ImGui.Checkbox(Name, ref isChecked))
                OnCheckedChanged(isChecked);
        }

        ImGui.PopID();
    }

    private void OnCheckedChanged(bool isChecked)
    {
        IsChecked = isChecked;

        // 同步关联节点
        foreach (var rel in _related)
            rel.IsChecked = isChecked;

        // 同步子节点
        foreach (var child in _children)
            child.OnCheckedChanged(isChecked);

        // 通知外部
        OnCheckChanged?.Invoke(Id, isChecked);

        // 通知父节点重新计算状态
        _parent?.ValidateChildStatus();
    }

    internal void ValidateChildStatus()
    {
        int checkedCount = 0, uncheckedCount = 0;
        foreach (var child in _children)
        {
            switch (child.IsChecked)
            {
                case true: checkedCount++; break;
                case false: uncheckedCount++; break;
            }
        }

        if (checkedCount == _children.Count)
            IsChecked = true;
        else if (uncheckedCount == _children.Count)
            IsChecked = false;
        else
            IsChecked = null;

        _parent?.ValidateChildStatus();
    }
}
