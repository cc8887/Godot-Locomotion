# Lyra 玩家与 NPC 共用 Shooter 移动服务

本批将普通 Lyra 玩家和 NPC 的简化移动替换为共同服务，加载 GASP58 原 Shooter 角色的真实配置，使用原地面速度计算及空中速度、重力和位移积分。ALS 人物、原完整 Main/Linked Layer、角色级 Montage/Notify 和最终 Rig 继续使用此前验证的资源与宿主。

这份记录关闭的是共用场景移动服务接入。Chaos/Jolt 同输入完整轨迹、复杂地形、原 StepUp/多接触/空中碰撞规则、跳跃顶点分步和全部物理时序仍开放。此前原 CMC 观察值重放下的动画精度通过，见 [ISPC 混合修正](2026-10-03-lyra-ispc-mixing.md)；它与本批实际 Jolt 场景验证具有不同范围。

## 原资源与配置

本机没有运行中的 Unreal Editor；按项目 MCP 优先规则检查后，使用已有只读 headless 原生探针。可选探针包 `package-cmc-motor-v2` 编译成功，原项目、角色源码、配置和资产不改。原 `B_Hero_ShooterMannequin_C` 的 CDO 和实际 PhysicsVolume 导出如下：

| 配置 | 原值 |
|---|---:|
| 胶囊半径 / 站立半高 / 蹲伏半高 | 35 / 90 / 65 cm |
| 站立 / 蹲伏最大速度 | 600 / 300 cm/s |
| 最小模拟输入速度 / 最大加速度 | 200 / 1200 cm/s、cm/s² |
| GroundFriction / 独立 BrakingFriction / Factor | 8 / 3 / 1 |
| 地面 / 空中 BrakingDeceleration | 1400 / 0 cm/s² |
| JumpZVelocity / 实际 GravityZ / TerminalVelocity | 500 / −980 / 4000 cm/s、cm/s²、cm/s |
| AirControl / BoostMultiplier / 严格小于的 BoostThreshold | 0.4f / 4 / 50 cm/s |
| FallingLateralFriction | 0 |
| WalkableFloorAngle / MaxStepHeight | 44.76508331298828° / 50 cm |
| JumpMaxHoldTime / JumpMaxCount / ApplyGravityWhileJumping | 0 / 1 / true |
| MaxSimulationTimeStep / MaxSimulationIterations | 0.05f / 8 |

原 `LyraCharacterMovementComponent::GetMaxSpeed` 没有 ADS 减速分支。因此 ADS 不额外改最大速度；普通示例的 Walk 输入使用半幅模拟输入，仍走同一套原速度规则。

新采集 `whole-main-cmc60-motor-v2-native.json` 的三个 Provider 各包含 250 组原 `CalcVelocity` 地面调用和 175 组原空中调用，三个 Provider 的配置与两套样本一致。空中样本包含 0、1/120、1/60、1/30、0.1 秒，七种初速、五种加速输入，覆盖低速 Boost、50 cm/s 门槛、超速刹车、无输入、升降和终端速度。原空中函数调用为 `GetFallingLateralAcceleration` → 临时去 Z 的 `CalcVelocity` → `NewFallVelocity` → `0.5f * (OldVelocity + Velocity) * Delta`。这不是把 0.1 秒输入当作完整 `PhysFalling` 的所有分步、顶点或碰撞路径。

`tools/export_lyra_character_motor.py` 校验原采集的一致性后，创建两个新文件：

- 本地运行资源 `assets/generated/lyra_als/character_motor_v2.json`，SHA256 `F02323BCC28074E2BFBC6B327A5F23D178EDDE12666AB0A69DC6D5114FF6E7E8`。
- 可携带测试夹具 `tests/Als.Core.Tests/Fixtures/Physics/lyra_character_falling_native.json`，包含原配置和 425 组原生结果。

