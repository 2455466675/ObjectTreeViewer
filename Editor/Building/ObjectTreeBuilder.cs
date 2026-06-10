using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 由运行时对象递归构建 <see cref="ObjectTreeNode"/> 树。
    /// 负责深度限制、循环引用检测、容器/结构体/类的展开策略。
    /// </summary>
    internal sealed class ObjectTreeBuilder
    {
        /// <summary>构建参数与限制。</summary>
        public sealed class Options
        {
            /// <summary>树的最大深度（根节点为第 0 层），超过则生成"二次展开"占位节点。</summary>
            public int MaxDepth = 5;

            /// <summary>节点总数上限，超过则中止以防卡死。</summary>
            public int MaxNodeCount = 20000;
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

        /// <summary>以 <paramref name="target"/> 为根构建整棵树并返回根节点。</summary>
        public ObjectTreeNode Build(object target)
        {
            if (target == null)
                return null;

            nextId = 1;
            var typeName = inspector.GetCSharpTypeName(target.GetType());
            var rootName = target.GetType().Name;

            var root = new ObjectTreeNode(null, NextId(), true, rootName, "", typeName)
            {
                OriginalObject = target,
                Depth = 0,
            };

            BuildChildren(root, target, rootName, typeName, isContainerItem: true, depth: 0);
            return root;
        }

        private int NextId() => nextId++;

        /// <summary>
        /// 递归填充节点。返回 false 表示已达节点上限，应中止整棵构建。
        /// </summary>
        private bool BuildChildren(ObjectTreeNode node, object obj, string name, string typeName,
            bool isContainerItem, int depth, FieldInfo sourceField = null, PropertyInfo sourceProperty = null)
        {
            if (nextId > options.MaxNodeCount)
            {
                Debug.LogWarning("当前对象数据过多！");
                return false;
            }

            if (obj == null)
            {
                AddOrFillLeaf(node, name, "null", typeName, isContainerItem, null, sourceField, sourceProperty);
                return true;
            }

            var type = obj.GetType();

            // 字符串与简单值类型作为叶子
            if (type == typeof(string) || inspector.IsSimpleValueType(type))
            {
                AddOrFillLeaf(node, name, obj.ToString(), typeName, isContainerItem, obj, sourceField, sourceProperty);
                return true;
            }

            // 结构体：展开字段/属性（标记为不可编辑由视图层判断）
            if (type.IsValueType)
            {
                var structNode = PrepareCompositeNode(node, obj, name, typeName, "", isContainerItem, sourceField, sourceProperty, depth);
                if (TryMakeDrillIn(structNode, obj, depth))
                    return true;
                return ExpandMembers(structNode, obj, type, depth);
            }

            // 循环引用（仅引用类型，按引用相等判断）
            if (node.IsCycle(obj))
            {
                AddOrFillLeaf(node, name, "循环引用", inspector.GetCSharpTypeName(type), isContainerItem: false, obj: null, sourceField, sourceProperty);
                return true;
            }

            // List / 数组
            if (obj is IList list)
            {
                var listType = inspector.GetCSharpTypeName(type);
                var listNode = PrepareCompositeNode(node, obj, name, listType, $"Count:{list.Count}", isContainerItem, sourceField, sourceProperty, depth);
                if (TryMakeDrillIn(listNode, obj, depth))
                    return true;
                return ExpandList(listNode, list, name, depth);
            }

            // Dictionary
            if (obj is IDictionary dict)
            {
                var dicNode = PrepareCompositeNode(node, obj, name, typeName, $"Count:{dict.Count}", isContainerItem, sourceField, sourceProperty, depth);
                if (TryMakeDrillIn(dicNode, obj, depth))
                    return true;
                return ExpandDictionary(dicNode, dict, depth);
            }

            // 普通 class
            {
                var classNode = PrepareCompositeNode(node, obj, name, typeName, "", isContainerItem, sourceField, sourceProperty, depth);
                if (TryMakeDrillIn(classNode, obj, depth))
                    return true;
                return ExpandMembers(classNode, obj, type, depth);
            }
        }

        /// <summary>
        /// 准备复合节点：非容器元素时新建子节点并挂载，容器元素时复用传入节点（避免多一层，例如（List<List<>>））。
        /// </summary>
        private ObjectTreeNode PrepareCompositeNode(ObjectTreeNode parent, object obj, string name, string typeName,
            string value, bool isContainerItem, FieldInfo sourceField, PropertyInfo sourceProperty, int depth)
        {
            ObjectTreeNode node;
            if (!isContainerItem)
            {
                node = new ObjectTreeNode(parent, NextId(), true, name, value, typeName)
                {
                    OriginalObject = obj,
                    SourceField = sourceField,
                    SourceProperty = sourceProperty,
                };
                parent.AddChild(node);
            }
            else
            {
                node = parent;
                node.Name = name;
                node.Value = value;
                node.Type = typeName;
                node.OriginalObject = obj;
            }
            node.Depth = depth;
            return node;
        }

        /// <summary>新增或填充叶子节点。容器元素直接填充传入节点，否则新建子节点。</summary>
        private void AddOrFillLeaf(ObjectTreeNode node, string name, string value, string typeName,
            bool isContainerItem, object obj, FieldInfo sourceField, PropertyInfo sourceProperty)
        {
            if (isContainerItem)
            {
                node.IsClass = false;
                node.Name = name;
                node.Value = value;
                node.Type = typeName;
                node.OriginalObject = obj;
                node.SourceField = sourceField;
                node.SourceProperty = sourceProperty;
            }
            else
            {
                var data = new ObjectTreeNode(node, NextId(), false, name, value, typeName)
                {
                    OriginalObject = obj,
                    SourceField = sourceField,
                    SourceProperty = sourceProperty,
                };
                node.AddChild(data);
            }
        }

        private bool ExpandMembers(ObjectTreeNode node, object obj, Type type, int depth)
        {
            foreach (var field in inspector.GetAllFields(type))
            {
                if (!memberFilter.ShouldInclude(field))
                {
                    continue;
                }

                try
                {
                    var value = field.GetValue(obj);
                    if (!BuildChildren(node, value, field.Name, inspector.GetCSharpTypeName(field.FieldType), false, depth + 1, sourceField: field))
                        return false;
                }
                catch (Exception e)
                {
                    AddErrorNode(node, field.Name, e);
                }
            }

            foreach (var prop in inspector.GetAllProperties(type))
            {
                if (!memberFilter.ShouldInclude(prop))
                {
                    continue;
                }

                try
                {
                    var value = prop.GetValue(obj);
                    if (!BuildChildren(node, value, prop.Name, inspector.GetCSharpTypeName(prop.PropertyType), false, depth + 1, sourceProperty: prop))
                        return false;
                }
                catch (Exception e)
                {
                    AddErrorNode(node, prop.Name, e);
                }
            }
            return true;
        }

        private bool ExpandList(ObjectTreeNode listNode, IList list, string name, int depth)
        {
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    var value = list[i];
                    var valueTypeName = value != null ? inspector.GetCSharpTypeName(value.GetType()) : "null";
                    var child = new ObjectTreeNode(listNode, NextId(), true, $"[{i}]", "", valueTypeName)
                    {
                        OriginalObject = value,
                        IsContainerEntry = true,
                        ContainerKey = i,
                        Depth = depth + 1,
                    };
                    listNode.AddChild(child);
                    if (!BuildChildren(child, value, $"[{i}]", valueTypeName, true, depth + 1))
                        return false;
                }
                catch (Exception e)
                {
                    AddErrorNode(listNode, $"{name}[{i}]", e);
                }
            }
            return true;
        }

        private bool ExpandDictionary(ObjectTreeNode dicNode, IDictionary dict, int depth)
        {
            foreach (DictionaryEntry entry in dict)
            {
                var key = entry.Key;
                var value = entry.Value;
                var keyStr = key?.ToString() ?? "null";
                var valueTypeName = value != null ? inspector.GetCSharpTypeName(value.GetType()) : "null";
                var child = new ObjectTreeNode(dicNode, NextId(), true, $"[{keyStr}]", "", valueTypeName)
                {
                    OriginalObject = value,
                    IsContainerEntry = true,
                    ContainerKey = key,
                    Depth = depth + 1,
                };
                dicNode.AddChild(child);
                if (!BuildChildren(child, value, $"[{keyStr}]", valueTypeName, true, depth + 1))
                    return false;
            }
            return true;
        }

        private void AddErrorNode(ObjectTreeNode parent, string name, Exception e)
        {
            var data = new ObjectTreeNode(parent, NextId(), false, name, $"获取失败: {e.Message}", "Error");
            parent.AddChild(data);
        }

        /// <summary>
        /// 若复合节点已达最大深度，则转为"二次展开"占位节点（不再展开子节点）。
        /// 返回 true 表示已转占位，调用方应跳过子节点构建。
        /// </summary>
        private bool TryMakeDrillIn(ObjectTreeNode node, object obj, int depth)
        {
            if (depth < options.MaxDepth)
                return false;

            node.IsDrillInPoint = true;
            node.IsClass = true;
            node.OriginalObject = obj;
            if (string.IsNullOrEmpty(node.Value))
                node.Value = "...";
            return true;
        }
    }
}
