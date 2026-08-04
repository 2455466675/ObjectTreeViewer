using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
// 项目中存在全局的 TreeView / TreeViewItem 类型，与 UnityEditor.IMGUI.Controls 同名冲突，
// 使用独立别名显式指向 Unity 的 IMGUI 实现（其余 RowGUIArgs/RenameEndedArgs 等无冲突，正常使用）
using UTreeView = UnityEditor.IMGUI.Controls.TreeView;
using UTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 对象树的 IMGUI TreeView：懒加载渲染。仅为“已构建”的数据节点生成 TreeViewItem；
    /// 可展开但尚未构建的节点挂一个占位子项以显示展开箭头，用户展开时在
    /// <see cref="ExpandedStateChanged"/> 中调用宿主增量构建该层后重建。
    /// 数据来源通过 <see cref="IObjectTreeViewHost"/> 提供，值写回交给 <see cref="ValueWriter"/>。
    /// </summary>
    internal sealed class ObjectTreeView : UTreeView
    {
        private readonly IObjectTreeViewHost host;
        private readonly NodePresenter presenter;
        private readonly ValueWriter valueWriter;

        // Id -> 数据节点映射，避免使用会遍历占位子项的 FindItem。
        private readonly Dictionary<int, ObjectTreeNode> idToNode = new Dictionary<int, ObjectTreeNode>();

        private ObjectTreeViewItem editingItem;
        private bool isEditing;

        public ObjectTreeView(TreeViewState state, IObjectTreeViewHost host, NodePresenter presenter, ValueWriter valueWriter)
            : base(state)
        {
            this.host = host;
            this.presenter = presenter;
            this.valueWriter = valueWriter;
            showAlternatingRowBackgrounds = true;
            showBorder = true;
            Reload();
        }

        protected override UTreeViewItem BuildRoot()
        {
            idToNode.Clear();
            var root = new UTreeViewItem { id = -1, depth = -1, displayName = "Root" };

            var rootNode = host.RootNode;
            if (rootNode != null)
                root.AddChild(BuildTreeItem(rootNode, 0));

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private ObjectTreeViewItem BuildTreeItem(ObjectTreeNode node, int depth)
        {
            var item = new ObjectTreeViewItem(node.Id, depth, presenter.GetDisplayName(node), node);
            idToNode[node.Id] = node;

            if (node.ChildrenBuilt)
            {
                foreach (var child in node.Children)
                    item.AddChild(BuildTreeItem(child, depth + 1));
            }
            else if (node.CanExpand)
            {
                // 占位子项：仅用于显示展开箭头。使用负 Id 避免与真实节点（正 Id）冲突。
                // 展开时会在 ExpandedStateChanged 中先构建真实子节点并 Reload，占位项不会真正渲染。
                item.AddChild(new ObjectTreeViewItem(-node.Id - 2, depth + 1, "", null));
            }

            return item;
        }

        /// <summary>用户展开/折叠状态变化时，为新展开且尚未构建的节点增量构建子节点。</summary>
        protected override void ExpandedStateChanged()
        {
            bool built = false;
            foreach (var id in GetExpanded())
            {
                if (idToNode.TryGetValue(id, out var node) && node.CanExpand && !node.ChildrenBuilt)
                {
                    host.EnsureChildren(node);
                    built = true;
                }
            }

            if (built)
                Reload();
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item as ObjectTreeViewItem;
            if (item?.Node == null)
            {
                base.RowGUI(args);
                return;
            }

            var labelRect = args.rowRect;
            var indent = GetContentIndent(args.item);
            labelRect.x += indent;
            labelRect.width -= indent;

            GUI.Label(labelRect, args.label, presenter.GetLabelStyle(item.Node));
        }

        protected override void KeyEvent()
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.F2 && HasSelection())
            {
                var selectedIds = GetSelection();
                if (selectedIds != null && selectedIds.Count > 0)
                    HandleActivate(selectedIds[0]);
            }
            base.KeyEvent();
        }

        protected override void DoubleClickedItem(int id)
        {
            if (!idToNode.TryGetValue(id, out var node))
                return;

            // 可展开节点：双击切换展开状态（子节点按需构建）
            if (node.CanExpand)
            {
                SetExpanded(id, !IsExpanded(id));
                return;
            }

            HandleActivate(id);
        }

        protected override bool CanRename(UTreeViewItem item)
        {
            return CanEditValue((item as ObjectTreeViewItem)?.Node);
        }

        protected override void RenameEnded(RenameEndedArgs args)
        {
            base.RenameEnded(args);
            EndEditing(args);
        }

        /// <summary>处理回车/双击的激活：bool 直接切换，其它进入重命名编辑。</summary>
        private void HandleActivate(int id)
        {
            var item = GetItemById(id);
            if (item?.Node == null)
                return;

            var node = item.Node;

            if (node.Type == "bool" && CanEditValue(node))
            {
                ToggleBoolValue(item);
                Event.current.Use();
                return;
            }

            if (CanEditValue(node))
            {
                StartEditing(item);
                Event.current.Use();
            }
        }

        /// <summary>在当前行集合中按 Id 查找对应的 ObjectTreeViewItem（不遍历占位项）。</summary>
        private ObjectTreeViewItem GetItemById(int id)
        {
            foreach (var row in GetRows())
            {
                if (row.id == id && row is ObjectTreeViewItem item && item.Node != null)
                    return item;
            }
            return null;
        }

        /// <summary>判断节点的值是否可编辑。</summary>
        private bool CanEditValue(ObjectTreeNode node)
        {
            if (node == null) return false;
            if (node.IsClass) return false;
            if (node.CanExpand) return false;
            if (node.Value == "null" || node.Value == "循环引用") return false;
            // 结构体字段不可编辑（修改 struct 字段需写回整个 struct，实现复杂）
            if (IsStructField(node)) return false;
            return true;
        }

        /// <summary>沿父链向上判断节点是否属于某个结构体。</summary>
        private bool IsStructField(ObjectTreeNode node)
        {
            var current = node.Parent;
            while (current != null)
            {
                var obj = current.OriginalObject;
                if (obj != null && obj.GetType().IsValueType)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        private void StartEditing(ObjectTreeViewItem item)
        {
            editingItem = item;
            editingItem.IsEditing = true;
            editingItem.EditValue = editingItem.Node.Value;
            editingItem.displayName = editingItem.Node.Value;
            isEditing = true;
            BeginRename(item);
        }

        private void EndEditing(RenameEndedArgs args)
        {
            if (editingItem == null || !isEditing)
                return;

            var editedNode = editingItem.Node;

            if (args.acceptedRename && args.newName != null && args.newName != args.originalName)
            {
                editingItem.EditValue = args.newName;
                ApplyValueChange(editingItem);
            }

            editingItem.IsEditing = false;
            editingItem.EditValue = null;
            editingItem = null;
            isEditing = false;

            // 就地刷新该节点的显示值并重建行，保留当前展开状态
            host.RefreshNodeValue(editedNode);
            Reload();
            SelectAndFocus(editedNode.Id);
        }

        private void ToggleBoolValue(ObjectTreeViewItem item)
        {
            var currentValue = item.Node.Value.ToLower();
            bool newValue;
            if (currentValue == "true")
                newValue = false;
            else if (currentValue == "false")
                newValue = true;
            else
            {
                Debug.LogWarning($"无法识别的 bool 值: {item.Node.Value}");
                return;
            }

            item.EditValue = newValue.ToString();
            ApplyValueChange(item);

            host.RefreshNodeValue(item.Node);
            Reload();
            SelectAndFocus(item.Node.Id);
        }

        private void ApplyValueChange(ObjectTreeViewItem item)
        {
            var result = valueWriter.Write(item.Node, item.EditValue);
            if (result.Success)
                Debug.Log(result.Message);
            else
                Debug.LogWarning(result.Message);
        }

        /// <summary>选中指定节点并让树视图获得键盘焦点，便于立即使用方向键导航。</summary>
        public void SelectAndFocus(int nodeId)
        {
            SetSelection(new List<int> { nodeId }, TreeViewSelectionOptions.RevealAndFrame);
            SetFocusAndEnsureSelectedItem();
        }

        /// <summary>展开根节点并选中指定节点（默认根节点），用于初始与刷新后的定位。</summary>
        public void ExpandRootAndSelect(int nodeId)
        {
            var rootNode = host.RootNode;
            if (rootNode != null)
                SetExpanded(rootNode.Id, true);
            SelectAndFocus(nodeId);
        }
    }
}
