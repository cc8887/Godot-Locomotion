# Refactored 武器状态机连续更新

## 本批实现

在 `D:/GodotALS` 的 `main` 将已编译的四武器规则、baked 拓扑、Aim 曲线和 QuickFeet 接入共享状态机执行器。

- `AlsOverlayStateMachine` 增加独立机器构造入口，不需要伪造旧 V4 外层五机器图。新旧规则通过显式输入入口区分，混合规则域或用旧输入驱动新图会拒绝。
- `AlsRefactoredWeaponMachineProfile` 冻结每条边的原始规则、compiled 身份、出口顺序、曲线、混合时长和通知局部索引。Bow/Pistol/Rifle 枚举显式映射，拒绝需要执行但未支持的状态/机器回调。
- `AlsRefactoredWeaponMachineRuntime` 持有每角色独立历史，提供 Prepare、ValidateCommit、Commit、Cancel。候选包含初始化/重入、状态更新权重、过渡栈、过渡步骤和通知，不提前执行游戏逻辑。
- 姿态求值仍需按有序混合栈处理；CandidateBoneWeight 仅是诊断贡献，不能代替有序姿态混合。

## 原生源码核对

本机 `D:/UnrealEngine/Engine/Source/Runtime/Engine/Private/Animation/AnimNode_StateMachine.cpp` 的 Update、SetState 和 TransitionToState：

1. 根据动画遍历计数判断重新相关；帧编号跳跃本身不一定重置。
2. 用当前状态时间判断过渡，同帧最多三次；每次切状态重置状态时间。
3. 首次更新完成条件遍历后清除活动混合，且不发本批所需的 transition-start 通知。
4. 推进全部过渡，再按活动状态去重更新；最后清理完成过渡并增加状态时间。
5. 重入仍有权重的状态时不强制初始化；四机器均未启用 AlwaysResetOnEntry。

`AlphaBlend.cpp` 的 Custom 分支明确将曲线结果限制到 [0,1]。原始 Aim_Out 的轻微超调仍保存在资源中，执行器按原生边界限制，不修改曲线键。

共享执行算法与过渡栈未另写一套；本批扩展其规则入口和资源归属。

## 验证

- 新增 10 项测试全部通过。
- 四武器各运行 30/60/120 Hz，共 3,600 个连续压力帧；每帧取消并重新 Prepare，检查完整活动栈、状态时钟、entry/update/notify/transition 候选一致，79 骨贡献和为 1，状态更新不重复。
- 独立定点检查首帧两次切换/混合丢弃/通知抑制；恰好 3 秒的门控与出口优先级；单帧 Ready→Relaxed→Ready→Aiming 的三次切换和通知顺序；QuickFeet 仅作用于指定退回边；Bow 独有时长；重入不重置；零权重更新；遍历计数回绕/跳跃；错误提交/帧/规则域拒绝。
- Import 相关新旧 Overlay 回归 **129 通过、0 失败、0 跳过**。
- Core 过渡栈回归 **15 通过、0 失败、0 跳过**。
- Godot 项目 Optimize 构建 **0 警告、0 错误**。
- 产物：`artifacts/refactored-weapon-runtime/weapon-runtime.trx`、`weapon-shared.trx`、`transition-stack.trx`。本批无编译/测试失败。

上述连续检查是托管事务和状态行为测试，尚未新增四武器实际 UE 连续轨迹 oracle。未启动 UE/Godot、未改插件或重导资源，未运行全量测试、十分钟性能或打包验收。

## 仍未完成

下一步需要实际 UE 连续状态/权重/通知对照、通知局部索引到生成类事件的绑定与原始 EventGraph 消费，再接 state 内部源更新和完整武器姿态。当前候选通知不是已执行的通知；本批也未更新 Sequence 时钟。

完整 Overlay 姿态仍为 9/13，普通 Demo 尚未切换到新 Refactored 图。实际移动状态机、统一宿主、Notify/root motion、Ragdoll/Flail/Get-up 整体验收及其他既有缺口继续保留。用户工作区修改未纳入本批，道具物理、音频和头颈拉伸诊断仍暂缓。
