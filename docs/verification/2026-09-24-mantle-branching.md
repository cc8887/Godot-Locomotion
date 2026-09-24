# Mantle 原生 Branching State 事务

本批在主目录 main 实现两个实际 ALS 原生状态的运行时绑定：SetLocomotionAction 和 EarlyBlendOut。普通 Demo 尚未接入 Mantle，不能将本批测试当作完整攀爬验收。

## 源码依据与行为

本机 UE `Engine/Source/Runtime/Engine/Private/Animation/AnimMontage.cpp` 的 HandleEvents、UpdateActiveStateBranchingPoints、BranchingPointEventHandler、Advance 和 Terminate，以及 ALS 两个对应 NotifyState 的实现是依据。两个类的构造函数都设置 bIsNativeBranchingPoint；不能用导出事件 tickMode=0 将它们当成普通排队通知。

- 先更新已激活状态，再执行经过的边界标记；区间为 start < position <= end，标记补充精确边界的 Begin/End。
- 整帧运动经过全部边界后，仍活动且未中断的 EarlyBlendOut 才 Tick 一次。单帧跨越完整窗口不会在已经 End 后触发提前停止。
- 四个实际条件为 OR：输入、LocomotionMode、RotationMode、Stance。停止指定物理 Montage 实例，保留实际 BlendOutOption；High 时长 .4，其余 .35 秒。
- 被中断的实例停止通知更新，活动状态到实例终止才 End。动作状态结束仅在当前标签仍匹配时清空，保留外部已设置的其他动作。

## 实现及边界

`AlsMantlingBranchCompiler` 使用实际动画输入 JSON 与已有 profile 的字节哈希绑定，校验六个 Montage 的状态类、身份、顺序、触发偏移和反射 payload。`AlsMantlingBranchingRuntime` 按物理 instance ID 保存活动位与动作标签，加入共享 Montage Begin/ValidateCommit/Commit/Discard/Clear 生命周期；候选失败不会发布状态，重试可重现事件。每个 branching owner 只绑定一个播放 bank。

目前是受限制的两个状态适配：单终止 section、正向播放、互不重叠状态、回调不跳转、PostLocomotion、禁用 Montage root motion、BlendOutTriggerSeconds=0。逐个重放实际边界后做最终 Tick，并非通用 UE 子步调度器。实际攀爬位移仍应由 MantlingRootMotionSource 接管。

没有新增独立 UE 回调 oracle；本批为本地源码推导及真实输入回归，尚不能宣称完整原生时序等价。后续需对照实际回调轨迹，特别是替换、自然结束与中断边界。序列 Footstep 仍未绑定；未来排队通知必须保留原生“收集事件在 Early Tick 前”的顺序，不能直接用 Tick 后的 Interrupted 标记过滤本帧事件。

## 验证

- Core Release Montage 与 branching 定向：154 通过，0 失败。包括精确动作结束/提前窗口起止边界、错误帧重试、已有候选不被重复 Begin 丢弃。
- Import Release Mantling 定向：62 通过，0 失败。六个真实 Montage 各覆盖无条件及四个提前停止条件；还验证中断延迟 End、外部动作标签保留、Discard 与大步长跨窗口。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。
- 结果在 `artifacts/mantle-branch/core-final.trx` 和 `import-final.trx`。早期测试调用不存在的 Stop 方法导致编译失败，改用现有 StopInstance 后通过；没有为通过测试修改容差。
- 无新 UE 插件修改/构建/导出，无新 Godot 场景运行或渲染验收，无全量 Core/Import 测试。

## 后续

原生回调轨迹对照 → 序列通知与资源身份映射 → 实际角色图/slot、障碍探测、移动目标与独立 Mantling motion 时序 → 中断、销毁和 Ragdoll 转换验收。旧物理稳定性 9/12、Flail 0/3、复杂相机与最终十分钟性能预算仍未完成。头颈拉伸、道具物理、音频继续暂缓。用户已有 plan、project.godot、诊断文件及生成 uid 未纳入本批。
