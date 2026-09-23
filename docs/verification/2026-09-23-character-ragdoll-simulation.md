# 普通角色初态进入动态物理执行对象

主目录 `D:/GodotALS`、main，接续 bf360c2。本批新增 `AlsCharacterRagdollSimulation`，将普通角色的真实激活初态接入 Core 岛、实际 Demo 环境接触和姿态回读。普通 Demo 的 gameplay Ragdoll 触发/胶囊切换尚未启用。

## 资源与执行边界

创建时使用角色当前提交帧、逐身体历史及进入限速，绑定同一个 skeleton/PhysicsAsset。加载既有 joint frame/settings、条件惯量、睡眠、运行时碰撞几何/过滤/接触参数，创建世界坐标岛。注册显式环境子树；拒绝借用另一个动态物理 owner。环境仍由场景更新，角色身体只由 Core 积分；Godot 代理冻结且 layer/mask 均为0。

对象持有并释放场景监听、查询空间、碰撞资源和身体代理。构造失败清理自身资源，不改变普通角色的位置、胶囊或动画状态。求解期间释放由场景锁拒绝，不先将整个 owner 标记为 disposed；求解失败后同一对象仍可重试。

每步通过既有 StepRagdollScene 接入重力、接触、场景 kinematic targets、电机输入及剩余八次限速。步号独立于动画身份，仅成功后递增。测试证明注入 solver Complete 异常后，身体、限速计数和动画身份不变，重试与不中断轨迹逐值相同。该保证限于 solver 事务；代理 Publish 或 scene CommitCapture 在求解成功后抛错的完整回滚仍未实现，不能泛称所有异常均原子回滚。

共享动画入口 StepCharacterAnimation 仅读取既有角色的 committed precise Flail：相同动画帧保持目标继续物理，新帧缺失 Flail 明确拒绝。不创建第二个播放器。独立的 StepHeldAnimation 用于动画保持及诊断；在尚未有 Flail 时使用进入姿态作为初始电机目标。

Capture 返回物理身体重建的逻辑姿态，目前非物理骨骼沿用进入姿态。接完整 Flail/最终显示时还需核对非物理骨骼的当前动画基底，不能把该保持入口当作最终动画验收。

现阶段仍沿用已有物理验收的均一解析材质策略，不支持任意场景材质混合；创建时拒绝不支持的资产材质/重力选项。创建成本尚未纳入性能预算。

## 运行证据

新增 `scenes/tests/character_ragdoll_simulation_smoke.tscn`，启动普通完整 Demo，注入 W 行走，从真实第20帧创建两个相同物理对象。确认所有资产身体与激活候选逐值相等，继承速度非零，环境13身体，总32身体。第一个物理步通过真实角色动画入口；后续普通 locomotion 新帧因无 Flail 被拒，避免误用最终混合姿态。然后仅为此诊断暂停普通角色，两个对象保持入口电机姿态独立运行两秒。

覆盖初始化失败不泄露、主线程约束、后续八次限速、solver 故障且拒绝在求解中释放、重试全轨迹一致、物理姿态有限/旋转单位化、骨盆不穿实际地面、存在接触、正常释放后子节点全部回收及 disposed owner 拒绝推进。

证据目录 `artifacts/character-ragdoll-simulation-20260923/`。`build-verified.log` 优化构建零警告/错误。最终日志为 `single60-verified.log`、`parallel60-verified.log`、`parallel30-verified.log`、`parallel120-verified.log`；分别运行120/120/60/240物理步，每组对比正常与故障重试两条轨迹，要求退出0且无 ERROR。这是短时集成/生命周期检查，不是两模型完整稳定性验收或视觉验收。

首轮 single60.log 因验证节点构造函数过早访问 frame-stage 静态选项，使 ConfigureDemo 被拒；改成配置 Demo 后再设置 Observe 阶段，未放宽生产配置保护。single60-configured 和各 *-final 是中间版本证据，最终以 verified 为准。

## 后续清单

接普通 Ragdoll 触发事务：创建成功后暂停 kinematic history，切换胶囊/移动模式，在下一帧给既有动画图输入 Ragdoll 和实际 pelvis velocity，消费其真实 Flail，回写物理显示/快照，接骨盆、胶囊和相机跟随；退出及停用/销毁也须拥有同一个 simulation 生命周期。本批尚无成功的新 Flail 帧到本对象的完整运行验收，也未将动态姿态显示在普通角色上。

静态9/12、Flail0/3与旧UE退出异常仍保留；Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能目标仍未完成。本批无 Core/Import 算法及 UE 源码更改，不重复其全量或旧矩阵。用户 P4 规划修改保持原哈希，未纳入提交。
