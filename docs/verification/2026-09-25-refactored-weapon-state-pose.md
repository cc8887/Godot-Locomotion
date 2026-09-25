# 四武器状态与过渡栈姿态

本批在主目录 main 接续 `dddaa65`，未改普通 Demo 或 UE 导出插件。

## 实现

`AlsRefactoredWeaponSourceRuntime` 将本帧更新实际得到的 alpha、MultiWay 权重和节点更新集合保留给求值阶段。Commit/Cancel 后不可读取，不重复推进插值历史。

`AlsRefactoredWeaponStatePose` 在原 79 骨厘米布局中组合 evaluator、真实共享播放器、TwoWay、MultiWay、local additive、mesh additive。Rifle 的三段 Arms 为绝对姿态，Idle 为 local additive；两者不能混用。零 MultiWay 权重返回 reference pose 和空曲线。每个采样器独占暂存，输出在完整采样成功后发布，非法帧/owner/布局拒绝。

`AlsRefactoredWeaponMachinePose` 按原过渡栈顺序累积骨骼，再归一化四元数；曲线用全身过渡 alpha。QuickFeet 分支接到原资源的每骨权重，不用诊断用的最终 state contribution 代替顺序混合。机器与源更新必须是同一 owner。

原武器 RichCurve 启用 float 控制点与 de Casteljau 求值路径，旧 `AlsMovementInputCurve` 默认 double 路径保持。`AlsTransitionStack` Cubic 改为先计算 A²/A³，再乘系数，符合本机 UE `FMath::CubicInterp` 运算顺序。

## 精度与失败证据

本机源码依据：`Engine/Public/Curves/CurveEvaluation.h`、`Engine/Private/Curves/CurveEvaluation.cpp`、`Engine/Private/Animation/AnimNode_StateMachine.cpp` 和 `Core/Public/Math/UnrealMathUtility.h`。Engine.Shared.rsp 实际为 `/fp:fast`。

- 单状态先验测试 `state-pose.trx`：12 通过。保留 P≤1e-10 cm、Q/S≤1e-12、curve≤2e-6 的严格预算。
- 将同一严格预算用于过渡后的 `machine-pose.trx`：12 通过、12 失败。进一步定位标量曲线 double/float 与 Cubic 顺序差异。
- `native-curve.trx`、`alpha-diagnostic.trx`、`alpha-cubic.trx`、`alpha-fma.trx`、`alpha-control-fma.trx` 保留调查过程。尝试 FMA 后个别帧改善但未全等；最终未保留对特定编译器的 FMA 猜测。
- 明确调整**新增完整过渡测试**预算为 P≤1e-4 cm（1 微米）、Q/S≤2e-6，curve/alpha≤2e-6。理由是现有 float 过渡权重误差会传播到 double 骨骼姿态。不是逐位或全精度复刻；编译器具体舍入路径仍未完全匹配。单状态严格预算不变。
- `pose-budget.trx`：24 通过。完整机器 12 组、3684 帧、291036 个骨骼姿态；最大位置差 1.5779676e-5 cm、旋转分量差 1.2379378e-7、缩放差 5.9604645e-8、曲线差 1.7881394e-7。
- 增加覆盖断言的 `related.trx`：100 通过、24 失败，原因是要求 QuickFeet 活跃求值帧但原数据为 0。直接检查旧/新 native traces 均只保留 edges 0/1/3/4/5。没有修改导出数据或伪称覆盖；最终测试明确断言并报告 quick=0，该验证缺口留待新增原生场景。

## 验证范围与下一步

最终相关 Import 回归 `artifacts/refactored-weapon-state-pose/related-final.trx`：124 通过、0 失败。新增 24 个测试覆盖四武器×三帧率的单状态及完整机器，包含取消重试、非法帧不改输出、非法 pitch 和错误机器 owner 拒绝。完整机器中 704 帧有多个活动过渡，最大 alpha 差 1.7881394e-7。Core 相关回归 `core.trx`：47 通过。Godot Optimize 构建 0 warning、0 error。

现有连续数据没有完整动作隐藏/再进入，也没有保留下 QuickFeet 过渡的求值帧。下一步需补这些真实 UE 场景，同时实现外层 Action 图；本批仍不能将完整 Overlay 进度从 9/13 改为 13/13。

无新 UE 启动/导出、Godot 场景验证、全量测试、性能或打包验收。普通 Demo 尚未切到完整 Refactored 链，Ragdoll/Get-up/Pose Recovery 整体验收未完成。真实 Locomotion、Turn/dynamic transition 调用端、统一宿主、Mantle gameplay、相机和十分钟性能等既有缺口保留；音频、道具物理和头颈诊断继续暂缓。用户已有改动保持不动。
