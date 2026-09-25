# 落地自动 Roll 与 Ragdoll 触发边界

本批直接在 `${env:GODOT_ALS_ROOT}` 的 `main` 推进，基于 `af7922d` 的地面 Roll。
普通完整入口现支持中等落差自动翻滚：按角色缓存的下落速度判断，以 1.3 倍速
播放原始 Roll，并朝落地前的水平速度方向转向。

**Ragdoll 的触发判定已接通，物理 Ragdoll 尚未实现。** 高落差和翻滚离地
会输出明确的 MovementAction 记录并阻止同帧误启 Roll，但仍不会启用布娃娃物理。
不能把分支测试通过解释为 P6 完成。

## 原版依据

本地 ALS-Refactored `b754d6f0f2bb03741d301f8fb88077ebfe561e17`：

- `AlsCharacter.cpp::NotifyLocomotionModeChanged`：仅移动模式切换时判断；
  先检查 Ragdoll 落地阈值，再检查 Roll 阈值；Roll 的落地播放倍率为 1.3。
- `FAlsRagdollingSettings` / `FAlsRollingSettings` 的 C++ 默认阈值分别为
  1000 / 700 cm/s，使用包含边界的比较。本项目统一为 m/s，保持 10 / 7。
- `RefreshLocomotion` 的速度在 Character tick 更新。CMC 内的模式变更回调
  读到的是上一 Character tick 的 LocomotionState.Velocity，不能改用
  MoveAndSlide 后已归零的垂直速度，也不能用当前帧额外施加重力后的速度替代。
- 水平速度至少 1 cm/s 才使用缓存速度 yaw，否则保持角色当前 yaw。
  阈值比较保留原生转换成 float cm/s 的边界，避免 0.01f m/s 表示误差。
- 普通落地才设置临时制动摩擦；进入 Roll/Ragdoll 分支时不执行该设置。
- Rolling 从 Grounded 进入 InAir 时选择 Ragdoll。

这里继续使用明确的 C++ 默认设置。尚未导出 Refactored DataAsset 对这些设置
的覆盖值，也未新增完整 UE Character 落地轨迹 oracle。

## 实现

Main Motor 在一次 MoveAndSlide 之后采集模式边沿与之前缓存的实际速度，
构造 `AlsMovementActionTransition`，随不可变帧输入传递。

中等落差生成当前帧唯一的 Roll Start 请求；同帧的手动 Start 合并为这一次
落地 Start，已接受动作的显式 Cancel 优先。输入附带 `AlsMontageActionParameters`：
1.3 绝对播放倍率和落地目标 yaw。普通手动请求保持默认参数，继续采用编译策略。

共同 Montage 所有者验证参数，在实际物理实例上设置倍率，不改变全局策略、不
新建时钟。后续动画、Notify、根运动和动作摘要都使用这个实例的真实时间。
RollingState 在接受后存储目标 yaw；之后的输入变化不会覆盖它。

高落差先选择 LandingRagdoll，翻滚离地选择 RollingInAir；两个分支的同帧
Roll 请求均被拒绝，结果明确保留触发类型、缓存速度和目标 yaw，供下一步真实
Ragdoll 消费。当前不会把 RagdollState 或 DriveMode 伪装成已运行物理模拟。

Main 已完成的碰撞输入在故障重试中保持不变。语义停用沿用请求高水位阻止复播；
角色换代显式清空旧 Start 参数及模式边沿。新的手动动作恢复自身默认倍率。
帧输入和结果仅在末尾追加字段，活动模式边沿也纳入结果摘要。

## 验证与限制

产物：`artifacts/landing-actions-20260920/`。

- 优化 Godot 构建通过，0 警告、0 错误。
- Release Core 最终回归 2593 通过（排除既有 P5A Golden / TraceSchema 旧夹具）；
  Import 2286 通过、1 项跳过。记录见 `core-final.trx` / `import.trx`。
