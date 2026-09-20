# 第三十三批：原生通知队列候选运行时

## 结果与边界

在既有 AlsTimelineRuntime 中新增 TryQueueAssetNotifies，消费上一批正式编译的
AlsAssetNotifyPolicy 和提取引用，生成可提交的队列与随机流候选值。不持有独立
时钟、永久队列或全局可变状态；调用方仍需把候选纳入唯一 P5 帧事务。

本批完成的是入队筛选、引用去重、合并和原子性，不是跨帧 Notify State 的
Begin/Tick/End。实际 Demo 的 Prepare/Finalize 尚未替换，v3 保护未放开。完整
来源元数据、NotifyMode 调度、Montage slot 门控、Pivot/状态事件和 Standing/
Detail/Stop 组装仍需推进，没有新的动作质量或滑步改善结论。

## 实现依据

依据本机引擎 AnimNotifyQueue.cpp、AnimTypes.h、RandomStream.h 的执行规则：

- 顺序为有效引用、leader/follower、专用服务器、权重阈值、LOD、概率、scope。
  权重等于阈值可通过；LOD 要求 filterLod 严格大于 predictedLod。
- 普通通知即使概率为 0 或 1，走到概率步骤也会推进随机流；State 不抽概率。
  scope 拒绝发生在概率之后，不能把 scope 提前而少消费一次随机数。
- 初始 seed=0x05629063，按 UE FRandomStream 的 uint32 更新和高位 float 转换；
  清空本帧队列只丢弃引用，保留已提交种子，不创建新随机序列。
- 普通通知可重复；State 使用原生事件指针/对象/名称等价规则，只保留第一次
  入队引用及其播放 handle、当前时间、活动标志和结束标志。不同播放 handle
  不强制拆开同一 State，不用后来的 ReachedEnd 或更高权重覆盖首次上下文。
- Append 合并已经过滤过的队列，不再次检查概率、权重、scope 等，只做原生
  State 唯一性处理。named/class-based 混合的原生比较方向也由对照覆盖。
  Core 可表达 named 引用，但当前正式动画集导出器的 named-only 限制没有解除。
- 空来源或空通知引用直接忽略，不抽随机数。无效非空身份、策略或数值拒绝。

新引用和队列上下文是顺序布局的 unmanaged 值。调用方提供 current/incoming、
scratch 和 destination；scratch 不得与任何输入/目标重叠，destination 可原位
替换 current。先验证，再在 scratch 构建，成功后才写 destination/输出 count/
candidateSeed。失败可改变 scratch，但不会留下部分发布结果；10000 次候选重试
零分配。当前是可提交组件，不宣称随机状态已进入 Demo 的正式事务存储。

## 原生对照

现有 AlsNotifyWindow commandlet 增加独立可选 -Queue，默认窗口提取路径保持。
新探针使用真实 FAnimNotifyQueue::AddAnimNotifies。Reset/Append 在引擎 DLL 中
没有导出，最终通过以下公开引擎调用链观测，而不是复制实现：

1. UAnimInstance::ClearQueuedAnimEvents 调用原生 NotifyQueue::Reset。
2. 通过实际 FAnimSync 的资产 Tick 把探针引用送入代理队列，随后
   FAnimInstanceProxy::PostUpdate 调用原生 NotifyQueue::Append。

合并探针的资产 Tick 只注入指定引用；被验证的是引擎真正执行的队列合并，不是
完整 ALS 资产播放或 State 回调。专用服务器案例使用 PIE World 的原生 NetMode，
并检查 Component.GetWorld 和 World.GetNetMode，未用固定真假值代替服务器判定。

12 个策略、148 个案例，包含 50 个专服案例、16 次 Append、40 次保留队列继续
执行，以及权重/LOD 边界、scope、普通重复、共享 State 对象、同类不同 State、
null、跨帧随机历史。逐项比较完整输出顺序、上下文、起始/候选 seed，并重试两次。

原生文件 `tests/Als.Core.Tests/Fixtures/P3/v4_notify_queue_native.json` 与独立重复
导出 `artifacts/unreal/notify-queue-repeat-20260910.json` 完全一致，SHA-256 为：
`7742BA649738A2FBB7096E9BDDB0EC5EF9A0C3047708E043B9FD8DB20DB5889A`。
两次退出 0、0 错误、0 警告，assets_saved=0。没有保存或重新导出 UE 模型/动画。

