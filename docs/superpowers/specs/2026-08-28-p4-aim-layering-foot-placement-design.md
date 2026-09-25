# P4 Aim、分层姿态和 Foot IK 设计

**状态：** P4 Task 1-17 自动实现闭环已通过；Godot Editor 八项手工观感验收待签收，P7 长时认证不属于本阶段

**日期：** 2026-08-28

**目标引擎：** Godot 4.7.2 .NET

**设计编写前代码基线：** `b7ab2e94029b5394891fa30628cad210116ffd3f`

**Task 17 closure locked base：** `1d941ee0611ca2f6af710deab7a6d63f07e2105c`

**参考实现：** `Sixze/ALS-Refactored` commit `b754d6f0f2bb03741d301f8fb88077ebfe561e17`

## 一、目标与完成边界

P4 在已完成的 P3 locomotion、真实 Mannequin 动画图和 Gather/Worker/Commit 三阶段线程合同上，一批完成以下能力：

- Looking Direction 和 Aiming 的完整视角姿态；
- AimOffset 与 mesh-space pose correction；
- Head、Spine、左右 Arm/Hand、Pelvis 和 Legs 的分层 mask 与 per-bone rotation；
- Standing/Crouching 的 Turn In Place；
- Standing/Crouching 的 Rotate In Place；
- Foot IK、Foot Lock 和 pelvis correction；
- 平地、斜坡、楼梯以及平移/旋转平台上的脚部处理；
- 1/10 角色 single/parallel 等价、短时性能门禁和可操作 Demo。

P4 在同一个功能分支中实施，但保留三个顺序检查点：

1. P4-Asset：曲线、additive base、profile、mask 和 golden 合同；
2. P4-Pose：Aim、Layering、Turn/Rotate 和 actor yaw；
3. P4-Feet：Foot IK、Foot Lock、pelvis、楼梯、斜坡和移动平台。

三个检查点共同通过后才能宣称 P4 完成。P4 不包含：

- 通用 Curve Runtime、Notify、Notify State、Sync Runtime 或 ActionPlayer；
- Overlay gameplay、武器 Aim Sweep、装备/切换和道具生命周期；
- Mantle、Roll、碰撞安全 Root Motion；
- Ragdoll、Get-up、Pose Recovery；
- 完整 ALS Camera；
- P7 的 30 秒热身、10 分钟 Release 最终性能认证。

P4 可以读取自身必需的 canonical 曲线，但不能借此提前实现 P5A 的通用事件和动作系统。

## 二、已确认方案

采用“参考优先的统一姿态管线”：

1. 扩展 UE 离线导出合同，补齐浮点曲线键值、root yaw 派生、AimOffset additive base 和 P4 profile 所需元数据；
2. 在 `Als.Core` 中实现不引用 Godot 的 View/Aim、Turn/Rotate、Foot Lock 和 pelvis 纯数据状态；
3. 在角色 Worker 中用生产 AnimationTree 求基础动画，再执行统一 component-space 姿态修正；
4. Main Gather 继续独占输入、物理查询和 SceneTree transform 写入；Worker 求解 actor yaw，Main Commit 只验证并锁存 target-yaw 和生命周期结果；
5. 先用 C# 实现并测量，只有 Profiler 明确证明姿态修改器是热点时才引入 GDExtension。

未采用的方案：

- 完全依赖 Godot AnimationTree/IK 节点：难以锁定 mesh-space additive、Foot Lock 平台空间和 single/parallel 姿态等价；
- 直接使用 GDExtension：当前没有证据证明 C# Worker 无法满足阶段预算，过早增加原生边界不符合性能治理顺序。

## 三、来源与可追溯性

P4 继续复用以下正式参考边界：

- `reference/als-refactored.lock.json`；
- `scripts/prepare-p3-reference.ps1` 的 origin、commit、patch SHA 和工作树完整性检查；
- `${env:ALS_REFERENCE_ROOT}` 的固定 detached HEAD；
- `${env:ALS_UE_PROJECT_ROOT}` 中用户拥有的 ALS V4 资产；
- P2A/P2B 已发布的 267 assets / 141 files 正式 manifest 和生成资源。

