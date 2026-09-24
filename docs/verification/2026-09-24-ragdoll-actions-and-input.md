# Ragdoll 动作中断与手动入口

承接用户“继续完善 ragdoll”，道具物理与头颈拉长诊断暂时搁置。工作在 .；原诊断文件和用户 P4 修改保留，不纳入本次提交。

## 行为

普通 Demo 按 G 进入 Ragdoll，允许在翻滚中切入。输入回调只提交请求，Main Lifecycle 在已提交动画和身体历史准备完毕后创建物理 owner；停用清除未消费请求。已处于 Ragdoll 时重复 G 不重新创建 owner。尚不能起身，HUD 明确注明。

本机 ALS 的 AlsCharacter_Actions.cpp::StartRagdollingImplementation 调用 Montage_Stop(0.2f)。进一步对照 Engine/Private/Animation/AnimInstance.cpp::Montage_StopInternal 及 AnimMontage.h::IsActive/IsStopped：仅停止 desired weight > 0 的实例，采用各 Montage 的 blend-out option，覆盖其 blend-out 时间；已经淡出的实例保持原有淡出。

实现以纯值 PhysicsDriven 标记传递到 worker 的 Montage 准备阶段，在下一次物理 Montage tick 前停止活动实例，保持共享时钟、组/槽位和旧淡出；不读取 worker 外的物理岛。保留事务回滚，首次动画帧失败后重试不重复分发动作结果。停止 Root Motion 归属和本帧提取，不再让胶囊消费翻滚位移。

动作逻辑新增 InterruptedByRagdoll=13，旧枚举数值不变；主线程确认一次中断，Rolling 状态随 outcome 清除。Ragdoll 期间新动作请求 RejectedBusy，并记录请求水位，避免同一请求反复拒绝/退出后重放。共享 Flail、骨盆跟随和物理显示仍沿用既有 owner。

## 验证

优化构建零警告/错误。Core Release 固定 JIT/串行、既定排除 AlsP5aGoldenTests 与 AlsP5aTraceSchemaTests：2904通过，TRX 为 artifacts/ragdoll-actions-tests/core-final.trx。新增三项覆盖停止及重试、动态转身跨组/已有淡出保持、停止先于物理 tick。首轮2902通过/1失败是新增枚举尚未更新冻结映射断言；补充=13后最终整批通过。

Import Release 定向 Montage/Ragdoll/BaseLayer：122通过，artifacts/ragdoll-actions-tests/import-focused.trx。本批未重跑 Import 全量。

实际 Godot 日志位于 artifacts/：

- ragdoll-actions-verified-single30.log：Single30，60动画/60物理步，翻滚中按G。
- ragdoll-actions-verified-parallel120.log：Parallel120，240/240步，翻滚中按G。
- ragdoll-actions-verified-entry60.log：Parallel60，120/121步，首次 Ragdoll 动画帧注入失败，重试及暂停三回调恢复。
- ragdoll-actions-verified-rendered60.log：最终真实渲染60Hz、同上故障暂停；额外确认结束时 ActionCount/StateCount均为0。四张截图在 ragdoll-actions-captures-final/，检查倒伏过程。
- ragdoll-actions-ordinary-regression.log：普通 Parallel Roll 替换与故障恢复通过，既有 entry/history/activation 检查通过。

各 Ragdoll 场景验证一次 InterruptedByRagdoll、胶囊额外积分0、RootMotionSource无位移、Rolling inactive；中途再次按G不重启Flail epoch，再按R只拒绝一次；身体世界位置和最终显示误差<1e-6m。渲染HUD的Errors=1来自主动注入，不是额外错误。

过程失败均保留：ragdoll-entry-roll60.log 的地面高度断言误用了站姿胶囊尺寸，已改为实际胶囊；ragdoll-entry-roll60-retry/detail.log 的空中位置逐位相等断言遇到1.4901161e-8m的变换舍入差，现用1e-6m比较。未修改物理求解阈值。没有 verified 前缀的其他日志是停止时序调整前的过程证据。

## 后续与限制

本批实现手动进入及动作中断，尚无退出/Get-up/Pose Recovery；G不是切换键。高落差/翻滚离地自动触发仍待接。单次物理 owner 的构造开销、冷启动和最终十分钟性能预算未验收；截图中的帧率不能作为性能通过证明。完整 Camera、Mantle、静态9/12、Flail0/3等旧未完成项保留。

无新 UE 编译/运行时对照，仅对本机源码规则核对；不声称整条 UE tick/网络流程逐帧等价。用户 P4 文件哈希仍为78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100。
