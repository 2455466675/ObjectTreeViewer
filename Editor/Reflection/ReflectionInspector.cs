using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 反射相关的通用查询：成员枚举、类型判定、C# 类型名格式化。
    /// 无状态，可被多个模块复用。
    /// </summary>
    internal sealed class ReflectionInspector
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        /// <summary>枚举类型及其基类的所有实例字段（去重，子类优先）。</summary>
        public IEnumerable<FieldInfo> GetAllFields(Type type)
        {
            var seen = new HashSet<string>();
            var current = type;
            while (current != null && current != typeof(object))
            {
                foreach (var f in current.GetFields(InstanceFlags))
                {
                    if (seen.Add(f.Name))
                        yield return f;
                }
                current = current.BaseType;
            }
        }

        /// <summary>枚举类型及其基类的所有实例属性（跳过索引器，去重，子类优先）。</summary>
        public IEnumerable<PropertyInfo> GetAllProperties(Type type)
        {
            var seen = new HashSet<string>();
            var current = type;
            while (current != null && current != typeof(object))
            {
                foreach (var p in current.GetProperties(InstanceFlags))
                {
                    // 跳过索引器（有参属性），避免误读
                    if (p.GetIndexParameters().Length > 0) continue;
                    if (seen.Add(p.Name))
                        yield return p;
                }
                current = current.BaseType;
            }
        }

        /// <summary>判断属性是否为自动属性（具有编译器生成的 backing field）。</summary>
        public bool IsAutoProperty(PropertyInfo property)
        {
            var backingFieldName = $"<{property.Name}>k__BackingField";
            var backingField = property.DeclaringType?.GetField(backingFieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return backingField != null && backingField.GetCustomAttribute<CompilerGeneratedAttribute>() != null;
        }

        /// <summary>是否为应作为叶子节点显示的"简单值类型"（基元、枚举、常用结构体等）。</summary>
        public bool IsSimpleValueType(Type type)
        {
            if (!type.IsValueType) return false;
            if (type.IsPrimitive) return true;
            if (type.IsEnum) return true;
            if (type == typeof(decimal)) return true;
            if (type == typeof(DateTime)) return true;
            if (type == typeof(TimeSpan)) return true;
            if (type == typeof(Guid)) return true;
            if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)) return true;
            if (type == typeof(Quaternion)) return true;
            if (type == typeof(Color) || type == typeof(Color32)) return true;
            if (type == typeof(Rect)) return true;
            if (type == typeof(Bounds)) return true;
            return false;
        }

        /// <summary>将 Type 格式化为 C# 风格类型名（含泛型、数组、可空）。</summary>
        public string GetCSharpTypeName(Type type)
        {
            if (type == null) return "null";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(long)) return "long";
            if (type == typeof(short)) return "short";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(ulong)) return "ulong";
            if (type == typeof(ushort)) return "ushort";
            if (type == typeof(sbyte)) return "sbyte";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(char)) return "char";
            if (type == typeof(string)) return "string";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(object)) return "object";
            if (type == typeof(void)) return "void";

            // 数组
            if (type.IsArray)
            {
                var rank = type.GetArrayRank();
                var brackets = "[" + new string(',', rank - 1) + "]";
                return GetCSharpTypeName(type.GetElementType()) + brackets;
            }

            // 可空类型 Nullable<T>
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
                return GetCSharpTypeName(underlying) + "?";

            // 泛型
            if (type.IsGenericType)
            {
                var backtickIdx = type.Name.IndexOf('`');
                var baseName = backtickIdx >= 0 ? type.Name.Substring(0, backtickIdx) : type.Name;
                var args = string.Join(", ", type.GetGenericArguments().Select(GetCSharpTypeName));
                return $"{baseName}<{args}>";
            }

            return type.Name;
        }
    }
}