P4 golden 不新建另一套上游锁。它复用现有 reference lock，并在所有 schema、fixture 和生成输出中写入相同的 40 字符 commit。任何新增兼容补丁都必须登记路径和 SHA，且继续使用同一固定提交。

关键参考符号包括：

- `UAlsAnimationInstance::RefreshLayering()`；
- `UAlsAnimationInstance::RefreshView()` 与 `RefreshSpine()`；
- `UAlsAnimationInstance::RefreshRotateInPlace()`；
- `UAlsAnimationInstance::RefreshTurnInPlace()`；
- `UAlsAnimationInstance::RefreshFeetOnGameThread()` 与 `RefreshFeet()`；
- `FAlsRotateInPlaceSettings`、`FAlsGeneralTurnInPlaceSettings` 和 `FAlsFootLockSettings`；
- `UAlsAnimationModifier_CalculateRotationYawSpeed`；
- `FAnimNode_AlsCurvesBlend` 的明确曲线覆盖/累加语义。

## 四、线程和所有权

### 4.1 Main Order 0：Gather

Gather 读取并发布不可变 `AlsFrameInput`：

- movement axes、gait、stance、rotation mode 和 jump；
- camera/view yaw、pitch 和 aim rotation；
- CharacterBody 实际 transform、velocity、acceleration 和 floor；
- 上一已提交帧提供的左右脚未修正 probe origin；
- 左右脚 ray/shape query 结果；
- 每个脚命中的 platform ID、platform transform 和 surface normal；
- 当前平台 transform、角速度、teleport/base-change 标记；
- animation quality tier 和槽位 identity。

Main 不读取 Worker 独占的 Skeleton 或 AnimationTree。任何新增世界数据都必须通过固定宽度合同发布。

### 4.2 Worker Order 1：Core 与 Visual

每个角色 Worker 独占：

- `AlsRuntimeState` 中的 View、Turn、Rotate、Feet 和 Pelvis 状态；
- AnimationTree、AnimationMixer 和 Skeleton3D；
- P4 曲线采样器和动画 phase；
- 预解析的骨骼 ID、父索引、mask 和 scratch buffers；
- 完整姿态回滚缓冲和结果双缓冲。

Worker 每帧严格按以下顺序执行：

```text
Read immutable frame input
  -> Evaluate P3 locomotion
  -> Evaluate View/Aim and Turn/Rotate
  -> Evaluate Foot Lock and pelvis targets
  -> Advance base locomotion / Turn / Rotate animation once
  -> Capture evaluated local pose
  -> Build component-space pose
  -> Apply Aim and upper-body layering
  -> Apply pelvis translation
  -> Apply left/right foot correction
  -> Rebuild affected local transforms once
  -> Publish result and pose digests
```

Worker 不执行物理查询，不访问其他角色，不启动嵌套线程池任务，不修改外部 SceneTree。

### 4.3 Main Order 2：Commit

Commit 只负责：

- identity、frame、generation 和完成状态验证；
- 验证并锁存曲线驱动的 target-yaw，供下一帧 Order 0 motor 消费；
- 缓存下一帧 foot probe request；
- 角色可见性、替换、停用和错误生命周期；
- 有界诊断和性能计数。

Commit 不写 Skeleton、AnimationTree、Modifier 或 SceneTree transform。Turn/Rotate 的 actor yaw 必须来自 Worker 与动画 phase 同步计算的结果，不能在 Commit 中重新推导；下一帧 Order 0 motor 在移动前消费锁存值并成为角色 transform 的唯一写入者。

## 五、资产与曲线合同

### 5.1 Manifest 曲线

当前 `AlsAnimationDefinition` 只保存曲线名称。P4 将曲线数据编译为固定结构：

```text
AlsFloatCurveDefinition
  StableCurveId
  CanonicalKind
  SourceName
  SourceProvenance
  PreInfinity / PostInfinity
  Keys[]

AlsFloatCurveKeyDefinition
  TimeSeconds
  Value
  Interpolation
  ArriveTangent
  LeaveTangent
```