## 测试与运行

| 检查 | 最终结果 |
| --- | --- |
| Core 常规 Debug，排除 P5aGolden/P5aTraceSchema | 1755/1755 |
| Import 全套 Debug | 768/768 |
| 窗口和队列 Core Release | 37/37 |
| 主快照与通知元数据 Import Release | 44/44 |
| Godot 优化 Debug 构建 | 0 错误、0 警告 |

本批新增 15 个 Core 测试和 1 个主快照联合测试。联合测试用正式来源定义提取
窗口，再使用正式策略和实际物理 handle 组装同一候选队列，跳过 TeleportEvaluator；
它证明数据接口能贯通，不是实际 Demo 分发验收。TRX 位于
`artifacts/test-results/notify-queue/`，未重跑大规模 Golden/Schema 或十分钟矩阵。

实际 Cycle single/parallel 各 180 帧、120 个来源时间检查帧通过，摘要保持：
result=C658034A39C5917B、full_pose=076E345A0151A012、
pose=D6B3D85394700CC6、root=309E8D0E0BEEB2CB。
双模式 late_transaction 验证既有来源同步、移动输入、Controller、姿势/P4 banks
共同回滚；第 13 帧诊断为预期注入。这不等于新队列已参与 Demo 回滚。
Godot 日志位于 `artifacts/notify-queue-*.log`。相机/输入摘要与前批相同。
真实资源 Standing Cycle 在 30/60/120 Hz 通过 15,216 次源时间检查、5,040 帧
移动入口检查和零分配检查，结果与前批相同，不作为新队列的可玩 Demo 验收。

## UE 构建

按 ue-diagnosing-plugin-build-load 技能执行完整 Editor 构建、所有适用插件审计、
独立 BuildPlugin、打包后审计、DataValidation 和普通 Editor 冷启动。没有改动
引擎源码、复制 DLL、编辑 BuildId 或用 Live Coding 代替完整构建。

保留两次首错：JSON 对象已是 TSharedRef 却再次调用 ToSharedRef 的编译错误；
随后直接调用未导出的 Reset/Append 导致链接错误。改为上面的公开引擎调用链后
完整构建通过。Import 联合测试还修正一次 Core/Import 枚举类型不一致的编译错误，
没有放宽测试容差。额外调试/验证技能不可用时，保留首错日志并逐项核验。

最终完整构建日志前缀（UE 项目内）：
`Saved/Logs/PluginBuild/20260910T154252479Z-a4f952f18f3c433ab2f001b266d36e35`。
BuildId=01414f6b-b795-4dcb-94de-fe2c50432fce，fingerprint 为
`8FBBE0EF084FDF5A093396914572CAF97EC23D0927A6B0E36EBAAC5AF3142482`。
AlsGodotExporter、AutoTestTools、BlueprintLisp 全部通过审计。独立插件包
`artifacts/unreal/AlsNotifyQueuePluginValidation-20260910/` 构建成功。

资产验证检查 688 个资产，退出 0、0 错误、3 个既有警告。正常非 NullRHI Editor
冷启动退出 0，三个模块加载成功；仍有此前两条 AutomationTest Condition failed
及 AI/Navmesh/材质/渲染线程警告，不是无错误 Editor 验收。日志分别为
`artifacts/unreal/notify-queue-datavalidation-20260910.log` 与
`artifacts/unreal/notify-queue-editor-restart-20260910.log`。

canonical 与安装插件源一致，`git diff --check` 退出 0，只有既有 LF/CRLF 提示。
保留用户未提交改动，无 commit、revert 或删除操作。

## 下一步

直接推进原生 State Begin/Tick/End 生命周期、终止与实例身份，随后把来源窗口、
NotifyMode、候选随机状态/队列、Pivot/状态事件纳入现有 Prepare/Finalize。
补全全角色来源与 Montage slot 条件，再迁移 v3 执行入口，不另造永久调度器。

继续 Main/Slot、Standing/Detail/Stop、惯性化、Lean/Sprint Impulse 及临时权重替换，
再按原计划完成动态上身、全部 Overlay/道具、Mantle/Roll/Root Motion、恢复/完整
相机和十分钟预算。音频仍暂缓，最终同输入多帧骨骼/画面与人工验收范围保持。
