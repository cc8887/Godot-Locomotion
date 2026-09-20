# UE 原生动态 Montage 生命周期对照（第九十五批）

本批承接完整性修复计划的 P5A 前置依赖。已建立实际 UE 运行时数值基准，
并修正动态 Montage 的延后混合重置；通用 ActionPlayer 所有权合并和完整
Worker/Demo 图接线尚未完成，不能宣布滑步、换髋或上身视觉修复完成。

## 原生基准与发现

新增 `UAlsMontageLifecycleProbe`，在真实 UAnimInstance 上调用原生
PlaySlotAnimationAsDynamicMontage、Montage_UpdateWeight、Montage_Advance
与 UpdateMontageEvaluationData。使用实际 Mannequin 骨架和八个站/蹲转身
动画，不用另写公式产生期望值。先更新旧 Montage 并冻结求值数据，再执行
本帧播放请求，与当前 BaseLayer 的调用顺序一致。

导出 40 组、8066 帧：八个资产各自 30/60/120 Hz，自然结束、跨站/蹲快速
替换、同帧多次替换、反向、零速率、零淡入/淡出、片尾起播、自定义/默认
淡出触发和大 delta。记录实例身份/顺序、活动与播放状态、位置/倍率、
当前和目标权重、Alpha、剩余时间、BlendTime，以及更新前冻结的求值列表。
UE 全局实例 ID 在每组内按首次出现顺序映射为单调序号，保留不同实例身份。

首轮 Core 对照 41 项中 7 项失败，定位到 FAlphaBlend 的延后重置语义：

- 新 Montage 的默认 FAlphaBlend 剩余时间为 0.2 秒。Play 设置新范围和
  时长，却不立即 Update，因此请求当帧的权重仍为零，零淡入也不例外。
- 首次 Stop 会调用 Update(0)，立即处理重置。
- 对已经 Stop 的实例缩短淡出时间，只设置范围和时长，保留当帧 Alpha、
  当前权重及剩余时间，到下一次 Update 才重置。

依据为本机 `AnimMontage.cpp` 的 Play/Stop/UpdateWeight，以及
`AlphaBlend.cpp` 的 SetValueRange/SetBlendTime/Update/ResetBlendTime。
旧实现对三种情况均立即重置，正常 0.2 秒路径的组件测试未揭示区别。

`AlsDynamicMontageRuntime` 现将待重置标记纳入实例候选状态，在下一次 Begin
推进权重前处理；首次中断仍立即重置。取消/重试同时恢复该标记、实例身份、
时间和冻结的求值数据。原有固定 P5 ActionPlayer 状态布局没有改变。

## 验证结果

- 原生专项 41/41；40 组的每帧均执行准备、对比、丢弃、重试、再对比和提交。
  浮点绝对误差上限 0.000003，实例数量、身份、顺序和布尔状态精确比较。
- Core 动态 Montage、ActionPlayer、通知、来源事件与契约相关回归 314/314，
  包含既有预热后无托管分配验证。首次红测保留在
  `artifacts/montage-native-first-test.log`；修复后日志为
  `montage-native-second-test.log`、`montage-native-core-regression.log`。
- Godot 构建零警告、零错误。八个实际转身资产采样通过；真实姿势/通知
  测试 2400 帧、50 次回调、58 次有事件的晚期故障重试通过；映射/旧入口
  各 3360 帧、12/6 次晚期故障通过。日志前缀 `artifacts/montage-native-`，
  后缀分别为 `clips.log`、`turn.log`、`mapped.log`、`legacy.log`。
- 生产 single/parallel 各 180 帧通过，结果摘要 `21E164D829153157`，
  完整姿势摘要 `CF9225D4DE9B2C8B`，来源事件均为 10。生产仍走旧 Standing
  入口；这证明回归保持，不证明新完整图已经接入 Demo。

## UE 构建与导出证据

依照 ue-diagnosing-plugin-build-load 工作流完成项目 Editor 全量构建、
三个项目插件审计、命令行导出、DataValidation 和隔离 BuildPlugin。

- 成功构建记录前缀
  `20260912T013628326Z-fcac6893757f43fcbb3379da3f4bcc01`，fingerprint
  `ADC27C4396615EF87D01B44F943785B2B226B758045E6488C4A261F7503CB407`，
  BuildId `369675c0-434c-4633-b2aa-532acf57bb8f`。
- 原生命令行导出退出 0，正式夹具
  `tests/Als.Core.Tests/Fixtures/P3/v4_dynamic_montage_native.json`，SHA256
  `5A91250DAD185040AA2833080EA1046D9610DFA7539FB772163AD70932EFE1DA`。
  两次普通 Editor 导出 SHA256 均相同，均有八资产/40 组成功标记。
- DataValidation 退出 0，688 个资产、0 error、3 warning。警告涉及旧
  PawnActionsComponent 与 Navmesh 版本，不能宣称全局零警告。
- 隔离包 `artifacts/unreal/AlsMontageLifecyclePluginValidation-20260912-95`
  构建退出 0；未部署包内 DLL。包构建后项目插件审计再次通过。

本批首轮编译错误为读取受保护的 Proxy 求值数据，改为派生 Proxy 提供只读
访问；后续链接错误为 NotifyQueue.Reset 未导出，探针改清空两个公开队列。
该探针只测生命周期，不派发 gameplay 通知，也不作为原生通知队列对照。

首次完整构建重建了引擎 NetCore 并更新引擎元数据，导致旧项目收据不匹配。
核实后使用技能脚本隔离旧生成产物，保存在 UE 项目
`Saved/BuildReceiptBackup/20260912T013454449Z`，可以恢复。没有手改 BuildId，
没有删除源码/资产，没有用叶插件 DLL 冒充项目构建。保留所有失败日志。

普通 Editor 首次虽成功导出并记录完整关闭日志，进程退出码为
`-1073741819`（0xC0000005）；没有新增本项目 Saved/Crashes 报告，原因未
确定。不能据此报告首次正常 Editor 生命周期成功。随后相同参数复查退出 0，
日志 `artifacts/ue-montage-lifecycle-editor-repeat2.log`，两次数据一致；原先
两条 AutomationTest Condition failed 仍存在，未将其描述成无错误启动。

## 适用边界与下一步

原生基准覆盖当前非加法、单片段、单 section、单循环、单位资产 RateScale
的动态转身 Montage。没有覆盖通用 Montage 多 section/多片段、branching
point、Notify State gameplay、Root Motion、完整 AnimBP 或完整角色骨骼输出
的 UE/Godot 逐帧对照。不得以 8066 帧数字扩展这些结论。

下一项继续通用 ActionPlayer 与动态 Montage 的同组互斥、独立实例/旧淡出
所有权及事件统一提交，然后闭合最终曲线反馈和 Worker/Demo 输出；再按
原规划完成动态上身、脚部与平台、Overlay/道具、Mantle/Roll、物理恢复和
Camera。已确认键鼠体验不变；基础视觉多帧/人工验收与最终十分钟预算仍未
完成。本批未提交 Git，也未回滚用户已有改动。
