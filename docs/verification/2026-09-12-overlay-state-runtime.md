# Overlay 状态转换完整性与 UE 原生逐帧对照

日期：2026-09-12；第一百一十四批。工作区为 `../GodotALS-p5a-events-actions`。

## 完整性结论与本批范围

当前资产的直接行为基准是 ALS V4 的 `ALS_AnimBP`。ALS-Refactored C++ 是
架构与算法参考，两者不能混用后仍宣称逐节点等价。相同片段和局部公式
不足以保证相同输出，还需要相同的状态门控、来源时钟、同步、混合空间、
曲线历史、通知生命周期和最终约束顺序。

这些缺口均属于已有规划。当前上身、换髋和起步滑步必须在 P3/P4 整链
修复中验收；所需 P5A 同步/通知能力前置，不等待 P5C/P6 完成。

本批实现五个 Overlay 状态机的定义编译和独立状态更新，共 26 个状态、
50 条转换，复用上批 148 个独立来源绑定。正式定义加载已接入编译器，
但正式 Demo 尚未消费完整 Overlay 姿势图，不能宣称上身效果已修复。

主要文件：

- `src/Als.Core/Locomotion/AlsOverlayStateDefinition.cs`：原生条件、转换、曲线、逐骨配置及机器定义。
- `src/Als.Core/Locomotion/AlsOverlayStateMachine.cs`：候选状态、转换栈、更新顺序、通知身份及惯性化请求。
- `src/Als.Import/Compilation/AlsOverlayStateCompiler.cs`：原始图、编译委托、出口顺序及绑定版本校验。
- `src/Als.Godot/Animation/AlsMovementGraphDefinition.cs`：正式定义加载。
- `tools/unreal/AlsGodotExporter/Source/AlsGodotExporter/Private/AlsAimStateTraceLibrary.cpp`：新增 Overlay 原生探针，保留 Aim 入口。
- `tools/unreal/export_overlay_state_trace.py` 与 `tests/Als.Import.Tests/AlsOverlayStateCompilerTests.cs`：原生导出及消费对照。

## 必须保留的原版语义

顶层有 13 个内容状态及一个 conduit。所有 Overlay 配对经 conduit 路由，
conduit 本身不进入、不采样。13 条出口使用惯性化：机器立即切换，同时
向外围发出 0.2 秒惯性化请求，不能替换成普通的 0.2 秒状态交叉混合。

Rifle、Pistol 1H、Pistol 2H 和 Bow 各有 Relaxed/Aiming/Ready 三个状态。
保留每帧最多三次转换、首次更新跳过混合/转换通知、失去相关性后重新
初始化，以及被中断转换的顺序和零权重更新。

Ready 超时出口采用严格 `elapsed > 3`。前三种武器读取上一已提交最终
输出的 `Enable_Transition == 1`；Bow 读取 `RotationAmount == 0`。
它们是不同曲线，不可统一。曲线出口先于移动出口，并保留 QuickFeet
逐骨权重及各自生成通知；Sprint/InAir、AimingIn/Out 的条件和时长也
来自各自原图。该武器收起规则不能误作所有左右换向的统一等待时间。

两个自定义混合曲线各核对 201 个原生样本，QuickFeet 核对 79 骨映射及
33 个原生配置案例。8 个转换通知保留原生生成身份，但 gameplay 分发、
跨图同步及外围惯性化消费者仍需后续正式接线。

## 验证结果

原生探针运行真实 UE 生成类、状态机及其子图，覆盖五个机器在
30/60/120 Hz 的 15 条轨迹，共 11,436 帧。逐帧比较状态、时间、混合栈、
权重、更新顺序、通知和惯性化请求；浮点容差为 2e-6，离散身份精确匹配。
覆盖 189 隐藏帧、186 相关但零权重帧、183 inactive 帧、922 叠加转换帧、
84 个生成通知、1,014 个惯性化请求。探针运行姿势求值以维持真实生命周期，
本批没有把最终骨骼姿势作为该状态轨迹的对照输出。

冷启动正式夹具 `tests/Als.Core.Tests/Fixtures/P3/v4_overlay_state_native.json`
与普通 Editor 重复导出逐字节相同，SHA256 均为
`95FCFD755ECFF6F64BB4B1307003F83411E404A796F4FCB386F89C27DB049D01`。

