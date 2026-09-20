# 第十七批：原生同步元数据与源绑定

日期：2026-09-10。继续完整性恢复计划 A/B，目标仍是完整可用的 Godot ALS 项目。

## 已补齐的源合同

扩展既有只读 `AlsStopGraphCommandlet -IncludeCycle`，不保存 UE 资产：

- 每个 BlendSpace 的 `bUseLegacySamplePointAnimationLengthCalculations`、
  `bShouldMatchSyncPhases` 和 `GetUniqueMarkerNames()` 返回的有效标记集合。
- 图中实际 Sequence、BlendSpace 样本、SequenceEvaluator 依赖的资产路径、长度、倍率。
- 每个资产原生 AuthoredSyncMarkers 的完整顺序、名称、时间和轨道索引。
- `syncSchemaVersion=1`，缺少新版数据的旧源图不能静默走默认值。

源图依旧含 69 张图，播放器绑定范围仍为 Cycle/Detail/Stop Plant 的 37 个
播放器/求值器和 59 个采样实例，不是完整 P5 的最终数量。
同步元数据资产表有 30 个序列、40 个标记，其中 ALS_N_Pose 来自外层 Not Moving
求值器。它已被读取并校验，但没有因此获得一个虚构的已接线播放器身份。
元数据闭包按源图依赖校验，不丢弃该依赖，也不接受没有源图引用的额外资产。

## 编译与消费

`AlsLocomotionSourceSyncCompiler` 作为既有源编译器的内部步骤：

- 与导入资产核对骨架、长度、有序标记名称/时间/SourceIndex/TrackIndex。
- 对照源采样表的资产倍率，拒绝缺数据、冲突数据和当前不支持的同步策略。
- 核对 BlendSpace 自身报告的有效标记集合与实际样本表，不能只看样本存在标记。
- 标记名在整个动画集范围内编译为统一数值符号，零为 None；当前为 Left/Right。
- 生成只含数值的 `RuntimeSyncPlayers`、`SyncSequences`、`SyncMarkers`；
  每个 `RuntimeSamples` 显式指向 SequenceIndex。
- Sequence AssetId 使用动画集 ID，BlendSpace AssetId 使用动画总数偏移加 BlendSpace ID，
  防止两种资产的同号 ID 混淆。PlayerId 与 SampleId 仍保留各自独立身份。
- 源绑定摘要版本升为 2，包含上述所有数据和符号表；公开数组防御性复制。

DetailMachineSmoke 已从这些数值绑定取资产 ID、序列索引、倍率、标记及配置，
不再在组件里假造一份无标记序列表。SourceState 仍是组件验证持有者，
不是已经完成的 P5 facade 或可玩 Demo 的事务所有者。

## 验证范围

新增编译回归先复现旧实现的错误：把 `bShouldMatchSyncPhases` 改为 true，
旧编译器没有抛异常。补完合同后，该不支持配置与缺字段、错误标记/轨道/顺序、
缺资产及错误有效标记集合均被拒绝。

FormalSourceBindingsDriveNativeMixedSyncReplay 使用本批源绑定驱动上一批混合 Sync：
1,152 帧连续使用 Core 自己的时间和历史，比较组 Leader/标记位置、播放器时间、
样本时间/DeltaTimeRecord、标记索引及距离。资产身份、倍率、标记表和初始化值来自
正式源编译结果，只有输入播放倍率及已解算权重/顺序来自原生探针。
这证明源绑定到同步求值的合同一致，不代表完整 AnimBP 或角色输入/运动回放已验收。

## 构建与导出

按 `ue-diagnosing-plugin-build-load` 技能完成整个项目 Editor target 构建和插件审计。
最终日志前缀：`20260910T084714189Z-c737b995af6942cfab82cebf6a5f6260`。
BuildId：`a62acd02-ebd3-4df3-b93a-71842617113c`。
构建状态 fingerprint：`E7FBB15FAE778AD24C8E604483582E17C9AE44286117277D414F4909D2895C27`。

两次独立冷启动导出均退出 0，结果逐字节一致：

`assets/config/v4_locomotion_source_graph.json`
SHA256：`60E5D6F540F186C1A2279004E25AD537A9166B814990B86992C4C03F83E0301F`。

原生日志在 `../AdvancedLocomotionSystemV/Saved/Logs/`：
`AlsSourceSync-20260910-0850.log` 和 `AlsSourceSync-20260910-repeat.log`。
输出 `ALS_STOP_GRAPH_OK graphs=69 plant_evaluators=12 assets_saved=0`、
`ALS_DETAIL_GRAPH_OK players=16 assets_saved=0`、
`ALS_CYCLE_GRAPH_OK blendspaces=7 sequences=2 assets_saved=0`。

本批没有修改引擎或 UE 资产，没有进行 UE GUI 重启、全项目 DataValidation、打包构建、
可玩 Demo 新截图或人工观感验收；这些不能由命令行探针和组件测试替代。

## 本批结果

- 导入测试 Debug 633/633，源编译专项 Debug/Release 各 32/32。
- Core 回归 1,575/1,575；明确排除 `AlsP5aGoldenTests` 和
  `AlsP5aTraceSchemaTests`，未把这两组耗时门禁记为本批通过。
- Godot 工程构建 0 warning、0 error。
- Detail 真实资源组件：`DETAIL_MACHINE_OK rates=30,60,120 states=6 bone_checks=71400 retries=1050 requests=38 resets=6 correction=0.064214 alloc=0B sync=asset_runtime identities=source_graph demo=not_connected`。
- 原生源导出两次均通过，SHA256 一致；原有混合 Sync 轨迹 fixture 没有改写。
- 镜头和输入源码 SHA256 未变，没有提交 Git 或还原用户修改。

## 后续

源局部绑定已补齐本批同步元数据，但完整 P5 物理布局、正式绑定摘要接入、
Gather/Worker/Commit 统一事务、Notify 生命周期以及 Cycle/Detail/外层起停接线
尚未完成。动态上身分层、Overlay 和原计划剩余阶段继续保留。
没有新的 Demo 滑步修复结论，镜头与键鼠输入保持不变。
