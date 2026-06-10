using UnityEditor;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 负责节点的呈现样式：显示文本与文字颜色。把展示规则从树视图逻辑中分离。
    /// 样式按颜色分类缓存，避免每行每帧分配 GUIStyle 造成持续 GC。
    /// </summary>
    internal sealed class NodePresenter
    {
        private static readonly Color ColorDrillIn = new Color(1f, 0.85f, 0.4f);
        private static readonly Color ColorNull = Color.gray;
        private static readonly Color ColorNumber = new Color(0.6f, 0.8f, 1f);
        private static readonly Color ColorString = new Color(1f, 0.8f, 0.6f);
        private static readonly Color ColorBool = new Color(0.8f, 0.6f, 1f);
        private static readonly Color ColorClass = new Color(0.8f, 1f, 0.8f);

        // 缓存的样式实例（懒初始化，避免在静态构造里访问 EditorStyles）
        private GUIStyle styleDrillIn;
        private GUIStyle styleNull;
        private GUIStyle styleNumber;
        private GUIStyle styleString;
        private GUIStyle styleBool;
        private GUIStyle styleClass;
        private GUIStyle styleDefault;

        /// <summary>构建一行的显示文本。</summary>
        public string GetDisplayName(ObjectTreeNode node)
        {
            if (node == null) return "null";

            if (node.IsDrillInPoint)
                return $"{node.Name}: {node.Value} ({node.Type})  ▶ 双击展开";

            if (node.IsClass || node.Value == "null")
                return $"{node.Name}: {node.Value} ({node.Type})";

            return $"{node.Name}: {node.Value}";
        }

        /// <summary>根据节点类型返回缓存的标签样式。</summary>
        public GUIStyle GetLabelStyle(ObjectTreeNode node)
        {
            if (node.IsDrillInPoint) return styleDrillIn ??= MakeStyle(ColorDrillIn);
            if (node.Value == "null") return styleNull ??= MakeStyle(ColorNull);

            switch (node.Type)
            {
                case "int":
                case "float":
                case "double":
                case "long":
                case "short":
                case "byte":
                    return styleNumber ??= MakeStyle(ColorNumber);
                case "string":
                    return styleString ??= MakeStyle(ColorString);
                case "bool":
                    return styleBool ??= MakeStyle(ColorBool);
            }

            if (node.IsClass) return styleClass ??= MakeStyle(ColorClass);
            return styleDefault ??= MakeStyle(EditorStyles.label.normal.textColor);
        }

        private static GUIStyle MakeStyle(Color color)
        {
            var style = new GUIStyle(EditorStyles.label);
            style.normal.textColor = color;
            return style;
        }
    }
}
