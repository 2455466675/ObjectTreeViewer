using System;
using System.Collections;
using UnityEngine;

namespace ObjectTreeViewerTool
{
    /// <summary>
    /// 负责把转换后的值写回对象：优先字段/属性，其次容器（List/Dict）索引器。
    /// </summary>
    internal sealed class ValueWriter
    {
        private readonly ValueConverter converter;

        public ValueWriter(ValueConverter converter)
        {
            this.converter = converter;
        }

        /// <summary>写回结果。</summary>
        public readonly struct WriteResult
        {
            public bool Success { get; }
            public string Message { get; }

            private WriteResult(bool success, string message)
            {
                Success = success;
                Message = message;
            }

            public static WriteResult Ok(string message) => new WriteResult(true, message);
            public static WriteResult Fail(string message) => new WriteResult(false, message);
        }

        /// <summary>
        /// 将文本值写回到节点对应的字段/属性/容器元素。
        /// </summary>
        public WriteResult Write(ObjectTreeNode node, string newValueText)
        {
            try
            {
                var targetObj = node.Parent?.OriginalObject;
                if (targetObj == null)
                    return WriteResult.Fail("无法获取父对象，无法修改值");

                object convertedValue = converter.Convert(newValueText, node.Type);
                if (convertedValue == null && node.Type != "null" && node.Type != "string")
                    return WriteResult.Fail($"值转换失败: {newValueText} -> {node.Type}");

                // 通过反射字段/属性写回
                if (node.SourceField != null)
                {
                    node.SourceField.SetValue(targetObj, convertedValue);
                    return WriteResult.Ok($"成功修改字段 {node.Name} 的值: {node.Value} -> {convertedValue}");
                }

                if (node.SourceProperty != null && node.SourceProperty.CanWrite)
                {
                    node.SourceProperty.SetValue(targetObj, convertedValue);
                    return WriteResult.Ok($"成功修改属性 {node.Name} 的值: {node.Value} -> {convertedValue}");
                }

                // 节点是容器（List/Dict）的简单值类型元素，通过容器索引器写回
                if (node.IsContainerEntry)
                {
                    if (targetObj is IList list && node.ContainerKey is int index)
                    {
                        list[index] = convertedValue;
                        return WriteResult.Ok($"成功修改列表 [{index}] 的值: {node.Value} -> {convertedValue}");
                    }
                    if (targetObj is IDictionary dict && node.ContainerKey != null)
                    {
                        dict[node.ContainerKey] = convertedValue;
                        return WriteResult.Ok($"成功修改字典 [{node.ContainerKey}] 的值: {node.Value} -> {convertedValue}");
                    }
                    return WriteResult.Fail($"无法修改容器元素 {node.Name}");
                }

                return WriteResult.Fail($"无法修改 {node.Name}，无法找到可写的字段、属性或容器");
            }
            catch (Exception ex)
            {
                return WriteResult.Fail($"修改值时出错: {ex.Message}");
            }
        }
    }
}