| 检查 | 结果 | artifacts 日志 |
| --- | --- | --- |
| Overlay 专项 | 37 通过，含上述全部原生帧、同帧重试、四所有者一致及 10,000 次热更新零分配 | `overlay-state-native-tests-first.log` |
| Release Import 全集 | 1,913 通过，1 项既有跳过 | `overlay-state-import-release-regression.log` |
| Release Core 的 Locomotion 命名空间 | 519 通过；此次筛选范围不同于前批 770 项移动集合 | `overlay-state-movement-core-regression.log` |
| Release Core 全集 | 2,328 通过，23 失败，不能标记全绿 | `overlay-state-core-release-regression.log` |
| 正式 Worker single/parallel | 各 600 帧通过，result EAAF62E4D0A80A76，full pose 3103E3B355BF1F3B，事件 28，lag/stale 0 | `overlay-state-production-single.log`、`overlay-state-production-parallel.log` |
| Godot Debug Optimize=true 构建 | 0 警告、0 错误 | `overlay-state-runtime-final-build.log` |
| UE 整项目 Editor 构建/插件审计 | 最终构建及三个项目插件审计通过 | `overlay-state-native-build-7.log` |
| UE 资产验证 | 0 错误、3 条既有告警 | `overlay-state-data-validation.log` |
| 隔离插件打包及打包后审计 | 实际退出码 0，三个项目插件审计通过 | `overlay-state-plugin-package.log`、`overlay-state-post-package-audit.log` |

Import 跳过项为 `AlsLayerBlendingRuntimeTests.NormalEditorRepeatsTheConsumedGraphAndInputSemantics`。
Worker 检查证明既有生产路径保持，不能代替 Overlay 最终层接入和视觉验收。

## 保留的失败与限制

全量 Core 的 23 项失败由一项旧转身姿势集合检查和 22 项 P5A 对照/模式
检查构成。前者四个 crouching turn 案例的 turnYawDelta 不符；后者报告
当前 binding snapshot 不满足固定 native plan/host plan。
`tools/Als.P5aOracle/Program.cs` 的 `P5aPlanBuilder.Build` 明确检查固定
definition/layout/graph/snapshot digest。测试已自动重编 Oracle，不能
简单归因于旧可执行文件。本批没有完成这些差异的归因与修复，也没有
替换固定摘要或放宽姿势容差来制造通过；需结合 P4/P5A 原生来源继续核对。

额外运行的未优化 Debug Import 曾出现 985 通过、1 跳过、1 分配断言失败，
随后在既有 Grounded/Standing/Main 缓存调用链发生栈溢出并中止，日志为
`overlay-state-import-regression.log`。按仓库验证脚本采用 Release 复测后
Import 全集通过；这不等于未优化 Debug 路径已修复。

首次原生探针因临时 Inertialization 节点缺少生成类 NodeData 而断言退出。
已改为复制真实 Overlay 编译节点模板，后续冷/普通 Editor 通过。首次构建
过程中也曾出现 BuildId/receipt 不一致；已按构建技能进行可恢复隔离并完整
重编三个插件。备份保留在
`../AdvancedLocomotionSystemV/Saved/BuildReceiptBackup/20260912T134805453Z`，
未删除资产或源代码。普通 Editor 的既有 Condition failed 信息保留在日志中。

本批未重新运行 P7 性能验收；前批十角色 P4 p95 2.559 ms 高于 2.5 ms 的
未通过项继续保留。未 commit/revert/merge，保留原有工作区修改。

## 下一步与完成标准

1. 完成 Overlay 完整姿势/更新图：BlendList、ModifyCurve、嵌套机器来源所有权、
   独立时钟和跨图同步，以及外围惯性化、生成通知的事务消费者。
2. 将 Aim/Overlay/BasePoses/LayerBlending 接入实际 Worker/Demo 的最终层，
   按原图顺序求值脊柱/手部修正，并反馈真正的最终曲线。
3. 闭合 Foot IK、Foot Lock、pelvis 与平台相对约束；核对本批暴露的 P4
   转身对照差异及 P5A 固定基准，使组件通过和整仓回归状态分别可检查。
4. 在相同输入、初始脚相位和帧率下做 UE/Godot 多帧对照及人工验证，检查
   双臂、上身侧倾、左右换髋等待、支撑脚位移。至此才能关闭当前视觉问题。
5. 按原计划继续 P5A 通用动作链、P5B 全 Overlay/道具玩法、P5C Mantle/Roll/
   Root Motion、P6 Ragdoll/Get-up/Pose Recovery/完整 Camera、P7 十分钟性能。
   音频按用户要求暂缓。
