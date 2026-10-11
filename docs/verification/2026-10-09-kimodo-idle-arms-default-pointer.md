# 2026-10-09 Kimodo v2 诊断包默认指针

应用户要求，将项目默认动画资源指针从 v8 切换到本轮 v2 包，便于普通游戏启动时直接检查新资源。该切换只改变默认选择，不改变 v2 的质量状态，也不代表生产发布门禁通过。

## 指针和加载行为

[`assets/config/als_resource_pack.json`](../../assets/config/als_resource_pack.json) 当前指向 `res://assets/generated/als_nonprop_20261009_idle_arms_v2/resource-pack.json`，并显式设置 `allowDiagnosticPack: true`。加载器只在未设置 `ALS_RESOURCE_PACK_PATH` 环境覆盖时接受该指针上的诊断包；显式环境覆盖仍遵循原来的验证规则。v2 资源包本身仍标记为 `diagnostic`，没有把生产状态改成已验证。

包摘要为 SHA-256 `2348d56fa56f97d4008a4fd5f3adb5984a5ccaca84a2eb34a43938990b60c741`，替换 123 个非道具身份，另有 60 个身份缺失或未通过源门禁。原有道具资源保留。整链结果仍是 `pipeline_complete_rejected`，`productionReady=false`、`activated=false`；71 项 E2E 为 70 项通过、1 项 `recovery-auto-roll` 失败。不能将默认指向诊断包解释为完整资源集已验收或正式发布。

## 启动验证

优化构建 `dotnet build GodotALS.csproj -p:Optimize=true` 已通过，0 错误、0 警告。随后从普通入口 `scenes/demo/als_demo.tscn` 启动 Godot，没有设置 `ALS_RESOURCE_PACK_PATH` 环境覆盖；普通 Demo 日志确认：

- `ALS_RESOURCE_PACK_SELECTED scope=non-prop replacements=123 pack=res://assets/generated/als_nonprop_20261009_idle_arms_v2/resource-pack.json`
- `ALS_ROOT_MOTION_POLICY enabled=False`

实际窗口标题为 `Godot ALS (DEBUG)`，进程保持响应。启动日志位于 `%APPDATA%/Godot/app_userdata/Godot ALS/logs/godot2026-10-09T13.50.10.log`。本次确认的是默认资源选择、导入后正常启动和 Root Motion 关闭状态；方向移动的人工游戏验收仍待用户继续验证。

结构化启动摘要见 [default-pointer-launch.json](../../artifacts/kimodo_als/idle-arms-20261009-v2/default-pointer-launch.json)。本轮此前的生成、转换、步态截图及失败边界见 [完整 Walk/Idle 生产验证](2026-10-09-kimodo-idle-consistent-arms.md)。
