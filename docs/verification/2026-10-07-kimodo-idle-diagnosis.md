# Kimodo 替换资源的 idle 姿态与循环诊断

2026-10-07。用户症状：idle 明显不对，举止扭捏。本批在当前主目录、已有未提交修改上诊断；没有覆盖生产生成资产，没有重新生成 Kimodo 动作或 UE 资产，也没有提交或推送。

## 结论

当前 idle 异常至少包含两部分：SecondaryMotion 的循环断点，以及 Overlay/Aim 和基础姿态不一致导致的固定姿态偏差。主要是 **生成资产语义不满足 ALS 分层合同，替换适配工具链没有验证这种语义**。不是文件损坏；也不能以先前修复 Manny→ALS 对齐、导入 smoke 通过推断最终 idle 正确。

本批不把不同动画与原版姿态有差异本身判为缺陷。循环突变由只替换一个资源的真实 Godot 对照隔离；固定姿态差定位到 Overlay/Aim 及其依赖组合，但没有将这些固定差进一步全部分配到生成、第一段重定向或某一最终骨控制算法。

## 当前实际链路

- `AlsAnimationRuntimeOptions.CompleteDemoFlags` 默认打开 Refactored stance 和 Layering。
- `AlsRefactoredDemoResources.Load()` 的 catalog 固定读取 `assets/config/refactored_animation_sources.json`；`AlsRefactoredStandingRestGraph.IdleSequence` 是 `/ALS/ALS/Animations/Base/A_Als_Stand_Pose`。普通 Demo 主 idle 不是替换树中的 `ALS_N_Pose`。
- `AlsRefactoredDemoResources.PostLayer` 的 base pose inputs 也来自原 `assets/config/refactored_base_pose_inputs.json`。
- V4 Overlay/Aim raw bank 则由 `AlsRawAnimationSourceLoader` 的 `ALS_RAW_SOURCE_GENERATED_DIR` 和 definition digest 选择。故当前 Kimodo 模式是原主移动/Layering 基础与替换 Overlay/Aim 的组合，不能称所有动画都已换成 Kimodo。
- `export_kimodo_raw_sources.py` 从目标 UE 动画读取完整姿态轨道，但直接注入 `original_row["evaluation"]`，包括 additiveType、baseAsset/baseFrame、Root 策略与曲线元数据。生成资源必须满足这些原合同，改名及时间重采样不会自动使它们成立。

## 资源取证

`artifacts/kimodo_als/audit_idle_contract_20261007.py` 只读检查并输出 [资源报告](../../artifacts/kimodo_als/idle_contract_audit_20261007.json)。

- 六域共 116 个唯一 raw JSON，逐 index 的文件 SHA256 全部匹配；126 个 manifest FBX 的文件 SHA256 全部匹配。
- `check_timing_v2.py` 当前重算：116 个唯一源，关键帧/帧率、float playLength、sequencePlayLength、轨道时间域四项不一致均为 0。日志：[时序诊断](../../artifacts/kimodo_als/idle_timing_check_20261007.log)。
- 当前 Stage3 产物记录 `retargetPoseAligned=true`、32 个 limb bones；批处理记录 123 ok/0 failed/0 skipped。这是当前磁盘记录复核，没有重新执行 UE 重定向。
- `ALS_N_SecondaryMotion` 原提示为 `A person stands idle with light breathing sway and weight shifts.`。其 raw evaluation 是 LocalSpace additive，减去 `ALS_N_Pose` 的第 0 帧。原双臂首尾局部旋转夹角为 0°，生成替换为 8.44°/8.54°。生成的 SecondaryMotion 与其生成 BasePose 第 0 帧，上臂初始局部差为 24.44°/15.37°，原对应为 3.65°/3.65°；骨盆原差 0°，当前 8.89°。独立生成的“放松站姿”不等于共享零差基底。
- `ALS_StanceVariation_Normal` 原有四帧，骨盆高度范围约 54.39 cm；生成提示只是 `A person stands in a neutral relaxed idle stance.`，当前高度范围只有 0.033 cm。四帧姿态表被连续放松站立替代。单独换回它并不能恢复整个 idle，见真实对照。
- `ALS_CLF_Pose` 的成功生成提示为站立并左手低持步枪，未要求蹲伏；它与原蹲伏基底左大腿第 0 帧局部旋转差约 113°。这属于额外发现，未在本批做蹲伏真实对照。
- Look Sweep 的生成运动及身体基底与原版不同，而 evaluation 仍引用原语义的 `ALS_N_Pose`。本批真实 Aim 对照显示该域参与固定姿态差；不能仅从名字 Sweep 或采样数一致推断瞄准姿态合同正确。

