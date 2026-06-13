using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 读取同目录下 <c>ViewerConfig.json</c> 中的用户配置：
    /// 预定义查询路径、排除的命名空间前缀等。
    /// 通过 <see cref="CallerFilePathAttribute"/> 定位脚本所在目录，从而稳定找到 JSON。
    /// </summary>
    internal sealed class ViewerConfigStore
    {
        [Serializable]
        private sealed class ConfigData
        {
            public List<string> paths = new List<string>();
            public List<string> excludedNamespacePrefixes;
        }

        private const string JsonFileName = "ViewerConfig.json";
        private const string LegacyJsonFileName = "QueryPathPresets.json";

        // 预定义路径记录上限，超出时移除最旧的一条（列表末尾），避免文件无限增长
        private const int MaxPathCount = 100;

        private static readonly string[] DefaultExcludedNamespacePrefixes =
        {
            "UnityEngine",
            "UnityEditor",
            "System.Reflection",
        };

        private readonly string configDirectory;

        /// <summary>JSON 配置文件的绝对路径（始终指向 <see cref="JsonFileName"/>）。</summary>
        public string JsonFilePath { get; }

        /// <summary>已加载的预定义路径列表（可能为空）。</summary>
        public List<string> Paths { get; private set; } = new List<string>();

        /// <summary>
        /// 生效的排除命名空间前缀（前缀匹配，含子命名空间）。
        /// 始终包含 <see cref="DefaultExcludedNamespacePrefixes"/>，并合并 JSON 中的额外配置。
        /// </summary>
        public IReadOnlyList<string> ExcludedNamespacePrefixes { get; private set; } = Array.Empty<string>();

        /// <summary>JSON 中配置的额外排除命名空间前缀（不含内置默认值）。</summary>
        private List<string> userExcludedNamespacePrefixes = new List<string>();

        /// <summary>是否存在至少一条预定义路径。</summary>
        public bool HasPaths => Paths != null && Paths.Count > 0;

        public ViewerConfigStore()
        {
            configDirectory = ResolveConfigDirectory();
            JsonFilePath = string.IsNullOrEmpty(configDirectory)
                ? null
                : Path.Combine(configDirectory, JsonFileName);
            Load();
        }

        /// <summary>重新从磁盘加载配置。</summary>
        public void Load()
        {
            Paths = new List<string>();
            userExcludedNamespacePrefixes = new List<string>();
            ExcludedNamespacePrefixes = MergeExcludedNamespacePrefixes(userExcludedNamespacePrefixes);

            var readPath = ResolveReadPath();
            if (string.IsNullOrEmpty(readPath) || !File.Exists(readPath))
                return;

            try
            {
                var text = File.ReadAllText(readPath);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                var data = JsonUtility.FromJson<ConfigData>(text);
                if (data == null)
                    return;

                if (data.paths != null)
                {
                    foreach (var p in data.paths)
                    {
                        if (!string.IsNullOrWhiteSpace(p))
                            Paths.Add(p.Trim());
                    }
                }

                userExcludedNamespacePrefixes = ParseUserExcludedNamespacePrefixes(data.excludedNamespacePrefixes);
                ExcludedNamespacePrefixes = MergeExcludedNamespacePrefixes(userExcludedNamespacePrefixes);
            }
            catch (Exception ex)
            {
                Debug.LogError($"读取配置文件失败: {ex.Message}");
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

        /// <summary>将当前配置写回 JSON 文件。</summary>
        private void Save()
        {
            if (string.IsNullOrEmpty(JsonFilePath))
                return;

            try
            {
                var data = new ConfigData
                {
                    paths = new List<string>(Paths),
                    excludedNamespacePrefixes = new List<string>(userExcludedNamespacePrefixes),
                };
                var json = JsonUtility.ToJson(data, true);
                File.WriteAllText(JsonFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"写入配置文件失败: {ex.Message}");
            }
        }

        private string ResolveReadPath()
        {
            if (string.IsNullOrEmpty(configDirectory))
                return null;

            var primary = Path.Combine(configDirectory, JsonFileName);
            if (File.Exists(primary))
                return primary;

            var legacy = Path.Combine(configDirectory, LegacyJsonFileName);
            if (File.Exists(legacy))
                return legacy;

            return primary;
        }

        private static List<string> ParseUserExcludedNamespacePrefixes(List<string> raw)
        {
            var result = new List<string>();
            if (raw == null)
                return result;

            foreach (var prefix in raw)
            {
                if (string.IsNullOrWhiteSpace(prefix))
                    continue;

                var trimmed = prefix.Trim();
                if (IsDefaultExcludedNamespacePrefix(trimmed))
                    continue;

                if (!result.Contains(trimmed))
                    result.Add(trimmed);
            }
            return result;
        }

        private static IReadOnlyList<string> MergeExcludedNamespacePrefixes(List<string> userExtras)
        {
            var merged = new List<string>(DefaultExcludedNamespacePrefixes);
            if (userExtras == null)
                return merged;

            foreach (var prefix in userExtras)
            {
                if (string.IsNullOrWhiteSpace(prefix))
                    continue;

                var trimmed = prefix.Trim();
                if (!merged.Contains(trimmed))
                    merged.Add(trimmed);
            }
            return merged;
        }

        private static bool IsDefaultExcludedNamespacePrefix(string prefix)
        {
            foreach (var defaultPrefix in DefaultExcludedNamespacePrefixes)
            {
                if (defaultPrefix == prefix)
                    return true;
            }
            return false;
        }

        /// <summary>通过编译期记录的脚本路径推导配置目录。</summary>
        private static string ResolveConfigDirectory([CallerFilePath] string callerFilePath = "")
        {
            if (string.IsNullOrEmpty(callerFilePath))
                return null;

            return Path.GetDirectoryName(callerFilePath);
        }
    }
}
