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

        /// <summary>搜索是否处于激活状态。</summary>
        bool IsSearchActive { get; }

        /// <summary>判断节点是否命中当前搜索。</summary>
        bool IsNodeMatchSearch(ObjectTreeNode node);

        /// <summary>判断节点是否为"当前选中"的搜索结果。</summary>
        bool IsCurrentSearchResult(int nodeId);

        /// <summary>请求对占位节点进行二次展开。</summary>
        void DrillInto(ObjectTreeNode node);

        /// <summary>请求回退到上一个树。</summary>
        void GoBack();

        /// <summary>在数据变更后请求重建树。可指定重建后应选中的节点 Id（null 表示选中根节点）。</summary>
        void RefreshTree(int? selectNodeId = null);
    }
}
