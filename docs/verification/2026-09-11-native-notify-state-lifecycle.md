# 第三十四批：原生 Notify State 跨帧生命周期

## 结果与范围

在既有 AlsTimelineRuntime 中增加 TryAdvanceAssetNotifyStates，补充 P5A 原计划所需的
class-based Notify / Notify State 分发候选，不增加永久队列、独立时钟或静态编号分配器。
调用方提供旧状态、已筛选引用、来源上下文、编号和 scratch，成功后获得活动状态、
回调序列与候选编号。失败只允许改 scratch，不发布任何目标、计数或编号。

这不是实际 Demo 接线完成。Prepare 的 v3 执行保护仍保留；当前源元数据只覆盖
30 个资产的 44 个普通通知，不能拿本批合成 State 探针当作全角色 State 元数据覆盖。
NoMergeOnConcurrentPlay 标志、播放实例上下文仍需纳入正式来源绑定及帧事务。
Named Blueprint 通知、Pivot/图状态事件、Montage slot 条件、回调引发的重入销毁
不在本接口中假装执行。named-only 有效引用明确拒绝，空来源/空通知忽略。

## 原生语义

依据本机 UE 5.9 AnimInstance.cpp 的 TriggerAnimNotifies、TriggerSingleAnimNotify、
EndNotifyStates，以及真实 UAnimInstance 调用结果实现：

- 队列顺序中为新 State 和普通 class Notify 分配编号；普通回调先发生，随后为旧
  状态 End、新状态 Begin、活动状态 Tick。编号顺序不等于最后的回调输出顺序。
- State 按对象等价匹配，保持原编号，但更新为本帧引用上下文。默认不会因为
  OccurrenceHandleId 改变就重启同一个通知对象。
- NoMergeOnConcurrentPlay 要求来源类型与实例编号同时匹配；Montage 与 AssetPlayer
  即使编号相同也不匹配，缺少来源实例上下文时不能延续。
- 匹配时按原生 RemoveAtSwap 移除旧状态，因此未匹配状态的 End 顺序不是简单
  保留原数组顺序。未匹配的 End 使用最后保留的引用，而非构造新的时间或结束标志。
- ReachedEnd 和 ActiveContext 是通知上下文，不是 Begin/Tick/End 的独立开关。
- 强制 AnimGraph/Montage 模式先筛选输入，并忽略默认跳过标志；被排除的旧来源
  可因此结束。EndAll 按旧状态顺序结束并清空，不消耗新编号。
- 默认 updated-source 策略下，未更新来源可以保留旧 State。当前 UE 实现的 Tick
  条件是 `!skipMontage || !skipGraph`，两个来源谓词互斥，保留项仍然 Tick。
  按实际执行结果保留，没有按旁边注释擅改为 AND。调用方负责从引擎等价更新状态
  解析 skip 标志；本批未接入生产来源 readiness 或同帧重复分发管理。
- int32 编号边界与本机引擎源码一致，包括 max 边界的重复零。本批正常范围有
  原生探针对照，极端 wrap 仅有源码级单测。未来编号需与角色/会话身份组合使用，
  不能把角色局部编号当作全局唯一身份。

数据为 unmanaged 顺序布局；输入、scratch 与目标做字节级重叠检查，支持目标原位
替换旧状态，禁止 scratch 别名及两个输出互相覆盖。候选构造/重试 10000 次零分配。
本 API 不执行用户回调；正式接线必须在唯一 Finalize 成功后按顺序分发，不能在
Worker 中提前触发外部副作用，也不能据此宣称已经支持回调中销毁/重新初始化。

## 原生验证

既有 AlsNotifyWindow commandlet 增加可选 `-Lifecycle`。探针只在队列之后注入测试
引用，通过真正 UAnimInstance 入口和派生 UAnimNotify/State 回调记录输出，不复制
被验证的生命周期算法。普通 Notify 哨兵在每次观测前后读取真实全局分配编号；
哨兵自身消费的编号在 fixture 中明确隔离，不冒充被测事件。

169 个连续案例、5 个策略，覆盖全部四种原生分发模式和 EndAll；1183 次真实回调：
普通通知 157 次、End 268 次、Begin 268 次、Tick 490 次。逐项比较活动状态顺序、
完整来源/时间/结束上下文、编号、回调类型和时长，并对每个候选重试两次。
并发引用刻意在队列筛选之后注入，本批验证的是 dispatch，不声称它们都能穿过
FAnimNotifyQueue 的前置 AddUnique。