- Core 专项检查包含 7 / 10 m/s 精确边界、Ragdoll 优先级、1 cm/s 方向阈值、
  无输入方向回退、播放参数错误拒绝、不同实例倍率隔离、丢弃重试及字段顺序。
- `landing_action_smoke` 在真实 CharacterBody 地面碰撞中依次验证中等、高、低
  落差，以及翻滚离地。高落差同时按 R，证明优先级没有被手动请求绕过。
- 30 / 60 / 120 Hz 分别完成 61 / 104 / 188 帧，均为 2 接受、1 拒绝、
  1 完成、1 取消，三个落地分支通过。中等落差根运动帧为 22 / 43 / 86。
- 60 Hz Single / Parallel 摘要均为 `170A725476EAE291`；落地帧注入
  BeforePublish 故障后仍得到相同摘要。重试没有重复积分，也没有重复接受。
- 落地候选 pending、结果 held 和 generation 三种生命周期边界通过：
  旧落地动作 0 接受，新手动动作 1 接受 / 1 取消；没有继承旧倍率或方向。
- 既有地面 Roll 烟测通过。其空中 R 拒绝用例降为 1 m 落差，以单独测试
  空中按键门控；中等/高落差自动行为现在由专门的落地烟测覆盖。
- 键鼠烟测通过 360 帧，覆盖 Alt、A、D 和 4 次鼠标事件；检查移动期间转向、
  Alt 松开后继续移动及上一已提交帧反馈。此项不是本机人工观感验收。

复现（ENGINE 指向 Godot 4.7.2 Mono console）：

```powershell
& $ENGINE --headless --path . res://scenes/tests/landing_action_smoke.tscn -- --hz=60
& $ENGINE --headless --path . res://scenes/tests/landing_action_smoke.tscn -- --hz=60 --single
& $ENGINE --headless --path . res://scenes/tests/landing_action_smoke.tscn -- --hz=60 --failure
& $ENGINE --headless --path . res://scenes/tests/landing_action_smoke.tscn -- --boundary=pending
& $ENGINE --headless --path . res://scenes/tests/landing_action_smoke.tscn -- --boundary=held
& $ENGINE --headless --path . res://scenes/tests/landing_action_smoke.tscn -- --boundary=generation
```

首轮烟测代码构建发现故障注入的目标类型推断和 Motor 诊断访问错误，已修正。
首轮广泛 Core 回归中，未改动的 `AlsRagdollFrameTests.WarmFrameStateClockAndPoseAllocateNothing`
曾报告 8080 字节分配，独立原样复跑为 0 并通过，保留在 `core.trx` 和
`ragdoll-allocation-recheck.trx`；随后完整范围复跑 2593 项全部通过。
首次偶发分配原因尚未确定，本批不据此声称最终零分配/十分钟性能认证完成。

## 下一步

下一项是原始 PhysicsAsset 的完整导出与 Godot 物理骨架消费，而非再增加一个
假 Ragdoll 标记。当前 `assets/generated/als_v4/als_manifest.json` 中：

- AnimMan_PhysicsAsset：21 个 body、20 个 constraint。
- Mannequin_PhysicsAsset：19 个 body、18 个 constraint。

现有 `AlsRigMetadataReader.cpp` 仅输出 body 的 `bone / primitiveCount` 和
constraint 的 `childBone / parentBone`。碰撞体尺寸/局部变换、质量、关节局部
坐标系/限制、驱动、碰撞排除等数据尚未完整导出，不能从这些数量推导真实物理模型。

后续按实际角色选用的原始 PhysicsAsset 补数据，建立主线程物理所有权、已提交
姿势到物理骨架的切换与速度继承，接入本批的两个 Ragdoll 触发边界，再完成
落地追踪、Get-up/Pose Recovery 和道具清理重装备。Mantle、完整 Camera、
复杂地形观感和最终十分钟性能预算仍保持在完整清单内。
