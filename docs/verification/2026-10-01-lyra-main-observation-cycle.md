# Lyra Main 观察更新与真实 Cycle 根接入

2026-10-01，继续在主目录移植 GASP58 Lyra，使用本机 UE 5.8.1 / Godot 4.7.2 .NET。新增 Main 的前六段观察更新、共同候选宿主，以及真实 Main → Linked Cycle 连续原生对照。角色仍为 ALS 68 skin / 81 logical；本批不关闭完整 Main 或生产入口。

## 原更新顺序和数据来源

实际 Blueprint 函数顺序为：

```text
UpdateLocationData
 → UpdateRotationData
 → UpdateVelocityData
 → UpdateAccelerationData
 → UpdateWallDetectionHeuristic
 → UpdateCharacterStateData
 → 尚待统一接入的 BlendWeight / RootYaw / Aiming / JumpFall / clear FirstUpdate
```

外部 native 工具创建真实 transient ACharacter 和 CharacterMovementComponent，设置受控 actor 位置/旋转、Movement 的速度/加速度/MovementMode 及 crouch 状态。实际 PropertyAccess 的 GameThread pre/post、WorkerThread pre 批次采集它们，再顺序调用上述六个原 Blueprint 函数。每帧记录实际输入快照、调用前历史及六个阶段后的完整字段；没有手写 native 公式替代原函数，也没有保存资源。

Godot 接收的是实际 actor/Movement **输入**，从自己的已提交历史独立计算。仍未验证 Godot physics/quaternion 到厘米/double 输入的 gather 转换。FirstUpdate、GameplayTag ADS/Firing 和上一阶段 RootYaw 是显式外部输入，本批不提前调用后五段 Main 更新或清除 FirstUpdate。

原语义与精度边界：

- 位移使用世界 XY 两点差与原 SafeDivide；首次清零 displacement 和 speed，但保存当前位置；瞬移不另加距离钳制。
- 旋转沿用前批已确认的 BreakRotator float Yaw → double 相减与 float delta → double 分母。首次保留 yaw speed；Lean 使用此刻尚未刷新 crouch 的字段。
- 速度先记上一更新 LocalVelocity2D 是否 **精确为零**，再更新方向和两个独立方向枚举历史。CalculateDirection 返回 float 后提升 double；带 RootYaw 和不带 RootYaw 的方向分别维护，不能用最大坐标分类代替死区。
- FVector / Rotator / Matrix 为 double，Win64 SIMD RotationTranslationMatrix 仍使用 float DEG_TO_RAD 常量提升 double。完整 pitch/roll 参与局部向量变换。
- HasVelocity / HasAcceleration 的平方 XY 长度与 `1e-6` **double** 比较；不要因为函数名含 Float 就窄化。Normal 的 tolerance 则是 `1e-4` float。
- PivotDirection2D 由世界 XY 加速度的 Normal 与上一世界 Pivot 向量 Lerp(.5)，再 Normal；它不是局部加速度差分。方向映射为原 enum 对向。
- 墙面启发式保留原三个条件：local acceleration XY 长度 > .1、velocity XY 长度 < 200、完整归一化向量 dot 在 [-.6,.6] double 闭区间。
- 角色状态最后刷新 Ground/Crouch，比较旧 crouch 和 WasADS；Firing 置零计时，否则累加 delta。仅 MOVE_Falling 时按 WorldVelocity.Z > 0 区分 Jumping/Falling。

另外修正了新增绑定中的枚举适配：UE 的 Forward/Backward/Left/Right 索引为 0/1/2/3，现有 Godot LyraCardinalDirection 的声明顺序为 Forward/Left/Right/Backward，必须显式映射。没有重排既有 enum 或修改旧资源。

## Main → Cycle 实际链路

`LyraMainObservationHost` 持有前六段历史，Prepare 返回六阶段不可变候选；取消不发布，重复/迟到候选拒绝。`LyraCycleLayerPoseHost.PrepareObserved` 直接读取候选中的新 Crouch/ADS、无 RootYaw 方向、DisplacementSpeed、墙面标志和原 DirectionAngle，执行现有真实 Cycle 源与 Warp 绑定。

`LyraObservedCycleHost` 共同持有 Main 观察与 Cycle 候选；Prepare / Evaluate / ValidateCommit / Commit / Cancel 纳入同一 owner。外层执行一次共同 Sync，提交前先校验两份完整依赖，再串行发布 Cycle 与 Main。该宿主仍依赖外层供给原图 active/weight、HipFire 权重和组件快照；尚未加入 Main Lean、完整状态机权重/惯性根和其它 provider。

native 联合采集也使用同一真实 Main 实例：先执行前六段，再执行原 LinkAnimClassLayers 的 UpdateCycleAnim / exposed handlers、完整八节点 Cycle root Initialize/CacheBones/Update，共同 Sync 后 Evaluate 原 Local HipFire、Orientation/Stride Warp 链。没有把单独采集的 Main 输出抄成 Cycle 输入。三套 Unarmed/Pistol/Rifle 的 ALS81 transient 适配沿用已验证的原资源、mask、spine 与 raw source 算子。

Game actor/Movement 与组件世界变换作为明确输入快照提供给 Godot；原始 requests 中没有人工填写的 `main` 字段。RootYaw/ADS/Firing/First、Layer 权重和相关性依然是受控边界，不把本项称为完整 BlueprintThreadSafeUpdateAnimation。

## 验证结果