导出器保留原始曲线，同时建立显式 canonical 映射。运行时使用整数 curve ID，不使用字符串查找。

Turn/Rotate 所需 canonical 曲线为 `RotationYawSpeedRadiansPerSecond`。生成顺序：

1. 若资产包含经过验证、单位明确的等价曲线，转换并记录源曲线；
2. 否则从 root bone yaw track 解包角度，在原动画采样率上离线求导；
3. 记录 `source_curve` 或 `derived_root_track` provenance；
4. 对不连续、非有限、零时长和无法确定符号的资产拒绝发布。

运行时角度和角速度统一使用弧度。UE 原始 degree 值只存在于导出输入和对照报告中。

### 5.2 AimOffset 与 additive base

P4 profile 必须严格引用：

- 一个基础 AimOffset stable ID；
- Down、Forward、Up 三条 sweep animation ID；
- 每条 sweep 的 additive type、base pose animation/frame 和 skeleton ID；
- pitch sample `-90° / 0° / 90°`；
- yaw sweep 的 phase 方向和 Godot/UE 符号映射。

三条 sweep 必须具有相同 skeleton、兼容 additive base、合法时长和可采样轨道。缺一项即编译失败。

### 5.3 P4 pose profile

新增版本化 P4 profile，至少包含：

- AimOffset stable ID；
- Standing/Crouching、Left/Right、90/180 的八个 Turn animation stable ID；
- Standing/Crouching、Left/Right 的四个 Rotate animation stable ID；
- animated turn angle、base play rate 和 blend duration；
- canonical curve aliases；
- `spine_01`、`neck_01`、`clavicle_l/r`、`hand_l/r`、`pelvis`、`thigh_l/r`、`foot_l/r` 等 mask roots；
- Foot IK、Foot Lock、pelvis 和 rotation amount curve IDs；
- Foot trace、pelvis damping、thigh/foot angle limits 和 platform thresholds；
- coordinate/sign provenance。

Profile 编译器根据 Skeleton hierarchy 把 mask root 展开为有序 bone ID 数组，并拒绝：

- 不存在或重复的骨骼；
- mask 越过声明边界；
- 左右骨骼不成对；
- skeleton 与动画不一致；
- 按名称或 basename fallback；
- 未使用但被悄悄接受的未知字段。

P3 profile 保持 schema v2 和既有 digest 不变。P4 使用独立 profile，避免重写 P3 golden。

## 六、数据合同

### 6.1 输入扩展

`AlsLocomotionCommand` 增加有限的 view/aim pitch。Main adapter 根据 camera basis 生成完整 `ViewRotation` 和 `AimRotation`，不再构造 yaw-only quaternion。

`AlsFootHit` 增加：

- platform ID；
- platform transform；
- hit collider identity；
- surface/point velocity；
- walkable/valid 标记。

所有矩阵、向量和四元数进入 Worker 前必须有限、正交或在声明误差内可归一化。

### 6.2 Runtime 状态

新增固定宽度、无引用状态：

- `AlsViewPoseState`：relative yaw/pitch、yaw speed、Head/Spine amount、last world yaw；
- `AlsTurnInPlaceState`：activation delay、turn type、phase、play rate、remaining angle；
- `AlsRotateInPlaceState`：direction、phase、play rate；
- `AlsFootPlacementState`：左右 lock amount、platform ID、platform-local target、offset 和 rotation；
- `AlsPelvisCorrectionState`：current/target offset 和 damping state；
- 上一已提交 foot probe origin。

Turn 和 Rotate 是 Grounded 内的并行姿态状态，不扩展 `AlsAnimationState` 顶层枚举，避免组合状态爆炸。

### 6.3 Frame 结果

`AlsFrameResult` 增加：

- aim angles、Head/Spine/upper-body weights；
- Turn type、phase、play rate、yaw delta；
- Rotate direction、phase、play rate、yaw delta；
- pelvis offset；
- left/right foot target、rotation、lock amount 和 platform ID；
- 下一帧 left/right foot probe origin；
- P4 modifier deterministic operation ticks（不是 wall-clock elapsed time）和稳定 reason code。

