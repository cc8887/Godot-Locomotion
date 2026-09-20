# BasePoses 独立求值、逻辑骨架与原始关键帧（第 103 批）

日期：2026-09-12。工作区：`../GodotALS-p5a-events-actions`。
承接第 102 批 LayerBlending 所缺的 BasePoses 上游与完整逻辑骨架。这是原
P3/P4 完整性补完，不是新增玩法范围。实际入口目前为
`scenes/tests/base_poses_smoke.tscn`；共享定义加载正式数据，但默认 Demo 的
输出仍是 BaseLayer。本批不关闭双臂、侧身、交错步或起步滑步问题。

## 修正的等价性缺口

相同动画资产不能保证采样数据与运行结果相同。本批发现并处理三处边界：

1. BasePoses 的 N/CLF 是两个固定时间 evaluator，具有各自节点身份、初始化
   与更新历史；不能借用移动 Idle 的同资产 player。MultiWay 初始化读已保留
   权重，Update 才读取本帧属性；零全局权重不等于跳过局部相关的 evaluator。
2. 原始 68 根实体骨关键帧先生成 11 根虚拟骨，再对实体骨做平移重定向。
   UE 的虚拟骨读取冻结的组件姿势，源骨若为虚拟骨还存在 source alias；
   不能先重定向、用当前已经写回的 VB 继续累积，或把缺少的 VB 填成 identity。
3. FBX 动画旋转已经有精度损失。N_Pose 的 `ik_foot_r` 首帧 XYZ Euler 为
   `(93.84858, 89.999, -98.27985)` 度，重建四元数与 Godot 实际值仅差约
   `4.55e-8`，与 UE 原始值差约 `0.00112156`，对应约 `0.1285°`。
   因此修复数据源，保持原比较门限；没有调整动画、增加姿势补偿或放宽测试。

精度损失的链路是 UE `FbxAnimationExport.cpp:204` 附近的
`raw BoneAtom → ToMatrixWithScale → FbxAMatrix.GetR → float Euler key`。
原始 float quaternion 的长度平方为 `1.0000000196870238`；近 90° 时
Euler 提取放大微小非单位误差。`CorrectAnimTrackInterpolation` 从第二帧
才开始，不是此次首帧差异的来源；主因也不是 ASCII 小数截断。

UE 直接参考还包括 `AnimDataModel.cpp` 的原始整数键读取、冻结组件姿势/
虚拟骨生成，以及 `AnimSequenceHelpers.cpp` 的 FRetargetingScope 析构顺序。
当前两资产骨架有 59 根 Skeleton 平移模式实体骨，另 9 根为 Animation。
本实现严格限定这两个实际存在的模式，不声称已支持任意 retarget 模式。

## 数据与代码

`v4_layering_inputs.json` 中 BasePoses 原图严格编译为 Root 671、MultiWay
669、N evaluator 670、CLF evaluator 668。保留 N/CLF 引脚顺序、零显式时间、
Teleport、Loop、DoNotSync、权重归一化/相关性门限和曲线缺席语义。

`assets/config/v4_base_poses_inputs.json` 仅包含源资产/骨架文本、轨道与曲线
元数据，供 retarget 编译器使用；不含最终测试姿势。
`assets/config/v4_base_pose_source_keys.json` 保存两资产的全部原始通道键，
每资产 68 轨道、2 键、30 Hz。只读原生 `AlsSourcePoseKeysLibrary` 直接读取
AnimDataModel 的 PosKeys/RotKeys/ScaleKeys，float 提升 double 后输出 JSON，
不经过 AnimPose、Transform、Rotator 或 FBX。空 scale 通道保持空数组，
编译器按 UE 规则取单位 scale。Python 脚本验证身份/格式并写正式源数据。

`AlsBasePoseSourceKeysCompiler` 按实体骨顺序编译不可变键表。UE 骨空间到
当前 FBX 骨空间的组合为 Y 反射：位置 `(x,-y,z)*0.01`、四元数
`(-x,y,-z,w)`、scale 不变；源 quaternion 不作 Normalize 或矩阵往返。
此边界区别于已有角色 canonical 坐标契约，不能把两种姿势数组直接比较。

