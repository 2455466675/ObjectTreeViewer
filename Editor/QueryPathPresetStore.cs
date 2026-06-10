using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 读取同目录下 <c>QueryPathPresets.json</c> 中预定义的查询路径。
    /// 通过 <see cref="CallerFilePathAttribute"/> 定位脚本所在目录，从而稳定找到 JSON。
    /// </summary>
    internal sealed class QueryPathPresetStore
    {
        [Serializable]
        private sealed class PresetData
        {
            public List<string> paths = new List<string>();
        }

        private const string JsonFileName = "QueryPathPresets.json";

        // 预定义路径记录上限，超出时移除最旧的一条（列表末尾），避免文件无限增长
        private const int MaxPathCount = 100;

        /// <summary>JSON 文件的绝对路径。</summary>
        public string JsonFilePath { get; }

        /// <summary>已加载的预定义路径列表（可能为空）。</summary>
        public List<string> Paths { get; private set; } = new List<string>();

        /// <summary>是否存在至少一条预定义路径。</summary>
        public bool HasPaths => Paths != null && Paths.Count > 0;

        public QueryPathPresetStore()
        {
            JsonFilePath = ResolveJsonPath();
            Load();
        }

        /// <summary>重新从磁盘加载预定义路径。</summary>
        public void Load()
        {
            Paths = new List<string>();

            if (string.IsNullOrEmpty(JsonFilePath) || !File.Exists(JsonFilePath))
                return;

            try
            {
                var text = File.ReadAllText(JsonFilePath);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                var data = JsonUtility.FromJson<PresetData>(text);
                if (data?.paths == null)
                    return;

                foreach (var p in data.paths)
                {
                    if (!string.IsNullOrWhiteSpace(p))
                        Paths.Add(p.Trim());
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"读取预定义查询路径失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 若路径不在预定义列表中，则将其插入为第一条并写回 JSON。
        /// 返回 true 表示发生了新增（已落盘）。
        /// </summary>
        public bool AddPathIfAbsent(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            path = path.Trim();
            if (Paths.Contains(path))
                return false;

            Paths.Insert(0, path);

            // 新记录插入到首位，超过上限时丢弃末尾（最旧）的记录
            if (Paths.Count > MaxPathCount)
                Paths.RemoveRange(MaxPathCount, Paths.Count - MaxPathCount);

            Save();
            return true;
        }

        /// <summary>将当前路径列表写回 JSON 文件。</summary>
        private void Save()
        {
            if (string.IsNullOrEmpty(JsonFilePath))
                return;

            try
            {
                var data = new PresetData { paths = new List<string>(Paths) };
                var json = JsonUtility.ToJson(data, true);
                File.WriteAllText(JsonFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"写入预定义查询路径失败: {ex.Message}");
            }
        }

        /// <summary>通过编译期记录的脚本路径推导 JSON 的绝对路径。</summary>
        private static string ResolveJsonPath([CallerFilePath] string callerFilePath = "")
        {
            if (string.IsNullOrEmpty(callerFilePath))
                return null;

            var dir = Path.GetDirectoryName(callerFilePath);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, JsonFileName);
        }
    }
}