所有字段必须进入 `AlsResultDigest`、single/parallel pair comparison、回滚验证、默认值和非法值测试。

## 七、View、Aim 和分层行为

### 7.1 相对角度

```text
RelativeViewYaw = Normalize(ViewYawWorld - CharacterYaw)
RelativeViewPitch = Clamp(Normalize(ViewPitchWorld - CharacterPitch), -90°, 90°)
ViewYawSpeed = Abs(ShortestAngleDelta(PreviousViewYawWorld, ViewYawWorld)) / DeltaTime
```

`±180°` 跨界必须走最短路径。非有限输入或无效 DeltaTime 使整个 Evaluate 事务失败且不修改 caller state/result。

### 7.2 AimOffset 采样

- pitch 在 Down/Forward/Up 三条 sweep 间连续混合；
- yaw 映射到每条 sweep 的相同 phase；
- phase 的 0/0.5/1 与左右/正前方向由 golden 固定；
- mesh-space additive delta 相对已验证 base pose 计算；
- Aim mask 只能改变声明的上半身骨骼。

纯 AimOffset 测试必须证明 pelvis、thigh、foot 和 root 未改变。

### 7.3 Head 与 Spine

- Head blend 为 view amount 与非 aiming amount 的组合；
- Spine aiming 进入使用 `0.1s` half-life；
- Spine aiming 退出使用 `0.7s` half-life；
- 退出时保持世界空间连续，残留 yaw 限制为 `±30°`；
- 移动平台相对旋转进入 last-world-yaw 修正；
- 最终 Spine yaw 乘以 profile/curve 提供的 Spine 权重。

### 7.4 Layering

Head、Spine、左右 Arm/Hand、Pelvis 和 Legs 分别具有 ordinary、additive、slot 或 local/mesh-space 权重。P4 只消费默认 locomotion 层；P5B Overlay 接入后再复用同一合同。

曲线合并遵循显式 override/accumulate 规则，不依赖普通 pose blend 的隐式结果。Arm local-space 权重为满权时关闭对应 mesh-space additive；其他情况下使用已编译的 canonical 权重。

## 八、Turn 和 Rotate In Place

### 8.1 Turn In Place

允许条件：

- `Grounded`；
- P3 已有 moving classification 为 false；
- `LookingDirection`；
- 第三人称；
- 当前无互斥 Turn/Rotate/action；
- transition gate 有效。

触发规则：

- `abs(viewYaw) > 45°`；
- `viewYawSpeed < 50°/s`；
- activation delay 按 `abs(viewYaw)` 从 `[45°, 180°]` 映射到 `[0s, 0.75s]`；
- 累计时间必须严格超过映射后的 delay；
- `abs(viewYaw) < 130°` 选择 90°，否则选择 180°；
- stance 和 yaw sign 决定八个 clip 中的一个。

播放规则：

- 基础 play rate 为 `1.2`；
- 若 profile 允许角度缩放，最终倍率乘以 `abs(requestedYaw / animatedTurnAngle)`；
- blend duration 为 `0.2s`；
- actor yaw delta 来自与当前 phase 相同的 canonical rotation curve；
- 动画 phase 与 actor yaw 在同一 Worker Evaluate 中推进。

移动、离地、stance 改变、rotation mode 改变、角色停用或 replacement 会取消 Turn 并清零排队/等待状态。

### 8.2 Rotate In Place

允许条件：

- `Grounded`；
- moving classification 为 false；
- `Aiming`；
- 当前无 Turn 或互斥 action。

规则：

- 第三人称 view yaw threshold 为 `50°`；
- 左右由 yaw sign 决定；
- view yaw speed `[180, 460]°/s` 映射到 play rate `[1.15, 3.0]`；
- play rate 以 `0.15s` half-life 平滑；
- Standing/Crouching 分别选择左右循环 clip；
- actor yaw 由相同 phase 的 canonical rotation curve积分。

移动 Aiming 继续使用 P3 rotation model。静止 Aiming 改用 Rotate 曲线驱动，不允许 P3 target-yaw 和 Rotate 同时写 actor yaw。Turn 与 Rotate 始终互斥。

