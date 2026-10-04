# Lyra TwoBoneIK 原生对照与右手接入

2026-09-30，直接在 `.` 主目录实施。源为 GASP58 / UE 5.8.1，运行端 Godot 4.7.2 .NET，继续使用 ALS Mannequin 的 68 根物理骨。本批接入右手 TwoBoneIK；左手武器空间目标、完整 SkeletalControls 和整个 Lyra 移植仍开放。

## 原图边界

读取当前 `pose_layer_contracts.json` 的原 `FullBody_SkeletalControls` 与 `UpdateSkelControlData`，沿用实际源图哈希门禁。原顺序为 Hand Retarget→武器空间 CopyBone→根骨偏移→右手 TwoBoneIK→左手 TwoBoneIK→FootPlacement→LegIK→weapon_r 缩放。

右手：`hand_r`，目标 `ik_hand_r` 的 BoneSpace 零偏移；肘目标为 `lowerarm_r` 的 BoneSpace `(0,50,0)` cm，保持原 FK 末端 component 旋转。左手：`hand_l`，目标 `ik_hand_l`，肘偏移 `(0,-50,0)` cm，采用目标旋转。默认允许 twist、不拉伸、不保持末端相对旋转，已由原生采集输出再次校验。

原更新公式为 `clamp((DisableHandIK ? 0 : 1) - DisableRHandIK, 0, 1)`，不是直接使用 CDO 初始 Alpha=1。当前九配置中 Unarmed/MM 与 Feminine/MF 的 DisableHandIK=true，其余为 false。此开关不影响前面的 Hand Retarget。

已有真实 Skeleton 元数据进一步确认 Manny 的 `weapon_r` 为第 88 根物理骨，父骨为 `hand_r`，`VB IK_Hand_L_weaponSpace` 的定义是 weapon_r→FK hand_l。ALS 不具备该骨/虚拟目标。不能把 IK 左手当成原 CopyBone 的来源，也不能在控制节点处重新取 FK 左手而丢失前面动画和瞄准的相对关系。

本批右手节点读取 FK 右臂、IK 右手和肘目标；前面的 CopyBone 只改 IK 左手，不改变这些输入。当前 `EnableControlRig=false` 的根骨偏移也不启用。因此先接右手有明确的依赖边界。新层在构造时拒绝导出默认值中 EnableControlRig=true 的配置，避免声称已支持缺失的前置根骨控制。

## 实现

- Core 新增 `AlsTwoBoneIkController`，使用既有 `AlsTwoBoneIk` 和 `AlsComponentPose` 的惰性 component 缓存、子骨恢复、原 LocalBlend 语义。新增精确 TRS 的 Begin/Export 重载，避免原生 oracle 先量化为 float；既有 float 重载未改变。
- 按实际节点的访问顺序先读取末端/下臂/上臂 local，再读取 component、Effector/Joint BoneSpace 目标；用原求解器计算三骨，右手保留末端旋转，左手算子可取目标旋转，再按原 Alpha 在局部空间混合。此 Core 控制器只覆盖当前源的默认 twist/stretch/relative-rotation 配置，不是所有 TwoBoneIK 模式。
- `LyraRightHandIkPoseLayer` 将 ALS local TRS 转为 UE 轴向/厘米，求值后只输出右上臂/下臂/手三骨；其余 local TRS 逐值保留。它按当前 profile 的 DisableHandIK 和上一源 DisableRHandIK 曲线计算 Alpha。
- 右手层由当前 `ItemAnimLayers` 实例共同持有，在姿态缓冲链的 Hand Retarget 后执行，仍只由主宿主最终发布一次。Motion 保留跨换类求值/应用计数，同类重绑保留原组件实例。
- Timing 新接 DisableRHandIK 的既有 native RichCurve 解析路径。当前五份 timing 未提供该曲线，普通场景使用缺失值 0；最终混合曲线/Montage 反馈与完整跨层事务仍待。

没有给 ALS 增加物理骨，没有修改 UE 工程/资产，也没有接入尚未还原的左手 CopyBone 或把现有 IK 左手替代它。普通 ALS Demo 未切换到本支线。

## 实际 UE 节点采集

外部 exporter scaffold 的 `UAlsHandControlLibrary::ReadTwoBoneHandPose` 从目标动画 RAW pose 建立实际骨架容器，调用真实 `FAnimNode_TwoBoneIK::EvaluateSkeletalControl_AnyThread` 与 `FCSPose.LocalBlendCSBoneTransforms`，安全导出前后 79 逻辑骨。

节点的 InitializeBoneReferences override 是 private。初次编译直接调用失败，日志 `two-bone-reader-build-first.log` 保留；最终 collector 按该实现登记 public 骨引用/目标和上下臂 cache，再调用真实求值/混合。它没有运行整份 AnimBP 的 CacheBones/Update 生命周期，不作为图时序证明。最终外部 BuildPlugin 退出 0，见 `two-bone-reader-build-final.log`。

