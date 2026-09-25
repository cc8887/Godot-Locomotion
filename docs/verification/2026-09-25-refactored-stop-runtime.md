# Refactored Stop 内层状态与固定帧姿态

本批在 `.`、main 完成；接续 Standing65 外层实现。普通 Demo 未切换。本批不代表停止动作或 Ragdoll 的视觉验收完成。

## 原图与实现

编译现有原始 authored/compiled 资源的 Stop53（machineIndex 3），包括 Entry、Lock Left Foot、Lock Right Foot、Plant Left Foot、Plant Right Foot。交叉校验原状态身份、十二个 evaluator 身份、回调成员、转换连线、规则属性路径、混合策略和通知；拒绝不同资源布局。

四条出口按下表依次判断，输入为原 Parent.FeetState.FootPlantedAmount，不使用旧 V4 的近似脚位判断：

| 优先级 | 条件 | 目标 | 混合时间 |
| --- | --- | --- | --- |
| 0 | amount > 0.5 | Lock Right Foot | 0 |
| 1 | amount <= -0.5 | Lock Left Foot | 0 |
| 2 | amount > 0 | Plant Right Foot | 0.1 秒 |
| 3 | amount <= 0 | Plant Left Foot | 0.1 秒 |

全部为 HermiteCubic 标准混合；不跳过首次混合。各目标无后续出口，因此本次停止已经选中的脚不会随输入抖动再次切换。重新初始化或失去相关性后再进入，才重新选择。进入左/右状态分别生成 PlayStopLeftTransitionAnimation / PlayStopRightTransitionAnimation 候选命令；本批尚不执行实际 Montage/Slot 动作。

Core 新增独立 Stop 规则域并复用共享状态执行器；Import 提供 Prepare/Commit/Cancel。Standing 驱动测试按实际状态源更新和初始化列表调用 Stop，保留外层已转 Idle 但 Stop 仍在淡出的源更新。

新增十二个固定帧 evaluator 的原资源编译与冻结采样。它们共享六条绝对行走动画，不创建推进时钟：

| 动画 | Plant Left 帧号 | Plant Right 帧号 |
| --- | --- | --- |
| Forward | 4 | 21 |
| Backward | 4 | 21 |
| Left Forward | 6 | 24 |
| Left Backward | 4 | 24 |
| Right Forward | 7 | 23 |
| Right Backward | 7 | 21 |

使用原 SamplingFrameRate 将帧号转为秒，再经过 float 时间边界采样 79 骨及曲线。资源层校验骨架一致、原序列、帧号、DoNotSync、teleport/loop 策略和无动态输入；输出复制到调用者缓冲区，调用者写入不会污染共享资源。

## 验证与失败记录

所有测试在主目录执行，测试配置 Release；记录位于 `artifacts/refactored-stop`。

- 新测试共 20 项：资源和浮点相邻边界、8 种状态机资源变异、4 种 evaluator 变异、固定帧采样输出隔离、3Hz 连续运行和外层集成。
- 单机 30/60/120 Hz 共 840 帧，逐帧 Cancel 重试；覆盖四个目标、零外层权重、相关性中断、原 0.1 秒混合和进入回调。
- Standing→Stop 联合 30/60/120 Hz 共 840 帧，验证四轮选脚、回调一次、Stop 淡出源继续更新及取消重试。仅状态更新集成，尚非完整停止姿态图。
- `stop-related.trx`：46 项通过，包含 Stop、Standing、既有 Direction native 与 Movement Details runtime 回归。没有放宽原 native 容差。
- `core-related.trx`：共享状态机、Transition stack 等 39 项通过。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误。

首轮 `stop-runtime.trx` 为 9 通过、3 失败：测试夹具写成 `phase % 4 switch`，实际运算顺序没有生成预期四相位。修为 `(phase % 4) switch`，生产判断未因测试改动；随后 `stop-runtime-second.trx` 12 通过，加入叶子采样后 `stop-evaluators.trx` 17 通过，最终相关 46 通过。失败记录保留。

固定帧比较使用独立调用同一序列 sampler，仅证明帧选择、布局与输出隔离，不能替代新 UE Stop 整图 oracle。本批没有 UE 插件改动、新导出、Godot 场景运行、全量测试或最终性能验证。

## 剩余工作

Stop 的 MultiWay、HipsDirection 枚举混合、原腿部 LayeredBoneBlend 和 FootLock 曲线尚未组合；五个状态的缓存读者也尚未接入外层 cache66 统一调度。继续完成这些后，接停止状态回调与 StopQuick 通知的实际动作播放、外层118惯性化及连续 UE 整图对照，再接普通 Demo。Ragdoll/Get-up、其他完整移植和最终性能目标仍保留；道具物理、音频及头颈专项仍暂缓。

用户已有 project.godot、规划文档、AlsLayerBlendingRuntime 及未跟踪诊断/uid 文件未纳入本批。
