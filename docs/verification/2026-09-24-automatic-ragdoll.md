# 自动 Ragdoll 入口

## 实现与原生依据

主目录 `.` 的普通 Demo 现在实际消费两个已有移动状态边界：

- InAir → Grounded，着地前缓存向下速度达到 10 m/s：LandingRagdoll 优先于 LandingRoll。
- Rolling 时 Grounded → InAir：RollingInAir 进入 Ragdoll。

本机 `Plugins/ALS/Source/ALS/Private/AlsCharacter.cpp:504` 的 NotifyLocomotionModeChanged 实现这两个分支；`Public/Settings/AlsRagdollingSettings.h` 默认启用落地触发，阈值 1000 cm/s。既有 Core `AlsMovementActionRules` 已包含这些默认规则，本批接通 host 消费，不修改阈值。

Demo 为 runtime context 提供 World 环境。BodyHistory 已有 Main Lifecycle 在已提交姿态和完成物理历史边界调用 ConsumeRagdollRequest；现在它同时读取 committed result 的 RequiresRagdoll，再复用手动入口创建 simulation。只接受 PublishedFrameId == RuntimeCommittedFrameId，不从尚未成功的 Motor 输入激活。

另修初始限速的速度来源：原生 `AlsCharacter_Actions.cpp:821` 使用 LocomotionState.Velocity，模式切换回调时仍为之前角色 tick 缓存值。Godot 已完成碰撞后，ActualVelocity 的垂直分量可能归零；在自动触发边界，CopyCommittedRagdollEntry 改用同一 committed MovementAction.CachedVelocity。每个身体的初速仍来自物理历史；没有把角色速度赋给所有身体。手动普通入口继续使用当前 committed velocity。

此处实现现有默认配置，不声称已导出所有蓝图配置覆盖，也不声称 UE/Godot 事件时序逐帧一致。Godot 将 owner 创建放在成功动画提交后，下一动画 tick 消费 physics-driven 状态。

## 验证

扩展真实 Demo 测试 `character_ragdoll_recovery_smoke.tscn` 的 `--auto=landing|roll`：入口不用 G；landing 从 8m 高处实际下落碰地，roll 先真正播放 Roll 再将胶囊抬到空中造成离地。退出仍使用普通 G 流程，完成两次自动倒地/起身和一次额外手动空中退出。

| 运行 | 结果 / artifacts 日志 |
| --- | --- |
| Parallel60 高落差 | 两自动入口、两起身完成、空中退出，0 错误；检查 entry 向下速度至少10m/s、初始限速至少1000cm/s；`auto-ragdoll-landing60-final.log` |
| Single30 Roll 离地 | 两自动入口、两 Roll 被 Ragdoll 打断、两起身完成；`auto-ragdoll-roll30.log`（缓存速度修正前首轮） |
| Parallel120 Roll 离地，触发帧 BeforePublish 故障 | 失败帧没有 simulation，保留自动 edge；重试后两完整入口/起身，第一次 Roll 按既有策略 RuntimeFailure 结束一次、第二次由 Ragdoll 结束一次；`auto-ragdoll-roll120-failure-final.log` |
| Parallel60 实际 OpenGL 渲染高落差 | 两入口/起身、0 错误、6 PNG；`auto-ragdoll-landing-rendered60.log` / `auto-ragdoll-landing-captures` |
| 原有 landing 分支隔离回归 | 三档着地、accepted2/busy1/completed1/cancelled1、43 root-motion 帧；`auto-ragdoll-routing60.log` |
| Optimize build | 0 warning / 0 error |

查看渲染图片01/03/06，呈现地面物理姿态、撑起、恢复移动；第一轮自然生成 face-up 分支，第二轮 face-down。不是完整跌落过程逐帧视觉验收或 UE trajectory oracle。

旧 LandingActionSmoke 专门将环境设 null，以继续隔离验证三档路由/优先级；它仍明确报告 routing_only，不能证明实际物理入口。上表新增测试才证明普通自动入口。

故障首轮 `auto-ragdoll-roll120-failure.log` 结束统计失败：测试误期待两次 InterruptedByRagdoll，但已有故障恢复会先终止第一个 Roll。最终分别严格断言一次 RuntimeFailure 和一次 Ragdoll 终止，不放宽为任意原因；失败日志保留。

## 未完成项及边界

Overlay 专用起身仍待接；真实跑下台阶/移动平台边缘、不同地形和更多配置的自动触发尚未覆盖。静态物理9/12、Flail0/3旧稳定性目标未重跑或关闭。没有本批 Core/Import 全量、UE 编译/导出、性能验收；全部后续规划仍有效，目标保持进行中。

头颈拉伸及道具物理按用户要求暂缓。用户 P4 文件及未提交诊断保留；P4 SHA256 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`。
