# Object Tree Viewer

Unity Editor 调试工具：通过反射查看、搜索、编辑与导出运行时对象树。

## 安装

通过 Package Manager 以本地路径安装，或在 `Packages/manifest.json` 中添加：

```json
"com.qingzheng.object-tree-viewer": "file:../LocalPackages/com.qingzheng.object-tree-viewer@1.0.0"
```

也可使用 Git URL 或将整个目录复制到目标工程的 `Packages/` 下作为嵌入包。

## 使用

菜单 `Zjwg/对象树查看器` 打开窗口。

- 在路径输入框填写 `类型名.成员[.成员...]`（如 `GameData.I.GrowthScoreData`），点击「获取对象」。
- 上方下拉框列出预定义路径（来自 `Editor/QueryPathPresets.json`），选中仅填充输入框，不触发查询。查询成功的新路径会自动记入预定义列表（上限 100 条）。
- F2 或双击叶子节点编辑值；bool 直接切换。
- 树最大深度 5 层，超出的复合节点显示 ▶，双击或选中后按 → 二次展开为新树；⬅ 或按 C 返回上一级。
- 搜索框支持 Enter 下一个 / Shift+Enter 上一个 / Esc 清除。
- 「导出」将当前树导出为 JSON。

## 说明

- 仅在 Editor 下生效（asmdef 限定 `includePlatforms: ["Editor"]`），不会进入运行时构建。
- 会过滤 Unity/反射/IO 等基础设施类型以减少噪音，过滤规则集中在 `Editor/Reflection/MemberFilter.cs`。
