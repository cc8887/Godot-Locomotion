# 完整蹲姿 Cycles 运行图

日期：2026-09-11。第五十二批。工作区 `ARCHIVED_P5A_WORKTREE_PATH`。

## 本批完成

此前 Stride、方向混合、Diagonal 与 Lean 由测试脚本手动拼接。本批新增可复用的
完整 Cycles 定义、候选更新器和 Godot 姿势图，明确区分外部来源初始化、内部
状态机重入和 SaveCachedPose 生命周期。尚未进入实际 Demo 的外层 Main 路径。

### 严格编译

`src/Als.Import/Compilation/AlsCrouchingCycleCompiler.cs` 复用已有方向/Stride/
Diagonal/Lean 编译器，并核验整个内容图的闭合性、compiled identity、单状态
Cycles 机的 baked/editor 所有权，以及八个来源与绑定表的一致性。

实际初始化顺序为 WalkPose 54、六个外部输入 `[49,50,55,51,52,53]`、Lean 48。
更新顺序为相关的 WalkPose、方向层自身完成缓存更新、Lean。ApplyAdditive 使用
Float alpha=1；虽然未启用的 AlphaCurveName 引脚写着 Weight_Gait，它不参与此
节点的实际 alpha。未知额外节点、外部链接、回调、其他 alpha/LOD/状态策略拒绝。

### 候选更新

`src/Als.Core/Locomotion/AlsCrouchingCycleRuntime.cs`：

- 单状态 Cycles 机复用 `AlsGroundedStateMachine`，新增枚举尾项，不改变旧值。
- 整个 Cycles 初始化/相关性重入时，初始化全部八个来源，包括当前不相关的六方向。
- 单独 Stride 方向分支重入只触发方向机重入，不重置六个外部动画来源。
- Stride 的历史随整个 Cycles 初始化重置，普通方向停用期间仍继续滤波。
- 继续按局部相关性提交，保留零全局权重上下文；Lean 使用整个 Cycles 权重。
- 来源初始化和更新由候选 sink 接收，运行时不推进动画时间或提交玩法回调。
- 状态绑定角色、代际和帧身份，拒绝跨角色/旧代际或身份与历史帧不符的输入。

### 姿势与曲线

`src/Als.Godot/Animation/AlsCrouchingCyclePoseGraph.cs` 实际执行：

`WalkPose / cached directions -> Stride -> component-space Diagonal -> local additive Lean`

使用已有 `AlsPoseCacheEvaluation` 的候选/已提交双实例、初始化与骨骼计数、求值
作用域。同一来源多次读取只采样一次；曲线与骨骼属于同一缓存载荷。初始化只走
相应状态，骨骼缓存按前帧相关状态、新初始化状态及求值所需状态访问，不无条件
把六个写入节点都当成初始状态的输入。

LinkedInputPose 的 Initialize/CacheBones 回调不转发至外部动画；外部输入绑定
在构造时验证名称和父链，来源初始化由 Cycles owner 处理。计数由外层调用方传入，
不是从局部帧号猜测全引擎的初始化/骨骼遍历计数。最终曲线按名称对应，各动画使用
自己的 CurveId，不假定不同动画中相同数值 ID 是同一曲线。

失败候选不能提交。相同前帧可重试；commit 只交换本组件缓存，完整 Worker/P5
事务由后续生产 owner 统一协调。本组件没有设置 Skeleton 或新增动画时钟。

## 本机源码依据

UE 根目录：`${env:UE_ENGINE_ROOT}`，`Engine/Source/Runtime/` 下：

- `Engine/Private/Animation/AnimNode_LinkedInputPose.cpp`：明确不转发 Initialize
  和 CacheBones；由拥有它的 LinkedAnimGraph 遍历所有外部输入。
- `Engine/Private/Animation/AnimNode_LinkedAnimGraph.cpp`：先初始化子图，然后
  遍历所有 InputPoses；更新调用 `UpdateAnimation_WithRoot`。
- `Engine/Private/Animation/AnimInstanceProxy.cpp`：每个 layer root 更新后排空
  自己的 SavedPoseQueue，因此方向缓存在外层 ApplyAdditive 的 Lean 之前更新。
- `AnimGraphRuntime/Private/AnimNodes/AnimNode_TwoWayBlend.cpp`、
  `AnimNode_ApplyAdditive.cpp`：A/B 初始化、相关性及 Base/Additive 先后顺序。
- `Engine/Private/Animation/AnimNode_StateMachine.cpp`：单状态图相关性、初始状态、
  CacheBones 的相关状态门控与求值前保障。
- `Engine/Private/Animation/AnimNode_SaveCachedPose.cpp`：复用前批已验证的缓存
  初始化/骨骼/求值计数规则，不给 LinkedInputPose 增加外部来源重置。
