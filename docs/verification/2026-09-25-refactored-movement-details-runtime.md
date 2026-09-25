# Refactored 起步与 Pivot 连续状态更新

## 实现

`AlsRefactoredMovementDetailsRuntime` 将上一批六状态、十一转换的原资源接入共享 `AlsGroundedStateMachine`。Core 增加独立 RefactoredMovementDetails 输入域，禁止与旧 V4 或方向状态机规则混用；复用原状态初始化、过渡栈、自动时间判断、更新遍历与惯性化逻辑，没有另写时钟或状态算法。

Import 资源冻结十六个原 Sequence 的 property identity、资产路径、原始长度和循环政策。每次 Prepare 接收严格按该原序排列的上轮节点观察快照，逐状态选择 CachedWeight 严格大于当前最大值的玩家。同权重选先到者，所有权重为零时没有相关玩家，即使只剩最小正 float 权重也参与选择。外层 Standing 状态机权重仍由完整图的冻结 Parent 输入提供。

自动退出使用 `Length - Time` 原始累计时间，触发值为 0；不除以 PlayRate，也不以 State.ElapsedSeconds 代替。当前这些原加速源均为非循环，通用 Core 的循环越界判断保持原逻辑。初始化或重新进入会输出清除播放器缓存权重的状态掩码，由未来完整图调度器落实到节点；本 runtime 不擅自修改共享播放器。

候选结果保留初始化序列、清权重掩码、普通有序过渡、每状态更新及 InertializationSync。惯性化请求使用已有 `AlsOverlayInertialRequest` 值类型，包含原机器 property 117、时长、UseBlendMode=true、HermiteCubic；没有每骨配置或自定义曲线。请求只是 enclosing frame 的候选输出，尚未消费到完整姿态惯性化节点。Cancel 后不可查询请求，Commit 才发布本机器的状态历史。

## 原实现依据

本地 UE `Engine/Source/Runtime/Engine/Private/Animation/AnimNode_StateMachine.cpp`：

- `GetRelevantAssetPlayerInterfaceFromState` 按 baked PlayerNodeIndices 遍历，排除 IgnoreForRelevancyTest，严格 `CachedBlendWeight > MaxWeight`；本批原播放器的 Ignore 政策已由 MovementPlayers compiler 验证为 false。
- `FindValidTransition` 使用资产原长度减累计播放时间，以及可选循环 DeltaTimeRecord，计算自动触发调整值。
- `TransitionToState` 以 CrossfadeDuration、BlendProfile、UseBlendMode、BlendMode、CustomBlendCurve 构造请求。
- Update 先更新所有过渡，再按旧序更新未完成过渡的状态，然后清理完成条目，最后更新唯一当前状态。

首次测试错误地要求惯性化切入只有一个状态更新，因此 `initial.trx` 1 失败/5 通过；修正后又错误假设旧状态权重必须为零，`related.trx` 1 失败/25 通过。核对原源码发现：零时长惯性化条目在清理前 QueryAlpha 仍为 0，因此旧普通过渡的状态仍可能以非零权重更新一次。实现本来保留了该顺序，本批未为满足测试而改变共享过渡算法。测试现明确验证旧更新总权重与新状态同步标记。期间一次 xUnit2031 分析器拒绝 Where→Assert.Single 写法，已改成谓词重载。

## 验证

- `artifacts/refactored-movement-details-runtime/final.trx`：26 项通过（新增 runtime 6、既有 details 资源 4、方向 runtime 10、移动方向 native 6）。
- 全部十一条边均被运行覆盖；检查 First Pivot 优先再次 Pivot、Second Pivot 优先自动返回 Run；检查首帧允许转换、每帧最多一次、普通与惯性化区别。
- 自动规则验证所有零权重、同权重顺序、微小正权重、动画结束前一个 float、无关状态持续 100 秒仍不能替代动画结束、重新初始化后的旧时间隔离。
- 30/60/120 Hz 共 630 个提交帧，逐帧 Cancel/重试与独立 owner 一致；包含零 delta、显式 reset、遍历 counter 跳帧重新进入。观察快照使用真实资产长度和受控权重／时间，不是完整源图自行生成的时钟。
- 非法 identity、时间、历史时间、权重、输入、清单长度、重复帧和错误 commit 拒绝，原已提交状态保持。
- `core.trx`：58 项通过，包括新规则域门禁、旧 Grounded/Detail/TransitionStack 行为及已有零分配检查。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 warnings / 0 errors。

没有新增 UE 导出、原生 Movement Details 连续对照、Godot 场景、全量或十分钟性能运行。复跑既有 native 方向测试不能替代本新子图的原生证据。

## 下一步

补齐起步与两次 Pivot 状态内部的姿态／权重图、实际 RefreshStanding/ResetPivot 回调与完整缓存遍历。统一图应从共享播放器的已提交节点数据形成上轮观察，落实 ClearCachedWeightStates、更新顺序及惯性化请求，再做 UE 连续对照；不能仅凭当前受控快照验收起步滑步或急转画面。

Standing/Crouching 主状态与 Stop、实际 Parent/Notify、完整角色宿主仍待推进。普通 Demo 未切换完整 Refactored；Ragdoll/Get-up/Pose Recovery、Mantle、Camera 和最终性能旧缺口不变。用户未提交文件保留；音频、道具物理和头颈诊断继续暂缓。