三套 Forward Jog Cycle，各取 0、37%、75% 时间；左右手 ×是否先用真实 Hand Retarget ×六 Alpha（0、1e-6、0.25、0.5、0.999995、1）×三 Effector BoneSpace 偏移（零、近端、远端），共 648 组。远端 `(150,-50,20)` cm 用于超臂长夹取；左手组仅是节点数学对照，没有原武器空间 CopyBone，不作完整左手姿态 oracle。Alpha 0 的 collector 仍直接求解节点再调用混合，不能证明图隐藏节点的访问行为。

Python 输出 immutable `two_bone_hand_native.json`，依赖三份 catalog、pose contracts、skeletal defaults、inventory 的精确字节 SHA-256，并记录六个源/目标 `.uasset` 哈希。采集前后及独立复核均未改变这些资产。

```text
LYRA_TWO_BONE_HAND_NATIVE_OK clips=3 cases=648 physical=68 logical=79 assets_saved=0
```

UE commandlet 实际退出 0，未见 Python Error/ensure/assert；工程已有编辑器插件和 GameplayTag 警告不在本批修复。使用外部插件，并在本次命令中禁用 AnimationData/ModelContextProtocol/Mocara，无工程配置变更。日志 `two-bone-native-export-first.log`、`two-bone-native-ue-full.log`。

生成文件 SHA-256：`5EA86CC5F6561D1304272DE84AED9F0F1B839400507B826191340DED6FBB2015`。文件在 ignored `assets/generated/lyra_als/`，不统一格式化。

## 运行验证

新 `lyra_two_bone_hand_smoke.tscn`：Core 对全部 648 组逐骨比较位置、单位旋转和缩放，含 216 部分权重和 216 远端输入；右手零偏移 108 组另验证 Godot 68 骨输入、姿态缓冲隔离、只有右臂三骨可变化，以及 Unarmed CDO 的真实禁用。

```text
LYRA_TWO_BONE_HAND_OK cases=648 physical=108 partial=216 far=216
nativePositionCm=8.541302975239736E-14 nativeQuaternion=4.493769573315014E-15
godotPositionM=8.881784E-16 godotQuaternion=2.3898048E-07
```

门槛为原生位置 `1e-8 cm`、单位旋转分量范数 `1e-10`、缩放 `1e-12`；Godot 单精度位置/单位 quaternion 分量范数各 `2e-6`。没有降低门槛或以端点位置代替旋转比较。

原 1260 帧缓冲隔离测试增加右手和 0/0.5/1 禁用量，原组件适配器链也在相同位置加入右手；30 坏缓冲拒绝保持。已有 324 Hand Retarget 原生对照、Unarmed/Pistol/Rifle 目录、五配置 2100 帧 LeftHand/Additives、实际 UE 八步绑定回归全部通过。共 12 个 headless 运行日志，各一个预期标记、退出 0、无 Godot ERROR/WARNING，见 `two-bone-final-verification.json`。

Rifle 30/60/120Hz 仍为 435/870/1740 物理帧，右手层求值 436/871/1741 次（含初始化），实际启用 391/780/1560 次；每帧断言 Unarmed Alpha=0、Pistol/Rifle Alpha=1。右上臂 local 最大旋转修正约 `0.17761 rad`，证明普通场景确实求解了网格手臂。六 Layer/六 Cycle 换源、47 Rifle 通知、同类重绑和 Additives 状态原门禁保持；Pistol 60Hz 回归通过。

最终 .NET 构建 0 警告/0 错误，Godot import 退出 0。最后的代码修改仅为截图增加 `--lyra-capture-prefix`，随后最终二进制运行 60Hz OpenGL 场景：同样 870 帧/780 次右手应用，保存 Cycle/Crouch/ADS/Jump/Turn 五张图，全部查看；角色完整可见，未见断裂或非有限姿态。当前没有武器网格和完整左手目标，不把这些静态截图作为真实握枪或连续观感验收。新图前缀 `two-bone-`，没有覆盖原五张资源批次截图。见 `two-bone-rifle-60-render.log`。

## 复跑与下一步

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-two-bone-hand.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_two_bone_hand_smoke.tscn
```

下一步先还原 ALS 中的 weapon_r/左手武器空间相对目标及其动画/Aim 混合，再按原序接 CopyBone 和左手节点。根骨 bool blend、FootPlacement/LegIK/武器缩放、Warping、完整共享 Sync/Notify/曲线/属性、Slot/惯性化、Shotgun/Feminine 移动资源、完整主图连续原生、人工和性能矩阵仍待。原 ALS R2–R7 和用户暂缓项保持开放。
