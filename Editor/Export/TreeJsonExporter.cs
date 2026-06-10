using System.Text;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 将已构建的 <see cref="ObjectTreeNode"/> 树序列化为 JSON 文本。
    /// 直接以树的当前形态输出：超过展开深度的复合节点（IsDrillInPoint）只有摘要、无子节点，
    /// 因此自然只记录简要数据，不会深入展开。
    /// </summary>
    internal sealed class TreeJsonExporter
    {
        /// <summary>将树转换为带缩进的 JSON 字符串。</summary>
        public string ToJson(ObjectTreeNode root)
        {
            var sb = new StringBuilder();
            if (root == null)
            {
                sb.Append("{}");
                return sb.ToString();
            }

            WriteNode(sb, root, 0);
            return sb.ToString();
        }

        private void WriteNode(StringBuilder sb, ObjectTreeNode node, int indentLevel)
        {
            var fieldIndent = Indent(indentLevel + 1);
            var closeIndent = Indent(indentLevel);

            sb.Append("{\n");

            sb.Append(fieldIndent).Append("\"name\": ").Append(Quote(node.Name)).Append(",\n");
            sb.Append(fieldIndent).Append("\"type\": ").Append(Quote(node.Type)).Append(",\n");
            sb.Append(fieldIndent).Append("\"value\": ").Append(Quote(node.Value)).Append(",\n");
            sb.Append(fieldIndent).Append("\"isComposite\": ").Append(node.IsClass ? "true" : "false");

            // 超过展开深度的占位节点：标注 truncated 但不输出子节点
            if (node.IsDrillInPoint)
            {
                sb.Append(",\n");
                sb.Append(fieldIndent).Append("\"truncated\": true\n");
                sb.Append(closeIndent).Append('}');
                return;
            }

            if (node.Children != null && node.Children.Count > 0)
            {
                sb.Append(",\n");
                sb.Append(fieldIndent).Append("\"children\": [\n");
                for (int i = 0; i < node.Children.Count; i++)
                {
                    sb.Append(Indent(indentLevel + 2));
                    WriteNode(sb, node.Children[i], indentLevel + 2);
                    if (i < node.Children.Count - 1)
                        sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(fieldIndent).Append("]\n");
            }
            else
            {
                sb.Append('\n');
            }

            sb.Append(closeIndent).Append('}');
        }

        private static string Indent(int level)
        {
            return new string(' ', level * 2);
        }

        /// <summary>对字符串做 JSON 转义并加引号；null 输出为 JSON null。</summary>
        private static string Quote(string s)
        {
            if (s == null)
                return "null";

            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
