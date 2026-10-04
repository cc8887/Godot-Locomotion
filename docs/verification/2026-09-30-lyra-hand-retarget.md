# Lyra SkeletalControls 首节点：Hand IK Retargeting

2026-09-30，主目录 `.`，源工程 `..\GASP58`，UE 5.8.1 / Godot 4.7.2 .NET。本批实现并接入 `FullBody_SkeletalControls` 的首个 Hand IK Retargeting 节点，保留完整手部/足部链路的后续工作；不是整个 SkeletalControls 已完成。

## 源规则与目标空间

沿用上一批已对当前源图逐字校验的 `pose_layer_contracts.json`，并读取本机 UE 5.8 `AnimNode_HandIKRetargeting.cpp`、`AnimNode_TwoBoneIK.cpp`、`BonePose.h` 的实现。

原顺序为：Local→Component → Hand IK Retargeting → 将 `VB IK_Hand_L_weaponSpace` 的位置/旋转复制到 `ik_hand_l` → 根骨偏移 → 右手 TwoBoneIK → 左手 TwoBoneIK → FootPlacement → LegIK → weapon_r 缩放 → Component→Local。右手保留末端原旋转，左手取 Effector 旋转；肘目标分别为 lowerarm 本地 ±50 cm 的 Y 方向。原图的 `DisableHandIK` 影响左右手求解 Alpha，**不关闭前面的 Hand IK Retargeting**。

`export_lyra_skeletal_control_defaults.py` 从当前 CDO 和两个 Skeleton 输出 `skeletal_control_defaults.json`：

- 九套 Item Layer 的 `Hand FKWeight`、两侧初始 HandIK Alpha 均为 1。运行 Alpha 后续由 `UpdateSkelControlData` 的 CDO 开关与曲线计算，不能把初始 1 当作 Unarmed 启用求解。
- 主 AnimBP CDO 的 `UseFootPlacement=false`、`EnableControlRig=false`。这些只证明默认值；`ShouldEnableControlRig` 仍会按曲线及 UseFootPlacement 更新，不能据此删掉根骨/足部控制。
- Manny 为 161 物理/164 逻辑骨，ALS 为 68 物理/79 逻辑骨，导出全部名字、父骨、参考姿态、映射、重定向模式及虚拟骨定义。
- **实际 `VB IK_Hand_L_weaponSpace` 为 weapon_r → FK hand_l**，不是 weapon_r → ik_hand_l。后续需要保留姿态处理前的这个相对关系，再随武器空间重建左手目标，不能直接用现有 IK 左手代替。

已有 Unarmed 的 9 个网格与 7 个非网格原生 Aim 姿态中，weapon_r 与 hand_r 的 mesh additive 旋转差（同向化后）最大约 `1.32e-16`，说明这些样本的武器相对手旋转保持不变。这是后续映射的线索；Pistol/Rifle、所有资源和完整连续层图尚未做同范围检查，不能据此关闭武器空间映射。

## 实现与生产接入

Core 新增 `AlsHandIkRetargeting.Retarget`，在 UE component 轴向和厘米单位计算 FK/IK 手位置差，按 `HandFKWeight` 选择左/右或双手插值；保持原 `IsRelevant`、`IsFullWeight` 和位移 nearly-zero 门槛，只修改 gun 的位置。

`LyraHandRetargetPoseLayer` 验证实际 ALS 68 骨序和父子关系、九配置中的当前 CDO 权重及 inventory 字节哈希。Godot 输入局部 TRS 转到 UE 轴向/厘米后合成 component 姿态，计算移动 gun 的目标，再按 skeletal-control Alpha 在局部空间混合平移；只写 `ik_hand_gun` 的局部位置。`ik_hand_l/r` 是其子骨，继承位移。单个局部平移算子不需要重建或增加物理骨。

在 `ILyraItemAnimationLayers`/Router 增加 typed factory，Unarmed/Pistol/Rifle 都构造当前配置的节点实例。独立 Lyra Demo 在原 Additives 后执行这一节点；下一帧及换层时先恢复其基底，再恢复 Additives/RootYaw/Aiming/LeftHand/HipFire。组件内计数直接用于逐帧和重新绑定的检查，不另建播放时钟。

`LyraUnarmedTiming` 将已接的 left-hand 曲线字典扩为 `(slot, curve)` 键，增加 `DisableHandIKRetargeting`；当前主机使用上一帧当前源曲线值计算 `clamp(1-value,0,1)`。现有五份 timing 没有 authored 手部控制曲线，因此普通运行覆盖缺失值 0 / Alpha 1；曲线合成/Montage 和真实 authored 曲线时序仍待，原 JSON 解析支持范围同上一批。

本批没有用此节点代替左右手 TwoBoneIK，没有把 `CreateHandRetargetLayer` 命名为整个 `FullBody_SkeletalControls` 的完成实现。网格手臂尚未由本批的新求解器驱动；修正的是后续求解器要使用的 IK 目标。

## 实际 UE 节点 oracle

在 Godot 仓库的 `tools/unreal/AlsV4AssetExporter` scaffold 增加 `UAlsHandControlLibrary::ReadHandRetargetPose`。读取 ALS 目标动画 RAW 姿态，调用 **实际 `FAnimNode_HandIKRetargeting::EvaluateSkeletalControl_AnyThread`**，再调用实际 `FCSPose.LocalBlendCSBoneTransforms` 和安全 Component→Local 转换，保存前后 79 逻辑骨。没有改 UE 引擎源码或部署插件到 GASP58 工程。

