# Lyra 原 Cycle 根图、Main 输入绑定与 ALS81 对照

2026-10-01，在 `.` 主目录继续实施。原生端为 GASP58 / 安装版 UE 5.8.1，运行端为 Godot 4.7.2 .NET，保持 ALS 原 68 skin / 81 logical。本批把原 Cycle 的 Main 属性绑定和本帧 Layer 回调接到双 Warp，并将 Godot 输出与同一次原编译 Cycle 根图 Update → 共同 Sync → Evaluate 对照。

关闭范围为三个 provider 的完整八节点 **Cycle 局部根图组件**及所测候选生命周期。Main 输入仍为受控快照；原 Main 全部更新、其余 Layer、角色通知/Montage、最终足部与生产入口仍开放。独立 Demo 回归不代表新组件已经接入普通玩法。

## 原绑定与更新顺序

实际 Cycle 编译闭包为 Root52 → CS-to-local46 → Stride45 → Orientation48 → local-to-CS47 → LayeredBlend50 → Cycle player51 / HipFire evaluator49。空间转换和两 Warp 共用组件姿态缓存，HipFire 与 Cycle 的源登记顺序沿用原子图。

原图的绑定为：

| Warp 引脚 | 原来源 | 类型边界 |
|---|---|---|
| Orientation 的 LocomotionAngle | GetMainAnimBPThreadSafe.LocalVelocityDirectionAngle | Main double → float pin |
| Stride 的 LocomotionSpeed | GetMainAnimBPThreadSafe.DisplacementSpeed | Main double → float pin |
| Stride alpha | 当前 Layer.StrideWarpingCycleAlpha | 回调 double → float alpha |

Cycle 使用不带 Offset 的角度，Start/Pivot 的角度绑定不同。原 Orientation alpha=1、Direction=(0,0,0)，并非此前手工 Warp fixture 的可变输入。

原 `FAnimNode_SkeletalControlBase::Update_AnyThread` 先更新子节点，再执行本节点 exposed handler。因此 Stride 必须读取本帧 `UpdateCycleAnim` 产生的 alpha。Godot 的 `PrepareBound` 先生成真实 source callback 候选，再从同一候选取得 alpha，随后准备 Orientation/Stride；禁止该宿主绕过绑定接受手工 Warp 输入。

`cycle_runtime_bindings.json` 的路径来自已冻结原图，四个属性的 double 类型来自实际编译对象反射，绑定值由所有活跃帧的原 exposed handler 读回证明。没有声称自动导出了完整 PropertyAccessLibrary 或完整 Blueprint VM。宿主仍须提供真实 Main 快照和组件上下文。

## 同一次原生根遍历

新增外部 exporter API `ReadCycleRuntimePoseTrace`，扩展既有 Cycle reader。临时 GamePreview 世界中登记真实 Main 与 ItemAnimLayers，调用原 FPoseLink 的 Initialize/CacheBones/Update、原暴露输入和回调；同一遍历登记全部源，在原 common Sync 完成后对同一根调用 Evaluate。输出包含完整 ALS81 local TRS、曲线、整数属性和生成的 RootMotionDelta，没有把旧 pose oracle 作为节点输入。

42 条实际源序列按规范 target asset path 映射到 transient ALS81 Sequence，避免用文件叶名代替身份。源 MatchSpeed 的 length/rootDistance 位值与既有资源定义逐帧核对。骨骼遮罩、spine04/05→spine03 去重仅在临时实例上适配，原节点的其它配置和实现保持原样。

登记阶段使用原兼容 Manny carrier 的 transient duplicate；登记后，仅该临时 mesh 的 Skeleton 切为 ALS81，Layer proxy RequiredBones 设置 ALS81、RAW=true、保留 translation retarget。这样原 LayeredBlend 的 skeleton/mask cache 与实际求值布局一致。carrier 的 geometry 不渲染、不做角色物理，不是一个经过验证的 ALS 蒙皮显示宿主。没有访问 proxy 的私有 Skeleton 字段，也没有修改或保存原 mesh/Skeleton。

Main 的方向、速度、站蹲/ADS/wall 等字段由请求快照设置，组件 yaw 覆盖 0/45/-90/170°。本批执行的是 Cycle 根的原 handler 与回调，不执行完整 Main EventGraph/BlueprintThreadSafeUpdateAnimation，也不 tick 世界。

每条轨迹给不带 Offset 与带 Offset 字段设置不同值；速度增加不能精确表示为 float 的小数，角度覆盖负数小数、±180 附近以及 360/720。独立 verifier 检查：

- 全部 3528 活跃帧读到不带 Offset 的角度，与错误字段的 float 值不同。
- 1479 个角度、全部 3528 个速度发生实际 double→float 舍入，原 pin 位值一致。
- 3519 个部分 Stride alpha、9 个零 alpha，3204 次相邻活跃帧 alpha 改变。
- 3780 物理帧、252 隐藏帧；隐藏帧无姿态输出。
- 原 2205 HipFire tick、225 个最终微小权重 tick、108 惯性请求均保留。
- 3255 个 root presence / 273 absence；2956 帧具有非零 root translation。

两次独立 UE 导出成功，第二次通过已有资源语义相等门禁并保留文件字节。最终 UE 退出 0，无 ensure/assert/Python Error；源项目既有编辑器/GameplayTag 警告保留。489 个包哈希复核未变，assets_saved=0。只构建外部 exporter，未修改引擎、GASP58 源码或工程配置。

