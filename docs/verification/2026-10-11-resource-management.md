# 2026-10-11 通用资源管理与启动优化

在主目录实现纯 .NET 的通用资源管理，按单资源身份共享加载结果，支持候选验证后替换、传递依赖失效和旧版本引用存活。Import 接入原始 JSON、骨骼、不可变 pose、T3D 索引和切片；不使用资源包版本作为缓存身份。保留已有 dirty 修改，本次没有更换活动包、重写生成资产、修改既有验收断言或提交/推送。

稳定接口、所有权和接入边界见[资源管理契约](../architecture/resource-management.md)，采集方法见[性能埋点](../architecture/performance-instrumentation.md)。

## 修复范围

- 每次 T3D 提取原先重新分行、扫描整棵对象树，并为大量无关对象拼接字符串；现在每份原文建立一次完整路径索引，只构造所需子图切片。保留 declaration/definition、foreign root、完整平衡、路径唯一性和缩进门禁。
- 多个编译器和 active/spare 构建复用校验后的 JSON、骨骼元数据及派生 pose；sampler、角色播放状态与事务仍独立。
- 顶层 startup scope 覆盖嵌套配置/context/图构建，结束后释放缓存及 factory。没有永久全局原文缓存。
- 第一 worker 帧原先延迟创建 source player，发生额外 159 次读取和 3 次 T3D 扫描；现在在绑定 notify 后、移交 worker 前构造，共用同一 startup scope。不推进帧、时钟或通知，不新建角色提交链路。
- EventSource 增加资源 load/hit/invalidate/replace/release/failure 事件，CLI 输出 `resource-cache.csv` 和 `summary.json.resourceCacheEvents`。

## 环境和证据

Godot 为 `4.7.2.stable.mono.official.ed1daf0bf`，构建 SDK 为 `C:/Users/yuchencui/.dotnet/dotnet.exe` 的 `8.0.425`；实际游戏 RuntimeInfo 为 `.NET 10.0.8`、X64、workstation GC。未设置 `DOTNET_ROOT` 或资源覆盖变量。dotnet-trace 为 `9.0.661903`，TraceEvent 为 `3.2.8`。

活动包为 `als_nonprop_crouch_slip_20261011_v2`，161 个替换身份。最终 trace 与普通无采集启动均确认 `ALS_RESOURCE_PACK_HASH_VALIDATION enabled=False verified=0 skipped=1768`。已有 Import 源载荷摘要/身份门禁保留；这些按需校验不等于重新启用整包扫描。

证据均位于 `artifacts/performance/`，该目录为本地 ignored 输出：

| 目录 | 用途 |
| --- | --- |
| `20261011-startup-instrumented-04` | 修改前细分基线，保留原始 nettrace、汇总和运行参数 |
| `20261011-resource-manager-01` | 中间版本；与其他测试并行，不能作为独立性能复测 |
| `20261011-resource-manager-02` | 中间共享缓存版本，用调用栈定位第一 worker 帧延迟读取 |
| `20261011-resource-manager-03`、`20261011-resource-manager-04` | 最终代码两次独立进程采集，程序集摘要相同 |
| `20261011-resource-manager-regressions` | Release TRX、生命周期、单/并行站姿回归及无采集普通启动日志 |

最终采集命令如下，03 使用独立输出目录。capture 拒绝覆盖已有目录，复现时应选择新名称。

```powershell
& C:/Users/yuchencui/.dotnet/dotnet.exe tools/Als.PerformanceTrace/bin/Release/net8.0/Als.PerformanceTrace.dll capture --godot F:/Github/Godot-Locomotion/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --output artifacts/performance/20261011-resource-manager-04 --dotnet C:/Users/yuchencui/.dotnet/dotnet.exe --timeout-seconds 180
```

采用普通 `als_demo.tscn`、headless、固定模拟 60 Hz、最高 60 FPS、180 帧；首个可见提交触发停止 trace。原始基线和最终采集的场景、包、实际运行时与 profile 一致，程序集变化对应本次实现；没有清空 OS 文件缓存。当前结果是同机进程启动比较，不是磁盘冷缓存或 GPU 首帧测量。

## 启动结果

| 指标 | 原始基线 04 | 最终 03 | 最终 04 |
| --- | --- | --- | --- |
| `demo.bootstrap` | 25.691 s | 13.676 s | 13.722 s |
| 首个可见姿态提交，相对 EventPipe 起点 | 27.013 s | 14.080 s | 14.118 s |
| active `worker.graph_build` | 18.257 s | 6.205 s | 6.272 s |
| `context.build` | 5.227 s | 4.757 s | 4.725 s |
| Refactored 全启动文件读取次数 | 999 | 161 | 161 |
| Refactored 不同文件数 | 161 | 161 | 161 |
| active T3D 提取次数 | 777 | 218 | 218 |
| T3D 索引构建次数 | 每次提取重新扫描 | 9 | 9 |
| active T3D 索引及提取总时间 | 11.015 s | 86.888 ms | 94.172 ms |
| startup 累计托管新分配 | 38.282 GB | 6.919 GB | 6.919 GB |

