using System.Collections.Generic;
using System.Reflection;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 对象树的数据节点，纯数据模型，不包含任何 UI 或反射逻辑。
    /// </summary>
    internal sealed class ObjectTreeNode
    {
        /// <summary>树内唯一 Id（用于 TreeView 选择/定位）。</summary>
        public int Id { get; }

        /// <summary>父节点，根节点为 null。</summary>
        public ObjectTreeNode Parent { get; }

        /// <summary>是否为复合类型（class/struct/容器），用于显示与编辑判断。</summary>
        public bool IsClass { get; set; }

        /// <summary>显示名称（字段名 / 属性名 / 索引）。</summary>
        public string Name { get; set; }

        /// <summary>显示值（叶子为实际值，复合为摘要）。</summary>
        public string Value { get; set; }

        /// <summary>C# 风格类型名。</summary>
        public string Type { get; set; }

        /// <summary>节点承载的真实对象，编辑与二次展开时使用。</summary>
        public object OriginalObject { get; set; }

        /// <summary>来源字段（若该节点由字段产生）。</summary>
        public FieldInfo SourceField { get; set; }

        /// <summary>来源属性（若该节点由属性产生）。</summary>
        public PropertyInfo SourceProperty { get; set; }

        /// <summary>若节点是容器元素，记录其在容器中的 key（List 为 int 索引，Dictionary 为原始 key）。</summary>
        public object ContainerKey { get; set; }

        /// <summary>是否为容器（List/Dict）中的元素。</summary>
        public bool IsContainerEntry { get; set; }

        /// <summary>节点在树中的深度（根节点为 0）。</summary>
        public int Depth { get; set; }

        /// <summary>是否为"可二次展开"的占位节点（因超过最大深度而未继续展开）。</summary>
        public bool IsDrillInPoint { get; set; }

        /// <summary>子节点列表。</summary>
        public List<ObjectTreeNode> Children { get; }

        public ObjectTreeNode(ObjectTreeNode parent, int id, bool isClass, string name, string value, string typeName)
        {
            Children = new List<ObjectTreeNode>();
            Parent = parent;
            Id = id;
            IsClass = isClass;
            Name = name;
            Value = value;
            Type = typeName;
        }

        public void AddChild(ObjectTreeNode node)
        {
            Children.Add(node);
        }

        /// <summary>
        /// 沿父链检测是否存在对同一引用对象的循环引用。
        /// 使用引用相等（ReferenceEquals）而非 HashCode，避免哈希碰撞导致的误判截断。
        /// </summary>
        public bool IsCycle(object obj)
        {
            if (obj == null)
                return false;

            var current = Parent;
            while (current != null)
            {
                if (ReferenceEquals(current.OriginalObject, obj))
                    return true;
                current = current.Parent;
            }
            return false;
        }
    }
}
