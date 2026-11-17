using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 集中管理"哪些字段/属性应被纳入树"的规则。
    /// 从构建逻辑中抽出，便于后续扩展新的过滤条件而不侵入 <see cref="ObjectTreeBuilder"/>。
    /// 类型过滤分三档：命名空间前缀、基类/接口匹配、精确类型。
    /// </summary>
    internal sealed class MemberFilter
    {
        // 基类/接口过滤：用 IsAssignableFrom 覆盖全部子类，
        // 否则像 FileStream、具体委托类型等会从精确匹配中漏过
        private static readonly Type[] ExcludedBaseTypes =
        {
            typeof(Stream),        // 各类 IO 流，含 OS 句柄，部分成员读取即抛异常
            typeof(Delegate),      // 委托指向 Target 对象与 MethodInfo，爆炸且无意义
            typeof(WaitHandle),    // 同步原语（Mutex/Semaphore/Event 等）
            typeof(MemberInfo),    // 反射元数据（Type/MethodInfo/...），递归近乎无限
            typeof(Exception),     // StackTrace/TargetSite/InnerException 链
        };

        // 精确类型过滤：与上述无继承关系的零散类型
        private static readonly HashSet<Type> ExcludedExactTypes = new HashSet<Type>
        {
            typeof(BinaryReader),
            typeof(BinaryWriter),
            typeof(TextReader),
            typeof(TextWriter),
            typeof(Assembly),
            typeof(Module),
            typeof(CancellationToken),
            typeof(CancellationTokenSource),
            typeof(IntPtr),
            typeof(UIntPtr),
            typeof(WeakReference),
        };

        private readonly ReflectionInspector inspector;
        private readonly IReadOnlyList<string> excludedNamespacePrefixes;

        public MemberFilter(ReflectionInspector inspector, IReadOnlyList<string> excludedNamespacePrefixes)
        {
            this.inspector = inspector;
            this.excludedNamespacePrefixes = excludedNamespacePrefixes ?? Array.Empty<string>();
        }

        public bool ShouldInclude(FieldInfo field)
        {
            // 自动属性的 backing field 与属性重复，保留属性即可
            if (field.Name.Contains("k__BackingField"))
            {
                return false;
            }

            if (IsExcludedType(field.FieldType))
            {
                return false;
            }

            return true;
        }

        public bool ShouldInclude(PropertyInfo prop)
        {
            // 仅展示自动属性，普通属性可能有副作用或仅是字段的转发
            if (!inspector.IsAutoProperty(prop))
            {
                return false;
            }

            if (IsExcludedType(prop.PropertyType))
            {
                return false;
            }

            return true;
        }

        // 汇总三档类型过滤规则
        private bool IsExcludedType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            if (ExcludedExactTypes.Contains(type))
            {
                return true;
            }

            foreach (var baseType in ExcludedBaseTypes)
            {
                if (baseType.IsAssignableFrom(type))
                {
                    return true;
                }
            }

            return IsExcludedNamespace(type);
        }

        private bool IsExcludedNamespace(Type type) =>
            NamespaceExclusion.IsExcluded(type, excludedNamespacePrefixes);
    }

    /// <summary>
    /// 按配置的命名空间前缀判断类型是否应被排除。
    /// </summary>
    internal static class NamespaceExclusion
    {
        /// <summary>
        /// 按命名空间段边界做前缀匹配：配置 "UnityEngine" 命中 "UnityEngine" 与 "UnityEngine.UI"，
        /// 但不命中 "UnityEngineExtensions"；配置 "System.Reflection" 同样不会误伤 "System.ReflectionX"。
        /// 用序数比较代替 StartsWith(prefix + ".")，避免每次判定都为拼接分配字符串——
        /// 该方法位于构建期的热路径上（每个成员、每个节点都要判定一次）。
        /// </summary>
        public static bool IsExcluded(Type type, IReadOnlyList<string> excludedNamespacePrefixes)
        {
            if (type == null || excludedNamespacePrefixes == null || excludedNamespacePrefixes.Count == 0)
                return false;

            var ns = type.Namespace;
            if (string.IsNullOrEmpty(ns))
                return false;

            for (int i = 0; i < excludedNamespacePrefixes.Count; i++)
            {
                var prefix = excludedNamespacePrefixes[i];
                if (string.IsNullOrEmpty(prefix))
                    continue;

                if (ns.Length == prefix.Length)
                {
                    // 完全相同的命名空间
                    if (string.Equals(ns, prefix, StringComparison.Ordinal))
                        return true;
                    continue;
                }

                // 只认 "prefix." 开头的子命名空间，拒绝 "prefixExtra" 这类同前缀的无关命名空间
                if (ns.Length > prefix.Length &&
                    ns[prefix.Length] == '.' &&
                    string.CompareOrdinal(ns, 0, prefix, 0, prefix.Length) == 0)
                    return true;
            }

            return false;
        }
    }
}
