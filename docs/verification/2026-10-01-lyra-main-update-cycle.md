# Lyra 完整 Main 更新与真实 Linked Cycle 联合宿主

2026-10-01，主目录 `.`，GASP58 / 本机 UE 5.8.1 / Godot 4.7.2 .NET。完成原 `BlueprintThreadSafeUpdateAnimation` 的十段函数与末尾 FirstUpdate 清除，并接入原完整 Cycle 根的共同候选事务。仍使用 ALS 原 68 skin / 81 logical；完整 Main 动画根、其余 Layer 和生产入口继续开放。

## 原函数与运行边界

```text
Location → Rotation → Velocity → Acceleration → Wall → CharacterState
 → BlendWeight → RootYaw → Aiming → JumpFall → FirstUpdate=false
```

前六段复用 `LyraMainObservationHost`。新 `LyraMainUpdateHost` 持有自己的 FirstUpdate、RootYaw、Upperbody、Aim 和弹簧历史，不能再通过每帧输入覆盖 First/RootYaw。`CommitFinal` 在共同候选预校验后发布 First=false 和最终 RootYaw；取消、迟到/重复提交或下游失败不会发布任何历史。

- Upperbody：实际 Montage 存在且 Ground 为 true 时为 1；否则按原 double `FInterpTo(..., 0, delta, 6)` 衰减。
- RootYaw：Accumulate 先减本帧 YawDelta；Dashing 或 BlendOut 再执行原 float `FloatSpringInterp`。Hold 不推进根角、AimYaw 或弹簧；即使关闭 RootYaw，在 Hold 分支也不主动清零。每次更新结束恢复 BlendOut。
- SetRootYaw：先窄化为 float，经 double FRotator NormalizeAxis 返回 float，再提升 double 按本帧新站蹲的原 ClampAngle 范围限制。开启时 AimYaw = -RootYaw，关闭且实际调用 Setter 时两项正零。
- Aiming：真实 Controller 的 BaseAimRotation.Pitch，按原 float NormalizeAxis 后提升 double。
- JumpFall：真实 Movement GravityZ 的 float 返回值提升 double，Jumping 时为 `-WorldVelocity.Z / gravity`，其它状态为零。

RootYaw 原参数为 stiffness 80、critical damping 1、mass 1、target velocity amount .5、无输出 clamp、不从目标初始化。`AlsKismetFloatSpring` 接收原 stiffness，复用共享 SpringDamper 内核并显式选择 expanded InvExp；已有 Rig/ALS 调用默认保留 Horner 分支。原生完整函数轨迹证明 Kismet 此实例使用 expanded 运算边界；本批没有取得函数内部反汇编，不能将此结论表述为反汇编确认。验证范围为 Lyra 实际参数，未新增其它 Kismet 参数全矩阵。

## 原生采集

外部只读 exporter 在独立 transient GamePreview 世界中创建真实 Character、Movement、Controller，运行真实 PropertyAccess pre/post 与 worker pre 批次后，以 ProcessEvent 调用原完整函数。不是手工依次调用十个函数。原动画类、宏更新顺序、CDO 和来源资产均沿用当前只读导出及源包哈希。

RootYawMode 是上一图回调的外部边界；Dashing/ADS/Firing、开关、相关性、图权重与 HipFire 权重也仍为受控输入。Controller、实际 Actor 变换、Movement 加速度/速度/Gravity 与 Montage 状态由真实 getter 采集；请求文件保存这些 INPUT，Godot 不读取 native 预期输出作为输入。

Montage 用真实动态实例建立活动状态，停止时执行原生零时间 teardown。UE `IsAnyMontagePlaying()` 实际检查 MontageInstances 数组；仅 Stop 不会立即使数组为空。此处验证的是 Main 的活动状态门控，不验收播放中的 Montage 时钟、通知、Slot 姿态或 Root Motion。

