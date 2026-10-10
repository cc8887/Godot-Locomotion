# Mantle v4 默认活动指针与游戏启动（2026-10-09）

应用户要求，为游戏内人工测试将 `assets/config/als_resource_pack.json` 从 `als_nonprop_20261009_idle_arms_v2` 切换到 `als_nonprop_20261009_mantle_v4/resource-pack.json`，沿用 `allowDiagnosticPack=true`。该诊断包只替换四个 V4／Refactored Low Mantle 身份；Walk／Run 等其余资源与 High 为原版。本次没有合并旧 v2 的 locomotion，也没有更改包的质量状态或资产载荷。

切换前实际验证全部 829 个索引载荷存在且 SHA-256 匹配；包摘要仍为 `cdb8302940e41603c326eca5feca547c53f92a05b25a76e31c8a31207f2b1906`。旧指针备份在 `artifacts/kimodo_als/mantle-active-20261009/previous-active-pointer.json`。

没有设置 `ALS_RESOURCE_PACK_PATH` 覆盖，从普通入口 `scenes/demo/als_demo.tscn` 执行 headless 180 帧启动检查，退出码 0；随后打开真实图形窗口供用户测试。两次启动均记录：

```
ALS_RESOURCE_PACK_SELECTED scope=non-prop replacements=4 pack=res://assets/generated/als_nonprop_20261009_mantle_v4/resource-pack.json
ALS_ROOT_MOTION_POLICY enabled=False
```

证据目录 `artifacts/kimodo_als/mantle-active-20261009/`：`default-startup.log`、`interactive-game.log`、`launch.json`。本次仅配置切换与真实启动，未修改 C# 或重新构建；实际 Low 自动场景及逐键方向检查见[朝向检查记录](2026-10-09-kimodo-mantle-orientation.md)。用户游戏内人工视觉验收仍待进行，High 源失败／缺失及之前完整运行矩阵的失败保持，默认诊断选择不代表完整资源包生产验收。
