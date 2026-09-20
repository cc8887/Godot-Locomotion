# Aim 顺序姿势求值与原生混合对照

日期：2026-09-12，第一百一十一批。仓库 `D:/GodotALS-p5a-events-actions`。
承接第 110 批原生网格和来源采样，继续原 P4 上身完整性修复。

## 已落地的执行链

新增 Core `AlsAimPoseRuntime`，直接消费第 109 批 `AlsAimFrameRuntime` 的
候选状态和七个独立 evaluator 输入，由 `AlsAimAnimationSourceSampler`
采样实际 ALSV4 动画。三台嵌套状态机现可输出完整 79 骨附加姿势与曲线。

- 九个状态的求值缓存与三个机器的中间输出分开存储。同一状态一遍求值中
  只采样一次，重复引用不会复用其他角色或其他帧的结果。
- 第一条转换混合来源/目标状态，后续转换以前一条的未归一化结果为来源。
  整台机器的转换栈结束后才归一化旋转；子机器先完成自己的归一化边界。
- Head profile 分别决定每骨来源/目标权重。曲线使用标量转换权重，按
  UE Override + Accumulate 混合；不是 TwoWayBlend 的 Lerp。
- 源曲线名即使权重为零也保留；目标权重大于 1e-5 才并入目标曲线名。
  零权重目标仍按状态图采样，不能把“未贡献曲线”误当“跳过来源求值”。
- 复用现有 `AlsStandingCycleCurves.Scale/Accumulate`，避免另写一套阈值
  不同的曲线混合。无附加的播放时钟、统一等待秒数或手臂纠偏常量。
- 临时数据按角色/代次独占。内部完整求值、输出有限性检查成功后才写调用方
  缓冲；来源中途失败不会发布半个姿势。下一次求值重新建立缓存。

`AimFrameSmokeChecks` 已使用这个真实姿势组件与 BaseLayer 输入协作，晚期
失败后同时检查 Aim 状态、来源操作、79 骨姿势和曲线重试一致。它仍是集成
夹具；Aim 相关性由夹具明确传入，不能据此把它等同于正式 BaseLayer Slot。

## UE 原生混合姿势

新增只读 `ReadRawBlendSpacePose`，使用实际 UBlendSpace.GetSamplesFromBlendInput
及 GetAnimationPose，RAW 容器启用 retarget、保留资产 Root Lock。
`export_aim_blend_pose.py` 在导出前逐资产核对既有源键、求值策略和文件哈希，
输出绑定到正式网格和 Aim 来源索引的哈希。没有改写或保存 UE 动画资产。

新夹具 `v4_aim_blend_pose_native.json` 包含 210 个静态组合和 30/60/120 Hz
共 420 帧，共 630 个完整姿势、49,770 骨。其中 385 个姿势同时含两个有效
样本，覆盖网格/时间边界、相等权重排序、微小权重以及超界钳制。
这是实际混合姿势对照，取代第 110 批只有单来源端点的限制。

Godot 直接调用实际 Aim 来源入口逐项比较样本数、顺序、权重、秒数、79 骨
变换和曲线 presence。最大位置误差 6.0174784e-7，四元数误差 3.7885883e-7，
缩放误差 0。冷/正常 Editor 两份 JSON 字节一致，SHA256：
`DBEF4D3EF79C1459A2743F58B9A610F1FC2980FAA10A8A1D0ED0D4200F4833CA`。

## 完成的检查

以下最终进程均已观察到实际退出 0，日志均在 `artifacts/`：

- `aim-pose-runtime-tests-accepted.log`：57 项相关 Import 测试，其中新增
  6 项姿势测试。30/60/120 Hz 共 2,520 帧通过独立的逐骨叶节点贡献检查
  嵌套平移结果，并验证状态缓存、来源晚期失败、错误身份/布局、重入防护
  及零权重曲线边界。旋转底层算术由既有 UE 对照验证；完整嵌套状态旋转
  的原生逐帧对照仍待完成，不用复制运行时算法的参考实现充当原生证据。
