# Ragdoll 退出决策与恢复姿态候选

工作目录 .，分支 main。承接 Ragdoll 退出/Get-up，不处理用户暂缓的道具物理与头颈问题；用户 P4 修改和之前诊断文件保留。

## 本批完成

对照本机 Plugins/ALS/Source/ALS/Private/AlsCharacter_Actions.cpp 的 StopRagdollingImplementation，以及 Engine/Source/Runtime/Core/Private/Math/UnrealMath.cpp 的 FQuat4d::Rotator：以 native world 骨盆旋转求 yaw/roll，roll <= 0 为朝上，朝上时 actor yaw 减 180 度；两个奇异分支将 roll 设为 0。不使用 Godot Euler 分解。落地选择起身，空中保持完整 native 骨盆速度，单位 cm/s。纯值决策不修改物理或动画。

Simulation 新增 PrepareRecovery：仅在角色活动、动画已提交、同一物理 owner 且至少完成一次物理步时，取得当前 committed animation 非物理局部姿态，以拟恢复的 Skeleton world 变换反算物理骨骼局部姿态，按实际物理骨骼顺序构造不可变 NamedPoseSnapshot。名称来自现有 Ragdoll 图配置。它不重 Seed、不推进 Flail、不修改场景骨架/胶囊/岛状态/限速计数。候选携带激活身份、动画身份、物理步数；后续物理步使候选过期，调用者提交前必须再校验。

这是 **退出交接的准备接口**，普通 gameplay 尚未调用；没有关闭物理、启动起身、恢复碰撞或解锁输入。传入的 grounded 与恢复 mesh transform 仍由后续退出 owner 提供，尚未将落地查询、朝向和 mesh 默认相对变换组合成实际退出事务。不能将本批视为完整起身闭环或 Pose Recovery 验收。

## 测试发现与修复

首次 Single30 的新回归索引越界。随后添加边界诊断，明确是旧 smoke 使用 Godot FindBone 的大小写敏感查询，遗漏了导出逻辑名称中的物理骨骼。已改为大小写不敏感字典，并断言映射覆盖整个实际 Skeleton、非物理分支检查数量非零。它也补强了旧物理显示回归的非物理骨骼覆盖；不能将旧日志当作这项补强前已经完整覆盖的证据。

失败日志保留：artifacts/ragdoll-recovery-candidate-single30.log、detail30.log、mapping30.log。生产 capture 未因此改变物理公式。首次 Core 定向编译因测试命名空间 Math 解析冲突失败，显式 System.Math 后通过。

## 最终验证

- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：零警告/错误。
- Core Release 固定 JIT/串行、既定排除 AlsP5aGoldenTests 与 AlsP5aTraceSchemaTests：2911 通过，包含新增 7 项方向/地面空中/奇异点/零 roll 边界/非法输入测试。TRX：artifacts/ragdoll-recovery-tests/core.trx。
- Import Release 定向 Ragdoll/AnimatedJoint：19 通过，TRX：artifacts/ragdoll-recovery-tests/import-focused.trx。未跑 Import 全量。
- artifacts/ragdoll-recovery-candidate-fixed30.log：Single30，翻滚中 G 进入，60 动画/60 物理步，16 recovery checks。
- artifacts/ragdoll-recovery-candidate-parallel120.log：Parallel120，240/240，16 checks。
- artifacts/ragdoll-recovery-candidate-retry60.log：Parallel60，首次 Ragdoll 动画故障重试与暂停三回调，120/121，16 checks；唯一 worker_evaluate 是主动注入。

三组验证在不同倒伏时刻构造地面/空中候选，故意改变恢复 component 朝向及位置，再通过现有 NamedPoseSnapshotRuntime 重建物理身体：世界位置误差要求 < 0.0001 m、旋转 Basis 近似相等，非物理局部 TRS 保留；错误身份/奇异变换拒绝、候选随物理步过期、准备前后物理及动画数据不变。既有实际显示最大世界误差分别为 9.702933e-7、4.3213367e-7、9.61096e-7 m。这些最大值属于显示断言，不是新快照的最大误差统计。

未新增 UE 构建/运行时 oracle，也未进行起身渲染或完整重复进出测试；方向公式仅有本机源码对照与 C# 测试。仍为单机范围。用户 P4 SHA256：78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100。

## 下一连接点

1. 本地 manifest 已有 Get-up 动画/Montage，但 p5a_animation_runtime、v4_action_montage_inputs 和 action notify 配置目前只接 Roll。补齐实际前后起身选择、原生 Montage 生命周期和 Notify 数据，不凭经验填写资产参数。
2. 在 Main 已提交边界组合落地查询/恢复 actor yaw/mesh 默认相对变换，准备快照；配置完整后才释放物理、恢复 capsule collision，空中继承速度。重置 kinematic 身体历史、物理显示步数等每次激活状态。
3. 把不可变快照交给原有 root/Ragdoll 图，并在退出混合仍访问 Ragdoll 分支期间保留。起身走现有共享 Montage/Notify/Root Motion 管线，处理输入锁、结束和被再次 Ragdoll 打断。
4. 验证朝上/朝下、空中退出、重复进入退出、失败重试/停用与真实多帧起身观感。

G 仍只进入 Ragdoll。自动触发、完整 Camera、Mantle、十分钟性能预算和旧静态 9/12、Flail 0/3等未完成项不因本批而关闭。
