# 第十一批：Detail 状态更新与姿势链

日期：2026-09-10。工作区：`../GodotALS-p5a-events-actions`。
范围：完整性补完 A 的 Detail 状态更新、相关播放器选择和姿势消费。
尚未接入可玩 Demo；本批时间输入不等同于完成 P5A Sync。

## 实现

- 新增 `AlsLocomotionDetailMachine`：按当前 V4 的固定策略，每次最多一个转换，
  首次更新允许转换，重新相关时初始化为 Walking。Conduit 使用终端内容边的时长
  和逻辑，不错误采用 Walking 到 Conduit 的 0.2 秒普通转换。
- 规则在推进当前状态时间之前执行；转换重置 elapsed，末尾再增加 delta。
  Pivot 的严格 elapsed > 0.1 门控未变成额外的全局换向冷却。
- 规则用的 MainGroundedWeight/DetailWeight 是 UE read buffer 中的历史机器权重，
  当前子状态更新权重另用 ContextWeight。两者独立传入，避免首帧/淡入时错误合并。
- 普通过渡复用活动转换栈；惯性化过渡的状态机混合时长为零，配置时长作为下游
  请求输出，不同时对源状态再做一次普通交叉混合。
- 返回逐状态更新顺序、最终上下文权重、惯性化同步上下文、InitializeStates 和
  ClearCachedWeightStates。进入仍有权重的状态时不重置播放器时间，但仍清除其
  缓存权重。原配置所有 AlwaysResetOnEntry=false，无状态/转换 Notify。
- `AlsTransitionStack.Advance` 新增清理前快照输出，原调用接口保留。Detail 先更新
  尚未完成的旧过渡子状态，再清除被新完成过渡覆盖的栈；同一状态每次最多更新一次。
  不把清理后的姿势权重当作清理前播放器 Update 的权重。
- 相关性判断使用调用方动画遍历 serial，而不是渲染帧时间或 DetailWeight==0。
  serial 相同或仅递增一次不重置，错过至少一次主动画更新才重新初始化。
  调用方必须传动画主更新计数，不能直接传会在动画跳帧期间继续增长的渲染帧号。
- `RelevantTimeRemaining` 消费更新前可见的缓存权重和同步后的源时间，取严格最大
  权重的播放器；同权重保留编译后列表中先出现者。没有正权重播放器返回 float.MaxValue，
  不是返回 0，也不是四个播放器最小/最大剩余时间。
- 机器状态为可复制值，候选求值不修改已提交状态；不持有新的动画时钟，不生成
  Notify，也不把 16 个播放器身份合并成四个动画资源身份。
- `AlsDetailPoseSampler.ComposeMachine` 按活动过渡层级消费六个内容状态的真实姿势，
  中间层不提前归一化旋转；`SampleMachineCurve` 使用同一套层级权重。惯性化请求
  由调用方送给共享下游历史组件。时间布局为 WalkRun / FirstPivot / SecondPivot /
  RunStart 各四个 F/B/L/R 源，共 16 个，不在此采样器中推进时间。

## 原生依据与导出

本地 UE 5.9：`AnimNode_StateMachine.cpp` 的 Update_AnyThread、TransitionToState、
SetState、UpdateTransitionStates、GetRelevantAssetPlayerInterfaceFromState；
`AnimTypes.h::FGraphTraversalCounter`；`AnimNode_AssetPlayerBase.cpp` 的权重缓存和
惯性化同步上下文；`AnimNode_SequencePlayer.cpp` 的初始化/时间读取；
`AnimNode_MultiWayBlend.cpp` 的子节点更新条件。

`AlsStopGraphCommandlet -IncludeDetail` 新增 16 个 SequencePlayer 的 compiledNodeIndex，
通过源 GUID 读取，不手填索引。编译器将 baked PlayerNodeIndices 映射成独立的
RelevancyPlayerOrder，并拒绝遗漏、重复、未知索引、未支持的 linked layer 和
自转换惯性化配置。当前原生次序恰好为 F/B/L/R，但实现不依赖这一巧合。