前六段独立连续对照：30/60/120Hz 各 12 秒，共 2520 帧 / 15120 阶段快照。已比较的标量、布尔和 native 方向值逐位一致；局部向量最大绝对分量差 `1.3642420526593924e-12 cm`，原本批 `1e-10 cm` 门槛未改。每帧取消/重试与 clean 更新一致；此处全部是无需 pose 的更新，拒绝 10080 个坏操作。

覆盖六次零 delta、三次 1e-6 delta、九次 first，312 个 wall、105 个 jumping、210 个 falling、九次 crouch 改变、六次 ADS 改变、四方向、低速/归一化门槛、旋转 pitch/roll、瞬移、世界 Pivot 历史和射击计时。

联合 Main → Cycle：三个 provider × 三频率 × 6 秒，共 3780 物理帧 / 22680 阶段快照、3528 次完整 Cycle 求值、285768 骨。源选择/惯性请求/rate/StrideAlpha、已比较的时钟、Marker 和 Warp pin 与 native 一致；pose 最大位置 `1.5543122344752192e-13 cm`、quaternion `1.3861819164579897e-15`、scale 0，保留原 `1e-8 cm / 1e-10 / 1e-12` 门槛。

14112 个整数属性逐项一致。本轨迹曲线输出均 absent，完整布局的存在性仍逐项比较；有值曲线继续由原 Cycle 105 / Lean composition 966 的回归覆盖。RootMotion 3406 次 present / 122 次 absent，3238 次非零区间位移；typed root 存在性与 TRS 逐项一致，root TRS 差 0。隐藏帧 252、HipFire tick 2205、tiny weighted tick 225、惯性请求 99。

联合宿主逐帧 Prepare、真实求值后取消/重试，检查观察/源/Warp 历史未发布；迟到候选、缺失求值、错时钟/区间/epoch 和重复提交拒绝，共 35469 次。隐藏帧保留观察更新及取消提交；**未验证联合 Cycle 活跃但跳过 Evaluate 的 update-only 提交**，当前 Cycle pose host 要求活跃帧先求值。

```text
LYRA_MAIN_OBSERVATION_GODOT_OK frames=2520 stages=15120
vectorCm=1.3642420526593924E-12 scalarAndFlags=exactBits retry=true updateOnly=true
LYRA_MAIN_OBSERVATION_CYCLE_GODOT_OK frames=3780 stages=22680 poseFrames=3528
observations=calculated callback=observedMain retry=true sharedCommit=true
production=false wholeMain=false
```

Debug 与 ExportRelease Optimize 均 0 错误/0 警告。运行验证使用 Debug .NET 构建；没有另做 Release 运行/渲染/人工观感/性能测试，亦未做本批多角色/并行宿主或完整 Main 验收。本批没有 Core 算法修改或额外 Core 全量测试。

原 Cycle native 已重采集，既有 requests/native/binding JSON 的语义/字节保留门禁通过；前六段 reader 提取共用 helper 后亦重采集并通过原 JSON 保留门禁。联合 native 两次独立采集相同，资源保存数 0。源/目标 492 包、原 234 logical clip 哈希复核通过。既有 UE 插件/SDK 等警告保留；没有 UE 源码、GASP58 插件/配置/资产修改。

Godot 原 Cycle 3528 帧 / 285768 骨 / 105 曲线 / root present3255、Lean composition 2100 帧 / 2121 根 / 966 曲线和独立 Rifle60Hz870帧/六次换层/871pose 均回归通过，无最终 ERROR/WARNING。

失败日志保留：首 native 编译的 TSharedRef Serialize 调用错误、共用 header 的 class/struct 前置声明不匹配均修复；首联合 smoke 的所有逐值比较已过，但误沿用旧轨迹 root present3255 的覆盖计数，改为新 native 明确记录的3406后通过。旧计数门禁继续保留原值，未放宽数值阈值。新 Cycle 方向映射的 cast 问题在完整联合验证前改为显式适配。

新增五份 ignored 资源：`main_observation_requests.json`、`main_observation_native.json`、`main_observation_policy.json`、`main_observation_cycle_requests.json`、`main_observation_cycle_native.json`。现有资源未覆盖/格式化。独立 verifier 检查实际输入/历史、位移/旋转/角色标志公式、阶段顺序、原 Cycle fresh pins、root 覆盖与包哈希。

主要证据在 `artifacts/lyra-analysis/main-observation-resource-verification.json`、`main-observation-final-verification.json` 和同目录 `main-observation-*-final.log` / `main-observation-*-regression.log`。重复执行：

```powershell
.\scripts\export-lyra-main-observation.ps1 -EngineRoot '../UE_5.8' -UnrealProject '..\GASP58\GASP58.uproject'
.\scripts\export-lyra-main-observation-cycle.ps1 -EngineRoot '../UE_5.8' -UnrealProject '..\GASP58\GASP58.uproject'
python tools/verify_lyra_main_observation.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . scenes/tests/lyra_main_observation_smoke.tscn
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . scenes/tests/lyra_main_observation_cycle_smoke.tscn
```

下一步完成后五段 Main 更新及原初始化/回调顺序，将此观察与真实 Cycle 根、Main Lean 合成、状态权重/惯性接为一个完整根；再推进其它 provider、共同 Notify/Montage、最终 FootPlacement/LegIK、Godot gather/生产入口和完整原生/视觉/性能验收。全部 ALS R2–R7、用户暂缓项及未提交修改保留。
