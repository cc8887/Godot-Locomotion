# Lyra 武器空间控制通道与 CopyBone

## 结论与关闭范围

继续复用 ALS Mannequin 的 68 根蒙皮骨；完整左手握持还需要独立的 `weapon_r` 动画通道和 `VB IK_Hand_L_weaponSpace` 逻辑通道。二者应在序列采样时进入姿态，并随源混合、HipFire、AimOffset、Slot 和惯性化传递。不能在 SkeletalControls 阶段从最终 FK 左手重新生成目标，也不能只提供固定的武器偏移。

本批已导出三层全部 189 条普通序列、45 个 AimOffset 样本的相关原生通道，完成 24,796 个关键帧/区间中点采样；新增 Core ComponentSpace CopyBone，432 组实际 UE 节点对照通过。**只关闭源通道资源和 CopyBone 组件对照；尚未关闭 Manny→ALS 控制通道重定向或普通 Demo 的左手 IK。**当前运行链仍只求解已接入的右手。

## 源骨架与图语义

`skeletal_control_defaults.json` 的实际 Manny Skeleton 为 161 根原始骨、164 根逻辑骨：

| 控制节点 | 逻辑索引 | 父/目标 |
|---|---:|---|
| `weapon_r` | 88 | 原始动画骨，父 `hand_r`（66） |
| `VB IK_Hand_L_weaponSpace` | 163 | source=`weapon_r`，target=FK `hand_l`（20） |

源 `UpperBodyMask` 对这两个通道的权重均为 1。ALS 当前有 68 根物理骨、79 根逻辑骨，未包含这两个节点。保留现有 11 个 ALS 虚拟骨后，再增加这两个控制节点，完整逻辑姿态应为 81 根；蒙皮绑定仍为原 68 根。`weapon_r` 是有动画的非蒙皮控制骨，不能误标成只有生成规则的虚拟骨。现有 raw sampler 的原始骨映射和最终 skin 发布映射需要区分。

原 `ABP_ItemAnimLayersBase` 的 CopyBone 位于 Hand IK Retargeting 后，按 ComponentSpace 将虚拟左手的 translation/rotation 复制给 `ik_hand_l`，`copyScale=false`。其后还有根骨 bool blend 和左右手节点；本批原生对照只执行该 CopyBone 及它的 LocalBlend，不执行整个图或这些相邻节点。

## 实际通道变化

`weapon_space_sources.json` 保存每条序列所有原始采样键及每个区间中点的 `GetAnimationPose` RAW 结果。普通序列另保存四个相关节点的组件姿态；additive 数据保留原 additive translation/mesh rotation/relative scale，不作为绝对局部姿态计算组件矩阵。

| 配置 | 普通序列 / Aim 样本 | weapon 单条内最大位移变化 | weapon 单条内最大旋转变化 | VB 最大 additive 位移 |
|---|---:|---:|---:|---:|
| Unarmed | 62 / 15 | 3.4931 cm | 3.0974 rad | 15.2420 cm |
| Pistol | 63 / 15 | 4.6916 cm | 0.5487 rad | 5.0547 cm |
| Rifle | 64 / 15 | 8.8580 cm | 0.9222 rad | 0.01363 cm |

上述“变化”以同一片段的第 0 秒为基准，不是全动画范围包围盒。Pistol weapon additive 位移最大为 1.4893 cm，其余两组 sampled weapon additive 位移为 0；这些零值不能推导出普通 weapon 通道恒定。

另在真实 Manny Idle 和 Forward Jog Cycle 上，用各自原 AimOffset 的 9 个网格加 7 个非网格输入执行实际 Mesh Space additive。将结果中的原虚拟骨组件位置与重新读取的 FK 左手比较：

| 配置 | 瞄准后 fresh FK 最大位置偏差 | 覆盖条件 |
|---|---:|---|
| Unarmed | 25.7006 cm | Cycle，yaw=-180 / pitch=-90 |
| Pistol | 59.4344 cm | Cycle，yaw=-180 / pitch=-90 |
| Rifle | 31.7123 cm | Cycle，yaw=180 / pitch=90 |

这是 Manny 源姿态中两种目标定义的差异，包含极限瞄准样本；不是 ALS 运行时左手误差或普通握枪观感结果。它证明在 AimOffset 后用 fresh FK 替换原 VB 会改变源语义。

普通序列的原采样键上，两者最大位置差约 `4.76e-13 cm`；区间中点可达 Unarmed `8.8896 cm`、Pistol `2.2973 cm`、Rifle `0.4332 cm`。因此即使尚未进入 AimOffset，也不能在已插值的 FK 姿态上临时生成 VB 代替原独立插值通道。下一阶段应保留关键帧通道和原生插值顺序。

审计工具为 `tools/analyze_lyra_weapon_space.py`，完整数值、片段与时间在 `artifacts/lyra-analysis/weapon-space-resource-audit.json`。只覆盖本批有限采样集合，不声明所有连续时刻或所有武器资源已验收。

## 导出与原生对照

外部 exporter 新增：

