namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 视图（<see cref="ObjectTreeView"/>）回调宿主的抽象，避免视图直接依赖具体窗口类。
    /// 便于测试与未来扩展（例如换用其他容器承载视图）。
    /// </summary>
    internal interface IObjectTreeViewHost
    {
        /// <summary>当前树的根数据节点（可能为 null）。</summary>
        ObjectTreeNode RootNode { get; }

        /// <summary>懒加载：按需构建指定节点的直接子节点（仅一层）。</summary>
        void EnsureChildren(ObjectTreeNode node);

        /// <summary>叶子节点的值被编辑后，就地重新读取该节点的显示值（不重建整棵树）。</summary>
        void RefreshNodeValue(ObjectTreeNode node);
    }
}
