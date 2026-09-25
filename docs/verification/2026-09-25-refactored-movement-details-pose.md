# Movement Details 状态姿态合成

工作目录 `.`，分支 `main`。接在 `c7d0eec` 的 Movement Lean/PoseMoving 外层之后；普通 Demo 入口未改变，用户已有修改保留。

## 实现

- `AlsRefactoredMovementDetailsPose` 冻结16独立播放器的原79骨布局与curve映射；与实际Movement cache profile/catalog绑定。
- Sample要求同帧的机器候选、已捕获真实源时间的source owner、原shared player owner和已求值Movement cache；布局/owner/帧不符拒绝。内部缓存每个状态一次，成功后才复制到调用方输出。
- Walk/Run透传完整Movement；Run Start From Walk、Run Start、First/Second Pivot按原F/B/L/R权重顺序混合各自四个local-additive源，然后ApplyAdditive(alpha=1)。原始曲线按同序混合后与Movement曲线累加。
- 零通道返回无增量，保留Movement姿态和curve，不叠加绝对参考姿态。原ApplyAdditive对子context设置 `bExpectsAdditivePose=true`，所以MultiWay的ResetToRefPose实际走additive identity。
- 普通过渡按原stack顺序Scale/Accumulate，全部边结束才统一归一化旋转；状态重复出现时复用本次求值缓存。惯性化零duration边已在Update清理，姿态输出为目标状态，外层119惯性化节点的历史/请求仍待接入，不能在本层额外普通blend。

本地原生依据：`AnimNode_StateMachine.cpp` Evaluate_AnyThread/EvaluateTransitionStandardBlendInternal，`AnimNode_MultiWayBlend.cpp`，`AnimationRuntime.cpp` BlendPosesTogether，`AnimNodeBase.h` FPoseContext.ResetToRefPose。没有新UE插件改动、构建或导出。

## 验证

- 新4项测试。三Hz各6秒，共1260提交帧/99540输出骨：实际共享Sync时间、全部六状态、多状态过渡、四通道/单通道/零通道、逐帧Cancel并重新Prepare、重复与独立sampler结果一致。
- 实际Movement cache使用原79骨reference基础姿态和持续变化的真实Lean资产。本批未把真实direction source/cache调度接入该测试。
- 单方向测试按shared player实际time直接采样原additive clip，独立验证平移相加、scale乘法、旋转相乘以及curve并集值；避免仅以相同图实现互比。
- 缺少Capture、错误machine owner、错误frame/layout拒绝，输出buffer保持原值；零通道采样数0，Walk/Run无过渡精确透传Movement。
- `initial.trx` 新三Hz3通过；`related.trx` 最终19通过（新4、Details source5、Movement cache4、Details runtime6），位于 `artifacts/refactored-movement-details-pose/`。无失败或门槛放宽。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0警告、0错误。

## 未完成与下一步

这不是新的UE整图pose oracle，也不是普通Godot角色或Ragdoll验收。无Godot场景、多帧截图、全量测试或性能预算验证。

下一步需把外层Movement读者与direction缓存统一调度，保留初始化counter、最大消费者context、skipped messages及惯性化requester；消费ResetPivot/Parent状态，接入119惯性化，再对整个Movement Details链导出UE连续参考。不能去掉原context门禁后串联两个独立Drain冒充原图调度。

Movement cache owner目前仍要求Evaluate后Commit，update-only路径待补；本批采样器不承担角色身份分发/原子提交协调，外层host需验证所有参与者后统一提交。其余Standing/Crouching机器、真实Parent/Notify/root motion/统一宿主，以及Ragdoll/Get-up/Pose Recovery和最终十分钟预算仍未完成。音频、道具物理、头颈排查继续暂缓。
