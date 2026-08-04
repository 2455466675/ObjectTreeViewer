# Object Tree Viewer

Unity Editor 调试工具：通过反射查看、编辑与导出运行时对象树（懒加载展开）。

## 安装

通过 Package Manager 以本地路径安装，或在 `Packages/manifest.json` 中添加：

```json
"com.qingzheng.object-tree-viewer": "file:../LocalPackages/com.qingzheng.object-tree-viewer@1.0.1"
```

也可使用 Git URL 或将整个目录复制到目标工程的 `Packages/` 下作为嵌入包。

## 使用

菜单 `Window/对象树查看器` 打开窗口。

- 在路径输入框填写 `类型名.成员[.成员...]`（如 `GameData.I.GrowthScoreData`），点击「获取对象」。
- 上方下拉框列出预定义路径（来自 `Editor/ViewerConfig.json`），选中仅填充输入框，不触发查询。查询成功的新路径会自动记入预定义列表（上限 100 条）。点击 ↻ 可重新加载配置文件（含命名空间过滤配置）。
- F2 或双击叶子节点编辑值；bool 直接切换。
- 树采用懒加载：初始仅展开一层，复合节点显示展开箭头 ▶，点击箭头或双击节点时才按需构建下一层子节点。
- 单节点子项数超过上限（默认 5000）或整树节点超过上限（默认 20000）时会截断并提示，避免超大对象一次性铺开。
- 「导出」将当前对象树完整（深度）构建后导出为 JSON。

## 配置

`Editor/ViewerConfig.json` 示例：

```json
{
    "paths": [
        "GameData.I.GrowthScoreData"
    ],
    "excludedNamespacePrefixes": [
        "Demo"
    ]
}
```

- `paths`：预定义查询路径列表。
- `excludedNamespacePrefixes`：在内置默认值之外额外排除的命名空间前缀（前缀匹配，含子命名空间）。内置默认值始终生效：`UnityEngine`、`UnityEditor`、`System.Reflection`；JSON 中只需填写额外项，省略或设为 `[]` 表示无额外配置。
- 若仍使用旧版 `QueryPathPresets.json`，加载时会自动兼容；保存时会写入 `ViewerConfig.json`。

基类/精确类型等其它过滤规则仍由 `Editor/Reflection/MemberFilter.cs` 维护。

## 说明

- 仅在 Editor 下生效（asmdef 限定 `includePlatforms: ["Editor"]`），不会进入运行时构建。