`AlsLogicalPoseExpansion` 保留原生正/负/非均匀与近零 scale 的变换规则。
`AlsBasePosesSourceSampler` 执行原始键 → 79 骨扩展 → 实体骨 retarget。
两资产均无曲线，输出保持 `Present=false`，不伪造 BasePose_N/CLF 曲线。
每角色独占 sampler scratch 与 `AlsBasePosesRuntime`；只有不可变源键可共享。
候选初始化、Update/Evaluate、取消/故障与同帧重试由 Core 独立所有者管理。

对照夹具 `tests/Als.Core.Tests/Fixtures/P3/v4_logical_pose_native.json` 单独
保存 UE RAW 求值的重定向前/后 79 骨姿势，以及 15 组、30 个变换运算输出。
生产代码不读取该文件。浮点门限保持位置 `2e-5 m`、quaternion 欧氏距离
`2e-5`（允许等价正负号）、scale `5e-5`。

## 验证记录

| 验证 | 结果 | artifacts 证据 |
| --- | --- | --- |
| Core evaluator、虚拟骨/变换、retarget、不可变键表 | 58/58 | `base-poses-core-source-keys.log` |
| Import 图/资产/retarget/源键 | 101/101，含两资产各 68 骨首键 quaternion 共 544 个分量位值一致 | `base-poses-import-source-keys-case-final.log` |
| Godot C# 构建 | 0 警告、0 错误 | `base-poses-source-keys-case-godot-build.log` |
| 真实源姿势与候选运行 | 840 帧、840 次取消重试、12 次注入故障、42 帧全局零权重 | `base-poses-source-keys-case-godot-smoke.log` |
| 真实资产 10 角色并行 | 单/并行各 1200 帧，10 个不同 worker，逐帧全部 TRS/曲线位值摘要与最终状态一致 | 同上 |
| 原始源键冷导出 / 普通 Editor | 均退出 0，JSON 逐字节一致，未保存资产 | `source-pose-keys-native-final.log`、`source-pose-keys-editor.log` |
| 默认生产 single / parallel | 各 600 帧、完整 BaseLayer 输出摘要保持 | `base-poses-production-single.log`、`base-poses-production-parallel.log` |

79 骨原生重定向前/后对照的最大位置误差 `2.8115454e-7 m`，最大 quaternion
欧氏距离 `3.475518e-7`；MultiWay 控制混合位置误差 0、quaternion
`1.3493576e-7`。10 角色分别使用 30/60/120 Hz，共享两份只读键表，每角色
scratch/运行状态独占，worker 不访问 Godot API。该检查不包含十分钟预算，
也不是完整角色图已切换的证明；原生姿势对照范围是 RAW 源采样。

生产两模式 result=`DFA5F7A4F3296FA3`、full pose=`88AAD97FC78B8895`、
root=`A4F6C26CBAB8A0E7`，各 28 个来源事件、lag/stale=0。新配置能由真实
入口加载且既有移动回放保持；本批没有改键鼠，也没有新增视觉效果验收。

正式源键 SHA256：`8C544724E91701C45EFD940083755E04D86BD32242090D6AB6B82E31417E2BEB`。
正式 retarget 源配置 SHA256：`5818A758D0C9EB53CAFDAB69BB9D7D5337C08CCFC8994D5898F0B2474ED0C287`。
逻辑姿势夹具 SHA256：`B3BAABEE1B75C90632C08845EFB50A0B7DDC65C88806D1781CD850326B1CB595`。
原始 quaternion 的符号及负零保留；Python 解析原生 JSON 的 `-0` 时显式保留
负零，避免普通整数解析丢失符号。骨名按 UE FName 比较语义忽略显示大小写，
例如源轨道 `pelvis` 与骨架 `Pelvis`。重复骨名即使仅大小写不同也被拒绝。

原生逻辑姿势夹具与普通 Editor 重复结果在所有实际数值字段一致；nativeText
仅存在每资产两块空 AssetImportData 的文本差异，不能称该夹具逐字节一致。

