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
        // 命名空间前缀过滤：这些命名空间的类型要么是引擎/反射基础设施，
        // 要么展开后牵出庞大且与业务数据无关的对象图
        private static readonly string[] ExcludedNamespacePrefixes =
        {
            "UnityEngine",
            "UnityEditor",
            "System.Reflection",
        };

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

        public MemberFilter(ReflectionInspector inspector)
        {
            this.inspector = inspector;
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
        private static bool IsExcludedType(Type type)
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

        private static bool IsExcludedNamespace(Type type)
        {
            var ns = type.Namespace;
            if (string.IsNullOrEmpty(ns))
            {
                return false;
            }

            foreach (var prefix in ExcludedNamespacePrefixes)
            {
                if (ns.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
