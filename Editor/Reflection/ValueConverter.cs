using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 将用户输入的字符串转换为目标类型的值。
    /// 判定与转换都以真实 <see cref="Type"/> 为准，而不是显示用的类型名字符串，
    /// 因此枚举、decimal、DateTime、Guid 以及 Unity 常用结构体
    /// （Vector2/3/4、Quaternion、Color、Color32、Rect、Bounds）都可以编辑：
    /// 结构体按其 ToString 显示格式里数字的出现顺序解析，编辑框预填的显示值可直接改数字后写回。
    /// <see cref="CanConvert"/> 与 <see cref="TryConvert"/> 共用同一套支持范围，
    /// 保证“界面允许编辑”与“实际能写回”不会脱节。
    /// </summary>
    internal sealed class ValueConverter
    {
        /// <summary>该类型是否支持由文本解析为值（用于界面预先判断是否可编辑）。</summary>
        public bool CanConvert(Type type)
        {
            if (type == null)
                return false;

            if (type == typeof(string) || type == typeof(char) || type == typeof(bool))
                return true;

            // 枚举的 TypeCode 与其底层整数相同，因此必须先于 IsNumeric 判断
            if (type.IsEnum)
                return true;

            if (IsNumeric(type))
                return true;

            if (type == typeof(DateTime) || type == typeof(TimeSpan) || type == typeof(Guid))
                return true;

            return IsSupportedStruct(type);
        }

        /// <summary>把文本解析为目标类型的值；失败时返回 false 并给出可读原因。</summary>
        public bool TryConvert(string text, Type type, out object value, out string error)
        {
            value = null;
            error = null;
            text ??= string.Empty;

            if (type == null)
            {
                error = "目标类型为空，无法转换";
                return false;
            }

            if (type == typeof(string))
            {
                value = text;
                return true;
            }

            if (type == typeof(char))
            {
                if (text.Length == 0)
                {
                    value = '\0';
                    return true;
                }
                if (text.Length > 1)
                {
                    error = "char 类型只能输入单个字符";
                    return false;
                }
                value = text[0];
                return true;
            }

            if (type == typeof(bool))
                return TryConvertBool(text, out value, out error);

            if (type.IsEnum)
                return TryConvertEnum(text, type, out value, out error);

            if (IsNumeric(type))
                return TryConvertNumeric(text.Trim(), type, out value, out error);

            if (type == typeof(DateTime))
                return TryConvertDateTime(text, out value, out error);

            if (type == typeof(TimeSpan))
                return TryConvertTimeSpan(text, out value, out error);

            if (type == typeof(Guid))
                return TryConvertGuid(text, out value, out error);

            if (IsSupportedStruct(type))
                return TryConvertStruct(text, type, out value, out error);

            error = $"不支持编辑 {type.Name} 类型的值";
            return false;
        }

        private static bool TryConvertBool(string text, out object value, out string error)
        {
            value = null;
            error = null;

            if (text.Length == 0)
            {
                value = false;
                return true;
            }

            if (bool.TryParse(text.Trim(), out var flag))
            {
                value = flag;
                return true;
            }

            var trimmed = text.Trim();
            if (trimmed == "1")
            {
                value = true;
                return true;
            }
            if (trimmed == "0")
            {
                value = false;
                return true;
            }

            error = $"无法把 \"{text}\" 转换为 bool（可输入 true/false/1/0）";
            return false;
        }

        private static bool TryConvertEnum(string text, Type type, out object value, out string error)
        {
            value = null;
            error = null;

            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                error = $"枚举 {type.Name} 需要输入名称或数值";
                return false;
            }

            // Enum.TryParse 同时接受名称（含 Flags 的 "A, B" 写法）与底层数值
            if (Enum.TryParse(type, trimmed, true, out var parsed))
            {
                value = parsed;
                return true;
            }

            error = $"无法把 \"{text}\" 解析为枚举 {type.Name}。可选值：{string.Join(", ", Enum.GetNames(type))}";
            return false;
        }

        private static bool TryConvertNumeric(string text, Type type, out object value, out string error)
        {
            value = null;
            error = null;

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Single:
                    if (TryParseDouble(text, out var floatValue))
                    {
                        value = (float)floatValue;
                        return true;
                    }
                    break;

                case TypeCode.Double:
                    if (TryParseDouble(text, out var doubleValue))
                    {
                        value = doubleValue;
                        return true;
                    }
                    break;

                case TypeCode.Decimal:
                    // decimal 单独解析，避免经 double 中转丢精度或溢出
                    if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue) ||
                        decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out decimalValue))
                    {
                        value = decimalValue;
                        return true;
                    }
                    break;

                default:
                    return TryConvertIntegral(text, type, out value, out error);
            }

            error = $"无法把 \"{text}\" 转换为 {type.Name}";
            return false;
        }

        private static bool TryConvertIntegral(string text, Type type, out object value, out string error)
        {
            value = null;
            error = null;

            var typeCode = Type.GetTypeCode(type);

            // ulong 超出 long 正半区，单独处理
            if (typeCode == TypeCode.UInt64)
            {
                if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unsignedValue) ||
                    ulong.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out unsignedValue))
                {
                    value = unsignedValue;
                    return true;
                }

                error = $"无法把 \"{text}\" 转换为 {type.Name}";
                return false;
            }

            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                !long.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
            {
                error = $"无法把 \"{text}\" 转换为 {type.Name}（整数类型不接受小数）";
                return false;
            }

            switch (typeCode)
            {
                case TypeCode.SByte:
                    if (parsed >= sbyte.MinValue && parsed <= sbyte.MaxValue)
                    {
                        value = (sbyte)parsed;
                        return true;
                    }
                    break;

                case TypeCode.Byte:
                    if (parsed >= byte.MinValue && parsed <= byte.MaxValue)
                    {
                        value = (byte)parsed;
                        return true;
                    }
                    break;

                case TypeCode.Int16:
                    if (parsed >= short.MinValue && parsed <= short.MaxValue)
                    {
                        value = (short)parsed;
                        return true;
                    }
                    break;

                case TypeCode.UInt16:
                    if (parsed >= ushort.MinValue && parsed <= ushort.MaxValue)
                    {
                        value = (ushort)parsed;
                        return true;
                    }
                    break;

                case TypeCode.Int32:
                    if (parsed >= int.MinValue && parsed <= int.MaxValue)
                    {
                        value = (int)parsed;
                        return true;
                    }
                    break;

                case TypeCode.UInt32:
                    if (parsed >= uint.MinValue && parsed <= uint.MaxValue)
                    {
                        value = (uint)parsed;
                        return true;
                    }
                    break;

                case TypeCode.Int64:
                    value = parsed;
                    return true;
            }

            error = $"\"{text}\" 超出 {type.Name} 的取值范围";
            return false;
        }

        private static bool TryConvertDateTime(string text, out object value, out string error)
        {
            value = null;
            error = null;

            var trimmed = text.Trim();
            // 先按当前区域解析（与节点显示值同源），再退回不变文化
            if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dateTime) ||
                DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dateTime))
            {
                value = dateTime;
                return true;
            }

            error = $"无法把 \"{text}\" 解析为 DateTime，示例：{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}";
            return false;
        }

        private static bool TryConvertTimeSpan(string text, out object value, out string error)
        {
            value = null;
            error = null;

            var trimmed = text.Trim();
            if (TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var timeSpan) ||
                TimeSpan.TryParse(trimmed, CultureInfo.CurrentCulture, out timeSpan))
            {
                value = timeSpan;
                return true;
            }

            error = $"无法把 \"{text}\" 解析为 TimeSpan，示例：00:01:30";
            return false;
        }

        private static bool TryConvertGuid(string text, out object value, out string error)
        {
            value = null;
            error = null;

            if (Guid.TryParse(text.Trim(), out var guid))
            {
                value = guid;
                return true;
            }

            error = $"无法把 \"{text}\" 解析为 Guid，示例：{Guid.Empty}";
            return false;
        }

        /// <summary>
        /// 解析 Unity 结构体。数字按出现顺序对应分量，因此用户可以直接在
        /// 预填的显示值上改数字（如 "(1.00, 2.00, 3.00)"、"(x:1, y:2, width:3, height:4)"）。
        /// </summary>
        private static bool TryConvertStruct(string text, Type type, out object value, out string error)
        {
            value = null;
            error = null;

            if (!TryExtractNumbers(text, out var numbers))
            {
                error = $"无法从 \"{text}\" 中解析出数字";
                return false;
            }

            if (type == typeof(Vector2) && numbers.Count == 2)
            {
                value = new Vector2((float)numbers[0], (float)numbers[1]);
                return true;
            }

            if (type == typeof(Vector3) && numbers.Count == 3)
            {
                value = new Vector3((float)numbers[0], (float)numbers[1], (float)numbers[2]);
                return true;
            }

            if (type == typeof(Vector4) && numbers.Count == 4)
            {
                value = new Vector4((float)numbers[0], (float)numbers[1], (float)numbers[2], (float)numbers[3]);
                return true;
            }

            if (type == typeof(Quaternion) && numbers.Count == 4)
            {
                value = new Quaternion((float)numbers[0], (float)numbers[1], (float)numbers[2], (float)numbers[3]);
                return true;
            }

            if (type == typeof(Rect) && numbers.Count == 4)
            {
                value = new Rect((float)numbers[0], (float)numbers[1], (float)numbers[2], (float)numbers[3]);
                return true;
            }

            if (type == typeof(Bounds) && numbers.Count == 6)
            {
                // 显示格式为 "Center: (...), Extents: (...)"，构造需要 size（= extents * 2）
                var center = new Vector3((float)numbers[0], (float)numbers[1], (float)numbers[2]);
                var extents = new Vector3((float)numbers[3], (float)numbers[4], (float)numbers[5]);
                value = new Bounds(center, extents * 2f);
                return true;
            }

            if (type == typeof(Color) && (numbers.Count == 3 || numbers.Count == 4))
            {
                value = new Color(
                    (float)numbers[0],
                    (float)numbers[1],
                    (float)numbers[2],
                    numbers.Count == 4 ? (float)numbers[3] : 1f);
                return true;
            }

            if (type == typeof(Color32) && (numbers.Count == 3 || numbers.Count == 4))
            {
                value = new Color32(
                    ToByteComponent(numbers[0]),
                    ToByteComponent(numbers[1]),
                    ToByteComponent(numbers[2]),
                    numbers.Count == 4 ? ToByteComponent(numbers[3]) : (byte)255);
                return true;
            }

            error = $"解析 {type.Name} 需要{DescribeExpectedInput(type)}，实际得到 {numbers.Count} 个数字";
            return false;
        }

        /// <summary>把分量钳制到 0~255，用于 Color32（显示格式 RGBA(255, 0, 0, 255) 即 255 制）。</summary>
        private static byte ToByteComponent(double value)
        {
            var clamped = Math.Min(Math.Max(value, 0d), 255d);
            return (byte)Math.Round(clamped, MidpointRounding.AwayFromZero);
        }

        private static string DescribeExpectedInput(Type type)
        {
            if (type == typeof(Vector2)) return "两个数字，如 (1, 2)";
            if (type == typeof(Vector3)) return "三个数字，如 (1, 2, 3)";
            if (type == typeof(Vector4)) return "四个数字，如 (1, 2, 3, 4)";
            if (type == typeof(Quaternion)) return "四个数字 (x, y, z, w)，如 (0, 0, 0, 1)";
            if (type == typeof(Rect)) return "四个数字 (x, y, width, height)，如 (0, 0, 100, 50)";
            if (type == typeof(Bounds)) return "六个数字 (中心 xyz, 半径 xyz)，如 (0, 0, 0, 1, 1, 1)";
            if (type == typeof(Color)) return "三或四个数字 (r, g, b[, a])，取值 0~1，如 (1, 0, 0, 1)";
            if (type == typeof(Color32)) return "三或四个数字 (r, g, b[, a])，取值 0~255，如 (255, 0, 0, 255)";
            return "数字";
        }

        /// <summary>
        /// 按出现顺序从文本中提取所有数字。用于解析结构体 ToString 的显示值，例如
        /// "(1.00, 2.00, 3.00)"、"RGBA(1.000, 0.000, 0.000, 1.000)"、
        /// "Center: (0.00, 0.00, 0.00), Extents: (0.50, 0.50, 0.50)"。
        /// 只认数字片段，其它字符（括号、标签、逗号、空格）一律当分隔符。
        /// </summary>
        private static bool TryExtractNumbers(string text, out List<double> numbers)
        {
            numbers = new List<double>();
            if (string.IsNullOrEmpty(text))
                return false;

            var index = 0;
            while (index < text.Length)
            {
                if (!IsNumberStart(text, index))
                {
                    index++;
                    continue;
                }

                var start = index;
                if (text[index] == '+' || text[index] == '-')
                    index++;

                while (index < text.Length && char.IsDigit(text[index]))
                    index++;

                if (index < text.Length && text[index] == '.')
                {
                    index++;
                    while (index < text.Length && char.IsDigit(text[index]))
                        index++;
                }

                // 指数部分（"1e-5"）；若不是合法指数则退回，交回外层按普通字符处理
                if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
                {
                    var exponentStart = index;
                    index++;
                    if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                        index++;

                    if (index < text.Length && char.IsDigit(text[index]))
                    {
                        while (index < text.Length && char.IsDigit(text[index]))
                            index++;
                    }
                    else
                    {
                        index = exponentStart;
                    }
                }

                var token = text.Substring(start, index - start);
                if (!TryParseDouble(token, out var number))
                    return false;

                numbers.Add(number);
            }

            return numbers.Count > 0;
        }

        /// <summary>判断 <paramref name="index"/> 处是否为一个数字的开头（含正负号与 ".5" 写法）。</summary>
        private static bool IsNumberStart(string text, int index)
        {
            var current = text[index];
            if (char.IsDigit(current))
                return true;

            if (current != '-' && current != '+' && current != '.')
                return false;

            if (index + 1 >= text.Length)
                return false;

            var next = text[index + 1];
            if (char.IsDigit(next))
                return true;

            // 允许 "-.5" 这种省略整数部分的写法
            return current != '.' && next == '.' && index + 2 < text.Length && char.IsDigit(text[index + 2]);
        }

        /// <summary>先按不变文化解析（"1.5"），失败再退回当前区域（"1,5"），兼容不同地区的输入习惯。</summary>
        private static bool TryParseDouble(string text, out double value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static bool IsNumeric(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsSupportedStruct(Type type)
        {
            return type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(Quaternion)
                || type == typeof(Color)
                || type == typeof(Color32)
                || type == typeof(Rect)
                || type == typeof(Bounds);
        }
    }
}