## 九、Foot probe、Foot Lock 和 pelvis

### 9.1 查询时序

ALS-Refactored 在 game thread 的新一帧动画更新前读取当前 mesh socket，因此它看到的是上一已求值姿态。Godot P4 保持等价顺序且不跨线程读取 Skeleton：

```text
Worker frame N: publish uncorrected foot probe origins
Commit frame N: cache probe requests
Gather frame N+1: transform requests with current character/platform transform and query physics
Worker frame N+1: consume immutable hits and solve placement
```

这是一帧环境反馈时序，不给玩家输入、motor 或 locomotion animation 人为增加一帧延迟。平台局部目标和当前平台 transform 用于消除平台移动造成的额外漂移。

### 9.2 Foot Lock

- 只有 Grounded、MotorDriven、有效 IK/lock 权重和 walkable hit 才能获得 lock；
- lock target 保存为 platform-local position/rotation；无平台时保存 world target 与 character transform provenance；
- 同一平台移动/旋转时用最新 transform 重建 world target；
- base change、teleport、离地、无 hit、权重失效、腿部过伸或角色停用时释放；
- lock amount 只能按 canonical curve 和释放规则变化，不能因浮点抖动重新捕获；
- thigh 和 foot angle 受固定 reference settings 限制。

平台消失或正常 ray miss 是可恢复状态，不生成 Worker exception。

### 9.3 Pelvis correction

- 每只脚根据 hit point、surface normal、capsule up 和 foot height 计算目标 offset/rotation；
- pelvis target 采用两脚所需的最低垂直修正；
- 向上和向下使用独立的 reference damping；
- pelvis 不能提升到导致脚离地，也不能下降到超过腿长/胶囊约束；
- 无有效脚目标时平滑回零；
- jump、fall 和无地面状态立即进入释放路径。

### 9.4 统一姿态写回

AnimationTree 每帧只 `Advance()` 一次。随后：

1. 捕获完整 local pose；
2. 以父索引顺序构建 component-space transforms；
3. 应用 Aim 与 upper-body mesh-space delta；
4. 应用 pelvis translation；
5. 计算并应用左右脚 correction；
6. 只重建受影响骨骼及子树的 local transforms；
7. 一次写回 Skeleton；
8. 计算 result、pose、full-pose 和 root digest。

Scratch arrays、mask 和 bone indices 在 Warmup 分配，稳态路径禁止动态集合、LINQ、字符串查找和托管分配。

## 十、错误处理和生命周期

### 10.1 导入失败

以下情况阻止 profile/资源发布：

- stable ID、文件或 animation reference 缺失；
- curve key 非有限、乱序、重复时间或插值非法；
- additive type/base pose/skeleton 不兼容；
- root yaw 无法展开或 derivative 符号不确定；
- 必需骨骼、mask root 或左右骨骼缺失；
- Turn/Rotate clip、标称角度或 curve provenance 不完整；
- 未知字段、重复字段和版本不匹配。

生成器使用同目录临时文件和原子替换；任一步失败保留旧正式输出并清理临时残留。

### 10.2 Worker 失败

Worker 在修改 Skeleton 前捕获完整姿态。Aim、Turn、Rotate、curve、pelvis 或 foot modifier 任一阶段抛错时：

1. 恢复精确 local bone pose 和 corrected visual root；
2. 不发布部分 `AlsFrameResult`；
3. 冻结最后有效视觉姿态；
4. 记录唯一、稳定、去重的 reason code；
5. 保持或恢复安全 `MotorDriven`；
6. replacement/recovery 前要求新的 generation 和成功 Worker commit。

Debug 下 worker exception、线程错误、非有限合同、stale/generation mismatch、非法双写和稳态分配立即失败。Release 下使用有界诊断，禁止每帧重复日志。

### 10.3 正常恢复路径

以下状态不作为异常：

- foot ray miss；
- walkable hit 变为不可用；
- 平台正常释放或被移除；
- 角色 jump/fall；
- Turn/Rotate 因移动、模式或 stance 改变取消；
- correction 达到约束后被 clamp。

