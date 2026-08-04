using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 由运行时对象按需（懒加载）构建 <see cref="ObjectTreeNode"/> 树。
    /// 构建时仅生成根节点与其直接子节点；更深层级在节点被展开时通过
    /// <see cref="EnsureChildren"/> 增量构建。负责循环引用检测与容器/结构体/类的展开策略。
    /// 一个 builder 实例服务于同一棵树，<see cref="nextId"/> 在整棵树生命周期内持续递增以保证 Id 唯一。
    /// </summary>
    internal sealed class ObjectTreeBuilder
    {
        /// <summary>构建参数与限制。</summary>
        public sealed class Options
        {
            /// <summary>整棵树的节点总数上限，超过则中止以防卡死。</summary>
            public int MaxNodeCount = 20000;

            /// <summary>单个节点一次展开允许生成的最大子节点数，超出以“未显示”占位提示，避免超大容器一次性铺开。</summary>
            public int MaxChildrenPerNode = 5000;
        }

        private readonly ReflectionInspector inspector;
        private readonly MemberFilter memberFilter;
        private readonly Options options;
        private int nextId;

        public ObjectTreeBuilder(ReflectionInspector inspector, MemberFilter memberFilter, Options options)
        {
            this.inspector = inspector;
            this.memberFilter = memberFilter;
            this.options = options ?? new Options();
        }

        /// <summary>以 <paramref name="target"/> 为根构建根节点，并构建其直接子节点（第一层）。</summary>
        public ObjectTreeNode BuildRoot(object target)
        {
            if (target == null)
                return null;

            nextId = 1;
            var root = new ObjectTreeNode(null, NextId(), true,
                target.GetType().Name, "", inspector.GetCSharpTypeName(target.GetType()))
            {
                Depth = 0,
                OriginalObject = target,
            };
            DescribeComposite(root, target);

            EnsureChildren(root);
            return root;
        }

        /// <summary>
        /// 确保 <paramref name="node"/> 的直接子节点已构建（仅一层）。已构建或不可展开则直接返回。
        /// </summary>
        public void EnsureChildren(ObjectTreeNode node)
        {
            if (node == null || node.ChildrenBuilt || !node.CanExpand)
                return;

            node.ChildrenBuilt = true;

            var obj = node.OriginalObject;
            if (obj == null)
                return;

            var type = obj.GetType();

            if (obj is IList list)
                BuildListItems(node, list);
            else if (obj is IDictionary dict)
                BuildDictItems(node, dict);
            else
                BuildMembers(node, obj, type);
        }

        /// <summary>递归构建整棵树（供导出使用）。受节点上限与循环引用保护。</summary>
        public void BuildFullTree(ObjectTreeNode node)
        {
            if (node == null)
                return;

            EnsureChildren(node);
            foreach (var child in node.Children)
                BuildFullTree(child);
        }

        private int NextId() => nextId++;

        private bool ReachedNodeLimit()
        {
            if (nextId > options.MaxNodeCount)
            {
                Debug.LogWarning("当前对象数据过多！已停止继续展开。");
                return true;
            }
            return false;
        }

        private void BuildMembers(ObjectTreeNode node, object obj, Type type)
        {
            int count = 0;

            foreach (var field in inspector.GetAllFields(type))
            {
                if (!memberFilter.ShouldInclude(field))
                    continue;
                if (!EnsureChildSlot(node, ref count))
                    return;

                try
                {
                    var value = field.GetValue(obj);
                    var child = NewChild(node, field.Name, inspector.GetCSharpTypeName(field.FieldType), sourceField: field);
                    FillNode(child, value);
                }
                catch (Exception e)
                {
                    AddErrorNode(node, field.Name, e);
                }
            }

            foreach (var prop in inspector.GetAllProperties(type))
            {
                if (!memberFilter.ShouldInclude(prop))
                    continue;
                if (!EnsureChildSlot(node, ref count))
                    return;

                try
                {
                    var value = prop.GetValue(obj);
                    var child = NewChild(node, prop.Name, inspector.GetCSharpTypeName(prop.PropertyType), sourceProperty: prop);
                    FillNode(child, value);
                }
                catch (Exception e)
                {
                    AddErrorNode(node, prop.Name, e);
                }
            }
        }

        private void BuildListItems(ObjectTreeNode node, IList list)
        {
            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!EnsureChildSlot(node, ref count))
                    return;

                try
                {
                    var value = list[i];
                    var typeName = value != null ? inspector.GetCSharpTypeName(value.GetType()) : "null";
                    var child = NewChild(node, $"[{i}]", typeName);
                    child.IsContainerEntry = true;
                    child.ContainerKey = i;
                    FillNode(child, value);
                }
                catch (Exception e)
                {
                    AddErrorNode(node, $"[{i}]", e);
                }
            }
        }

        private void BuildDictItems(ObjectTreeNode node, IDictionary dict)
        {
            int count = 0;
            foreach (DictionaryEntry entry in dict)
            {
                if (!EnsureChildSlot(node, ref count))
                    return;

                var key = entry.Key;
                var value = entry.Value;
                var keyStr = key?.ToString() ?? "null";
                var typeName = value != null ? inspector.GetCSharpTypeName(value.GetType()) : "null";
                var child = NewChild(node, $"[{keyStr}]", typeName);
                child.IsContainerEntry = true;
                child.ContainerKey = key;
                FillNode(child, value);
            }
        }

        /// <summary>
        /// 校验是否还能继续为 <paramref name="node"/> 追加子节点：
        /// 达到单节点子数上限或整树节点上限时追加占位提示并返回 false。
        /// </summary>
        private bool EnsureChildSlot(ObjectTreeNode node, ref int count)
        {
            if (count >= options.MaxChildrenPerNode)
            {
                AddInfoLeaf(node, "…", "子节点过多，已截断显示");
                return false;
            }
            if (ReachedNodeLimit())
            {
                AddInfoLeaf(node, "…", "已达节点上限");
                return false;
            }
            count++;
            return true;
        }

        private ObjectTreeNode NewChild(ObjectTreeNode parent, string name, string typeName,
            FieldInfo sourceField = null, PropertyInfo sourceProperty = null)
        {
            var child = new ObjectTreeNode(parent, NextId(), false, name, "", typeName)
            {
                Depth = parent.Depth + 1,
                SourceField = sourceField,
                SourceProperty = sourceProperty,
            };
            parent.AddChild(child);
            return child;
        }

        /// <summary>依据实际值填充节点的显示、类型与可展开性（不构建其子节点）。</summary>
        private void FillNode(ObjectTreeNode node, object value)
        {
            if (value == null)
            {
                node.IsClass = false;
                node.Value = "null";
                node.CanExpand = false;
                node.OriginalObject = null;
                return;
            }

            var type = value.GetType();

            // 字符串与简单值类型作为叶子
            if (type == typeof(string) || inspector.IsSimpleValueType(type))
            {
                node.IsClass = false;
                node.Value = value.ToString();
                node.CanExpand = false;
                node.OriginalObject = value;
                return;
            }

            // 循环引用（仅引用类型，按引用相等判断）
            if (!type.IsValueType && node.IsCycle(value))
            {
                node.IsClass = false;
                node.Value = "循环引用";
                node.CanExpand = false;
                node.OriginalObject = null;
                return;
            }

            node.OriginalObject = value;
            DescribeComposite(node, value);
        }

        /// <summary>为复合值（class/struct/容器）填写摘要与可展开性。</summary>
        private void DescribeComposite(ObjectTreeNode node, object value)
        {
            node.IsClass = true;
            var type = value.GetType();
            node.Type = inspector.GetCSharpTypeName(type);

            if (value is IList list)
            {
                node.Value = $"Count:{list.Count}";
                node.CanExpand = list.Count > 0;
            }
            else if (value is IDictionary dict)
            {
                node.Value = $"Count:{dict.Count}";
                node.CanExpand = dict.Count > 0;
            }
            else
            {
                node.Value = "";
                node.CanExpand = HasExpandableMembers(type);
            }
        }

        /// <summary>是否存在至少一个会被纳入树的成员（不读取其值，仅用于决定展开箭头）。</summary>
        private bool HasExpandableMembers(Type type)
        {
            foreach (var field in inspector.GetAllFields(type))
            {
                if (memberFilter.ShouldInclude(field))
                    return true;
            }
            foreach (var prop in inspector.GetAllProperties(type))
            {
                if (memberFilter.ShouldInclude(prop))
                    return true;
            }
            return false;
        }

        private void AddErrorNode(ObjectTreeNode parent, string name, Exception e)
        {
            var data = new ObjectTreeNode(parent, NextId(), false, name, $"获取失败: {e.Message}", "Error")
            {
                Depth = parent.Depth + 1,
            };
            parent.AddChild(data);
        }

        private void AddInfoLeaf(ObjectTreeNode parent, string name, string message)
        {
            var data = new ObjectTreeNode(parent, NextId(), false, name, message, "")
            {
                Depth = parent.Depth + 1,
            };
            parent.AddChild(data);
        }
    }
}
