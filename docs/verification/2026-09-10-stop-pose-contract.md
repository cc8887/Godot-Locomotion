# Stop Plant 原生数据合同

日期：2026-09-10。范围：完整性补完 A 的起停子项，尚非起停运行时完成。

## 实现范围

- `AlsStopGraphCommandlet` 使用 UE 原生反射与 EdGraph API 导出 27 个图，包括外层
  Locomotion States、Stop 内部状态/规则及 ShouldMoveCheck；不保存或改动 UE 资产。
- 导出私有 folded Node 属性、枚举项、状态通知、转换默认参数和输入引脚连接。
  使用结构化 JSON；不从本地化节点标题或字符串化结构体猜测动画资源。
- `AlsStopPoseProfileCompiler` 编译左右 Plant 子图的实际姿势连接，解析到已导出的
  6 个 Walk 资源、12 个独立采样节点，以及对应骨架的物理骨骼分支和后代掩码。
- 检查不支持的 Sync/动态时间输入、错误骨架/腿/曲线/枚举、缺失资源、重排权重、
  断开的输出连接和重复采样身份，失败时抛出 `ALSSTOP001`，不回退到通用 Idle。

原生文件：`assets/config/v4_stop_graph.json`，300017 字节。
SHA256：`83C3D5FCC86910BB3EFEECE301E34EFEE447E18BC289EC77F14AAB038B3A59C7`。

## 关键源语义

时间为秒，不是归一化相位。顺序为 F、B、LF、LB、RF、RB。

| 状态 | 六个 Walk 采样时间 | 覆盖分支 | 最后曲线写入 |
| --- | --- | --- | --- |
| Plant Left Foot | .133, .133, .200, .133, .233, .233 | ik_foot_l + thigh_l | FootLock_L = 1 |
| Plant Right Foot | .700, .700, .800, .800, .766, .700 | ik_foot_r + thigh_r | FootLock_R = 1 |

Node 属性中的 `explicitTime` 为 0；实际时间在 `ExplicitTime` 输入引脚。
ModifyCurve 的 Node `curveValues` 也为 0，输入引脚 `CurveValues_0` 实际为 1。
BlendList 的 Node 默认混合时间为 .1，但引脚可覆盖为 0。因此读取结构体而忽略
输入引脚，也会产生表面上已移植、行为却错误的结果。

左右 Plant 均用四通道 VelocityBlend 混合；LF/LB 分支只在原生
`TrackedHipsDirection == LB`（值 5）时选择 LB，RF/RB 只在 RB（值 3）时选择 RB。
它不是请求移动方向，也不能直接复用当前 Cycle 的方向到 lateral 权重映射。
左 selector 的进入时间为 0/0，右 selector 为 0/.1，Linear。

覆盖层使用 `bMeshSpaceRotationBlend=true`，分支深度为 0，曲线合成为 Override，
然后 ModifyCurve Blend 写 FootLock。采样器 Teleport + DoNotSync，不应因读取固定姿势
而额外推进时间、抽取 Root Motion 或发出源序列 Notify。

原生外层转换的默认模式实际为 HermiteCubic：NotMoving->Moving .3 秒，
Moving->Stop 0 秒，Stop->NotMoving .3 秒；优先级 2 的 Moving->NotMoving 为 .2 秒，
带 `->N QuickStop ` 事件（原名末尾有空格）。这批仅导出，未执行这些转换。
内部 Pre-Stop 到两条 Conduit 的序列化时长为 .2 秒，Conduit 到 Lock 为 0，
到 Plant 为 .1 秒。已对照本机 UE `AnimNode_StateMachine.cpp` 的
`FindValidTransition`（约 863 行）与 `Update_AnyThread`（约 495 行）：递归进入 Conduit
后保留最终通向内容状态的 TransitionRule，沿途边只追加到 SourceTransitionIndices；
`TransitionToState` 读取该最终边。因此混合时长来自 Lock/Plant 末端边，不是将 .2 秒
与末端时长相加。此结论来自引擎源代码，尚无本批 UE 状态机逐帧轨迹验证。

## 验证

- 按 `ue-diagnosing-plugin-build-load` 执行完整 Editor target 构建及插件产物审计：通过。
  未进行正常编辑器 GUI、打包与全平台认证，不将此视为完整插件发布认证。
- 只读命令 `-run=AlsStopGraph` 返回 0：
  `ALS_STOP_GRAPH_OK graphs=27 plant_evaluators=12 assets_saved=0`。
- `dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --no-restore -v quiet`：541/541，
  包含本批 18 项 Stop 合同测试。
- `dotnet build GodotALS.csproj --no-restore -v quiet`：0 错误、0 警告。
- 现有 `standing_cycle_smoke.tscn`：30/60/120 Hz × 3 起始相位通过，9 次换髋、
  222 等待帧、9 次回滚、热路径 0 B；这验证的是既有 Cycle 路径，不是 Stop 运行时。
- 附加 Core Release 全量回归在运行超过 15 分钟后主动取消（仍在调用 P5A Oracle，
  未返回完整结果），不记为通过。本批没有修改 Core 运行时；先前 Core 通过记录不改写。

## 尚未闭环

本批没有改变 Demo 姿势，不把导出/编译测试当作 Godot 与 UE 的多帧姿势对照。
以下工作仍开放，补完计划 A 不打勾：

1. ShouldMove 的输入来源、外层状态权重门控、QuickStop 和状态重置。
2. Feet_Position 的支撑脚 Lock/Plant 判定、进入事件和 Conduit 执行时序。
3. Mesh Space Plant 姿势在生产图中的独立采样身份、外层混合顺序和 FootLock 消费。
4. P5A 播放/曲线/事件/Sync 的统一事务提交与回滚，不能建立永久独立 Stop 调度器。
5. UE/Godot 同输入不同起始相位、30/60/120 Hz 的状态/权重/时间/最终骨骼对照及截图。
