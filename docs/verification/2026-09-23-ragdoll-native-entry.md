# 原生 Ragdoll 初速保留观察

主目录 main，接续 `9c2fa42`。本批新增 UE 冷导出 `-PhysicsEntryOutput=<new absolute path>`，不改变 Godot runtime 或资产。

## 修正入口判断

仅看 UPrimitiveComponent/FBodyInstance 的 collision-enabled 分支，容易推断 QueryOnly→QueryAndPhysics 必然导致骨架身体速度重置。实际受控原生观察不支持这个结论：当前两套骨架在两种初始 collision 模式下，都在 collision enable、SetSimulatePhysics(true)、ResetAllBodiesSimulatePhysics 之后保留逐身体线速度和角速度。不能以组件级通用源码分支代替骨架实际行为。

探针在独立 GamePreview world 中创建 ACharacter，加载真实 Mannequin/AnimMan mesh/PhysicsAsset。设置角色速度 `(300,-400,120)` cm/s，并给第 i 个身体设置区分身份的线速度 `(600+i,40-i,-80)` 与角速度 `(.1+i*.01,.2,-.3)`。分别从 QueryOnly 与 QueryAndPhysics 执行 ALS 相同顺序的 detach、capsule NoCollision、mesh object-type/碰撞切换、模拟开启与 asset-type reset；记录 created、seeded、collision_enabled、simulate、reset_asset_types 五个阶段。

结果：4 case、每阶段共 80 个身体（Mannequin 19、AnimMan 21，各两种模式）；240 个切换后身体记录与 seeded 线速度/角速度逐值一致。两模型各一个 Kinematic 身体保持不模拟，其余 Default 身体进入模拟。结果没有把 `(300,-400,120)` 广播给身体。

另直接加载 `/ALS/ALS/Character/B_Als_Character.B_Als_Character_C` 默认对象：mesh collision=QueryOnly (1)，bDeferKinematicBoneUpdate=false。ALS C++ 构造函数也明确指出延迟 kinematic bone update 会影响进入时的速度继承。

这是 **同步受控切换参考**，没有动画求值、物理 tick、实际 ALS StartRagdolling 调用或 PIE 输入。它证明切换保留已有逐身体速度，不证明这些速度在正常动画历史里如何计算。也不把观察到的保留等同于证明内部对象必然未重建。

下一步应实现/验证动画驱动阶段的逐身体 kinematic 速度历史，再把最近成功提交的身体状态交给动态积分；角色速度继续只作为 ALS 限速上限输入，不能替代逐身体初速。瞬移、漏帧、首次创建和物理线程时间边界需要原生进一步对照。

## 验证及门禁

- 完整 Editor target 构建及所有适用项目插件审计通过。fingerprint `64F98753DA631762A2647943DF4957D2B07808BED0D9C7C3DF0C04FDDDE5B0D0`，BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`。
- 两次冷导出退出 0，字节一致，SHA256 `3E7C814110BD7D3EF369F23B149B6BCA645DA440AB868DA8ACADB35260821177`。
- `tools/physics/VerifyRagdollEntry.ps1` 验证 case 覆盖、顺序、身体绑定、受控 seed、240 次速度保持和最终 simulation type，通过。
- DataValidation 退出 0，0 error / 3 既有 warning。
- 普通 Editor 首轮 PID 24068 插件加载标记成功，但关闭日志后进程退出 `0xC0000005`，且两条旧 Condition failed 保留；退出异常未修复，不能称所有门禁通过。
- 确认首轮进程退出后独立复跑，PID 27288 加载标记成功并退出 0，仍有两条旧 Condition failed。复跑成功不覆盖首轮失败，也不证明间歇退出异常已解决。

日志与参考在 `artifacts/ragdoll-native-entry-20260923/`。初次构建因系统缺 .NET 10 启动失败，改用 UE 自带 runtime；随后修正 TObjectPtr 显式 Get 和 TSharedRef JSON 调用编译问题，最终完整构建通过。导出源主目录与 UE 插件镜像三个文件逐个哈希相同。技能要求的不可用 superpowers 辅助技能由手工源码检查、独立冷复导和日志审查替代，没有跳过完整目标审计。

本批不重复 C# 全量/落地矩阵：最近 Core 2892（既定过滤）、静态 9/12、Flail 0/3 边界不变。普通 Ragdoll 激活、胶囊/Flail/骨盆跟随/显示退出，以及 Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能验收均保留。用户 P4 修改未改变、未提交。
