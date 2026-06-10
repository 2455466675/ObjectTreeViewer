namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 将用户输入的字符串转换为目标基元类型的值。
    /// 转换失败返回 null（string/bool 等有专门的空值约定）。
    /// </summary>
    internal sealed class ValueConverter
    {
        /// <summary>
        /// 尝试将字符串转换为指定的 C# 类型名对应的值。
        /// </summary>
        /// <param name="valueStr">输入文本</param>
        /// <param name="targetType">目标 C# 类型名（如 "int"、"bool"）</param>
        /// <returns>转换后的装箱值；不支持或失败返回 null</returns>
        public object Convert(string valueStr, string targetType)
        {
            if (string.IsNullOrEmpty(valueStr))
            {
                if (targetType == "string") return "";
                if (targetType == "bool") return false;
                return null;
            }

            try
            {
                switch (targetType)
                {
                    case "int": return int.Parse(valueStr);
                    case "float": return float.Parse(valueStr);
                    case "double": return double.Parse(valueStr);
                    case "long": return long.Parse(valueStr);
                    case "short": return short.Parse(valueStr);
                    case "byte": return byte.Parse(valueStr);
                    case "char": return valueStr.Length > 0 ? valueStr[0] : '\0';
                    case "bool": return bool.Parse(valueStr);
                    case "uint": return uint.Parse(valueStr);
                    case "ulong": return ulong.Parse(valueStr);
                    case "ushort": return ushort.Parse(valueStr);
                    case "sbyte": return sbyte.Parse(valueStr);
                    case "string": return valueStr;
                    default: return null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