按 `ue-diagnosing-plugin-build-load` 技能完成整个 Editor target 构建及插件审计，
再冷启动只读导出。Build fingerprint：
`E6C9526BCD3AF489AD0709283204FFFC6D1D297D4C60F36E6DFABC2DCE4A2A78`。
构建日志前缀：`20260910T054013946Z-a2ce058eceb54fea8ad31575b66d0cec`。
导出日志：`artifacts/detail-player-order-native-20260910.log`，退出 0，0 errors / 0 warnings。

```text
ALS_STOP_GRAPH_OK graphs=51 plant_evaluators=12 assets_saved=0
ALS_DETAIL_GRAPH_OK players=16 assets_saved=0
```

删除新字段后的结构化比较证明新旧图完全一致；仅新增 16 个编译索引字段。
更新前文件保留为 `artifacts/detail-graph-before-player-order-20260910.json`。
当前 `assets/config/v4_locomotion_detail_graph.json` SHA256：
`983B6FC513AC3021F1877ED5C3FA38DBC1E52EB16B27972337C67BA5B2541F58`。
仍为无 BOM UTF-8。未保存 UE 资产，未复制 DLL，未修改引擎。
未执行 GUI 重启、项目数据验证或打包，不将本导出流程作为插件发布验收。

## 验证结果

- Core Locomotion 356/356，新状态机相关测试 13 项。覆盖首次/Conduit、每次转换上限、
  0.1 秒严格边界、清理前更新、带权重重入、遍历间断、同 serial 和零权重、无相关
  播放器、严格最大权重及同权重顺序、候选复制、无分配和非法输入。
- Import 全量 592/592。新增编译索引完整性、重排后的独立相关性顺序，以及
  30/60/120 Hz 真实编译规则驱动的状态转换测试。
- Godot 构建 0 warnings / 0 errors。
- 新增 `detail_machine_smoke.tscn`：真实 Detail 动画与基准经过状态姿势合成及下游
  惯性化，覆盖六个内容状态、普通/惯性转换、重新相关、38 个惯性化请求；逐层姿势和
  曲线对照、原地输出、无效时间失败前不改输出及候选重试通过。
- 此 smoke 的播放器时间来自明确标记的测试用非同步推进器，消费初始化/更新信号，
  并回传缓存权重和时间。它不是新的生产时钟，更不是 UE Sync 等价性证明。
- 预热后每个频率 1000 次状态更新、真实采样、曲线合成、历史复制/求值为 0 B 托管分配。

```text
DETAIL_MACHINE_OK rates=30,60,120 states=6 bone_checks=71400 retries=1050 requests=38 resets=6 correction=0.064214 alloc=0B sync=test_feed demo=not_connected
```

最终分离历史/当前权重后的日志：`artifacts/detail-machine-weight-buffers-final-20260910.log`。
原有 Standing Cycle、Stop Plant、Detail Pose 回归通过，摘要与上一批相同；日志分别为
`artifacts/detail-machine-cycle-regression-20260910.log`、
`artifacts/detail-machine-stop-regression-20260910.log`、
`artifacts/detail-machine-pose-regression-20260910.log`。
Camera/Input 文件哈希不变。未跑全量 Core/P5A/P7，未重新录制可玩 Demo 多帧截图。

## 尚未完成

状态更新依据是源码和原生编译数据，本批没有直接运行整张 UE 状态机生成逐帧轨迹。
上一批 900 帧惯性化原生节点对照只证明那个组件，不能冒充本批完整状态机验证。
Initialize/ClearWeight 信号及 InertializationSync 上下文仍需正式接入 P5A 事务和播放器。

现有 `AlsSyncRuntime` 要求 Loop=1，且依赖双脚成对标记，不能用于 Detail 的非循环
Run Start / Pivot 1 / Pivot 2 组。下一步扩展该统一 Sync 路径，核对非循环时间、
Leader 选择/切换/加入和惯性化同步语义，配套原生运行轨迹，不在 Detail 内再建调度器。
随后将 Detail、ShouldMove、Not Moving / Moving / Stop / Plant 接入实际 Demo，完成
P5A 源身份/事件/回滚事务，再继续动态 Layering 和原定 P5B/P5C/P6/P7。
本批仍没有改变可玩 Demo 输出，不能宣称起步滑步和交错步已经修复。