原 `character_motor_v1.json` 与其余既有导出不覆盖、不格式化。V2 资源仍受 Git 忽略；只有代码的检出不构成可运行的 Lyra 交付。

首次准备且上述采集/输出均不存在时，在主目录执行以下命令；它们拒绝覆盖既有证据和 V2 资源。引擎路径默认使用本机 UE 5.8，按用户要求不另做 5.9 差异分支；GASP58 路径及 exporter package 准备要求见脚本。已具备本批资产的当前主目录无需重复导出。

```powershell
./scripts/build-lyra-whole-main-oracle.ps1 -EngineRoot '../UE_5.8' -UnrealProject ../GASP58/GASP58.uproject -PackageName package-cmc-motor-v2
./scripts/capture-lyra-whole-main.ps1 -RunTag cmc60-motor-v2 -PackageName package-cmc-motor-v2 -Hz 60 -Case physics -FrameLimit 60
python tools/export_lyra_character_motor.py
```

## 场景路径

`LyraCharacterMovementSettings` 从 V2 资源创建共享不可变配置；`LyraSceneMovementService` 同时供普通玩家和 `LyraSceneCharacter` 使用，删除两处不同的 `MoveToward`、10.5 m/s 跳速、19.6 m/s² 重力和 1.2 m 蹲伏高度。

每帧先处理真实蹲伏/净空和原 Emote 请求，再冻结 Montage 区间、计算输入及速度，调用一次 `MoveAndSlide`，最后从真实 Body/碰撞获取动画观察。根运动覆盖横向速度，空中重力继续更新；动画的取消、重试复用同一物理凭据。首次角色注册后，在真实近地检测确认可走地面时执行一次初始化 FloorSnap，随后才处理蹲伏，避免尚无历史接触的 Godot 角色被误当作空中。该初始化落位与逐帧一次 `MoveAndSlide` 分开说明。

位移速度与帧末速度分开传递：Jolt 使用原中点积分的位移，角色发布经实际接触投影的帧末速度。`LyraPhysicalMovementResult` 保存独立捕获的物理起始 Actor/Component，Warp 用它校验是否错误使用蹲伏前位置；动画重试不得改变这个凭据或再次移动。

蹲伏在地面保持基底、空中保持中心。起身先检测原微量膨胀的站立胶囊；地面受阻时尝试靠近真实地面，空中受阻时按原短胶囊下扫查找可站立基底，再做重叠检查。失败保留蹲伏，后续物理帧重新检查。普通动画停止预测也使用真实速度和原摩擦/刹车配置。GroundInfo 地面距离为零，空中中心射线扣除实际半高。

## 验证及失败记录

Core 的 250 组地面、175 组空中加速度/速度/位移按原 `1e-10` cm/s²、cm/s 或 cm 门槛通过；连同地面预测和根运动相关测试，12 项通过、0 失败、0 跳过。首次测试新增代码误引用 `Math` 命名空间、首次物理测试误用 `GravityZ` 字段导致编译失败，均修正并保留日志。

首次 Jolt 跳跃检查失败：请求的中点速度和帧末速度正确，真实位置额外包含约 0.344 毫米的 Jolt SafeMargin 恢复。通过独立 `BodyTestMotion(Motion=0, RecoveryAsCollision=true)` 测得相同恢复量，验证“中点位移 + 实际恢复”后通过；积分门槛没有放宽。Godot 随附 API 文档明确 SafeMargin 会在实际运动之前推开角色。该恢复与原 CMC 地面悬空距离不同，不能据此声称两物理引擎的位置逐值等价。

第一轮场景矩阵的三频物理/普通十角色和 Root Motion 通过，Warp 检查失败，因为旧测试按固定高度差推算起身位移，遗漏原微量膨胀；改为比较两个独立路径捕获的真实 Actor/Component，不放宽误差门槛。独立 Emote 回归暴露初始无地面接触时蹲伏角色的错误下落；补初始化落位后，Emote 六角色和 Warp 六角色均通过。以上失败日志和第一轮矩阵保留。