只读原生接口需要更新项目插件；按 UE 构建/加载技能完整重建 Editor target，
不使用 Live Coding 或单插件 DLL 覆盖来替代。最终构建前缀
`20260912T072847381Z-171e0e420be24750be090f5454d9b268`，三项目插件审计通过，
BuildId=`369675c0-434c-4633-b2aa-532acf57bb8f`，输入 fingerprint=
`7CDC0DD3F5ECF4C04CCCE131599B5C78997BD38B7A68AF1012F391B93E47191B`。
构建日志为 `source-pose-keys-ue-build-final.log`。原生源文件在仓库导出器与
UE 项目插件两处保持一致。普通 Editor 在此次完整构建后成功重启并退出。
DataValidation 退出 0：688 个资产、0 错误、3 条既有 AI/Navmesh 警告，日志
`source-pose-keys-data-validation.log`。隔离 BuildPlugin 退出 0，实际编译
`UnrealEditor Win64 Development`（Editor-only 插件，不是游戏打包），日志
`source-pose-keys-plugin-package.log`，产物位于
`artifacts/unreal/AlsSourcePoseKeysPluginValidation-20260912-103`。
包后 `source-pose-keys-postflight-audit.log` 退出 0，三个项目插件通过，
无 UE/UBT/UAT 残留。原生日志仍有既有 AutomationTest 自测错误，不写成
所有日志零错误。

首错保留：错误坐标空间比较导致 pelvis 大误差；纠正空间后发现遗漏的
Skeleton 平移重定向；补入重定向后，IK 四元数比较暴露上述 FBX 数据损失。
另有 Python 无法读取 Skeleton.virtual_bones、RawAnimSequenceTrack 属性名
与 protected 属性读取失败，均保留原日志。它们没有被计入成功验证。
另保留真实轨道显示大小写差异导致 Import/Smoke 拒绝的首错，已按原生 FName
语义修正并增加成功映射和大小写重复拒绝检查。
原生首次 wrapper 启动缺少 .NET 10 runtime，随后显式使用引擎自带运行时；
首次实际 C++ 编译发现 FQuat4f 不提供 operator[]，改为直接 X/Y/Z/W 读取后
完整构建通过。首错日志分别保留为 `source-pose-keys-ue-build.log` 与
`source-pose-keys-ue-build-fixed.log`。
另保留 UAT 参数被批处理保留为字面量而生成 `../UnrealEngine/$packageOutput`
的记录；构建自然退出后核实该目录仅为本次生成产物，用 PowerShell
`Move-Item -LiteralPath` 可恢复地移到上述 artifacts 路径，原临时位置已不存在。
没有删除源文件或向项目部署隔离包 DLL。

## 接下来

动态序列必须先分别扩展相邻原始键，再按原生规则插值，随后重定向。
当前 Main Movement/BaseLayer 的 75 个 player、109 个 sample 来源仍使用旧
68 骨 FBX 采样；这一批的两份 BasePoses 不能替代其整体迁移。
下一批具体顺序为：

1. 从实际来源闭包去重资产，并包含 additive base、固定 evaluator、Turn 与
   Action segment。109 个 sample 不是 109 个资产。导出全精度键、轨道存在性、
   插值/时长/帧率、retarget source/ref pose、Root Lock 和 additive 依赖。
2. 建立通用序列的整数/非整数时刻求值对照，保留 UE 缺轨参考骨、逐键 VB、
   插值、retarget、Root Lock 顺序；当前绑定器在 FBX 轨道中提前锁 root 的
   处理不能直接沿用。明确区分 Animation 与 Skeleton 以及未支持的新模式。
3. 迁移非 additive 移动和真实 Montage Slot，再分别处理 Detail/Lean/Landing
   的局部或 mesh rotation 差分。目标/base 各自完成源采样，不能一律取 base
   第 0 帧。核对动画属性/TransformCurves，未支持的数据明确拒绝。
4. 将中间缓存、Stride/Diagonal/Lean、Slot 和惯性历史统一为 79 骨，补齐目前
   被裁剪为物理骨的 QuickFeet 等逐骨权重，最后输出才投影回 68 实体骨。
   保留当前 player 身份、共享 tick/Notify、缓存顺序和共同 Commit/Cancel。

继续补齐移动来源、真实 Overlay/Aim 和外围主图，再让 LayerBlending 的最终
姿势、曲线与所有来源状态一起提交，闭合角色朝向、上身及脚部消费者。

之后仍按原顺序进行 P3/P4 整链视觉验收、P5A 通用动作/通知/同步剩余项、
P5B 全部 Overlay 与道具、P5C Mantle/Roll/Root Motion、P6 Ragdoll/Get-up/
Pose Recovery/完整 Camera，以及 P7 人工与十分钟性能预算。音频继续暂缓。
