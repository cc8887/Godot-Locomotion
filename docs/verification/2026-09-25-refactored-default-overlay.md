# Refactored Default Overlay 图接入

本批在 `D:/GodotALS` 的 `main` 实现原版 `AB_Als_Default` Overlay 层，接入上一批共享 source-player 调度接口。普通 Demo 尚未改用该图；不是全部 Overlay、完整动画链或 Ragdoll 完成证明。

## 实现与来源

- 编译器读取现有哈希校验 catalog 中实际 AnimBlueprint，核对 14 个编译节点、原始图的双向 pose 连线、5 项父实例属性绑定、固定帧、同步组、混合和相关性政策。原始 Blueprint 的 class 声明和对象内容分开，按图抽取后合并解析；同名节点不会跨图混淆。
- Default_Poses 的帧 0/1/2，分别参与行走、蹲伏与空中分支。SequenceEvaluator 显式帧按 `SamplingFrameRate.AsSeconds` 换算，不能使用 additive base-frame 的 sampled-key 分母。
- Standing/Crouching 权重经 MultiWayBlend 归一化，零总权重返回 reference pose、空曲线。TwoWayBlend 保留相关性阈值；曲线保留 Present/Absent 信息。
- 空中 GroundPredictionAmount 的插值上升速度为 20、下降速度为 5。首次更新直接取目标；空中分支不相关时保持历史，再次相关不隐式重置。规则核对本机 UE `AnimNode_TwoWayBlend.cpp`、`AnimNode_MultiWayBlend.cpp`、`InputScaleBias.cpp` 和 `AnimNode_SequenceEvaluator.cpp`。
- `A_Als_Idle` 按 local additive 权重 0.75 叠加。注意 authored struct 的 Alpha 默认值为 1，真正的图引脚默认值和编译值为 0.75；编译器核对后两者，未把 struct 默认值误当执行值。
- profile 冻结实际骨架、三帧 pose/curve、reference pose、Idle 曲线映射；runtime 只保存角色独占的候选帧和插值历史，源播放由外部共享 owner 管理，可与其他图节点共享 Secondary Motion 同步组。

调用顺序：Overlay Prepare → 收集 SourceInput 至共享 players.Prepare → Overlay Evaluate → 所有参与者 ValidateCommit → 无异常后分别 Commit。任何阶段失败应 Cancel 全部参与者。仅下游 Slot 覆盖 Evaluate 的 update-only 帧允许提交更新历史；source-player 时钟仍须由宿主统一提交。此类不替代最终整角色事务协调器。

## 验证

- 新增 7 项：真实图编译；7 类图变更拒绝；预测插值/隐藏分支；30/60/120 Hz 各两秒（共 420 帧）的共享 Idle、每帧取消重试和重复 Evaluate；重新初始化；错误资源导致 fault 后禁止 Commit；update-only；中间权重及零权重 reference/curve fallback。
- 420 帧中三个地面端点分支核对全部 79 骨与实际固定帧采样加 Idle 的结果，并核对所有曲线；空中连续分支验证候选/提交一致性。中间混合另有解析数值检查。**这不是新增的 UE Default Overlay 整图连续 golden 对照**。
- Release 相关测试最终 **14 通过、0 失败**，包含新 7 项、source-player 2 项、Sync trace 1 项和 PostLayer 4 项。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore -v quiet`：**0 错误、0 警告**。
- 首轮开发存在两个编译错误（误用未暴露的 MeshSpace 属性、向量 Length API），均修正。测试曾因图声明抽取不完整、TwoWayBlend 的 BlendNode 字段、Alpha 引脚覆盖 struct 默认值而失败；修正后通过。失败 TRX 与最终结果保留在 `artifacts/refactored-default-overlay/`，没有放宽数值断言。
- 未变更 UE 插件/资产、未启动 UE 或 Godot、未跑全量测试、未测零分配或十分钟性能。

## 后续

仍需新增 Default Overlay 原生整图连续对照，然后接入其余 Overlay、真实 Locomotion/Standing/Crouching 状态机和回调、统一源身份与 Notify/root motion、完整后处理以及普通角色宿主。不能把本批默认图视作其余十二种 Overlay 已实现。

原有 Ragdoll/Flail 失败项、完整 Get-up/Pose Recovery、普通场景整链视觉验收和十分钟性能预算仍未关闭。音频、道具物理、头颈拉长诊断继续按用户要求暂缓。用户原有未提交改动未纳入本批提交。
