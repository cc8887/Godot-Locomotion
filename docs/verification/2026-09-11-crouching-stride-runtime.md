# Crouching StrideBlend 子图与运行时

日期：2026-09-11；完整性补完第四十八批。

## 结论与范围

本批完成 CLF Cycles 的 StrideBlend 子图编译和可执行组件：固定 WalkPose 与
六方向输出之间的双路混合、滤波、更新相关性、姿势和单条曲线求值。
不是完整 Cycles，也没有把蹲姿或 Main 接入 Demo。没有新的人工移动截图，
不能据此宣布起步滑步、交错步或上身改善。

现有用户修改保留，未 commit/revert/merge；未改 UE 插件、资产、键鼠输入。

## 原生依据

正式 `assets/config/v4_locomotion_source_graph.json` 中：

- 图归属 `BaseLayer.AnimGraphNode_StateMachine_4.(CLF) Locomotion Cycles.AnimStateNode_0.(CLF) Locomotion Cycles`。
- StateResult -> ApplyAdditive.Base -> ComponentToLocal -> ModifyBone -> LocalToComponent -> TwoWayBlend。
- TwoWayBlend.A = SequenceEvaluator 141，对应正式 player 54 / sample 80，WalkPose 显式时间 0。
- TwoWayBlend.B = `(CLF) CycleBlending`，其六来源映射由已有方向编译器验证。
- Float Alpha 读取 `StrideBlend` 动态引脚，不读序列化 alpha 0；Scale/Bias = 1/0。
- `bInterpResult=true`，递增/递减速度 10；不预先 Clamp/Map。
- `bResetChildOnActivation=false`，`bAlwaysUpdateChildren=false`；不因子分支重新相关而重置其播放器。

本机 `D:/UnrealEngine/Engine/Source/Runtime/` 源码：

- `Engine/Private/Animation/InputScaleBias.cpp`：首次更新直接保存输入；后续 FInterpTo；内部历史在最终 alpha 截断之前保存。
- `AnimGraphRuntime/Private/AnimNodes/AnimNode_TwoWayBlend.cpp`：A/B 相关性、A 先 B 后更新；单一相关分支获得完整父更新上下文。
- `Engine/Public/Animation/AnimTypes.h`：相关条件为 `weight > 1e-5`，满权重条件为 `weight >= 1-1e-5`。
- `Engine/Private/Animation/AnimationRuntime.cpp`：先以 `1-alpha` 缩放 A，再以重新计算的 `1-WeightOfPoseOne` 累积 B，最后归一化旋转。
- `Engine/Public/Animation/AnimCurveTypes.h` 与 `Core/Public/Math/UnrealMathUtility.h`：曲线另有阈值分支，使用 `A + Alpha * (B-A)`。

这里是源码级对照，没有新增 UE 执行探针，也不是完整原生 AnimBP 逐帧等价证明。

## 修改

- `src/Als.Core/Locomotion/AlsCrouchingStride.cs`：值类型候选历史，不持有动画时钟；调用方显式初始化。零权重分支不混入姿势，单路保留原始姿势，不额外归一化。输出可与任一完整输入原地重合，拒绝错位重叠。
- `src/Als.Import/Compilation/AlsCrouchingStrideCompiler.cs`：验证实际图路径、A/B 来源、骨架、显式采样时间、动态输入所有者、滤波和生命周期策略。仅编译 stride 子图，不宣称验证下游骨骼控制或 Lean 网格。
- `src/Als.Godot/Animation/CrouchingSourceSmoke.cs`：复用正式 player/sample 身份、实际导入 WalkPose 与方向姿势；追加滤波候选重试、纯分支和曲线组合检查。
- 新增 `AlsCrouchingStrideTests` 19 项、`AlsCrouchingStrideCompilerTests` 16 项，包括 30/60/120 Hz、边界、非法输入、原地缓冲、专用线程零分配及源图变异拒绝。

## 验证

日志和 TRX：`artifacts/test-results/crouching-stride/`。

| 检查 | 结果 |
| --- | --- |
| Core 新专项 | 19/19 |
| Import 新专项 | 16/16 |
| Core 常规，排除原有 P5A golden/schema 两类 | 1882/1882 |
| Import 全套 | 994/994 |
| Core 新专项 Release | 19/19 |
| Import 蹲姿相关 Release | 123/123 |
| Godot 构建 | 0 warning / 0 error |
| 蹲姿实际资源 | 420 帧，7140 次来源姿势采样，30 个通知 |
| Stride 组件 | A-only 202、B-only 94、混合 124，重试一致 |
| 方向组件 | 六状态、30 转换、315 帧中断混合 |
| Standing/Stop/Pivot 实际 Controller | Standing 5040、Pivot 5040、Detail 1890、Sprint 1260 帧 |
| Main 资源组件 | 来源 1050、受控 profile 420、中断 104；零分配 |
| 单/多线程 Worker | 各 180 帧，10 个通知，首次第 25 帧 |
| 双模式 late_source_event | 第 25 帧失败候选回滚，无通知泄漏 |

生产摘要保持：result `A9DF0647AFC3574C`、full pose `04D4A5651B87E0E4`、
pose `2DED5435A66BCAEC`、root `309E8D0E0BEEB2CB`。

没有放宽既有阈值、修改旧 oracle 或重写历史失败记录。本批未执行全套 P4 脚本、
UE 构建/启动或十分钟性能测试；不把这些检查写成通过。

## 未完成及下一步

本批 smoke 使用受控 Stride 输入（含超出 0..1 的值以验证截断次序），各动画
来源仍按原有固定权重走共享 Sync。相关性权重虽然已计算并专项验证，尚未用于
实际缓存遍历、停更/恢复和源初始化；方向机仍独立受组件夹具驱动。

继续原计划，不新增可选范围：

1. 完成 `ik_foot_root` 的原生组件空间缩放与局部恢复；部分 alpha 必须按骨骼控制器局部混合语义验证，不能仅凭数学直觉替代。
2. 补 Lean 原生网格/轴滤波导出。当前 `AlsBlendDefinition` 有轴范围/网格分辨率和五个样本，但没有实际网格权重或轴滤波参数；Grounded 源表也未包含这些字段。不能臆造五点插值。
3. 组合完整 Cycles 的采样、加法基准/曲线、缓存更新和求值生命周期；借用统一 Sync 时间，不另造播放时钟。
4. 接主蹲姿姿势、Main 的生产上游权重和源初始化，再补最终曲线、动态上身与完整脚部约束。
5. 按原 P5A/P5B/P5C/P6/P7 完成通用动作/通知、Overlay/道具、Mantle/Roll/Root Motion、Ragdoll/Get-up/Camera 和最终性能/人工验收；音频仍暂缓。
