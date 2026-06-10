using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 管理树搜索：关键字状态、命中收集、结果导航与搜索栏绘制。
    /// 通过回调与宿主解耦（获取根节点、获取视图、请求重绘）。
    /// </summary>
    internal sealed class TreeSearchController
    {
        private string searchText = "";
        private string activeSearchText = "";
        private readonly List<int> resultIds = new List<int>();
        private readonly HashSet<int> matchSet = new HashSet<int>();
        private int resultIndex = -1;

        private readonly Func<ObjectTreeNode> rootProvider;
        private readonly Func<ObjectTreeView> viewProvider;
        private readonly Action repaint;

        public TreeSearchController(Func<ObjectTreeNode> rootProvider, Func<ObjectTreeView> viewProvider, Action repaint)
        {
            this.rootProvider = rootProvider;
            this.viewProvider = viewProvider;
            this.repaint = repaint;
        }

        public bool IsActive => !string.IsNullOrEmpty(activeSearchText);

        public bool IsNodeMatch(ObjectTreeNode node)
        {
            if (!IsActive || node == null) return false;
            return matchSet.Contains(node.Id);
        }

        public bool IsCurrentResult(int nodeId)
        {
            if (resultIndex < 0 || resultIndex >= resultIds.Count) return false;
            return resultIds[resultIndex] == nodeId;
        }

        /// <summary>绘制搜索栏并处理键盘交互（Enter / Shift+Enter / Esc）。</summary>
        public void DrawSearchBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField("🔍", GUILayout.Width(20));

            var evt = Event.current;
            bool pressedEnter = evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter);
            bool pressedEscape = evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape;

            GUI.SetNextControlName("SearchField");
            var newSearchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField);
            if (newSearchText != searchText)
            {
                searchText = newSearchText;
                ExecuteSearch();
            }

            if (pressedEnter && GUI.GetNameOfFocusedControl() == "SearchField")
            {
                Navigate(evt.shift ? -1 : 1);
                evt.Use();
            }

            if (pressedEscape && GUI.GetNameOfFocusedControl() == "SearchField")
            {
                Clear();
                GUI.FocusControl(null);
                evt.Use();
            }

            if (IsActive)
            {
                var countLabel = resultIds.Count > 0 ? $"{resultIndex + 1}/{resultIds.Count}" : "0/0";
                EditorGUILayout.LabelField(countLabel, GUILayout.Width(50));

                if (GUILayout.Button("▲", EditorStyles.toolbarButton, GUILayout.Width(22)))
                    Navigate(-1);
                if (GUILayout.Button("▼", EditorStyles.toolbarButton, GUILayout.Width(22)))
                    Navigate(1);
            }

            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(22)))
                Clear();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>执行搜索：收集命中、重建并展开树、定位首个结果。</summary>
        public void ExecuteSearch()
        {
            activeSearchText = searchText.Trim();
            ResetResults();

            var root = rootProvider();
            var view = viewProvider();

            if (string.IsNullOrEmpty(activeSearchText) || root == null)
            {
                if (view != null)
                {
                    view.Reload();
                    view.ExpandAll();
                }
                repaint();
                return;
            }

            CollectResults(root);
            if (resultIds.Count > 0)
                resultIndex = 0;

            if (view != null)
            {
                view.Reload();
                view.ExpandAll();
                if (resultIds.Count > 0)
                    FrameResult();
            }

            repaint();
        }

        /// <summary>在数据刷新后，按当前关键字重新计算命中（不改变搜索框文本）。</summary>
        public void RecomputeAfterRefresh()
        {
            if (!IsActive)
                return;

            ResetResults();
            var root = rootProvider();
            if (root == null)
                return;

            CollectResults(root);
            if (resultIds.Count > 0)
                resultIndex = 0;

            var view = viewProvider();
            if (view != null)
            {
                view.Reload();
                view.ExpandAll();
                if (resultIds.Count > 0)
                    FrameResult();
            }
        }

        /// <summary>清空搜索状态并重建树。</summary>
        public void Clear()
        {
            searchText = "";
            activeSearchText = "";
            ResetResults();

            viewProvider()?.Reload();
            repaint();
        }

        private void ResetResults()
        {
            resultIds.Clear();
            matchSet.Clear();
            resultIndex = -1;
        }

        private void CollectResults(ObjectTreeNode node)
        {
            if (node == null) return;

            var keyword = activeSearchText.ToLower();
            bool match =
                (!string.IsNullOrEmpty(node.Name) && node.Name.ToLower().Contains(keyword)) ||
                (!string.IsNullOrEmpty(node.Value) && node.Value.ToLower().Contains(keyword)) ||
                (!string.IsNullOrEmpty(node.Type) && node.Type.ToLower().Contains(keyword));

            if (match)
            {
                resultIds.Add(node.Id);
                matchSet.Add(node.Id);
            }

            foreach (var child in node.Children)
                CollectResults(child);
        }

        private void Navigate(int direction)
        {
            if (resultIds.Count == 0) return;

            resultIndex += direction;
            if (resultIndex >= resultIds.Count)
                resultIndex = 0;
            else if (resultIndex < 0)
                resultIndex = resultIds.Count - 1;

            FrameResult();
            repaint();
        }

        private void FrameResult()
        {
            if (resultIndex < 0 || resultIndex >= resultIds.Count) return;
            var view = viewProvider();
            if (view == null) return;

            var targetId = resultIds[resultIndex];
            view.SetSelection(new List<int> { targetId }, TreeViewSelectionOptions.RevealAndFrame);
            view.FrameItem(targetId);
        }
    }
}
