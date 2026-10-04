# Lyra Main Lean 原资源与 ALS81 静态采样

2026-10-01，工作目录 `.`。沿用 GASP58/UE5.8.1 当前资产、已有 Retargeter 和 weapon 控制标定，Godot4.7.2mono。只关闭原资源与静态采样；完整 Lyra 移植继续开放。

同日后续 [Main Lean runtime 验证](2026-10-01-lyra-main-lean-runtime.md) 已完成三个原播放器的平滑/时钟/共同 Sync 源组件，并在全新目录生成三个专用目标，验证首次生成 ensure 修复且 24 组姿态差为 0。以下为资源批当时的边界及失败记录；Main ApplyAdditive、完整主图和生产接入仍开放。

## 原图配置

Main 编译节点12/16/22分别属于 Start/Cycle/Pivot，property index90/86/80。三者是独立 BlendSpacePlayer，引用同一个 `BS_MM_Rifle_Jog_Leans`，loop/ignoreRelevancy=true、DoNotSync、rate1/start0/resetOnAssetChange=true；各分支把 Lean 通过原 ApplyAdditive 接到其 linked base pose。此处归属由后续 [编译 ApplyAdditive 链接验证](2026-10-01-lyra-main-lean-composition.md) 纠正；资源顺序、已有三源 native 数值及字节哈希不变。

| 原样本顺序 | 动画 | LeanAngle | 新 slot |
|---|---|---:|---|
| 0 | MM_Rifle_Jog_Lean_Center | 0 | main_lean_center |
| 1 | MM_Rifle_Jog_Leans_Left | -20 | main_lean_left |
| 2 | MM_Rifle_Jog_Lean_Right | 20 | main_lean_right |

三序列均是 `AAT_LocalSpaceBase / ABPT_AnimFrame / frame0`，基底是 Center（Center 自引用）。两key，playLength0.03333333507180214秒。无源曲线、骨属性或 transform curves。所有 sample rateScale1，singleFrame/mirror=false。

原 BlendSpace 是1D两段，归一化顶点0/.5/1、sample索引[1,0]/[0,2]，轴范围[-20,20]，禁用grid/wrap/axisToScale。三个轴 filter time 均0；**sample weight speed3、EaseInOut=true**。allowMeshSpaceBlending=false，allowMarkerBasedSync=true、matchSyncPhases=false、legacySampleLength=true、最高权重Notify、无perBone overrides。静态插值不能代替运行平滑。

后续 Main 输入仍需执行原 `UpdateRotationData`。同日后续验证确认：ActorYaw与上一WorldRotation的两次 BreakRotator Yaw 先输出 float，再提升 double 相减/除 float DeltaSeconds，乘原站立.0375或蹲伏/ADS .025系数；首更新只清零 Yaw差和Lean，保留已算速度，WorldRotation仍保存原double。资源批当时没有把该输入或三个播放器接普通运行入口；组件进展见上述链接，完整Main与生产入口继续开放。

## 资源与取样

仅在 GASP58 `/Game/GodotLyraRetarget/MainLean` 新建三个 ALS 目标 Sequence。现有源/目标489包及234旧logical clip字节哈希全部不变，最终来源闭包492包。新JSON位于 ignored `assets/generated/lyra_als/main_lean/`，不能用仅有代码的检出交付运行。

同一 `LyraLogicalSourceBank.Load(includeMainLean: true)` 验证原catalog/calibration/source_nodes/inventory依赖，再在原库尾部增加三个source；同一81骨、同一curve/attribute布局，不另建角色骨架。源计数237、additive48；普通旧调用仍显式默认234。新增 definition 区分 LocalSpace 与 MeshSpace additive，原45个Aim按旧算法采样，Lean按原frame0局部差分采样。

原68蒙皮骨仍直接发布，69raw/81logical包含weapon与虚拟控制通道。UE每次比较目标79logical与扩展81logical的前68骨，24个源时间点的skin差为0；新增控制骨不改变现有渲染模型或权重。