这些路径必须产生确定性状态和 reason code，并平滑释放对应权重。

## 十一、测试与 Golden

### 11.1 TDD 顺序

每个生产行为遵循 RED、GREEN、REFACTOR：

1. 先写一个只描述目标行为的失败测试；
2. 运行并确认因功能缺失而失败；
3. 写最小实现；
4. 运行 focused test 和相关回归；
5. 保持绿色后再重构；
6. 每个独立合同或纵向行为单独提交。

### 11.2 UE 导出与 schema

覆盖：

- 曲线 key、tangent、插值和 infinity 行为；
- 原始/canonical curve 双记录；
- root yaw unwrap 和 derivative；
- additive base 和 AimOffset sample；
- mask/profile stable ID；
- strict JSON、路径安全、原子发布；
- 完整导出双运行 byte/hash 确定性；
- 非法 fixture 的精确错误路径和 actual/expected 值。

### 11.3 Core 单元测试

覆盖：

- view/aim pitch 从输入完整进入 frame contract；
- `179°/-179°` 最短 yaw 路径；
- pitch clamp 和 Aim weight；
- half-life 的分帧/整帧一致性；
- Turn threshold、delay、L/R 90/180、stance 和取消；
- Rotate threshold、方向、play rate 和互斥写入；
- curve phase/yaw delta 同步；
- Foot Lock 获取、保持、释放和防抖；
- platform-local target 的平移、旋转、base change 和 teleport；
- pelvis 上/下 damping、无 hit 回零和腿部约束；
- 非法输入事务不修改 caller state/result；
- 新字段 digest mutation；
- hot path steady-state `0 B` 分配。

### 11.4 Cross-engine golden

扩展现有 `AlsLocomotionTrace` commandlet/schema 和原子发布脚本，固定生成：

- Aim center/up/down/left/right 的状态、curve 和关键骨骼 local transform；
- Standing/Crouching L/R 90/180 Turn 的选择、phase、play rate 和累计 yaw；
- Standing/Crouching Rotate L/R 的方向、play rate 和累计 yaw；
- 平地、斜坡、楼梯的 pelvis/foot target；
- 平台平移、旋转、base change、teleport 和 release；
- 使用的 source paths、reference commit 和 patch hashes。

状态、ID、方向、phase 选择和 reason code 精确一致；骨骼 transform 使用声明的 position/rotation tolerance，不要求跨引擎 bitwise 相同。

### 11.5 Godot headless smoke

必须证明：

- Aim up/down/left/right pose 均不同且固定；
- 纯 Aim 只改变声明上半身骨骼，root/pelvis/feet 不变；
- Turn 八个 clip 选择正确，actor yaw 与 curve 同步；
- Rotate 四个循环方向、play rate 和 yaw 正确；
- 每帧 AnimationTree 只 advance 一次；
- flat/slope/stairs/platform 上 pelvis 和 feet target 正确；
- 平台旋转时 lock target 保持平台局部不变；
- jump、fall、base change 和 teleport 释放 lock；
- modifier 中途失败恢复完整 pose/root；
- 停用、替换、恢复和 generation 不暴露旧姿态。

### 11.6 1/10 角色矩阵

复用生产角色和 Worker 路径：

- 1、10 角色；
- single、parallel 两种模式；
- 120 帧 warmup；
- 600 帧 measured；
- character 0 在 warmup 后执行一次 production replacement；
- result、pose、full-pose、root、Aim、Turn/Rotate、Feet digest 配对一致；
- missing、stale、generation、lag、thread error 均为 `0`；
- model、curve、controller、modifier、skeleton、exchange、commit 分配均为 `0`；
- `foot_gather` 分配必须透明报告，但不属于 P4 七桶零分配门禁；
- 每个 measured frame 有且只有一次 animation advance 和一次完成提交。

## 十二、性能门禁