新版本读取字节为 145,086,456，每个 Refactored 文件仅读取一次；T3D 缓存减少重复提取，218 是实际请求的不同切片，不是动作资产数量。托管分配为包含子区间的累计新分配量，GB 使用十进制；它不是驻留内存，也不能和子区间再相加。旧 `native_graph.extract.itemCount` 是每次全量扫描输入行数，新版本是切片输出行数，两者不能按同一单位比较。

最终两轮均 3,022 spans、2 个里程碑、0 丢失、0 验证错误，游戏/采集器退出 0；线程样本分别为 7,622/7,617。各 2,743 个资源事件包含 1,781 次命中、481 次成功加载和 481 次释放，没有资源失败；每个加载版本有一条对应释放。CSV 与 JSON 计数核对一致。03/04 的 `comparison-to-startup04.json` 保存 CLI 的阶段差值，兼容不含资源事件的旧 summary。

首个可见提交为角色 0、generation 1、frame 2，表示姿态提交成功，不表示 GPU 已绘制。剩余主要 CPU 路径包括 04 的 active 图构建自身约 4.95 s、movement definition 编译；重复提取修复没有关闭所有启动热点。

## 回归验证

最终 `dotnet build GodotALS.csproj -p:Optimize=true` 零警告、零错误。Core/Import/工具测试使用 Release。

| 检查 | 结果与证据 |
| --- | --- |
| Core 资源管理和 EventSource | 15/15，`core-resource-manager-final.trx` |
| Import 缓存/原始图切片 | 16/16，`import-resource-manager-final.trx` |
| CLI 解析/比较既有专项 | 5/5，`performance-tools.trx` |
| 较广 Import 相关矩阵，中间版本 | 1,175/1,176，`import-related-matrix.trx`；既有摘要失败保留 |
| P4 生命周期，中间版本 | 退出 0，`p4-lifecycle.*.log`；替换/过期/故障/恢复等断言通过，注入诊断保留 |
| 最终 60 Hz 单线程站姿回归 | 退出 0，1,700 帧，`stance-single.*.log` |
| 最终 60 Hz 并行站姿回归 | 退出 0，1,700 帧，`stance-parallel.*.log` |
| 最终默认入口，无采集器 | headless 180 帧退出 0，stderr 空，`demo-untraced.*.log`；进程总墙钟约 14.754 s，含有限帧运行与退出，不是单独启动区间 |

Core 专项覆盖普通 JSON Provider/Loader 并发只加载一次、单资源替换/回滚、传递失效、旧 lease/依赖保活、管理器释放、错误重试、循环/类型/线程门禁、同路径同长度同时间戳文件替换及清理异常。Import 覆盖 scoped 复用、独立/嵌套作用域、单载荷更换、坏摘要拒绝、不可变 pose 共享而 sampler 独立、Root Motion 选项分离、精确旧算法切片对照及非法原文；三个真实 Blueprint 各取六个子图与旧提取输出逐字节一致。

单/并行两个场景的断言计数一致：standing 922、crouching 529、grounded 1,525、air 104、sprint 103、footsteps 56、pivot notifies 3、dynamic requests 13；均记录 `ALS_PRODUCTION_NOTIFY_LAYER_OK`、`ALS_REFACTORED_STANCE_DEMO_OK`、`ALS_REFACTORED_REST_FEEDBACK_OK` 和 `ALS_REFACTORED_MOTION_OK`。该证据是现有受控真实 Godot 场景回归，不是完整原生姿态或蒙皮逐帧对照。

较广矩阵唯一失败仍为 `AlsRawAnimationSourceCompilerTests.ActualProductionSourceBankCoversRequestedPlayersAndNativeDependencies` 第 345 行：当前 manifest 的 DefinitionDigest 与旧 `assets/config/v4_movement_source_inputs.json` 的声明摘要不同，发生在调用 raw compiler 前。原始埋点阶段已经记录同一失败；本次没有重写清单、跳过测试或放宽断言。中间版本矩阵不能作为最终全量矩阵证明，后续最终代码以专项、普通启动和单/并行真实场景验证覆盖。

## 覆盖边界

本次完成通用管理基础及上述 Import/启动路径接入。Godot 场景、模型、贴图与全部 Lyra loader 尚未迁移；没有自动文件监视、运行中角色热重建、异步流式加载或持久化预编译缓存。Core API 可显式替换资源，普通 Demo 尚未提供自动热重载流程。未执行最终全量矩阵、GPU/人工视觉、Linux/云端运行或长期帧性能验收，原路线图的生产门禁保持开放。
