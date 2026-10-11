# Godot 资源扫描范围

Godot 导入范围与 ALS 运行时活动包指针是两个独立机制。活动指针只选择运行载荷；历史包没有 `.gdignore` 时，编辑器仍扫描和导入它们。

`tools/godot_resource_scan.py` 保留以下顶层生成目录参与扫描：

- `assets/generated/als_v4`：ALS 原版 FBX、材质和编译资源。
- `assets/generated/als_v4_raw`：原版动画源。
- `assets/generated/lyra_als`：Lyra 动画、图、Rig、装备和原生数学资源。
- `assets/config/als_resource_pack.json` 指向的当前活动包。
- 本次通过 `--include` 明确指定的候选包。

其他顶层生成目录添加 `.gdignore`，文件、历史报告、原始字节和导入缓存保留。不会修改活动指针、导出 JSON 或动画。工具默认预览，`--apply` 才修改标记；保留目录只允许移除空标记或工具自己的标记，自定义标记冲突会在修改前报错。

```powershell
# 预览 / 应用当前活动范围
python tools/godot_resource_scan.py
python tools/godot_resource_scan.py --apply

# 验证旧包或导入新候选前，额外开放需要的目录，可重复 --include
python tools/godot_resource_scan.py --apply --include res://assets/generated/<candidate>/resource-pack.json

# 候选验证结束后恢复活动范围
python tools/godot_resource_scan.py --apply
```

完整 Kimodo pipeline 的 `verify_runtime` 在 Godot 导入前自动执行此工具，包含本次候选并保存独立范围报告；失败阻止导入。候选验证后仍保持可访问，下次范围同步时不再需要的候选退出扫描。独立旧版验证脚本不自动同步，运行它们前使用上述命令。

`.gdignore` 会让资源退出 Godot 的资源发现及正常 `ResourceLoader` 加载；它不是删除资产。外部 Python/FBX 分析和 Motion Viewer 的普通磁盘访问仍可读取历史文件。需要 Godot 对照的旧包必须先显式重新开放并执行导入，不能只设置运行时环境变量。

范围工具及 pipeline 修改都在版本控制中；生成目录的标记沿用现有 ignored 规则。代码检出或更换活动包之后，需要重新执行范围同步。

实测及覆盖边界见 [2026-10-09 验证](../verification/2026-10-09-godot-resource-scan-scope.md)。
