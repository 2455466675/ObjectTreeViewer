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
    /// 对象树的 IMGUI TreeView：负责构建 TreeViewItem、绘制行、双击/F2 编辑与二次展开。
    /// 数据来源与搜索状态通过 <see cref="IObjectTreeViewHost"/> 提供，值写回交给 <see cref="ValueWriter"/>。
    /// </summary>
    internal sealed class ObjectTreeView : UTreeView
    {
        private readonly IObjectTreeViewHost host;
        private readonly NodePresenter presenter;
        private readonly ValueWriter valueWriter;

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
            var root = new UTreeViewItem { id = -1, depth = -1, displayName = "Root" };

            var rootNode = host.RootNode;
            if (rootNode != null)
            {
                var item = host.IsSearchActive
                    ? BuildFilteredTreeItem(rootNode, 0)
                    : BuildTreeItem(rootNode, 0);
                if (item != null)
                    root.AddChild(item);
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private ObjectTreeViewItem BuildTreeItem(ObjectTreeNode node, int depth)
        {
            var item = new ObjectTreeViewItem(node.Id, depth, presenter.GetDisplayName(node), node);
            foreach (var child in node.Children)
            {
                var childItem = BuildTreeItem(child, depth + 1);
                if (childItem != null)
                    item.AddChild(childItem);
            }
            return item;
        }

        /// <summary>构建过滤后的树：仅保留命中节点及其祖先路径。</summary>
        private ObjectTreeViewItem BuildFilteredTreeItem(ObjectTreeNode node, int depth)
        {
            bool selfMatch = host.IsNodeMatchSearch(node);
            List<ObjectTreeViewItem> matchedChildren = null;

            foreach (var child in node.Children)
            {
                var childItem = BuildFilteredTreeItem(child, depth + 1);
                if (childItem != null)
                {
                    matchedChildren ??= new List<ObjectTreeViewItem>();
                    matchedChildren.Add(childItem);
                }
            }

            if (selfMatch || matchedChildren != null)
            {
                var item = new ObjectTreeViewItem(node.Id, depth, presenter.GetDisplayName(node), node);
                if (matchedChildren != null)
                {
                    foreach (var child in matchedChildren)
                        item.AddChild(child);
                }
                return item;
            }

            return null;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var item = args.item as ObjectTreeViewItem;
            if (item?.Node == null)
            {
                base.RowGUI(args);
                return;
            }

            var rect = args.rowRect;
            var labelRect = rect;
            labelRect.x += GetContentIndent(args.item);
            labelRect.width -= GetContentIndent(args.item);

            // 搜索匹配时绘制高亮背景
            if (host.IsSearchActive && host.IsNodeMatchSearch(item.Node))
            {
                var highlightColor = host.IsCurrentSearchResult(item.Node.Id)
                    ? new Color(1f, 0.6f, 0f, 0.3f)   // 当前选中结果：橙色
                    : new Color(1f, 1f, 0f, 0.15f);   // 其他匹配结果：淡黄色
                EditorGUI.DrawRect(rect, highlightColor);
            }

            GUI.Label(labelRect, args.label, presenter.GetLabelStyle(item.Node));
        }

        protected override void KeyEvent()
        {
            var evt = Event.current;
            if (evt.type == EventType.KeyDown)
            {
                // C 键：回退到上一个树（无需选中节点）
                if (evt.keyCode == KeyCode.C)
                {
                    host.GoBack();
                    evt.Use();
                    return;
                }

                if (HasSelection())
                {
                    var selectedIds = GetSelection();
                    if (selectedIds != null && selectedIds.Count > 0)
                    {
                        var id = selectedIds[0];

                        if (evt.keyCode == KeyCode.F2)
                        {
                            HandleActivate(id);
                        }
                        else if (evt.keyCode == KeyCode.RightArrow)
                        {
                            // 右箭头：若选中的是二次展开占位节点，则触发展开
                            var item = FindItem(id, rootItem) as ObjectTreeViewItem;
                            if (item?.Node != null && item.Node.IsDrillInPoint)
                            {
                                host.DrillInto(item.Node);
                                evt.Use();
                                return;
                            }
                        }
                    }
                }
            }
            base.KeyEvent();
        }

        protected override void DoubleClickedItem(int id)
        {
            // 优先处理二次展开占位节点
            var clickedItem = FindItem(id, rootItem) as ObjectTreeViewItem;
            if (clickedItem?.Node != null && clickedItem.Node.IsDrillInPoint)
            {
                host.DrillInto(clickedItem.Node);
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
            var item = FindItem(id, rootItem) as ObjectTreeViewItem;
            if (item?.Node == null)
                return;

            if (item.Node.Type == "bool" && CanEditValue(item.Node))
            {
                ToggleBoolValue(item);
                Event.current.Use();
                return;
            }

            if (CanEditValue(item.Node))
            {
                StartEditing(item);
                Event.current.Use();
            }
        }

        /// <summary>判断节点的值是否可编辑。</summary>
        private bool CanEditValue(ObjectTreeNode node)
        {
            if (node == null) return false;
            if (node.IsClass) return false;
            if (node.Children.Count > 0) return false;
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

            int editedNodeId = editingItem.Node.Id;

            if (args.acceptedRename && args.newName != null && args.newName != args.originalName)
            {
                editingItem.EditValue = args.newName;
                ApplyValueChange(editingItem);
            }

            editingItem.IsEditing = false;
            editingItem.EditValue = null;
            editingItem = null;
            isEditing = false;

            // 重建后保持选中刚编辑的节点
            host.RefreshTree(editedNodeId);
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
            // 重建后保持选中刚切换的节点
            host.RefreshTree(item.Node.Id);
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
    }
}