两次 commandlet 退出 0、0 错误、0 警告，assets_saved=0；未保存模型或动画资产。
fixture `tests/Als.Core.Tests/Fixtures/P3/v4_notify_lifecycle_native.json` 与独立重复
导出 `artifacts/unreal/notify-lifecycle-repeat-20260911.json` 字节完全一致：
`519E93A477189632417D4AD038A7311EBD4F86C8A38EE00BA28120126E035AD8`。

## 回归结果

| 检查 | 结果 |
| --- | --- |
| 新增生命周期 Debug | 25/25 |
| Core 常规 Debug，排除 AlsP5aGoldenTests / AlsP5aTraceSchemaTests | 1780/1780 |
| 通知窗口/队列/生命周期 Release | 62/62 |
| Import 全套 Debug | 768/768 |
| Godot 优化 Debug 构建 | 0 错误、0 警告 |
| 实际 Cycle single/parallel | 各 180 帧通过，120 帧来源时间检查 |
| 实际 Cycle 双模式 late_transaction | 来源同步/输入及既有运行时、姿势共同回滚通过 |

TRX 位于 `artifacts/test-results/notify-lifecycle/`。保留两类首轮失败：新增测试把
六项回调误计为七项，修正计数但未改变运行时或顺序预期；首轮更宽泛排除所有
Golden 的 Core 集合共 1671 项，旧缓存零分配测试报告 7168 字节。该旧断言未改，
固定关闭 tiered compilation 后单独复核 1/1、正确常规集合复跑 1780/1780。
复跑通过不代表已定位第一次分配的原因，也不删除失败记录。

实际运行摘要保持 result=C658034A39C5917B、full_pose=076E345A0151A012、
pose=D6B3D85394700CC6、root=309E8D0E0BEEB2CB。失败注入第 13 帧诊断为预期。
这说明既有 Demo 未回归，不证明新生命周期已参与 Demo 回滚或已改善滑步。
相机和输入源码 SHA 与第三十三批相同。未重跑十分钟矩阵、完整 P4 门禁或视觉截图。

## UE 构建和保留项

按 ue-diagnosing-plugin-build-load 技能完成全项目 Editor 目标构建、三插件审计、
独立 BuildPlugin、打包后审计、DataValidation 和非 NullRHI Editor 冷启动。
未修改引擎源码、复制 DLL、编辑 BuildId 或执行 Live Coding；技能引用的两个
superpowers 调试/验证技能未列于当前可用目录，采用首错记录和逐项证据检查。

完整构建日志前缀：
`Saved/Logs/PluginBuild/20260910T160608899Z-171a04cd73174112ab0d3d6dba25249f`。
日期是 UTC，本文采用香港时间 2026-09-11。
BuildId=015ca4ed-618b-4c74-9b03-a4854c06ae7b，输入 fingerprint：
`C2441AB0DA7B4D2380F44AB6D29D8576E51E591FD938A640E53DBFEFC5AC597A`。
独立包 `artifacts/unreal/AlsNotifyLifecyclePluginValidation-20260911/` 成功，约 65 秒。

资产验证 688 个资产，退出 0、0 错误、3 个既有警告。正常 Editor 冷启动退出 0，
AlsGodotExporter、BlueprintLisp、AutoTestTools 模块加载成功；仍保留此前相同的
两条 AutomationTest Condition failed，以及旧 AI/Navmesh/材质/渲染线程警告。
不能报告无错误 Editor 验收。日志在 `artifacts/unreal/notify-lifecycle-*-20260911.log`。

## 继续顺序

本批属于原规划 B/P5A，并不是用后续动作功能绕开基础移动欠缺。
下一步将来源窗口、队列随机状态、State 及图状态/Pivot 事件、正式来源模式纳入
已有 Prepare/Finalize 的同一候选存储和主线程分发，再迁移 v3 执行入口。
补齐正式 State 标志/上下文和全角色来源、Montage slot 条件；不再重复实现已通过
原生对照的窗口、队列或生命周期组件。

随后完成 Main/Slot、Standing/Detail/Stop、惯性化、Lean/Sprint Impulse 与真实权重，
按同输入 UE/Godot 状态/曲线/源时间/骨骼和多帧图像验收基础移动；继续动态上身、
全部 Overlay/道具，再到 P5C、P6、P7。音频仍暂缓，未 commit、revert 或删除用户改动。