最终矩阵由 `scripts/verify-lyra-character-motor.ps1` 执行；证据标签 `cmc-motor-v2-final`。Debug 与真实 ExportRelease Optimize 各九项检查全部通过，最终 18 个 headless 进程退出 0、无 Godot ERROR/WARNING；两次最终构建各 0 警告、0 错误，Optimize 后六个 Debug 文件逐个哈希恢复。

每种构建包含：

| 实际场景 | 帧与覆盖 |
|---|---|
| 30/60/120 Hz 六角色物理 | 合计 5040 次 MoveAndSlide 和逐角色每帧重试；速度/ADS/刹车/净空/空中换姿/墙/跳跃/落地 |
| 30/60/120 Hz 普通十角色 | 合计 1680 玩家帧、16800 角色 MoveAndSlide；玩家逐帧重试、迟到失败、装备换类与同类复用、源/武器通知 |
| 60 Hz Root Motion 六角色 | 2880 次 MoveAndSlide、运动前取消与动画重试；独立五种非零动作碰撞/滑动/垂直/旋转及迟到拒绝 |
| 60 Hz Warp 六角色 | 2880 次 MoveAndSlide、双阶段重试，8 次开放目标到达、动态目标/墙/禁用/暂停/销毁 |
| 60 Hz Emote 六角色 | 2880 次 MoveAndSlide、双阶段重试，7 激活/6 结束/3 移动清理，含初始化蹲伏净空 |

两种构建的普通三频完整报告及物理三频完整报告逐项相同。物理测试跳高约 1.2752–1.2760 m，独立查询扣除 Jolt 恢复后的中点误差小于 `2e-6` m，跳跃帧末速度误差为零。

另有 Debug 60 Hz 单玩家真实 NVIDIA OpenGL 渲染：480 物理帧、480 动画重试、7 张 `FramePostDraw` 截图，普通距离的 standing/movement/aim/crouching/jump/landing/reverse 全部图像抽查。ALS 模型、持枪及跳跃/落地可见；截图不是近景握持和完整人工观感验收。实际图片身份保存在 `cmc-motor-v2-render60-debug/frames.json`，汇总图 `cmc-motor-v2-render60-contact.png`。

独立审计 `tools/verify_lyra_character_motor.py` 通过，输出 `artifacts/lyra-analysis/character-motor-v2-integrity.json`：425 原生样本、12 managed、18 场景、程序集及源文件哈希均核验。866 个既有 JSON、709 个原 UE 包、9 项配置及 3 份原角色源码逐文件保持；只读 UE 采集 180 个原 Main 帧退出 0、`assets_saved=0`。渲染阶段另有 `character-motor-v2-render-integrity.json` 保存七图及场景审计哈希。整体标记仍为 `nativeWorldTrajectoryParity=false / completeAcceptance=false / goalComplete=false`。

## 开放范围

- 服务当前接入 30/60/120 Hz，并拒绝超过 50 ms 的单次物理子步；尚未实现原 `MaxSimulationIterations` 下的完整外层分步或跳跃顶点时间退款。
- 物理使用 Jolt `CharacterBody3D` 的实际滑动和地面检测；原 50 cm StepUp、perch、双墙修正、受限 AirControl、移动平台/传送等完整 CMC 行为尚未验收。
- 最终 Rig 和通知消费者仍执行此前的原宿主；另一个 FootPlacement 分支、近景握持、复杂地形、性能及人工全矩阵仍待。
- 14 个装备 Layer 入口继续共享角色内 `ItemAnimLayers` 实例；多命名 Group、未分组独立实例、Unlink/default/self/persistence 和通用任意 AnimBP 仍开放，见 [ALS 与接口复核](2026-10-03-lyra-als-interface-review.md)。

全部已有 ALS/用户修改、原资源哈希依赖及暂缓项保留；本批没有提交或推送，整体目标继续 active。
