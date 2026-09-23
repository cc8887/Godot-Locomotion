# 独立 Flail 驱动实际 Core 物理步

新增 `AlsAnimatedJointInputs`，固定绑定一个物理岛并核对身体/关节数量、关节两端次序和动态 pelvis。输入使用原始 authored definition；创建修正后的 connector 仍只用于 solver。每次 Prepare 从岛读取已提交关节目标及真实 pelvis COM 线速度，执行独立 Flail→native locals→启用轴驱动输入，不维护另一份提前推进的 target history。

`AlsCoreJointHost.StepAnimatedScene` 已提供场景入口，核对岛归属后沿用既有 Capture→Step→Publish→CommitCapture。此新场景入口本批仅构建验证，尚无普通 demo 调用；实际积分验证直接调用同一个 Core Island。

`RagdollFrameSmoke --motor-physics` 使用真实 Mannequin 完整身体/关节、创建修正帧及 conditioned inertia。已提交 Flail 驱动真实 Island.Step，下一动画帧用岛的 pelvis 速度计算 Flail 播放速率，下一物理步用其计算刚度。动画隐藏/恢复 snapshot 阶段不积分；这是受控连接测试，不是普通游戏的完整物理生命周期。

## 结果

目录 `artifacts/animated-joint-step-20260923/`，最终 Optimize 构建零错误/警告。私有和共享动画源两模式，30/60/120 Hz，各四 owner 单线程与四 owner 并行均通过。每 owner 分别执行 69/138/276 个真实 Flail 驱动物理步；物理输出有限，成功后各目标已发布到 solver。

并行回放每步先注入接触 Gather 异常，再重试：异常后所有身体和 joint definition 逐值不变，再 Prepare 的 target/K/C 逐值相同。物理姿态和速度全部加入 digest；其结果与不注入失败的单线程回放一致，私有/共享源之间也一致：

- 30 Hz `BCA430C7AB1568084C159B235D4BFA6CC15A6A252473E46B46EFFB338DF21E33`
- 60 Hz `77EABB41AE912D247CB08AF805FFDC25D935D524E9A9098F29312D5DB38A59CF`
- 120 Hz `5E6E4E4B51FE4EA4263C61C203998A4C93530EB7F3244A0EF3351FD3120088B7`

日志 `private-final.log`、`shared-final.log`。这是无重力/无实际接触、无睡眠的整链动画驱动连接测试，不证明 UE 完整轨迹等价、接触落地稳定性、视觉效果或全部模型验收。测试动作时序受控，恢复快照仍来源于既有动画测试，不伪称物理 Get-up。

## 后续

接真实场景与动画驱动的共同接触步，再普通角色初态 Seed、胶囊所有权、骨盆位置跟随、初始限速、物理展示和退出恢复。普通 demo Ragdoll 仍未接通，最近落地矩阵9/12（三项30Hz旧失败）；Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能目标全部保留。本批无新原生导出/UE构建、全量测试或落地矩阵，主目录main，用户P4修改未动。
