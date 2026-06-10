// 项目中存在全局的 TreeViewItem 类型，使用独立别名指向 Unity 的实现以避免冲突
using UTreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 承载 <see cref="ObjectTreeNode"/> 的 TreeViewItem，附带行内编辑状态。
    /// </summary>
    internal sealed class ObjectTreeViewItem : UTreeViewItem
    {
        public ObjectTreeNode Node { get; }
        public bool IsEditing { get; set; }
        public string EditValue { get; set; }

        public ObjectTreeViewItem(int id, int depth, string displayName, ObjectTreeNode node)
            : base(id, depth, displayName)
        {
            Node = node;
            IsEditing = false;
        }
    }
}
