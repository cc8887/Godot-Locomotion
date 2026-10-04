# Lyra 控制骨重定向与 ALS 逻辑源姿态

本批在主目录实施，源项目为 GASP58，运行端为 Godot 4.7.2 .NET，导出端为本机 UE 5.8.1。继续使用 ALS Mannequin 模型；新增独立动画骨架适配：**68 根蒙皮骨、69 根 raw 动画骨、81 根逻辑骨**。不需要改现有网格的 skin 权重。

本记录关闭控制通道资源和原生源采样对照。后续 [81 骨 Layer 与双手运行接入](2026-09-30-lyra-logical-layers.md) 已将独立 Lyra 示例迁到完整逻辑姿态并接 CopyBone/左手 IK；以下运行边界为本批当时状态。完整主图仍未验收。

## 骨架和重定向规则

原 ALS Skeleton 为 68 raw / 79 logical，其中有 11 个虚拟骨。新增 `weapon_r` 为有关键帧的 raw 非蒙皮骨，父为 ALS `hand_r`；新增 `VB IK_Hand_L_weaponSpace` 的 source 为 weapon、target 为 ALS FK `hand_l`。

| 节点 | 新逻辑索引 | 发布用途 |
|---|---:|---|
| 原 ALS raw 骨 | 0–67 | 保持原蒙皮顺序 |
| `weapon_r` | 68 | 独立动画通道，不发布到 skin |
| 原 11 个 ALS 虚拟骨 | 69–79 | 原定义按名字保留，索引后移 |
| `VB IK_Hand_L_weaponSpace` | 80 | 源关键帧生成，再独立插值和混合 |

当前 IK Retargeter 不包含 ALS weapon 链，单纯添加同名骨不会搬运武器动画。新增 `UAlsLyraControlRigLibrary` 读取实际 Retarget Pose 中两套右手的组件旋转，计算 `C = inverse(TargetHandRotation) * SourceHandRotation`。每个 Manny weapon 原始关键帧转换为 `P' = C.Rotate(P)`、`Q' = normalize(C * Q)`，保持厘米偏移和原 scale，在 UE 写回 binary32 track。当前 C 为 `(-0.09209842265121482, -0.022191707675823, 0.002475175044825863, 0.9954995138944502)`。

这是新增的 ALS 控制骨适配策略；它以当前 retarget pose 为标定，不代表 ALS 和 Manny 的手型、握持或全身变形完全等价。仍需运行时混合、武器附着和视觉验证。

Skeleton 和 Sequence 均由 `DuplicateObject` 复制到 transient package。Sequence 在 `SetSkeleton` 后调用 `UpdateWithSkeleton`，按骨名刷新轨道的 `BoneTreeIndex`；原 retarget reference 也按名字迁入新布局。AimOffset 的中心序列作为其余样本的 frame-zero additive base，自引用中心仍保持自引用。未保存任何 `.uasset`。

## 原生导出与不可变资源

`export_lyra_logical_controls.py` / `export-lyra-logical-controls.ps1` 生成被 Git 忽略的 `assets/generated/lyra_als/logical_controls/`：

- `calibration.json`：真实手部标定、81 骨层级/参考姿态/模式、68 骨 skin 映射、11 个依赖 JSON 和 475 个源/目标包哈希。
- `clips/*.json`：189 普通序列（62 Unarmed、63 Pistol、64 Rifle）和 45 个 AimOffset 样本，234 文件共 154,993,628 字节；包含完整原始 TRS 关键帧和原序列 metadata。
- `sampling.json`：本机引擎的 152 个真实时间选择样本和 `RoundSubframe` 配置。
- `catalog.json` / `native.json`：文件字节哈希和 936 个原生姿态样本。每条序列取长度的 0、0.37、0.75、1，分别保存未 retarget 的 raw 输出和实际 retarget/root-lock/additive 输出。

重复导出仅接受与已有 JSON 语义相等的结果，保持已有文件字节。末次完整重导正常退出 0；独立核对 475 个包、11 个依赖和全部 234 个 clip 哈希。上述 936 样本中的原 68 骨与扩展前 UE 输出位置/旋转/scale 差均为 0；这是该采样范围的实测，不是连续主图验收。

| 文件 | SHA-256 |
|---|---|
| calibration | `c3a20daf775577b0ef80518fd7403d6c9e790f197648810d502c4f49348fc043` |
| catalog | `fefa36da0197537fb0b04b766f509312e392bf2557c0f6f6f57220a66f569985` |
| sampling | `1b0cb340c7af6c00c64044e3c7110600dcbab9e149c831f9ab2fc94ff1e29363` |
| native | `13401a2df89f15d459fe92c698a87799205d9939f252b81d1f573d9f1c967bd4` |