完整更新与 Cycle 使用同一角色候选，由 `LyraObservedCycleHost(..., completeMain: true)` 接新 Main，继而执行实际原 Cycle callback、两个源、共同 Sync、LayeredBoneBlend、Orientation 与 Stride 根。Cycle 的 Warp pin 读取本帧计算的 DirectionAngle 与 DisplacementSpeed。各历史在整个候选通过后共同发布。

## 验证记录

最终计数、误差、构建和回归日志见 `artifacts/lyra-analysis/main-update-final-verification.json`；资源与输入边界的独立检查见 `main-update-resource-verification.json`。

- 原完整更新三频率 2520 帧，First 三次、Hold 630 帧、非零旧角 disabled+Hold 63 帧、Dash+Accumulate 53 帧；Montage 地面 630 / 空中 210 帧、Upperbody 衰减 626 帧，跳跃 105 / 下落 210 帧。取消重试、active update-only 与10086坏操作拒绝通过。
- 联合三个 provider / 三频率共3780帧，3528活跃/252隐藏；285768骨、14112整数属性、曲线全缺失的存在性、3406 RootMotion存在/122缺失/3238移动，以及99惯性请求通过。所有Main标量/flags/spring、源时钟与pin逐位同；向量最大1.3642420526593924e-12cm，pose最大1.5543122344752192e-13cm / quaternion1.3861819164579897e-15，RootMotion TRS误差零。共同候选每帧取消重试，35469坏操作拒绝。
- Debug 与 ExportRelease Optimize 构建均0错误0警告；Godot实际执行使用Debug。Core原生Rig/Pelvis3、Standing连续原生六组、旧Main观察→Cycle、旧Cycle根、Main Lean合成与60Hz Rifle Demo回归通过，最终Godot日志无ERROR/WARNING。既有Demo仍870物理帧/871姿态/6次换层。

初次 AimYaw 比较遇到 `-0`：UE JSON 数值为 0，但位模式为 `8000000000000000`。现从 native hex/uint bits 恢复 tail 的 double/float，保留符号。随后 60Hz RootYaw 首个 spring 更新出现约 1.9e-6° 差；按源 expanded 表达式修正 Kismet 分支，保持原逐位门槛。所有标量、spring 和布尔/枚举均严格比较，向量沿用前六段的每分量 1e-10 cm 门槛。

首版轨迹全数值通过后覆盖失败，发现 disabled+Hold 未带非零旧根角，Dashing 未与 Accumulate 重叠；补充真实输入窗口。原始五个 JSON 已逐文件 SHA256 验证后归档到 `artifacts/lyra-analysis/main-update-initial-capture/`，未覆盖任何之前交付的资产。第一次 Montage teardown 编译误直接访问 protected 方法，已通过派生访问器调用原方法，失败日志保留。

首个零时间 teardown 仅 Advance，终止后仍留 invalid entry，实际 getter 门禁使采集返回空并失败。已补原生 DispatchQueuedAnimEvents，按原生命周期在派发后删除 invalid entry；失败完整UE日志为 `main-update-ue-teardown-incomplete.log`。本批只读导出、无资产保存或引擎修改；UE项目已有GameplayTag/插件警告仍保留。

```powershell
.\scripts\export-lyra-main-update.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\scripts\export-lyra-main-update-cycle.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python tools/verify_lyra_main_update.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_update_smoke.tscn
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_update_cycle_smoke.tscn
```

## 余下完整目标

本批关闭的是完整 Main **更新函数**与所测 Cycle 根的联合执行。Main 的实际 LocomotionSM/状态权重/惯性、Start/Pivot/其它 Layer 根、三个 Lean 与真实主根合并、共同角色通知与 Montage、最终 FootPlacement/LegIK、Godot 实际角色 gather、生产入口与整链连续/视觉/性能验收仍开放。联合活跃 Cycle 仍要求 Evaluate 才能 Commit；完整 Main 更新组件支持 update-only，不能据此宣称完整角色已支持活跃 update-only。未做新渲染、人工矩阵、多角色并行、打包或 UE PIE 线程调度验收，原 ALS R2–R7 与用户暂缓项继续保留。