- `ReadWeaponSpaceSamples`：实际序列采样，选取 FK 双手、weapon 和左手 VB。
- `ReadRawAimingPose2D`：复用现有 BlendSpace 采集器，实际原 Skeleton / AimOffset / base pose 求值，不重定向到 ALS。
- `ReadWeaponSpaceCopyPose`：以调用者的逻辑局部姿态初始化原生 FCSPose，执行实际 `FAnimNode_CopyBone::EvaluateSkeletalControl_AnyThread` 与 `LocalBlendCSBoneTransforms`，返回 164 根逻辑骨局部输出。

三配置 ×两基底 ×16 瞄准输入 ×Alpha 0/0.25/0.5/1，共 384 组。另取两类瞄准输入，分别改变 weapon、IK 左手和 IK gun 的非均匀缩放，共 48 组，明确验证 `copyScale=false` 和目标父缩放；这些是受控姿态夹具，没有修改序列资产。

`weapon_space_native.json` 保留 384 组，`weapon_space_native_scaled.json` 保留追加 48 组。三份生成数据的 SHA-256：

```text
weapon_space_sources.json       CF938B6E38452E250287C24914F6557F4056F80F5151A259451995EC698791AA
weapon_space_native.json        DB5B4913994EC0F7896798188E3D023A7C6F5508B2E4477CDCE6EF9023126828
weapon_space_native_scaled.json 03CAEDF08DA905C6790DBD741AEDD1E5B5FB2D2027CB7D415A3653097F6A8830
```

源数据依赖 catalog、Aim inventory、图合同、CDO/骨架和遮罩的精确字节哈希。首次导出及增加缩放夹具后的重复导出均退出 0；重复导出校验已有 source/native 数据语义相同，不重写历史 JSON。最终独立复核全部 470 个源/目标包哈希未变。数据仍在 ignored `assets/generated/lyra_als/`，不能从仅有代码的检出宣称可运行。

UE 5.8.1 外部 BuildPlugin 退出 0；两次 commandlet 退出 0，最终标记：

```text
LYRA_WEAPON_SPACE_OK ordinary=189 additive=45 native=384 scaled=48 assets_saved=0
```

日志 `weapon-space-reader-build-first.log`、`weapon-space-export-first.log`、`weapon-space-native-ue-first.log`、`weapon-space-export-scaled.log`、`weapon-space-native-ue-full.log` 保留。命令只禁用外部采集不需要的 AnimationData/ModelContextProtocol/Mocara，无 UE 工程配置或引擎源码更改；现有 UE 编辑器插件/GameplayTag 警告仍保留。未发生 Python Error/ensure/assert。

## Core 与 Godot 验证

`AlsComponentCopyBoneController` 持有角色逻辑布局，按原访问顺序获取源/目标组件姿态，选择 TRS 字段，再经现有精确 FCSPose local blend 输出。它只实现 ComponentSpace，未扩展或宣称 World/Parent/BoneSpace。

`lyra_weapon_space_smoke.tscn` 校验依赖、234 条源通道、全部关键帧/中点、432 组逐骨局部位置/旋转/缩放、输入隔离及目标缩放保留：

```text
LYRA_WEAPON_SPACE_NATIVE_OK ordinary=189 additive=45 samples=24796
cases=432 scaled=48 logical=164
positionCm=8.380941176316589E-14 quaternion=4.344434768182728E-16
scale=1.1102230246251565E-16 productionLeftIk=pending
```

门槛为位置 `1e-8 cm`、单位 quaternion 分量范数 `1e-10`、缩放 `1e-12`。未放宽阈值。既有 TwoBoneIK 648、Hand Retarget 324 和姿态缓冲 1260 帧回归通过。Rifle 60Hz 普通受控场景 870 物理帧、871 次层发布、780 次右手求解、六次换层/换循环、47 条通知及同类重绑通过。五个最终运行日志退出 0、无 Godot ERROR/WARNING，见 `weapon-space-final-verification.json`；最终 .NET 构建 0 警告/0 错误，Godot import 退出 0。仅资源/Core 变更，本批未新增渲染、人工观感或性能验收。

## 下一阶段接入顺序

1. 明确并用 UE 夹具验证 Manny→ALS 右手坐标系中的 weapon 通道变换规则；保留普通原始通道与 mesh additive 通道的不同语义。
2. 在原采样键处生成完整 ALS 81 根逻辑姿态，包含现有 ALS 虚拟骨和新增目标；插值与多个源混合都携带这些通道，最终只发布原 68 根 skin 骨。
3. 让共同 ItemAnimLayers 实例的 HipFire、Aiming、Additives、SkeletalControls 使用同一逻辑 pose/curve/attribute 布局；按照原主图顺序接 CopyBone 和左手 IK。现有物理 68 骨 AnimationPlayer 交叉淡化仍须迁移到携带控制通道的源姿态求值。
4. 补齐统一源/Sync/Notify/Slot/惯性化和连续主图对照，再验收实际握持。不得在 CopyBone 前读取当前最终 Skeleton 左手作为替代目标。

Interface 签名、ItemAnimLayers 共享 owner 和当前组件生命周期沿用已验证接入；本批不把它们扩写为完整图执行完成。原 ALS R2–R7、完整手足链、Shotgun/Feminine 移动资源及其他未关闭事项继续开放。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-weapon-space.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_weapon_space_smoke.tscn
python tools/analyze_lyra_weapon_space.py --root assets/generated/lyra_als `
  --output artifacts/lyra-analysis/weapon-space-resource-audit.json
```
