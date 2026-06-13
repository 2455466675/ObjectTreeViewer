using System;
using System.Collections.Generic;
using System.Reflection;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 将形如 "类型名.成员.成员" 的路径解析为运行时对象。
    /// 支持静态/实例的字段与属性，沿基类向上查找。
    /// </summary>
    internal sealed class MemberPathResolver
    {
        private readonly IReadOnlyList<string> excludedNamespacePrefixes;

        public MemberPathResolver(IReadOnlyList<string> excludedNamespacePrefixes)
        {
            this.excludedNamespacePrefixes = excludedNamespacePrefixes ?? Array.Empty<string>();
        }        /// <summary>解析结果，包含成功标志、对象与可读的错误信息。</summary>
        public readonly struct ResolveResult
        {
            public bool Success { get; }
            public object Value { get; }
            public string Error { get; }

            private ResolveResult(bool success, object value, string error)
            {
                Success = success;
                Value = value;
                Error = error;
            }

            public static ResolveResult Ok(object value) => new ResolveResult(true, value, null);
            public static ResolveResult Fail(string error) => new ResolveResult(false, null, error);
        }

        /// <summary>解析路径。路径至少需要"类型名.成员"两段。</summary>
        public ResolveResult Resolve(string path)
        {
            if (string.IsNullOrEmpty(path))
                return ResolveResult.Fail("路径为空");

            var members = path.Split('.');
            if (members.Length < 2)
                return ResolveResult.Fail("路径格式应为 类型名.成员[.成员...]");

            var typeName = members[0];
            var type = FindType(typeName);
            if (type == null)
                return ResolveResult.Fail($"找不到类型: {typeName}");

            Type currentType = type;
            object current = null;

            for (int i = 1; i < members.Length; i++)
            {
                var member = members[i];

                var staticField = GetStaticField(currentType, member);
                if (staticField != null)
                {
                    current = staticField.GetValue(current);
                    currentType = current?.GetType();
                    continue;
                }

                var staticProp = GetStaticProperty(currentType, member);
                if (staticProp != null)
                {
                    current = staticProp.GetValue(current);
                    currentType = current?.GetType();
                    continue;
                }

                var instField = GetInstanceField(currentType, member);
                if (instField != null)
                {
                    current = instField.GetValue(current);
                    currentType = current?.GetType();
                    continue;
                }

                var instProp = GetInstanceProperty(currentType, member);
                if (instProp != null && instProp.CanRead)
                {
                    current = instProp.GetValue(current);
                    currentType = current?.GetType();
                    continue;
                }

                return ResolveResult.Fail($"找不到成员: {member}");
            }

            return ResolveResult.Ok(current);
        }

        private Type FindType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type != null && !IsExcludedNamespace(type))
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null && !IsExcludedNamespace(type))
                    return type;

                // GetTypes 在部分程序集加载失败时会抛 ReflectionTypeLoadException，
                // 此时回退到已成功加载的类型继续匹配，避免整个查找中断。
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }

                foreach (var t in types)
                {
                    if (t != null && t.Name == typeName && !IsExcludedNamespace(t))
                        return t;
                }
            }
            return null;
        }

        private bool IsExcludedNamespace(Type type) =>
            NamespaceExclusion.IsExcluded(type, excludedNamespacePrefixes);
        private FieldInfo GetStaticField(Type type, string name)
        {
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        private PropertyInfo GetStaticProperty(Type type, string name)
        {
            while (type != null)
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (prop != null) return prop;
                type = type.BaseType;
            }
            return null;
        }

        private FieldInfo GetInstanceField(Type type, string name)
        {
            if (type == null) return null;
            return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private PropertyInfo GetInstanceProperty(Type type, string name)
        {
            if (type == null) return null;
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }
    }
}
