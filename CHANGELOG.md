# Changelog

本文件记录此包的所有重要变更。

## [1.2.0]

### Changed
- 重构为懒加载树：初始仅展开一层，展开节点时在原树上按需增量构建其子节点，不再换根重建；显著降低大对象的构建成本与内存占用。
- 编辑叶子值后就地刷新该节点显示，保留当前展开状态。
- 移除深度上限（`MaxDepth`），改用单节点子项上限（`MaxChildrenPerNode`，默认 5000）与整树节点上限（`MaxNodeCount`）作为安全阀。
- 导出改为使用独立构建器做完整（深度）构建，不影响 UI 的懒加载树。

- 配置文件从包内 `Editor/ViewerConfig.json` 迁移至工程 `UserSettings/ViewerConfig.json`（用户私有、默认不入版本库），改用 `Application.dataPath` 定位而非 `[CallerFilePath]`，使其在预编译 DLL / 只读 PackageCache 等形态下也能正常读写。

### Removed
- 移除树搜索功能（与懒加载互斥）。
- 移除二次展开（换根）与 ⬅ / C 返回上一级功能。
- 移除包内出厂 `ViewerConfig.json`（配置改由 UserSettings 按需生成）。
- 移除旧版 `QueryPathPresets.json` 兼容读取。

## [1.1.0]

### Changed
- 二次展开后 ⬅ 返回上一级时，恢复该级树的展开/滚动状态并重新选中当初钻入的节点，不再重置为全新树。

## [1.0.1]

### Changed
- 调整菜单入口路径，更新包元数据。

## [1.0.0]

### Added
- 首个版本：作为 Editor 工具包发布。
- 反射构建对象树：支持 class / struct / List / Dictionary / 数组，深度上限与二次展开。
- 路径查询、预定义路径下拉（JSON 存储，上限 100 条）、字段/属性/容器元素编辑、bool 切换。
- 树搜索（高亮、上一个/下一个导航）、JSON 导出。
- 成员过滤（Unity / 反射 / IO / 委托等基础设施类型）。