## Godot 实现与严格对照

`LyraCycleRuntimeBindings` 验证字节依赖、四个原字段类型、路径和常量。新的 bound 模式仍由调用角色持有共同 Sync； source clocks、marker records、typed root、双 Warp history 和 pose 归同一个候选提交。输入无效发生在 source Prepare 后时，取消该 source 候选，重试不遗留 pending。

`lyra_cycle_runtime_smoke.tscn` 使用自己的 source callback/common Sync、raw 采样、HipFire blend、RootMotion 区间与双 Warp 求值；期望值只用于对照，不驱动播放器/节点。源时钟、playrate、双精度 alpha、marker、inertia、原 pin 位值逐帧核对。姿态/曲线/整数属性与 root 一并比较。

每个活跃帧在求值后取消，确认已提交的源和两 Warp history 未变，再重试并逐值比较。旧候选、重复/未求值/隐藏求值、不同 Sync time/root interval、迟到 HipFire epoch 均拒绝。每个物理帧还故意传 NaN Main angle，并尝试绕过 bound 模式，确认 source Prepare 之后的失败不发布历史。总坏操作拒绝 31689 次。

```text
LYRA_CYCLE_BINDINGS_GODOT_OK frames=3528
angle=Main.LocalVelocityDirectionAngle speed=Main.DisplacementSpeed
alpha=freshCallback doubleToFloat=nativePins retry=true

LYRA_CYCLE_LAYER_POSE_GODOT_OK frames=3528 bones=285768
curves=105 attributes=14112 dataProbes=72
positionCm=1.0775914125888947E-13 quaternion=8.968100940422724E-16 scale=0
retry=true stage=OriginalCycleRoot generatedRootMotion=True production=false

LYRA_CYCLE_RUNTIME_GODOT_OK frames=3528 bones=285768 rootPresent=3255
rootPositionCm=0 rootQuaternion=0 rootScale=0 retry=true
stage=OriginalCycleRoot production=false wholeMain=false
```

原门槛保持 position 1e-8 cm / quaternion 1e-10 / scale 1e-12。旧 authored 数据算子的 72 个原生探针仍用于相同 LayeredBlend 算子检查。

首次完整 Godot 对照在 Unarmed/30Hz/frame21 的根旋转差 2.235174179398752e-8，超出原门槛。定位原 `FRotator::NormalizeAxis(float pin)` 实际提升到 double 做 Fmod/±360，再窄化回 float；Core 原 float 加减使负数小数角丢失精度。按原调用修正 Core，没有提高阈值或替换 oracle。新增三个负数小数角测试，检查角度输入与其独立方向向量产生相同 Warp。

Core Orientation/Stride/RigTwoBoneIK 共19项、Standing 原生连续6项通过，无失败/跳过；Debug 和 Release Optimize 均0错误0警告。旧 Orientation、Stride 与 source 隐藏重初始化回归通过，旧独立 Demo 60Hz 870物理帧/六换层/871最终姿态发布通过。最终这些 Godot 日志无 ERROR/WARNING。本批没有新增渲染、人工玩法、全量或性能验收。

## 失败证据与资源

首次 native reader 编译误尝试访问 `FAnimInstanceProxy::Skeleton`，C2248 拒绝；已改用上述 transient carrier。首次构建日志 `cycle-runtime-reader-build-first.log` / `-first-full.log` 保留，最终 `cycle-runtime-reader-build-carrier.log` 成功。首 Godot 精度失败日志 `cycle-runtime-godot-first.log` 保留，修正后为 `cycle-runtime-godot-normalize.log`。

新资源仍在 ignored `assets/generated/lyra_als/`，未格式化或覆盖旧资源。仅有代码的检出无法运行该场景。

| 新资源 | SHA-256 | 字节数 |
|---|---|---:|
| cycle_runtime_bindings.json | `a9e0e277a0804acd8ac294c5ea243ae57634822eaeb10d231221aa0bba77d15f` | 2138 |
| cycle_runtime_requests.json | `cc51b3a989b62f0eda29026c4ed83ba24c58b15b2cd5e69a4a9177cc0245a96b` | 1610811 |
| cycle_runtime_native.json | `b195c942cde3585ca4dcfb3f6a98e1c5b7dcb6d36cada751e7f9eb4ecb2319ff` | 61426004 |

证据目录 `artifacts/lyra-analysis/`：native 两次导出/完整 UE 日志、reader 构建、Godot 首失败/修正、verifier JSON、Core/Standing TRX、Debug/Optimize 和三个旧组件/独立 Demo 回归。最终门禁清单为 `cycle-runtime-final-verification.json`。

复跑：

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-cycle-runtime.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python tools/verify_lyra_cycle_runtime.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_cycle_runtime_smoke.tscn
```

下一步以真实角色观测驱动完整 Main 更新，补齐原 Main 三个 Lean 源及其它 linked 入口的回调/源库存，把各入口纳入同一角色 bank/Sync、Main 原状态权重和惯性链；随后统一 Notify/Montage、FootPlacement/LegIK 和普通入口替换，补全连续原生、视觉和性能验收。旧 Demo 的全局 HipFire、单 phase clock 和线性 crossfade 尚未由该 Cycle 组件替换。整个 Lyra 移植目标继续开放，原 ALS R2–R7 与用户暂缓项保留。
