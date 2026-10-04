# Lyra 左手姿态层与 FullBody Additives

2026-09-30，在 `.` 主目录实施，源工程为 `..\GASP58`，UE 5.8.1 / Godot 4.7.2 .NET。延续同一 ALS Mannequin 模型和 68 根物理骨；本批没有新增完整 Shotgun 移动资源，也不代表完整 Lyra 主图验收。

## 当前源图与配置

`export_lyra_pose_layer_contracts.py` 从当前 `ABP_ItemAnimLayersBase` 只读导出并抽取 `FullBodyAdditives`、`LeftHandPose_OverrideState`、`FullBody_SkeletalControls` 三个动画层，另导出 `SetLeftHandPoseOverrideWeight`、`UpdateSkelControlData` 两个函数。五段文本与 `GASP58/Saved/BP2DSL/Exports/20260913-134530-535215/Lyra` 保留快照逐字一致。

`pose_layer_contracts.json` 保存五段文本及 SHA-256、Base 资产哈希、九套 CDO 的源路径/启用标志/资产哈希和 inventory 字节哈希。Godot 加载时检查全部必需名称与数量、固定源文本哈希和九套配置，拒绝缺项或不同图版本。

整份 AnimBP 导出仍产生四条 `SKIP:LinkedPureExpression` 错误：Aiming 的两个 RotationOffsetBlendSpace X/Y 引用了 `LinkedInputPose`，BlueprintLisp 暂不能表达。UE 命令行因此退出 **1**，不能称整图导出无错误。三张本批目标图和两个函数仍成功导出，且逐字对照通过。包装脚本仅在“目标完整标记存在、唯一错误恰为这四条、UE 退出 1”时接受本批有限范围；其他错误立即失败，完整日志保留。

最终输出：

```text
LYRA_POSE_LAYER_CONTRACT_OK graphs=3 functions=2 profiles=9 assets_saved=0
LYRA_POSE_LAYER_SCOPE_OK engineExit=1 excludedAimErrors=4
```

证据：`artifacts/lyra-analysis/pose-layer-contract-scoped.log` 与 `pose-layer-contract-ue-full.log`。此前两轮严格要求 UE 退出 0 的失败及 Python 读取受保护 Graph.Nodes 的探测失败仍保留，未修改 UE 插件/引擎来规避错误。

## ALS 资源复用

九套实际 CDO 中，Unarmed/Pistol/Rifle 及各 Feminine 层默认关闭左手覆盖；Shotgun、ShotgunFeminine 启用，分别绑定 `MM_Shotgun_Idle_Hipfire`、`MF_Shotgun_Idle_Hipfire`。两条普通 Sequence 经现有 `RTG_UE5Manny_UE4Manny` 转到 ALS，目标位于 `/Game/GodotLyraRetarget/LeftHandPoses`。

`export_lyra_left_hand_poses.py` 用 UE 原始姿态读取器输出第 0 秒的 79 根逻辑骨、68 根物理骨映射及曲线，记录源/目标 `.uasset` 哈希，写入 `left_hand_pose_catalog.json`。已有目标和 JSON 不覆盖；复跑校验相同载荷，源 `.uasset` 导出前后哈希一致。首次导出与最终包装脚本复跑均退出 0：

```text
LYRA_LEFT_HAND_POSES_OK clips=2 physical=68 logical=79
```

最终复核 Base、九套 CDO 及两条序列的源/目标共 14 次资产哈希比较通过（Base 也在九套 CDO 中，因此包含一次重复资产比较）。哈希清单 `artifacts/lyra-analysis/pose-layers-hashes.json`；输出 `pose-layers-hash-ok.log`。

| 生成文件 | SHA-256 |
|---|---|
| `pose_layer_contracts.json` | `4890579941284FF6D1A0633939C1D47943F8A8B311F61AEB7CCF3D853CECF640` |
| `left_hand_pose_catalog.json` | `D794EA339CFB7389F5FC56924B259C68D3A3C93B150ACD376FB982AB50A2AA3A` |

两个 JSON 位于被 Git 忽略的 `assets/generated/lyra_als/`，仅有代码检出不能运行本批组件。两条 Shotgun 姿态只能用于左手组件验证，不能替代完整 Shotgun Locomotion/Aiming 资源。

## Interface 与 Linked Layer 的接入

沿用 `ILyraItemAnimationLayers` 的 typed 提供者和 `LyraLinkedLayerRouter`；主角色保存移动/RootYaw/源时钟，当前 Unarmed/Pistol/Rifle 提供各自 CDO 资源和姿态层实例。本批在接口增加 `CreateLeftHandPoseLayer`、`CreateFullBodyAdditivesLayer`，换层先构造并校验新实例，再恢复旧层输入、切换提供者。新 Additives 实例初始化状态，不从旧武器继承层内状态。

本批独立 Demo 的姿态顺序为当前 Locomotion 源 → 已接 HipFire → LeftHandPose → Aiming → RootYaw → FullBodyAdditives。下一帧和换层时按相反顺序恢复每层基底，避免叠加结果成为下一次输入。这里仍是现有单活跃源主机，尚未实现 UE Linked Anim Layer 的完整共享实例组、跨源同步/曲线合成或角色级姿态事务。

`LyraLeftHandPoseLayer` 按原函数计算 `clamp((enabled ? 1 : 0) - DisableLeftHandPoseOverride, 0, 1)`；第 0 秒 SequenceEvaluator 姿态经原 `LeftFingersMask` 的 ALS 映射，在 **局部空间**混合位置、旋转和缩放。空资产采用骨架参考姿态。当前三套普通运行层默认权重为 0；两个 Shotgun 启用配置在组件场景中真正混合手指，尚未接为普通可切换武器。

