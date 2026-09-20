# 第三十一批：循环资产通知窗口

## 结果与边界

在既有 `AlsTimelineRuntime` 中补充 `TryExtractAssetNotifies`，旧非循环入口转发到同一
实现。循环资产按本机 UE 的实际 Tick 窗口分段提取，保留跨圈重复 State 引用、定义
原始顺序及结束标志，不用上一帧提交 cursor 代替当前 Tick 的起点和 delta。

这是 P5A 原执行链缺失的前置语义，不是第二套时钟或队列。有效通知元数据尚未接入
生产主绑定，NotifyQueue 过滤/去重、Notify State 跨帧生命周期、Pivot 和源状态事件
尚未纳入 Prepare/Finalize。旧 P5 Prepare 继续拒绝 v3，不能提前放开给旧规则执行。
本批没有 Demo 画面捕获，也没有起步滑移、交错步、上身或完整 ALS 已修复的结论。

## 原生依据与实现

本机 `../UnrealEngine` 的 `AnimSequenceBase.cpp` 中 GetAnimNotifies、
GetAnimNotifiesFromDeltaPositions，以及 AnimationRuntime.cpp 的 AdvanceTime
是本批依据。现有 AlsNotifyWindow commandlet 新增可选 `-Looping`，通过带真实
FAnimTickRecord 的 FAnimNotifyContext 调用资产提取。默认非循环导出保持原合同。

- 新 schema 2 记录每个窗口是否循环，以及目标平台实际计算出的 nativeMaxPasses。
- 提取使用 float 逐段推进；正好到达端点不额外跨圈，零推进仍按原生重叠规则处理。
- 循环段以原始定义顺序输出，不排序或提前合并 State。结束标志逐段计算。
- 保留原生先无符号转换再 clamp 的遍历上限，不擅自改成绝对圈数。长度 1、起点
  0.5 时，delta=-2.5 的原生上限是 1000，+2.5 是 2；后者可截断剩余尾段。
  该行为由当前 UE Win64 原生探针验证，不宣称跨平台转换行为一致。
- 先按完全相同的遍历计数，再写入调用方缓冲；第二圈以后才发生的溢出也不留下
  部分输出。非有限输入/结果拒绝，既有定义、唯一事件 ID 和有效端点校验保持。

原生探针包含 6 个真实资产和 5 个合成序列；真实资产中 Walk F 有 2 条通知，五个
Detail 资产无通知。合成序列覆盖未排序、重叠、端点和有效触发偏移，不冒充真实
ALS AnimBP 或完整 NotifyQueue 行为。

新文件 `tests/Als.Core.Tests/Fixtures/P3/v4_looping_notify_window_native.json`
为原生命令生成，11,353 个窗口，其中循环 1,627、同步非循环对照 9,726；
另保留旧 11,265 个非循环窗口回归。重复导出文件位于
`artifacts/unreal/looping-notify-repeat-20260910.json`，两份 SHA-256 一致：
`AF97109EBE6C663A754409BDC91B8C251A280FE88B371BA8E0AAF4C2EF8ED85B`。
两次命令均退出 0，报告 assets_saved=0、0 错误及 0 警告，没有重存 UE 资源。

## 测试与构建

| 检查 | 结果 |
| --- | --- |
| 原有通知专项 Debug | 15/15 |
| 新旧通知专项 Debug / Release | 各 22/22 |
| Core 常规 Debug，排除 P5aGolden/P5aTraceSchema | 1740/1740 |
| Import 全套 Debug | 737/737 |
| Godot 优化 Debug 构建 | 0 错误、0 警告 |

TRX 位于 `artifacts/test-results/looping-notify/`。新增覆盖全部原生窗口、正反向
边界分段及端点、晚期缓冲溢出原子性、非有限倍率和 1000 次提取/重试零分配。
本批未重跑大规模 P5aGolden/Schema 矩阵，不将常规 Core 结果记为全矩阵通过。

实际 Cycle single/parallel 各 180 帧、120 个时间检查帧通过，摘要保持：
result=`C658034A39C5917B`、full_pose=`076E345A0151A012`、
pose=`D6B3D85394700CC6`、root=`309E8D0E0BEEB2CB`。
双模式晚期失败注入验证来源同步、移动输入、Controller、姿势和 P4 banks 一起
回滚；第 13 帧诊断是预期注入。日志 `artifacts/looping-notify-*.log`。

真实资源 Standing Cycle 在 30/60/120 Hz 通过：9 次换髋过渡、371 个等待帧、
15,216 次源时间检查、5,040 帧移动入口检查，采样与活动求值均零分配；结果与
前批相同。该资源测试不是完整 Standing/Detail/Stop Demo 或视觉验收。

按 ue-diagnosing-plugin-build-load 技能执行完整 Editor 目标构建、所有适用插件
审计、独立 BuildPlugin、打包后再次审计、DataValidation 和非 NullRHI 冷启动。
未复制叶 DLL、修改 BuildId 或使用 Live Coding 绕过完整构建。

首次 UBT 在进入 C++ 前因系统缺少 .NET 10 失败；保留首个失败日志，没有修改系统
安装。后续仅对子进程设置 UE 自带 .NET 10 的 DOTNET_ROOT，完整构建成功。
成功日志前缀 `Saved/Logs/PluginBuild/20260910T144710921Z-426436f32712487fb92c7b9a77ebbdc4`，
BuildId=`dda2cb23-1f68-4a51-848b-0e0267b97b5f`，构建状态位于 UE 项目
`Saved/PluginBuildState/AdvancedLocomotionSystemVEditor.json`。AlsGodotExporter、
AutoTestTools 和 BlueprintLisp 均通过审计。独立插件包在
`artifacts/unreal/AlsLoopingNotifyPluginValidation-20260910/`，BuildPlugin 退出 0。

资产验证检查 688 个资产，退出 0、0 错误、3 个旧 AI/Navmesh 相关警告。正常 Editor
冷启动退出 0、三个插件加载成功，但日志仍有此前记录过的两条
`LogAutomationTest: Error: Condition failed`，以及 AI/Navmesh/材质/渲染线程警告。
这不是无错误 Editor 验收。日志分别为
`artifacts/unreal/looping-notify-datavalidation-20260910.log` 和
`artifacts/unreal/looping-notify-editor-restart-20260910.log`。

canonical 与已安装 commandlet 源码摘要相同；已人工确认的相机/输入摘要不变。
保留现有未提交改动，没有 commit、revert 或删除用户文件。`git diff --check`
退出 0，仅有既有 LF/CRLF 提示；本批启动的 UE/Godot/测试进程均已退出。

## 后续执行顺序

1. 把有效 Trigger/EndTrigger、NotifyMode、follower/权重/LOD/概率等原生条件纳入
   正式绑定；补 NotifyQueue 的 State 引用去重及跨帧生命周期，不能按播放器身份
   擅自假设 State 永远独立，也不能继续使用旧 winner 规则替代原生队列。
2. 来源 Tick 窗口、事件与历史统一进入现有 Prepare/Finalize 和失败回滚，再迁移
   v3 执行入口。已有主快照和本批提取入口直接复用，不再包装新旁路。
3. 组装 Main/Slot 上游、Standing/Detail/Stop、惯性化和 Lean/Sprint Impulse，替换
   临时 MovingWeight；对齐状态、源时间、曲线、骨骼和同输入多帧画面。
4. 继续动态上身、P5B 全 Overlay/道具、P5C Mantle/Roll/Root Motion、P6 恢复/完整
   相机及 P7 十分钟预算。音频仍暂缓，最终人工验收与完整移植范围不变。