`LyraMainLeanBlendSpace` 只执行原静态1D权重、顺序、裁剪与归一化；先用double归一化输入，再窄化float计算分段权重。采样时间保留原float normalized×float playLength后提升double的边界，所有局部additive按原权重累积后一次normalize。没有运行clock、平滑history、Sync或root属性生产逻辑。

## 验证结果

UE直接 raw/animation pose各24组（每源8时间点，含边界、负时间及越界），11种角×3normalized时间共33个原 BlendSpace pose。Godot完全自行计算权重/源采样/合成，native值仅用于比较。

```text
LYRA_MAIN_LEAN_RESOURCES_NATIVE_OK sources=237 logical=81 skin=68
localSamples=24 staticBlends=33 oldSamples=936
positionCm=8.331852114593072E-15 quaternion=2.336249397476605E-16 scale=0
oldPositionCm=1.4163191318420816E-13 oldQuaternion=6.58317845524286E-16 oldScale=0
scope=resourcesAndStaticPose runtimeSmoothingAndClocks=pending
```

原门槛保持 position1e-8cm/quaternion1e-10/scale1e-12，权重/时间逐位一致。每次Lean取样还验证独立occurrence和全局曲线/属性absence；扩展库直接重验旧936组raw/输出，包含原mesh Aim，保证共享扩展不改变旧源。

Debug和Release Optimize均0错误0警告；原Cycle runtime3528活跃帧/285768骨、root TRS0差、源/引脚逐位对照及旧Demo60Hz870帧/六换层/871姿态发布回归通过，最终三个Godot日志无ERROR/WARNING。没有Core算法修改，未额外重复Core测试；未新增渲染/人工玩法/全量/性能验收。

`tools/verify_lyra_main_lean.py`核对七个新JSON文件、492包及旧234clip哈希、原source配置、依赖、原additive基底/空metadata与native覆盖。详细SHA/大小见 `artifacts/lyra-analysis/main-lean-resource-verification.json`；catalog SHA `74a74d06d220005099d557f65b3f14e519a926eedc2ad1476ad3866c889ccfa7`。

## 失败证据与复跑边界

首inventory调用向原triangulation reader误传数组，改为其对象协议后成功；日志 `main-lean-inventory-first.log`/`-corrected.log`保留。

首次目标生成中，动画压缩触发 `BoneContainer.h:616 SkeletonToCompactPose.IsValidIndex` ensure，随后Python误写反射名称 `pose2_d`，进程-1，不能算成功。已修正Python名称为实际 `pose2d`，三个已生成目标的两次独立UE重导退出0、无ensure/assert/Python error，旧资源JSON语义一致且字节未覆盖。初次生成ensure根因尚未通过新的空目录重建证明修复，**首次冷生成仍是独立验收项**；不删除这三个资产或覆盖旧目标来掩盖证据。首完整UE日志保留为 `main-lean-resources-ue-first.log`，最新完整日志为 `main-lean-resources-ue-full.log`；wrapper明确拒绝native错误和ensure。

首Debug构建误用歧义FileAccess，已补Godot限定；首Godot对照发现normalized乘法误用double长度，修为原float乘法后通过。`main-lean-debug-build-first.log`、`main-lean-godot-first.log`保留，未提高误差门槛。其余证据及最终门禁记录均在 `artifacts/lyra-analysis/`。

```powershell
.\scripts\export-lyra-main-lean.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python tools/verify_lyra_main_lean.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_lean_resources_smoke.tscn
```

后续必须补三个真实player的权重速度历史/clock、原节点Initialize/Update/共同Sync/Evaluate、Main ApplyAdditive，再与现有Cycle/原状态权重及最终inertia合成。原Interface入口/provider实例和其余Start/Stop/Pivot/空中/Idle闭包、统一Notify/Montage、最终FootPlacement/LegIK、生产入口与全链原生/视觉/性能验证继续开放，原ALS路线及用户暂缓项保留。