`LyraUnarmedTiming` 为已有源数据增加 `DisableLeftHandPoseOverride` 曲线读取，支持现有 Core 的 constant/linear/cubic、无权重切线及常量外推，不支持的资源显式拒绝。主机读取上一帧当前源的值供下一更新使用；这还不是原最终合成曲线/Montage 的统一反馈，不能作为完整曲线时序等价证明。

本机现有五份 timing（Unarmed 基础/辅助/剩余、Pistol、Rifle）均没有 authored `DisableLeftHandPoseOverride` 曲线，因此普通运行本批实际覆盖的是缺失值为 0 的路径。0/0.5/1 混合来自组件显式输入，新增曲线 JSON 解析分支尚未以真实包含该曲线的序列验证。

`LyraFullBodyAdditivesLayer` 实现原可达状态：初始 `Identity`，离地进入 `AirIdentity`。原 `AirIdentity → LandRecovery` 的规则是字面量 `false`，因此普通落地后仍保留 `AirIdentity`；两个可达空状态产生 identity additive。已有三套 Jump Recovery additive 保持资源状态，未人为启用原图关闭的落地分支。

`FullBody_SkeletalControls` 本批仅核对源图，未实现。其武器空间左手目标和 `weapon_r` 等 Manny 骨在 ALS 中缺失，仍需显式虚拟目标、手部 IK/足部节点及对应骨架策略。

## Godot 验证

最终 `dotnet build .\GodotALS.csproj -c Debug --no-restore -v quiet` 从 `..` 执行，0 警告、0 错误。Godot headless editor import 退出 0。

`scenes/tests/lyra_pose_layers_smoke.tscn` 使用实际 ALS 网格、变化中的 Rifle Cycle 基底和五套配置，分别在 30/60/120Hz 求值两秒，共 2100 帧：

- 启用配置覆盖 0/0.5/1 权重及大于 1 的 disable 输入钳制，实际产生 6954 次手指骨变化；关闭配置权重为 0。
- 未遮罩骨的位置/旋转/缩放逐值不变；遮罩骨与独立读取的 UE 参考姿态混合期望比较，四元数符号等价的分量差、位置与缩放距离门槛均为 `2e-6`。
- 每帧恢复输入姿态，位置/旋转/缩放逐值相同。另验 identity additive 输出保持原姿态、离地/落地状态及新层初始化。

最终输出：

```text
LYRA_POSE_LAYERS_OK profiles=5 frames=2100 changedFingers=6954 additive=Identity/AirIdentity landing=false skeleton=68
```

首轮测试使用 `Quaternion.AngleTo` 判断未变化骨，非完全单位四元数使相同分量也出现非零角度，测试失败保留在 `pose-layers-first.log`。改为未遮罩逐值比较、遮罩按符号等价分量比较；同时为 `%` switch 输入加显式括号并加入完整权重覆盖断言。没有放宽生产算法或骨骼归一化门槛。早期 `AlsCurveKey` 参数顺序编译错误已修正。

最终代码跑三套目录门禁，Unarmed/Pistol/Rifle 均退出 0；Rifle 仍为 64 clips / 192 poses / 324 routes，同一骨架三层共 189 普通源 / 1450 Notify / 650 Marker。日志 `pose-layers-{unarmed,pistol,rifle}-catalog-final.log`。

普通独立 Demo 复跑原 Rifle 换层轨迹，逐帧检查两层各求值一次、三套运行配置左手权重为 0、68 骨有限与归一化，以及实际跳跃落地和 Q 重新绑定后的 Additives 状态：

| Physics Hz | 物理帧 | 姿态层帧（含初始化） | Layer/Cycle 切换 | ADS/中间权重帧 | Rifle 通知/转身反馈 | 落地/重绑门禁 |
|---|---:|---:|---|---|---|---|
| 30 | 435 | 436 | 6/6 | 77/22 | 47/17 | 均通过 |
| 60 | 870 | 871 | 6/6 | 155/47 | 47/36 | 均通过 |
| 120 | 1740 | 1741 | 6/6 | 310/99 | 47/72 | 均通过 |

日志 `artifacts/lyra-analysis/pose-layers-rifle-{30,60,120}-runtime.log`。Pistol 60Hz 原换层回归亦退出 0，两次 Layer/两次 Cycle 换源、30 ADS 帧，见 `pose-layers-pistol-60-runtime.log`。最终四个组件/目录场景与四个普通运行场景没有 Godot ERROR/WARNING。没有新增本批渲染、完整 Shotgun 玩法、UE 连续主图姿态 oracle、人工或性能验收；既有 Rifle 渲染证据属于前一批。

## 复跑

```powershell
.\scripts\export-lyra-pose-layer-contracts.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\scripts\export-lyra-left-hand-poses.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . res://scenes/tests/lyra_pose_layers_smoke.tscn
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . res://scenes/demo/lyra_unarmed_demo.tscn -- `
  --lyra-rifle-switch-smoke --lyra-unarmed-hz=60
```

左手导出需要已有 version 2 外部 `AlsV4AssetExporter` 包（与前批相同），可用 `-ExternalPlugin` 指定路径。源图包装脚本的 scoped 标记只说明本批目标完整，不关闭整图四条 Aiming 导出错误。SkeletalControls/Warping/完整状态图、Shotgun/Feminine 普通资源、跨源通知与共享 Sync、主图连续原生对照仍待。原 ALS R2–R7 状态不变。