外部 version 2 包通过 `build-gasp58-exporter.ps1 -ExternalOnly` 构建，最终 BuildPlugin 退出 0，日志 `artifacts/lyra-analysis/hand-retarget-reader-build-populated-pose.log`。新增 UFUNCTION 不改既有导出 API。它是只读节点采集工具，仍需本地构建的包才能复跑。

三套已重定向 Forward Jog Cycle 各取 0、37%、75% 时刻；九个 FK 权重包含负值、0、relevance 门槛、0.25/0.5/0.75、full-weight 门槛、1、超过 1；四个 Alpha 为 0/0.25/0.5/1，共 **324 组**。collector 对每组都直接评估节点，再施加指定 Alpha；Alpha 0 的 collector 仍会访问 FK/IK component 缓存，与完整主图“无关节点不求值”的访问范围不同，不能作隐藏图时序证明。

`export-lyra-hand-retarget.ps1` 顺序运行默认值和 oracle 两个 UE 进程，最终都退出 0、无 UE Error；输出：

```text
LYRA_SKELETAL_CONTROL_DEFAULTS_OK ...
LYRA_HAND_RETARGET_NATIVE_OK clips=3 cases=324 physical=68 logical=79 assets_saved=0
```

初次采集因 `ConvertComponentPosesToLocalPosesSafe` 的输出 CompactPose 未先拷贝/分配导致 Array 越界、UE 退出 3，失败日志 `hand-retarget-native-export.log` 保留。已改为用当前 Pose 初始化输出后调用转换，最终重新采集成功；没有用重试容忍该断言。

## Godot 和 Core 对照

`scenes/tests/lyra_hand_retarget_smoke.tscn` 对全部 324 组原生输入运行 Core，比较所有 79 逻辑骨；当前 CDO FKWeight=1 的 36 组另在真实 ALS Skeleton3D 上验证局部平移、其他局部骨逐值不变、左右 IK 目标的真实全局继承位移及基底逐值恢复。

最终输出：

```text
LYRA_HAND_RETARGET_OK cases=324 changed=162 partial=162
nativePositionCm=6.934620449763122E-14 nativeQuaternion=2.768664317900422E-16
godotPositionM=1.1920929E-07 skeleton=68
```

Core 位置门槛 `1e-8 cm`，单位四元数符号等价分量范数门槛 `1e-10`，缩放门槛 `1e-12`；Godot 单精度输入/输出位置和目标传播门槛 `2e-6 m`。这证明首节点的位置运算和姿态恢复，不是最终手臂/武器姿态 oracle。

首轮测试比较未归一化的原始四元数，出现 `3.687e-8` 差并失败，位置已为 `6.93e-14 cm`。UE CS→Local 会归一化访问过的骨；独立复核归一化后的非 gun 骨方向差只有约 `2.62e-16`。测试现验证源 quaternion 模长误差小于 `1e-6`，再比较单位旋转，保留原 `1e-10` 方向门槛，未放宽阈值。初失败与最终结果分别见 `hand-retarget-native-smoke-first.log`、`hand-retarget-native-smoke-final.log`。

最终 .NET Debug build 0 警告/0 错误，Godot editor import 退出 0。上一批五配置 2100 帧 LeftHand/Additives、Unarmed/Pistol/Rifle 三套目录门禁全部回归通过，日志 `hand-retarget-{pose,unarmed,pistol,rifle}-catalog.log`。

普通 Rifle 换层轨迹在 30/60/120Hz 仍为 435/870/1740 物理帧、六次 Layer/六次 Cycle 换源、47 条 Rifle 通知；各自 HandRetarget 调用 436/871/1741 次（含初始化），逐帧次数与物理帧对应，跨层累计不丢失。最大移动 gun 的未缩放修正位移约 31.312/31.304/31.301 cm。ADS、五段空中、蹲伏、原地转身、落地 Additives 状态和重新绑定初始化原门禁保持通过。Pistol 60Hz 两次换层/两次 Cycle/30 ADS 帧回归通过。日志 `hand-retarget-rifle-{30,60,120}-runtime.log`、`hand-retarget-pistol-60-runtime.log`，无 Godot ERROR/WARNING。

未新增渲染、人工玩法或性能验收；原普通 ALS Demo 不切到 Lyra。本批 .NET 最后一次修改只增加原生 smoke 的单位旋转比较及目标传播检查，运行时/Core 算子之后没有再修改。

## 资产和复跑

独立复核三套 Cycle 的源/目标 `.uasset` 六次及九套 CDO 九次，共 15 次资产哈希比较；全部仍与原 catalog/合同相同，清单 `artifacts/lyra-analysis/hand-retarget-hashes.json`。

| 生成文件 | SHA-256 |
|---|---|
| `skeletal_control_defaults.json` | `AE22118834008DEEDE96BE6500C5125E21FD07194DCAA1D806487572DED244F7` |
| `hand_retarget_native.json` | `80BB8765D22C71FEEF7F9C2B99D5544486A4D490EFDAE15F79CCC8F68046371F` |

两个文件均在 ignored `assets/generated/lyra_als/`，保持字节级依赖、不统一格式化。已有输出复跑只做语义比较，差异立即拒绝。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-hand-retarget.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . res://scenes/tests/lyra_hand_retarget_smoke.tscn
```

下一步仍为原武器空间虚拟目标 → 根骨 bool blend → 两侧 TwoBoneIK → 足部/LegIK/weapon_r，再完成主图 Warping、多源 Sync/曲线/通知、Shotgun/Feminine 资源、完整连续原生及人工/性能矩阵。本批只关闭 Hand IK Retargeting 节点，整个 Lyra 移植与 ALS R2–R7 保持开放。