P4 短时门禁沿用总性能合同的阶段阈值。矩阵同时运行 `1/10` 角色的
`single` 和 `parallel`，但两种模式的性能语义不同：`parallel` 是实际
多角色 Worker 临界路径，承担阶段性能硬门禁；`single` 是确定性参考路径，
用于证明与 `parallel` 的行为等价，不把十个串行 Worker 的总 CPU 时间误当
成并行临界路径。`single` 仍必须通过完整功能、摘要、错误计数、代际和稳态
七桶零分配门禁，并且必须报告真实 timing；其 timing 不作为 P4 性能硬门禁的通过
条件。

| 指标 | P4 要求 |
| --- | ---: |
| 10 角色 `parallel` Gather + Commit 主线程 p95 | `<= 1.5 ms` |
| 10 角色 `parallel` Worker 动画关键路径 p95 | `<= 2.5 ms` |
| 10 角色 `parallel` ALS 整体关键路径 p99 | `<= 4.0 ms` |
| 热身后七个受控托管分配桶 | `0 B` |
| stale / duplicate / missing / generation error | `0` |

前 10 个角色在 `parallel` 运行时必须保持 60 Hz 完整 AimOffset、Layering、Turn/Rotate 和 Foot IK。`single` 使用同一完整功能路径，仅改变 Worker 所属线程组，不能关闭 IK、简化图、降低频率或使用不同功能路径换取摘要等价。

若未达标，优化顺序固定为：

1. 消除分配、字符串查找和重复参数写入；
2. 合并 curve sampler 和 pose scratch 访问；
3. 合并 Aim、pelvis、feet 的 component-space 遍历；
4. 减少无效 clip/parameter 更新；
5. 只有 Profiler 证明 modifier 是热点时才评估 GDExtension。

Task 9 的受控短时证据显示，非零 Aim modifier 为 `47/68` 个 affected bones，约 `0.58-0.64 ms/角色`、七个受控桶 `0 B`。setter 只占分段样本约 `0.7%`，主要成本在 clip sampling 和 component/local rebuild。Task 15 随后用正式四格矩阵关闭了多角色短时预算风险；clean-worktree full 证书中 `10/parallel` 的 Gather+Commit p95、Worker p95、整体 p99 分别为 `998 us`、`2036 us`、`3937 us`。该证据不得通过关闭 Aim、减少 mask 或降低更新频率取得，也不能冒充 P7 长时 Release 证书。

P4 的 120/600 frame 结果是阶段证据。P7 才执行 i7-10700、30 秒热身和 10 分钟 Release 最终认证。

## 十三、可操作 Demo

扩展现有 `p3_locomotion_demo.tscn` 或复制为版本化 P4 demo，保持当前已确认正确的键鼠和相机语义：

- W/A/S/D 移动；
- Shift gait；
- Ctrl stance；
- Space jump；
- V rotation mode；
- RMB Aiming；
- mouse orbit；
- Esc capture。

场景增加：

- 连续缓坡；
- 不同高度和深度的楼梯；
- 平移平台；
- 绕垂直轴旋转的平台；
- 可从平台走到固定地面的 base-change 路径。

手工验收：

1. RMB 下 AimOffset 能连续看上/下/左/右，pelvis 和 feet 不被上半身修正污染；
2. LookingDirection 静止旋转镜头能触发正确 L/R 90/180 Turn，无 actor snap 和明显脚滑；
3. Aiming 静止超过阈值时 Rotate clip 与 actor yaw 同步；
4. 移动、蹲伏、跳跃和 rotation mode 切换能正确取消/切换 Turn/Rotate；
5. 平地、斜坡和楼梯上双脚不漂移、不反折，pelvis 无明显跳变；
6. 平移/旋转平台上 Foot Lock 保持平台局部目标，平台切换和 teleport 正确释放；
7. HUD 显示 frame、mode、aim angles、turn/rotate、foot lock、pelvis、errors 和 worker timing；
8. 场景中始终只有一个可见生产 rig，不增加教程或说明文字。

手工验收补充观感，不能替代自动门禁。

## 十四、验证闭环

新增 `verify-p4.ps1`，执行顺序固定为：