- `Engine/Private/Animation/AnimNode_SequencePlayer.cpp`：初始化执行 exposed
  inputs，零起点且有效速率为负时使用序列末尾；继续使用已有源初始化函数。

本批没有修改或启动 UE 插件，没有新增完整原生 AnimBP 动态逐帧探针。源码核对
与已有公共算法探针不是完整原版最终骨骼等价的证明。

## 实际资源验证

场景 `scenes/tests/crouching_cycle_smoke.tscn`。日志与 TRX 位于
`artifacts/test-results/crouching-cycles/`。

新场景使用正式绑定、实际动画库、既有 `AlsCycleSyncFrame` 载体和共享 Sync/P5
入口。初始化更新 epoch/时间并清缓存权重，Lean 按原生网格返回顺序提交相关样本，
不固定提交五个等权样本。输入及外层 owner 仍为组件夹具，不是生产 Controller。

| 检查 | 最终结果 |
| --- | --- |
| 30/60/120 Hz，各六秒，模拟整图停用四帧 | 1248 个实际更新帧 |
| 整个 Cycles 初始化 | 6 次，含反向播放初始化 |
| 方向分支 | 660 帧参与，588 帧停用 |
| 实际方向缓存来源求值 | 2385 次，每帧不超过 6 次 |
| 本子图全部输出曲线 | 3 条，共 3744 次逐值对照 |
| 来源通知 | 5 个；身份与重试结果相同 |
| 越界采样故障注入 | 3 次拒绝提交，恢复后骨骼/曲线与原候选相同 |
| 活动姿势及曲线求值 | 预热后 100 次重复候选，30/60/120 Hz 均 0 B |

直接对照分支使用相同真实来源时间，独立采样全部六方向后按既有组件顺序组合，
与缓存执行路径逐骨骼/逐曲线精确比较。这证明整链接线与候选缓存一致，不等于
已与 UE 完整图逐帧最终姿势比较。全图曲线 presence、UE 自定义属性及最终角色
曲线写回仍要在生产集成中继续核对，本表不指所有 ALS 曲线。

其他验证：

- 新增 `AlsCrouchingCycleTests` 26 项，随 Import 最终全套 1073/1073 通过。
- Core 常规 1882/1882，沿用排除 P5A golden/trace schema 两组的原命令。
- 蹲姿/Lean 相关 Release 最终 202/202；Godot 最终构建零警告/错误。
- Standing/Pivot 各 5040 帧，Detail 1890，Sprint 1260；Main 1050 来源帧、420
  混合帧、104 中断帧；前批蹲姿 420 帧回归通过。
- Worker 单/多线程各 180 帧，10 个来源事件，首帧 25；两模式晚期来源事件失败
  回滚无泄漏，注入异常为预期诊断。
- 生产摘要未变：result `A9DF0647AFC3574C`、full pose `04D4A5651B87E0E4`、
  pose `2DED5435A66BCAEC`、root `309E8D0E0BEEB2CB`。

## 保留的失败

1. 新构造函数把骨名字典当作整数键索引，CS1503；改成名字到 Godot 索引的显式映射。
2. `cycles-first.log`：同名曲线的跨动画 ID 假设被真实资源拦下。按动画分别映射，
   对应已有导入器 `AlsAnimationSetCompiler` 使用每个动画的 curveIndex。
3. `cycles-second.log` 与 `cycles-coverage-diagnostic.log`：测试 `quarter % 8 switch`
   缺少括号，1248 帧全部停用方向，缓存/事件为零；修正为 `(quarter % 8) switch`。
4. `cycles-active.log`：方向姿势覆盖通过但事件为零。原始 Footstep 阈值为 0.3，
   该夹具最大来源权重只有 0.65 * 0.4 = 0.26。增加交替单方向输入覆盖高权重场景，
   不修改原始通知阈值。`cycles-events.log` 随后通过；再加入反向初始化与失败
   候选测试，最终 `cycles-identity-final.log` 通过。
5. 身份测试最初把 int character 传给 uint 参数，CS1503；修正测试类型后最终全套
   与 Release 复跑通过。未放宽数值、分配或事件守卫。

## 下一步

继续蹲姿主状态的 Idle/Rotate/Stop、Turn Slot、曲线覆盖与两腿 Stop 组合，接外层
缓存、Main 及生产来源收集/提交。不能把此处可复用 Cycles 组件当成 Demo 已接通。
之后闭合最终曲线/动态分层及原 P5A-P7，Standing 已知 WeightFactor/零速度回退
边界仍在清单中。没有新移动截图、全套 P4 或最终十分钟性能采样，不关闭滑步、
交错步或上身人工验收。键鼠与原始资产未修改，音频暂缓；未 commit/revert/merge。