## Godot 源采样与时间精度

`LyraLogicalSourceBank` 在主线程加载、验证和编译不可变源资源。每个播放器 occurrence 构造自己的 `LyraLogicalSourceSampler`，拥有独立 scratch 和 additive base sampler；没有新增动画时钟或共享可变姿态缓存。采样顺序为：扩展每个源关键帧的虚拟骨 → 原生 TRS 插值 → translation retarget/root lock → Mesh Space additive。新增 VB 没有显式轨道，其余 11 个 ALS VB 保留原显式轨道。

原 ALS 的 raw 时间 fixture 对应一个优化掉 `FFrameTime` 构造函数 `(subframe + .5f) - .5f` 的原生构建。本机安装版 UE 的实测保留两个 binary32 舍入，两个 fixture 不相同。因此 Core 新增显式 `AlsRawFrameTimeRounding` 配置，原 ALS 默认值保留；此资源 bank 使用 `RoundSubframe`，在 FEvaluationContext 和 GetKeyIndices 的两次转换中执行原舍入。

这是实测编译行为差异，未断言由引擎小版本导致，也没有放宽误差门槛。两套 152 样本的 key/alpha/sampleTime 均逐位比较通过。

`lyra_logical_source_smoke.tscn` 比对所有 936 × 81 骨的 raw 和 output，包含 180 个 additive case；每个 case 再核对独立 occurrence 和重复采样逐值一致。原门槛为 position `1e-8 cm`、单位 quaternion `1e-10`、scale `1e-12`。

```text
LYRA_LOGICAL_SOURCE_NATIVE_OK sources=234 raw=69 logical=81 skin=68 cases=936
additive=180 positionCm=1.4163191318420816E-13
quaternion=6.58317845524286E-16 scale=0
virtual=beforeInterpolation occurrenceScratch=isolated runtimeLeftIk=pending
```

Core 时间组 28 项、raw/retarget/post-process 回归 26 项全部通过。Debug 及 Release Optimize 构建均 0 错误/0 警告。既有共同 Layer 缓冲 1260 帧/30 次拒绝、Rifle 60Hz 870 物理帧/871 姿态/780 次右手应用通过，最终 Godot 日志无 ERROR/WARNING。这些场景回归仍覆盖旧 68 骨生产链，不证明新 81 骨已经接入。验证汇总为 `artifacts/lyra-analysis/logical-controls-final-verification.json`。

## 保留的失败证据

首轮复制 Sequence 只调用 SetSkeleton，没有刷新旧 VB 轨道索引，原 bone69 在 idle/0 秒偏离约 10.78 cm。已通过 `UpdateWithSkeleton` 修正；首批完整输出保存在 `artifacts/lyra-analysis/logical-controls-indexing-first/`，首次 smoke 和 UE 日志保留。

索引修订重导尚未完成时误提前启动过一次依赖测试，读取了缺失 catalog，退出 1；保留 `logical-controls-source-native-indexing.log`。随后确认导出进程正常退出，再执行全部对照。

修正索引后的首轮在 jog_fwd_stop/0.518 秒发现 root 位置差 `2.7879866593139013e-7 cm`；反推 alpha 并实际采集 152 个 UE selector case，确认上述时间舍入差异。保留 `logical-controls-source-native-final.log`；修正后通过日志为 `logical-controls-source-native-final-pass.log`。没有修改原生样本、跳过 case 或提高阈值。

## Interface / Layer 的后续接线

沿用已验证的 `ALI_ItemAnimLayers` 14 个 typed 入口、共同 `ItemAnimLayers` owner 和同类重绑保留实例规则，详见 `2026-09-30-lyra-linked-layer-contracts.md`。资源 bank 不替代实例：每个源播放器的时钟/同步历史和 scratch 应归当前角色的该 owner，不能每个入口建立一个武器实例。

下一阶段将共同 pose 缓冲从 68 扩到 81：移动源、HipFire、LeftHandPose、Aiming、Additives、RootYaw 和 SkeletalControls 都传完整逻辑姿态。源时间复用已有播放/距离匹配选择结果；最终仅按 skin 映射发布前 68 骨。随后接 CopyBone 和左手 TwoBoneIK，再验证换层/ADS/移动/取消恢复与实际握持。

本批没有实现完整曲线/attribute 容器、图级共享 Sync/Notify/Slot/惯性化、FootPlacement/LegIK、完整主图连续 UE oracle，也没有新增渲染、人工观感或性能验收。源 metadata 中的 attribute 信息保留但未由 sampler 求值。整个 Lyra 支线继续开放，不计入 ALS R2–R7 的关闭状态。