- `aim-pose-runtime-core-regression.log`：36 项既有姿势混合、真实逐骨权重
  和转换栈测试通过，包含既有 UE FTransform 算术对照。
- `aim-pose-runtime-build-accepted.log`：Godot C# 构建 0 警告、0 错误。
- `aim-pose-runtime-smoke-accepted.log`：上述 630 个原生姿势对照；十角色
  single/parallel 各 6,000 帧真实 Aim 状态与来源采样逐帧一致，每帧均取消
  后重试，七个来源全部覆盖。热 Update/Evaluate/Commit 10,000 帧分配 0。
  此结果不等同于十角色完整 Demo 的 P7 性能预算。
- `aim-pose-runtime-native-repeat.log`：正常 Editor 重复导出的 630 个姿势
  对照通过；不是复用冷启动文件来伪装重启验证。
- `aim-pose-runtime-base-layer-accepted.log`：3,360 帧输入协作回归，302 个
  隐藏帧不求值，3,058 帧真正执行 Aim 姿势，12 次晚期失败重试一致。
- `aim-pose-runtime-production-single.log`、`aim-pose-runtime-production-parallel.log`：
  既有正式 Worker 各 600 帧通过，结果 EAAF62E4D0A80A76、完整姿势
  3103E3B355BF1F3B、事件 28，lag/stale 均为 0。证明既有路径未回归，
  不能用这些不变摘要证明新 Aim 已连接到正式最终层。

## 原生构建与执行记录

本批使用 ue-diagnosing-plugin-build-load 技能完成整项目 Editor 构建、三插件
加载审计、冷导出、正常 Editor 重启、DataValidation、隔离插件打包和包后
审计。构建日志 `aim-pose-runtime-editor-build.log`，实际 BuildId：
`32625286-3b92-4623-8525-d0b0d390b3d1`，项目插件均 PASS。

最终隔离包 `artifacts/unreal/AlsAimPosePluginValidation-20260912-111-2` 已真正
构建 Win64 Editor 插件，并含 DLL/模块清单。新增 cpp 和修改的头文件在
仓库、UE 项目插件、隔离包中哈希一致；包后审计通过。DataValidation 汇总
0 errors / 3 warnings，旧 PawnActionsComponent/导航网格问题仍保留。
正常 Editor 实际退出 0，日志有两条既有 Condition failed，未称其零错误。

首次轨迹没有覆盖 SwitchSidesBlendPose；连续正弦会经过 Looking Forwards，
于是加入稳定回看后直接左右换侧的输入。每角色还显式覆盖 No Offset 初态，
没有放宽七来源覆盖断言。首次打包误传 NoHostPlatform=false，被 RunUAT
按开关存在处理而跳过 Editor 构建；不把该退出 0 当作成功证据，已改用没有
该开关的新隔离目录重跑。旧日志与生成物保留，没有删除或覆盖既有包。

## 完整性边界与下一项

目前完成的是 Aim 真实来源混合、嵌套状态姿势组件与帧事务协作。完整 UE
嵌套状态机的逐帧运行探针尚未完成；现有证据由 UE 源码、导出定义/曲线/
Head 数值、真实 BlendSpace 姿势和组件轨迹共同组成，不能称为整个 AnimBP
的逐帧数值等价。默认 Demo 尚未使用这个最终上身消费者，没有视觉修复结论。

下一项补完整 Aim 原生状态运行对照，并继续真实 Overlay 来源与
BasePoses/LayerBlending 的最终消费、脊柱/手部修正及最终曲线反馈。然后
闭合 Foot IK/Foot Lock/pelvis/平台，以同输入/同脚相位多帧对照验收上身、
换髋与支撑脚滑移。当前 P3/P4 视觉问题不推迟到 Mantle/Ragdoll 之后。

原 P5A 通用动作/事件、P5B 全 Overlay 和道具玩法、P5C Mantle/Roll/Root
Motion、P6 Ragdoll/Get-up/Pose Recovery/完整 Camera、P7 十分钟预算继续
保留，音频暂缓。本批未 commit/revert/merge，保留既有工作区改动。