## 真实 Godot 对照

新增 `scenes/tests/kimodo_idle_diagnostic.tscn` 与 `KimodoIdleDiagnostic.cs`，实例化普通 `AlsDemoEntry`，沿同一角色 Gather/Worker/Commit 与物理显示链路执行；没有注入测试姿态、关闭 IK 或新建动画时钟。60 Hz、single、Default Overlay、不输入键鼠，每轮 360 个物理帧（6 秒），保存最终显示 Skeleton 的 component pose。分析丢弃前 120 帧，比较后 240 帧的同 tick 骨旋转。

8 轮成功（2880 个采集帧）。均保持 Kimodo FBX/compiled tree，只在 `artifacts/kimodo_als/idle_ab_*` 创建独立 raw 对照树，并更新其准确 SHA256；引用同一 source 的全部 index 同步选择同一份 raw。没有覆盖生产 raw。`original` 仅表示 raw 替换回原版，本批没有重导原 FBX。

| raw 对照 | 左上臂最大单帧变化 | 右上臂 | 右手 | 说明 |
| --- | ---: | ---: | ---: | --- |
| 全原版 raw | 0.144° | 0.144° | 0.014° | 同 Demo 对照 |
| 当前 Kimodo raw | 10.045° | 8.429° | 18.986° | 异常复现 |
| 只换回 Normal | 11.092° | 7.444° | 18.729° | 不是单个 Normal 能解决 |
| 只换回 SecondaryMotion | 0.135° | 0.130° | 0.021° | 大幅突变消失，固定姿态仍错 |
| SecondaryMotion 与 BasePose 换回 | 0.181° | 0.174° | 0.091° | 固定差仍在其他叠加层 |
| Overlay 域及共享依赖换回 | 0.144° | 0.143° | 0.014° | 仍有 Aim 固定偏差 |
| Aim 域及共享依赖换回 | 9.232° | 8.743° | 18.178° | SecondaryMotion 问题仍在 |
| Overlay + Aim 及共享依赖换回 | 0.144° | 0.144° | 0.014° | 分析的 12 根骨恢复原 raw 对照 |

当前 Kimodo 对原 raw 的 component 旋转平均差：左上臂 49.845°、右上臂 54.767°、骨盆 20.794°。只换回 SecondaryMotion 后仍有约 52.025°/54.201°上臂固定差，说明去除循环突变不能修复全部“扭捏”。这不是关节局部误差或相邻帧跳变量，勿混用。

完整结果：[idle_ab_results_20261007.json](../../artifacts/kimodo_als/idle_ab_results_20261007.json)。原始采集与日志分别位于 `idle_ab_20261007`、`idle_ab_20261007_v2`、`idle_ab_20261007_v3`；分析脚本为 `analyze_idle_ab_20261007.py`。

## 失败证据与复现边界

- 系统 `dotnet` 只有 SDK 10，不能满足当前 global.json；首次构建失败记录在 `idle_diagnostic_build_20261007.log`。使用已安装的 `C:/Users/yuchencui/.dotnet/dotnet.exe build GodotALS.csproj -p:Optimize=true`，成功 0 warning/0 error，日志 `idle_diagnostic_build_local_20261007.log`。没有改 SDK pin。
- 第一版部分替换夹具未统一跨域共享 raw，Overlay/Aim 两轮触发 SHA256 拒绝，失败日志保留。v2 改为按 assetId 全局一致选择及索引更新；失败不计成功轮数，未放宽 hash 校验。
- 这批是实际 headless Godot 的 Default idle 数据隔离，不是人工视觉签收，也不是 13 Overlay、所有姿态、移动/蹲伏/复杂地形或长时性能验收。未重新证明整个 BVH/FBX 的坐标轴正确。

## 修复方向

优先保留原版固定 BasePose、Normal/站蹲姿态表、Aim Sweep 和配套 additive/curve 语义，单独评估生成的运动片段。生成 SecondaryMotion 应相对选定的同一 BasePose 制作，去掉恒定姿态偏置，并验证循环首尾位置、旋转及速度连续性。若坚持全替换，应重新制作整组语义资产、各帧姿态/曲线与同步标记，再按真实最终 idle 验收；不应只把独立生成的完整动作改名、压缩到原时间域并继承原合同。

本批完成诊断与隔离，不宣称正式修复已交付。原始生成资源保留供后续逐段比较。
