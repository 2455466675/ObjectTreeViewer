using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 对象树查看器主窗口：作为各模块的组合根（composition root），负责装配依赖、
    /// 绘制工具栏、协调取对象 / 刷新 / 二次展开 / 回退等流程。
    /// 除菜单入口外不使用静态成员。
    /// </summary>
    public sealed class ObjectTreeViewerWindow : EditorWindow, IObjectTreeViewHost
    {
        // ——— 依赖模块（在 OnEnable 中装配）———
        private ReflectionInspector inspector;
        private MemberFilter memberFilter;
        private MemberPathResolver pathResolver;
        private ObjectTreeBuilder.Options buildOptions;
        private NodePresenter presenter;
        private ValueWriter valueWriter;
        private TreeSearchController search;
        private ViewerConfigStore configStore;
        private TreeJsonExporter exporter;

        // ——— 运行状态 ———
        private object targetObject;
        private ObjectTreeNode rootNode;
        private ObjectTreeView treeView;
        private TreeViewState treeViewState;

        private string memberPath = "GameData.I.GrowthScoreData";
        // 预定义路径下拉框当前选中索引
        private int presetSelectedIndex;

        // 历史根对象栈，用于 ⬅ 回退（保存进入当前树之前的各级根对象）
        private readonly Stack<object> historyStack = new Stack<object>();

        // ——— IObjectTreeViewHost ———
        ObjectTreeNode IObjectTreeViewHost.RootNode => rootNode;
        bool IObjectTreeViewHost.IsSearchActive => search != null && search.IsActive;
        bool IObjectTreeViewHost.IsNodeMatchSearch(ObjectTreeNode node) => search != null && search.IsNodeMatch(node);
        bool IObjectTreeViewHost.IsCurrentSearchResult(int nodeId) => search != null && search.IsCurrentResult(nodeId);
        void IObjectTreeViewHost.DrillInto(ObjectTreeNode node) => DrillInto(node);
        void IObjectTreeViewHost.GoBack() => GoBack();
        void IObjectTreeViewHost.RefreshTree(int? selectNodeId) => RefreshTree(selectNodeId);

        [MenuItem("Window/对象树查看器")]
        public static void ShowWindow()
        {
            GetWindow<ObjectTreeViewerWindow>("对象树查看器");
        }

        private void OnEnable()
        {
            treeViewState ??= new TreeViewState();

            inspector = new ReflectionInspector();
            configStore = new ViewerConfigStore();
            memberFilter = new MemberFilter(inspector, configStore.ExcludedNamespacePrefixes);
            pathResolver = new MemberPathResolver(configStore.ExcludedNamespacePrefixes);
            buildOptions = new ObjectTreeBuilder.Options { MaxDepth = 5, MaxNodeCount = 20000 };
            presenter = new NodePresenter();
            valueWriter = new ValueWriter(new ValueConverter());
            search = new TreeSearchController(() => rootNode, () => treeView, Repaint);

            exporter = new TreeJsonExporter();            // 若存在预定义路径，默认显示并填充第一条
            if (configStore.HasPaths)
            {
                presetSelectedIndex = 0;
                memberPath = configStore.Paths[0];
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawTreeView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawPresetDropdown();

            EditorGUILayout.LabelField("成员路径 (格式: 类型名.成员):", EditorStyles.boldLabel);
            memberPath = EditorGUILayout.TextField(memberPath);

            EditorGUILayout.BeginHorizontal();

            // ⬅ 回退按钮：仅在有历史记录时可用
            EditorGUI.BeginDisabledGroup(historyStack.Count == 0);
            if (GUILayout.Button($"⬅ 返回 ({historyStack.Count})", GUILayout.Height(25), GUILayout.Width(120)))
                GoBack();
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("获取对象", GUILayout.Height(25)))
                GetObjectByPath(memberPath);

            if (GUILayout.Button("刷新当前对象", GUILayout.Height(25)))
                RefreshTree();

            // 导出按钮：仅当存在已查询对象时可用
            EditorGUI.BeginDisabledGroup(targetObject == null || rootNode == null);
            if (GUILayout.Button("导出", GUILayout.Height(25)))
                ExportTree();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "示例:\n" +
                "• GameData.I.GrowthScoreData\n\n" +
                "按 F2 编辑 | 搜索框支持 Enter 下一个 / Shift+Enter 上一个 / Esc 清除\n" +
                $"树最大深度为 {buildOptions.MaxDepth} 层，超出的复合节点显示 ▶，双击或选中后按 → 可二次展开为新树，⬅ 或按 C 返回上一级\n",
                MessageType.Info);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            if (treeView != null)
            {
                search.DrawSearchBar();
                EditorGUILayout.Space(3);
            }
        }

        /// <summary>
        /// 绘制预定义路径下拉框。选中后仅填充到输入框，不触发查询。
        /// 无预定义路径时，主项显示 JSON 文件所在路径作为提示。
        /// </summary>
        private void DrawPresetDropdown()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("预定义路径:", EditorStyles.boldLabel, GUILayout.Width(80));

            if (configStore.HasPaths)
            {
                var options = configStore.Paths.ToArray();
                if (presetSelectedIndex < 0 || presetSelectedIndex >= options.Length)
                    presetSelectedIndex = 0;

                int newIndex = EditorGUILayout.Popup(presetSelectedIndex, options);
                if (newIndex != presetSelectedIndex)
                {
                    presetSelectedIndex = newIndex;
                    // 仅填充输入框，由用户手动点击"获取对象"触发查询
                    memberPath = configStore.Paths[newIndex];
                    GUI.FocusControl(null);
                }
            }
            else
            {
                // 无预定义路径：用主项提示用户在该 JSON 文件中创建
                var hint = $"在此创建预定义路径: {configStore.JsonFilePath}";
                EditorGUILayout.Popup(0, new[] { hint });
            }

            // 重新加载按钮，便于手动编辑 JSON 后刷新
            if (GUILayout.Button("↻", GUILayout.Width(24)))
                ReloadConfig();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>重新读取配置文件并重置下拉框选择。</summary>
        private void ReloadConfig()
        {
            configStore.Load();
            memberFilter = new MemberFilter(inspector, configStore.ExcludedNamespacePrefixes);
            pathResolver = new MemberPathResolver(configStore.ExcludedNamespacePrefixes);
            presetSelectedIndex = 0;
            if (configStore.HasPaths)
                memberPath = configStore.Paths[0];
            if (targetObject != null)
                RefreshTree();
            GUI.FocusControl(null);
        }

        private void DrawTreeView()
        {
            if (treeView == null)
            {
                EditorGUILayout.HelpBox("请先获取对象", MessageType.Info);
                return;
            }

            var rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000);
            treeView.OnGUI(rect);
        }

        private void GetObjectByPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("请输入路径");
                return;
            }

            var result = pathResolver.Resolve(path);
            if (result.Success && result.Value != null)
            {
                Debug.Log($"成功获取对象: {result.Value.GetType().Name}");
                targetObject = result.Value;

                // 查询成功且路径不在预定义列表中，则记录为第一条并刷新下拉框
                if (configStore.AddPathIfAbsent(path))
                {
                    presetSelectedIndex = 0;
                    Debug.Log($"已将路径添加到预定义列表: {path}");
                }
                else
                {
                    // 已存在则同步下拉框选中项到该路径
                    var idx = configStore.Paths.IndexOf(path.Trim());
                    if (idx >= 0)
                        presetSelectedIndex = idx;
                }

                // 通过路径获取新对象视为全新起点，清空回退历史
                historyStack.Clear();
                RefreshTree();
            }
            else
            {
                Debug.LogError($"无法解析路径: {path}（{result.Error}）");
                treeViewState = new TreeViewState();
                treeView = null;
            }
        }

        /// <summary>
        /// 导出当前树为 JSON：弹出文件夹选择窗口，文件名为根节点名称。
        /// 超过展开深度的复合节点只记录摘要（truncated），不深入展开。
        /// </summary>
        private void ExportTree()
        {
            if (rootNode == null)
            {
                Debug.LogWarning("没有可导出的树，请先获取对象");
                return;
            }

            var folder = EditorUtility.OpenFolderPanel("选择导出文件夹", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder))
                return; // 用户取消

            try
            {
                var json = exporter.ToJson(rootNode);
                var fileName = SanitizeFileName(rootNode.Name) + ".json";
                var fullPath = System.IO.Path.Combine(folder, fileName);
                System.IO.File.WriteAllText(fullPath, json);

                Debug.Log($"已导出对象树到: {fullPath}");
                EditorUtility.RevealInFinder(fullPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"导出失败: {ex.Message}");
            }
        }

        /// <summary>清理文件名中的非法字符。</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "ObjectTree";

            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>根据当前 <see cref="targetObject"/> 重建数据树与视图。</summary>
        /// <param name="selectNodeId">重建后应选中的节点 Id；为 null 时选中根节点。</param>
        internal void RefreshTree(int? selectNodeId = null)
        {
            if (targetObject == null)
            {
                treeView = null;
                return;
            }

            var builder = new ObjectTreeBuilder(inspector, memberFilter, buildOptions);
            rootNode = builder.Build(targetObject);

            // 全新的树（获取对象/二次展开/回退，selectNodeId 为 null）使用全新状态，
            // 避免旧树按节点 Id 残留的展开/滚动位置串到新树；
            // 编辑刷新（带 selectNodeId）则保留状态以维持用户当前展开与滚动。
            if (selectNodeId == null || treeViewState == null)
                treeViewState = new TreeViewState();
            treeView = new ObjectTreeView(treeViewState, this, presenter, valueWriter);

            if (search.IsActive)
                search.RecomputeAfterRefresh();
            else
            {
                treeView.Reload();
                // 默认选中指定节点（如编辑过的节点），否则选中根节点并聚焦，便于方向键导航
                var targetId = selectNodeId ?? rootNode?.Id;
                if (targetId.HasValue)
                    treeView.SelectAndFocus(targetId.Value);
            }
        }

        /// <summary>
        /// 二次展开：将占位节点的对象作为新的根，清空当前树并重建。
        /// 当前根对象压入历史栈以支持 ⬅ 回退。
        /// </summary>
        internal void DrillInto(ObjectTreeNode node)
        {
            if (node?.OriginalObject == null)
            {
                Debug.LogWarning("该节点没有可展开的对象");
                return;
            }

            if (targetObject != null)
                historyStack.Push(targetObject);

            targetObject = node.OriginalObject;

            search.Clear();
            RefreshTree();
            Repaint();
        }

        /// <summary>
        /// 回退到上一个树：从历史栈取出上一级根对象并重建。回退不保留任何状态。
        /// </summary>
        private void GoBack()
        {
            if (historyStack.Count == 0)
                return;

            targetObject = historyStack.Pop();

            search.Clear();
            RefreshTree();
            Repaint();
        }
    }
}
