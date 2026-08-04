using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 对象树查看器主窗口：作为各模块的组合根（composition root），负责装配依赖、
    /// 绘制工具栏、协调取对象 / 刷新 / 懒加载展开等流程。
    /// 树采用懒加载：初始仅展开一层，用户展开某节点时再增量构建其子节点。
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
        private ViewerConfigStore configStore;
        private TreeJsonExporter exporter;

        // ——— 运行状态 ———
        private object targetObject;
        private ObjectTreeNode rootNode;
        private ObjectTreeBuilder builder;   // 服务于当前树的懒加载构建器
        private ObjectTreeView treeView;
        private TreeViewState treeViewState;

        private string memberPath = "GameData.I.GrowthScoreData";
        // 预定义路径下拉框当前选中索引
        private int presetSelectedIndex;

        // ——— IObjectTreeViewHost ———
        ObjectTreeNode IObjectTreeViewHost.RootNode => rootNode;
        void IObjectTreeViewHost.EnsureChildren(ObjectTreeNode node) => builder?.EnsureChildren(node);
        void IObjectTreeViewHost.RefreshNodeValue(ObjectTreeNode node) => RefreshNodeValue(node);

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
            buildOptions = new ObjectTreeBuilder.Options { MaxNodeCount = 20000, MaxChildrenPerNode = 5000 };
            presenter = new NodePresenter();
            valueWriter = new ValueWriter(new ValueConverter());
            exporter = new TreeJsonExporter();

            // 若存在预定义路径，默认显示并填充第一条
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
                "点击箭头或双击复合节点可展开（按需构建子节点）| 按 F2 编辑叶子值\n",
                MessageType.Info);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);
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
        /// 导出当前对象树为 JSON：弹出文件夹选择窗口，文件名为根节点名称。
        /// 导出使用独立构建器做完整（深度）构建，不影响 UI 的懒加载树；受节点上限与循环引用保护。
        /// </summary>
        private void ExportTree()
        {
            if (targetObject == null)
            {
                Debug.LogWarning("没有可导出的对象，请先获取对象");
                return;
            }

            var folder = EditorUtility.OpenFolderPanel("选择导出文件夹", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder))
                return; // 用户取消

            try
            {
                var exportBuilder = new ObjectTreeBuilder(inspector, memberFilter, buildOptions);
                var exportRoot = exportBuilder.BuildRoot(targetObject);
                exportBuilder.BuildFullTree(exportRoot);

                var json = exporter.ToJson(exportRoot);
                var fileName = SanitizeFileName(exportRoot.Name) + ".json";
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

        /// <summary>根据当前 <see cref="targetObject"/> 重建根节点与视图（懒加载，仅构建第一层）。</summary>
        private void RefreshTree()
        {
            if (targetObject == null)
            {
                treeView = null;
                return;
            }

            builder = new ObjectTreeBuilder(inspector, memberFilter, buildOptions);
            rootNode = builder.BuildRoot(targetObject);

            // 每次重建都视为全新树，使用全新视图状态，避免旧树的展开/滚动位置串到新树
            treeViewState = new TreeViewState();
            treeView = new ObjectTreeView(treeViewState, this, presenter, valueWriter);
            treeView.Reload();

            if (rootNode != null)
                treeView.ExpandRootAndSelect(rootNode.Id);
        }

        /// <summary>
        /// 叶子节点的值被编辑后，就地重新读取该节点的显示值（不重建整棵树），以保留当前展开状态。
        /// </summary>
        private void RefreshNodeValue(ObjectTreeNode node)
        {
            var parent = node?.Parent?.OriginalObject;
            if (parent == null)
                return;

            try
            {
                object value = null;
                if (node.SourceField != null)
                {
                    value = node.SourceField.GetValue(parent);
                }
                else if (node.SourceProperty != null && node.SourceProperty.CanRead)
                {
                    value = node.SourceProperty.GetValue(parent);
                }
                else if (node.IsContainerEntry)
                {
                    if (parent is IList list && node.ContainerKey is int index && index >= 0 && index < list.Count)
                        value = list[index];
                    else if (parent is IDictionary dict && node.ContainerKey != null)
                        value = dict[node.ContainerKey];
                }

                node.Value = value?.ToString() ?? "null";
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"刷新节点值失败: {ex.Message}");
            }
        }
    }
}
