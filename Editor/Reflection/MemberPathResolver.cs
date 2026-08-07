using System;
using System.Collections.Generic;
using System.Reflection;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 将形如 <c>类型名.成员.成员</c> 或 <c>命名空间::类型名.成员.成员</c> 的路径解析为运行时对象。
    /// 简单类型名必须唯一；存在同名类型时返回全部候选，要求使用命名空间限定。
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

        /// <summary>
        /// 解析路径。支持两种格式：
        /// <list type="bullet">
        /// <item><description><c>类型名.成员[.成员...]</c>：类型简单名必须唯一。</description></item>
        /// <item><description><c>命名空间::类型名.成员[.成员...]</c>：按完整类型名精确匹配。</description></item>
        /// </list>
        /// 多级命名空间既可写成 <c>MyGame.Data::GameData</c>，也可写成
        /// <c>MyGame::Data::GameData</c>。
        /// </summary>
        public ResolveResult Resolve(string path)
        {
            if (!TryParsePath(path, out var typeName, out var members, out var exactTypeName, out var parseError))
                return ResolveResult.Fail(parseError);

            var candidates = FindTypes(typeName, exactTypeName);
            if (candidates.Count == 0)
                return ResolveResult.Fail($"找不到类型: {typeName}");

            if (candidates.Count > 1)
                return ResolveResult.Fail(BuildAmbiguousTypeError(typeName, candidates));

            Type currentType = candidates[0];
            object current = null;

            foreach (var member in members)
            {
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

                var ownerType = currentType?.FullName ?? "null";
                return ResolveResult.Fail($"在类型 {ownerType} 上找不到成员: {member}");
            }

            return ResolveResult.Ok(current);
        }

        private static bool TryParsePath(string path, out string typeName, out string[] members,
            out bool exactTypeName, out string error)
        {
            typeName = null;
            members = null;
            exactTypeName = false;
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "路径为空";
                return false;
            }

            path = path.Trim();
            var namespaceSeparatorIndex = path.LastIndexOf("::", StringComparison.Ordinal);
            if (namespaceSeparatorIndex >= 0)
            {
                var namespacePart = path.Substring(0, namespaceSeparatorIndex).Trim();
                var typeAndMembers = path.Substring(namespaceSeparatorIndex + 2).Trim().Split('.');
                if (string.IsNullOrWhiteSpace(namespacePart) || typeAndMembers.Length < 2 ||
                    string.IsNullOrWhiteSpace(typeAndMembers[0]))
                {
                    error = "命名空间路径格式应为 命名空间::类型名.成员[.成员...]";
                    return false;
                }

                // 允许 MyGame.Data::Type 与 MyGame::Data::Type 两种命名空间写法。
                namespacePart = namespacePart.Replace("::", ".").Trim('.');
                typeName = namespacePart + "." + typeAndMembers[0].Trim();
                members = CopyMembers(typeAndMembers, 1);
                exactTypeName = true;
                return ValidateMembers(members, out error);
            }

            var segments = path.Split('.');
            if (segments.Length < 2 || string.IsNullOrWhiteSpace(segments[0]))
            {
                error = "路径格式应为 类型名.成员[.成员...]";
                return false;
            }

            typeName = segments[0].Trim();
            members = CopyMembers(segments, 1);
            return ValidateMembers(members, out error);
        }

        private static string[] CopyMembers(string[] segments, int startIndex)
        {
            var members = new string[segments.Length - startIndex];
            for (int i = startIndex; i < segments.Length; i++)
                members[i - startIndex] = segments[i].Trim();
            return members;
        }

        private static bool ValidateMembers(string[] members, out string error)
        {
            foreach (var member in members)
            {
                if (!string.IsNullOrEmpty(member))
                    continue;

                error = "成员路径中存在空名称";
                return false;
            }

            error = null;
            return true;
        }

        private List<Type> FindTypes(string typeName, bool exactTypeName)
        {
            var result = new List<Type>();
            var seen = new HashSet<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (exactTypeName)
                {
                    var exactType = assembly.GetType(typeName, false);
                    AddTypeCandidate(exactType, result, seen);
                    continue;
                }

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

                foreach (var type in types)
                {
                    if (type != null && type.Name == typeName)
                        AddTypeCandidate(type, result, seen);
                }
            }

            return result;
        }

        private void AddTypeCandidate(Type type, List<Type> result, HashSet<Type> seen)
        {
            if (type != null && !IsExcludedNamespace(type) && seen.Add(type))
                result.Add(type);
        }

        private static string BuildAmbiguousTypeError(string typeName, List<Type> candidates)
        {
            var message = $"类型名 {typeName} 不唯一，请使用 命名空间::类型名.成员 路径。候选类型：";
            foreach (var candidate in candidates)
            {
                var assemblyName = candidate.Assembly.GetName().Name;
                message += $"\n- {candidate.FullName}（程序集: {assemblyName}）";
            }
            return message;
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
