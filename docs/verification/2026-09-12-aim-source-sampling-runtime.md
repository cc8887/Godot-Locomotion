# Aim 原生网格与附加来源采样

日期：2026-09-12，第一百一十批。工作区 `../GodotALS-p5a-events-actions`。
承接第 109 批 Aim 状态与帧历史，继续原 P4 完整上身执行链。

## 结果与完整性边界

正式图定义现在加载 Aim 专用网格和来源绑定；独立来源采样器可输出真实
79 骨 Mesh Space Additive 姿势。七个 evaluator 的播放历史仍由各角色的
AimFrameRuntime 持有，资源共享不合并播放身份。

纠正第 108 批关于“缺失来源”的判断：三个 Sweep 和 ALS_N_Pose 已经存在于
76 资产原始源库中。本次冷导出的四个文件与既有文件字节相同，未覆盖原始
源文件。缺口是 Aim 专用绑定、网格采样与最终消费者，而不是这些动画尚未
导出。新增来源视图复用现有四个资源及骨架对象，保留独立图绑定。

当前尚未将 Aim 状态的顺序姿势求值接入正式 Demo 最终层。原生混合姿势及
完整嵌套状态机逐帧对照仍待完成；不能把来源端点对照称为完整 Aim 图对照。
本批没有新增视觉通过结论，上身、换髋、起步滑步仍须整链验收。

## 原生事实与实现

- ALS_N_Look 是 BlendSpace1D，清单将它放在 AimOffsets 类别。编译器从实际
  两类来源中唯一定位资产，再验证原生类型，不靠目录分类推断节点语义。
- 三个样本按 F、D、U 绑定；都是 RotationOffsetMeshSpace，AnimFrame 基准
  为 ALS_N_Pose 第 0 帧，长度 1 秒。它们不是普通姿势。
- Pitch 范围 -90 至 90，四段、五个网格点；轴滤波和样本权重平滑均为零。
  保留原生顶点顺序、双精度坐标到 float 余数的转换、相等权重的排序、
  小于 1e-5 的剔除及归一化。没有添加人为延迟。
- Sequence evaluator 输入为秒，BlendSpace evaluator 输入为归一化时间；
  teleport 不推进时钟。当前样本均长 1 秒，仍保留这两种输入语义。
- 来源采样器按网格输出顺序混合已求值的附加姿势，保留曲线 presence。
  原始数据只读共享，临时姿势/曲线缓冲按角色独占，并检查并发误用。
- ReuseResourcesFrom 在共享前检查来源身份、文件哈希、完整骨架映射及
  参考/retarget 数据；不同动画图仍保留各自绑定、根来源和 evaluator 数量。

主要实现：`AlsAimBlendSpace`、`AlsAimSamplingCompiler`、
`AlsAimAnimationSourceSampler`、`AlsRawAnimationSourceLoader.LoadAim`，以及
`AlsMovementGraphDefinition.AimSampling/AimRawSources`。

## 验证证据

以下日志位于 `artifacts/`，所列最终进程均已实际退出 0：

- `aim-source-tests-final.log`：132 项相关 Import 测试通过，包含 19 项 Aim
  网格/来源专项，覆盖数据拒绝、绑定隔离和资源共享。
- 原生网格导出包含 253 个静态输入、30/60/120 Hz 共 840 帧连续输入；
  编译器逐项核对权重和样本顺序。
- `aim-source-raw-verified.log`：4 资产、144 个原生姿势、11,376 骨通过；
  最大位置误差 3.126582e-7，四元数误差 1.8096182e-7。
- `aim-source-additive-verified.log`：3 资产、156 个原生附加姿势、12,324 骨
  通过；最大位置误差 6.599748e-7，四元数误差 3.5838008e-7。新 Aim
  evaluator 来源入口另直接通过 39 个原生端点姿势。混合姿势原生对照待办。
- 原始/附加来源均通过四所有者单线程与并行各 1,920 次采样位值一致；
  Aim 来源入口 10,000 次热调用托管分配为 0。此项不等于 P7 整角色预算。
- `aim-source-build-final.log`：Godot C# 构建 0 警告、0 错误。
- `aim-source-production-single.log`、`aim-source-production-parallel.log`：
  实际 Worker 各 600 帧通过，结果 EAAF62E4D0A80A76、完整姿势
  3103E3B355BF1F3B 一致且保持既有基线；事件 28，lag/stale 均为 0。
  这是现有移动链的回归，不是新 Aim 最终层已接入的证明。
- `aim-source-base-layer-regression.log`：3,360 帧协作回归通过，302 隐藏帧、
  12 次晚期故障重试一致；Aim 来源更新 3,058 次、初始化 63 次。相关性仍由
  夹具提供，日志中的 demo=not_connected 边界保留。

## UE 构建和重复导出

本批遵循 ue-diagnosing-plugin-build-load 技能的完整构建、加载审计、冷启动、
正常 Editor、DataValidation、隔离打包及包后审计流程。新增只读 Aim 网格
导出函数和 -Aim 分支，没有保存或修改 UE 动画资产。

最终构建日志 `aim-source-editor-build-2.log`，包后审计日志
`aim-source-post-package-audit.log`，三项目插件均 PASS；BuildId 为
`cdf84943-cc02-491b-a98b-93bc6dca227b`。两处改动的 C++/头文件在仓库、
项目插件和隔离包中哈希一致。隔离包位于
`artifacts/unreal/AlsAimSourcePluginValidation-20260912-110`。

正常 Editor 重复导出 `aim-source-editor-repeat.log` 已捕获实际退出码 0；
网格、来源索引、原始姿势和附加姿势四份 JSON 均与正式冷导出字节一致。
日志仍有两条 Condition failed 及现有地图/材质等告警，不声称日志零错误。
DataValidation 实际退出 0，汇总 0 errors / 3 warnings，涉及旧
PawnActionsComponent 和导航网格兼容性。

保留三项执行问题的记录：初次 UE 构建缺少正确 .NET 10 运行时，修正仅限
该原生构建进程的 DOTNET_ROOT；初次 Godot 来源查找/索引哈希选择错误已
修正并重跑；RunUAT 接收到字面量 $packagePath，生成包经精确路径和内容
核验后移至上述隔离目录。没有删除源目录或更改系统 .NET 配置。

## 下一阶段

先完成混合 Aim 姿势的 UE 原生对照与嵌套状态按转换栈顺序的逐骨求值，
再接真实 Overlay/BasePoses/LayerBlending 最终姿势及曲线；之后闭合真正
最终曲线反馈、Foot IK/Foot Lock/pelvis/平台约束。用同输入、同脚相位的
UE/Godot 多帧对照验收上身、反向换髋及支撑脚滑移。

原 P5A 通用动作/事件、P5B 全 Overlay 和道具玩法、P5C Mantle/Roll/Root
Motion、P6 Ragdoll/Get-up/Pose Recovery/完整 Camera、P7 十分钟性能验收
继续保留。当前 P3/P4 视觉问题不推迟到这些后续大功能之后。音频暂缓，
本批未 commit/revert/merge，保留既有工作区修改。