1. Debug build；
2. P4 input/profile/curve focused tests；
3. P4 Core golden tests；
4. Aim/Layer/Turn/Rotate Godot smokes；
5. Foot/pelvis/platform Godot smokes；
6. P4 demo 300-frame smoke；
7. P4 1/10 single/parallel matrix；
8. repository Pester suite；
9. 完整 `verify-p3b.ps1` 非 Skip 回归；
10. Release tests；
11. locked-base repository closure、`git diff --check` 和 clean worktree；
12. 唯一 `P4_VERIFICATION_OK` 标记。

`-Focused` 允许输出诊断和各子门禁证据，但唯一顶层/终态成功标记必须精确为 `P4_FOCUSED_VERIFICATION_OK regression=skipped`，绝不能输出 full marker `P4_VERIFICATION_OK`。所有子进程输出必须捕获全部 PowerShell streams，并拒绝非零退出码、`SCRIPT ERROR:`、`ERROR:` 和显式 ALS failure marker。

## 十五、完成定义

只有同时满足以下条件，P4 才完成：

1. 固定 `b754d6f0...` reference、补丁和资产来源可追溯；
2. 曲线键值、root yaw canonical curve、additive base 和 mask 可以重复导出；
3. P4 profile 只通过 stable ID 和编译后 bone/curve ID 运行；
4. AimOffset、Head/Spine 和 upper-body layering 在真实 Mannequin 上正确；
5. Standing/Crouching Turn L/R 90/180 正确；
6. Standing/Crouching Rotate L/R 正确；
7. actor yaw 与 Turn/Rotate curve 同 phase，无双写和一帧视觉滞后；
8. flat/slope/stairs 的 Foot IK、Foot Lock 和 pelvis correction 正确；
9. 平移/旋转平台保持 platform-local lock，base change/teleport 正确释放；
10. Worker modifier 失败恢复精确完整姿态；
11. 可操作 P4 Demo 通过手工观感验收；
12. 1/10 single/parallel 所有摘要一致；
13. 前 10 个角色保持 60 Hz 全质量 P4 路径；
14. 稳态 model、curve、controller、modifier、skeleton、exchange、commit 七个托管分配桶和线程/帧/generation 错误为 `0`；`foot_gather` 分配透明报告但不进入 P4 零分配门禁；
15. `10/parallel` 的 P4 阶段 p95/p99 满足第十二节阈值；`10/single` 报告真实 timing，并通过功能、等价、错误和七桶零分配门禁；
16. P0-P3、Debug/Release 和资产导入回归全部通过；
17. 未修改 Godot Core，未维护引擎 fork，未无证据引入 GDExtension；
18. 文档记录 profile、曲线、mask、线程所有权、golden、性能和验收结果。

P4 完成后按总路线进入 P5A Curve/Notify/Sync/Action 基础，再进入 P5B Overlay gameplay 和 P5C Mantle/Roll/Root Motion。

## 十六、参考

- `docs/superpowers/specs/2026-08-25-godot-als-port-design.md`
- `docs/architecture/p3-basic-locomotion.md`
- `docs/superpowers/specs/2026-08-26-p3-basic-locomotion-design.md`
- `reference/als-refactored.lock.json`
- `scripts/prepare-p3-reference.ps1`
- `scripts/generate-p3-golden.ps1`
- `tools/unreal/AlsLocomotionTrace`
- `tools/unreal/AlsGodotExporter`
- `src/Als.Core/Contracts/AlsFrameInput.cs`
- `src/Als.Core/Contracts/AlsFrameResult.cs`
- `src/Als.Core/Contracts/AlsRuntimeState.cs`
- `src/Als.Godot/Animation/AlsLocomotionGraphBuilder.cs`
- `src/Als.Godot/Locomotion/AlsP3WorkerRoot.cs`
- `${env:ALS_REFERENCE_ROOT}\Source\ALS\Private\AlsAnimationInstance.cpp`
- `${env:ALS_REFERENCE_ROOT}\Source\ALS\Private\AlsCharacter.cpp`
- `${env:ALS_REFERENCE_ROOT}\Source\ALSEditor\Private\Modifiers\AlsAnimationModifier_CalculateRotationYawSpeed.cpp`
