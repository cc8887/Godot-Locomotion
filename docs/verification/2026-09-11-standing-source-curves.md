# Standing 来源曲线缓存与实际消费

日期：2026-09-11，第六十八批。工作区 `ARCHIVED_P5A_WORKTREE_PATH`。

## 变化与依据

此前站姿骨骼经过八个内部缓存，但曲线使用动画最终总权重直接展开求和。现在
动画来源曲线随骨骼写入同一 SaveCachedPose 载荷；Forward/Sprint 别名读取
同时复用二者，不重复推进来源。Curve ID 按每个动画独立绑定，共四个实际来源
曲线名称；不假定同名曲线在不同动画有相同数值 ID。

新 Core `AlsStandingCycleCurves` 区分三种语义：

- BlendSpace 与 MultiWayBlend 按输入顺序加权覆盖、累加。
- BlendList 的双路路径与 Mask TwoWayBlend 使用曲线 Lerp，保留相关性边界和
  `a + alpha * (b - a)` 的实际运算顺序；Mask 保留两次 `1 - alpha` 的重算。
- 状态机每层过渡用 Override/Accumulate，曲线使用普通 alpha，不使用腿部
  BlendProfile。不存在的曲线与存在但值为零的曲线不同；不相关目标不引入名字。

本机 UE 依据：`AnimationRuntime.cpp` 的 BlendCurves 和 BlendTwoPosesTogetherInPlace，
`AnimCurveTypes.h` 的 Lerp/Override/Accumulate，`AnimNode_StateMachine.cpp`
的曲线过渡。读取了 MultiWayBlend 与 ModifyCurve 节点；没有修改 UE 插件、
执行新的 UE 探针或重新导出资产。

Godot 的 `AlsCyclePoseSampler` 按实际样本顺序求曲线并同步保存候选/提交曲线。
`AlsCycleDetailGraph` 的已更新 Cycle 和 Main 的 Cycle Save 读取该载荷，而不是
在骨骼求完后另行展开总权重。该链进入实际 Standing 控制器；完整 Main/Demo
仍有独立的后续接线工作，不能把 Main 测试夹具当成最终玩法入口。

## 验证

- 新 Core 专项 Debug/Release 各 11/11，覆盖名称存在性、相关性边界、Lerp
  浮点顺序、非相关方向、普通身体过渡贡献及非法输入。Core 常规 1939/1939，
  仍排除 P5A golden/trace schema；Import 全套 1178/1178。
- 真实资产 30/60/120 Hz 共 1260 帧，覆盖六方向，5040 次来源曲线数值检查。
  对照使用相同受控方向/权重下的旧加权参考，不能当作 UE 完整 AnimBP 真值。
  36 个分支案例继续通过，7315 次输入采样、8575 次缓存求值。
- 六次故障恢复同时检查骨骼、已提交曲线和重试曲线，逐值一致。每频率 200 次
  活动缓存求值测得 0 B，包含本批新增曲线采样/混合。
- Standing/Sprint/Detail/Pivot 长回放通过：九次换髋、371 个等待帧保持；Main
  六缓存与旧入口各 1680 帧，六缓存入口 2360 次原始姿势检查、故障重试通过。
- Worker 单/并行各 180 帧、十个来源事件，结果摘要均为 `21E164D829153157`，
  完整骨骼摘要均为 `CF9225D4DE9B2C8B`。结果摘要随曲线运算变化，骨骼摘要
  保持前批；没有强行固定旧结果摘要。晚期事件/整体事务回滚通过，泄漏回调零。
- Godot 优化构建零警告、零错误，涉及文件空白检查通过。

TRX 位于 `artifacts/test-results/standing-source-curves/`。日志为同目录上层
`standing-source-curves-cache-final.log`、`standing-source-curves-first-cycle.log`、
`standing-source-curves-first-main.log`、`standing-source-curves-main-legacy.log`、
`standing-source-curves-worker-single.log`、`standing-source-curves-worker-parallel.log`、
`standing-source-curves-rollback.log`。

保留首错：`curves.trx` 为新专项 10/11，浮点测试写错了第二个操作数，改成
原本要验证的相消输入后通过；没有修改运行时算式迎合测试。另保留
`standing-source-curves-cache.log`，初次 Feet_Position 对照失败是夹具替换
Transitions/SprintBlend 后未同步更新旧参考使用的 BodyStateWeights/SprintWeight。
补齐同一受控输入后通过，没有放宽误差阈值或修改动画数据。

## 尚未完成

本批只完成来源曲线缓存及更新后消费，以下工作继续保留：

1. 六方向源图在 MultiWayBlend 之后写 `YawOffset`，分别读取 FYaw、BYaw、
   LYaw、RYaw。站姿当前尚未消费这些写入；已有导出脚本可定位两个 CurveVector，
   仍需核对正式数据及其更新函数、每状态输入历史，不能用零默认值宣称接通。
2. 各方向节点 DesiredAlphas 等输入仍未独立保存；首次 Update 前的空样本、
   重初始化保留控制值与完整初始化/骨骼计数传播仍缺。零方向权重 UnitX 回退
   尚需核对。旧未求姿势查询和未更新入口保留旧直接曲线路径，未伪装成缓存读取。
3. 来源缓存有 presence 语义，外层 Detail/Standing 等部分组合仍把输出名称
   物化为存在；最终完整图的缺失曲线传播还需继续核对。
4. 完整 Main Movement、真实 Slot/Montage、主图外最终惯性化、统一 Demo 提交，
   最终 Layering/Add/LS/Lean/YawOffset 与完整 Foot IK/Lock/pelvis，以及原
   P5A-P7 的事件/动作、Overlay/道具、攀爬翻滚、布娃娃恢复、相机和性能验收。

没有新移动截图、平台路线、短矩阵或十分钟采样；平台脚锁和既有性能失败保持
未关闭。未宣告起步滑步、换髋、交错步或上身视觉修复。音频继续暂缓，未改变
已确认键鼠，也没有提交、回滚或合并工作区。
