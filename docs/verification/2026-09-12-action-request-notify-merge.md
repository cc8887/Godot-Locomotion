# 动作请求所有权与 Montage 通知合并

日期：2026-09-12，第九十七批。承接 P5A 前置依赖和完整 BaseLayer 接线。

## 本批实现

新增 AlsMontageActionRuntime，将请求历史、优先级、可中断规则、取消、替换和
完成结果绑定到 AlsMontageRuntime 的实际实例 ID。请求层不维护第二套动画
时间，也不保存单个旧动作姿势尾部。逻辑请求结束后，物理实例按原生混合规则
继续淡出；自然完成以物理实例终止为准。外部同组转身造成的中断只报告一次。

Begin、ApplyRequest、Complete、ValidateCommit、Commit/Discard 覆盖请求和
物理实例的同一候选帧。Complete 在所有播放命令之后核对最终所有者。请求历史、
播放序号和结果可同帧丢弃重试。结果容量沿用既有两条上限；超限会拒绝整个
候选，不能发布部分结果。该请求层与完整 Worker 的失败适配尚未连接。

正式 P5A 动作配置编译为请求策略，并与已验证的物理 Montage、section、
生命周期逐项核对。共享 MovementGraphDefinition 加载这些策略。真实 Roll
姿势 smoke 通过请求层启动、替换和自然完成，同时保留动态转身及全部旧淡出。
BaseLayer 正式映射入口仍未调用这些动作请求；这里没有宣称 Demo 已切换。

通知桥新增 IAlsMontageNotifyBinding 和 Montage 自身通知入口，现有八资产
Turn binding 继续限制为瞬时 Queued Notify，没有放宽原有导入契约。
最终队列按 Montage 自身、Proxy 来源、相关 Slot 片段的顺序合并；State
AddUnique 去重时保留第一个完整上下文，包括来源类型、句柄、Montage ID 对应
的播放 epoch、时间、权重和 NoMerge 规则。修正旧代码按合并后下标查找原始
Montage 数组的假设，防止状态去重后后续通知错位。输出事件保留 SourceActionId。

## 对照依据

直接检查本机 UE 5.9 源码，无本批网络资料或 UE 插件改动：

- AnimMontage.cpp，FAnimMontageInstance::HandleEvents，2778 行起：已中断
  实例不抽取；Montage 自身 Queued Notify 先进入 AnimInstance 队列，然后
  片段通知进入 Slot 队列。两者上下文均采用 Montage 当前时间和实例 ID。
- AnimInstanceProxy.cpp，664–665 行：先 Append Proxy 队列，再 ApplyMontageNotifies。
- AnimNotifyQueue / 既有 Core 队列规则：State AddUnique 保留先接受的引用，
  最终 Append 不重复概率抽样或再次过滤。

本批没有重新导出原生轨迹，没有新增原生 Roll Notify State 逐帧 oracle。
新增混合状态测试为合成契约夹具，不替代真实 Roll 通知导入和原生行为对照。

## 验证与失败记录

- Core 相关 355 项通过。新增请求层 15 项、通知合并 5 项，覆盖旧请求重放、
  拒绝后取消原所有者、无效命令、恢复优先、物理淡出、完成/中断、结果溢出、
  直接/来源/Slot 去重顺序、完整 64 位播放身份、状态结束、回滚和预热零分配。
  日志：artifacts/action-request-core-final.log。
- Import 相关 57 项通过，包含实际 Roll 请求配置和拒绝不匹配物理资产。
  日志：artifacts/action-request-import-final.log。
- Godot 构建零警告、零错误。实际 Roll/转身姿势 240 帧、240 次重试，4 次接受、
  3 次替换、1 次自然完成；46 次旧实例重叠采样、340 次曲线采样包含重试计数。
  日志：action-request-build.log、action-request-mixed.log。
- 映射 BaseLayer 3360 帧、12 次最终 Slot 故障；八资产转身通知 2400 帧、50 次
  回调、58 次有事件的晚期故障重试通过。日志：action-request-mapped.log、
  action-request-turn.log。两者均为组件测试，不是实际 Demo 移动验收。
- 生产 single/parallel 各 180 帧通过，结果摘要 21E164D829153157、完整姿势
  CF9225D4DE9B2C8B、来源事件各 10，与上一批一致。日志：action-request-single.log、
  action-request-parallel.log。生产仍执行旧 Standing 输出，仅证明本批没有
  破坏既有接线，不能证明新完整图已进入 Demo。
- 初次请求测试因 xUnit2012 编译失败，改为 Assert.Contains；初次导入测试缺少
  Contracts using，已修正。较大 Core 批次中一次零分配检查测得 5248 字节；
  单独关闭分层编译的诊断通过，随后将新测试按仓库惯例加入不并行的 Allocation
  collection，默认编译设置下全部 355 项通过。没有分配堆栈证据，不将该次
  噪声进一步归因于某个运行时函数，也没有放宽零分配断言。

## 下一步与未完成项

1. 导出并严格绑定 Roll Montage 自身的 Notify State 和片段通知，统一
   Montage 过滤随机流、Slot 相关性、状态身份与最终事件提交。当前新桥只完成
   合并/分发支持，不能将合成夹具写成真实资产已接通。
2. 将请求、通知、真实 BaseLayer Slot 姿势接到统一帧所有者，替换旧
   ActionPlayer/P5 lane；再把完整 Main Movement 输出接入 Worker/Demo。
3. 闭合最终曲线 presence/上一帧反馈、YawOffset 角色消费、动态上身
   Layering/Add/LS、Aim/手部，以及 FootLock/pelvis/平台。滑步、交错步、
   换髋和上身问题仍需实际移动多帧及人工验证，不能由组件测试关闭。
4. 原 P5A 通用 section/片段、P5B 全 Overlay/道具、P5C Mantle/Roll/Root Motion、
   P6 Ragdoll/Get-up/Recovery/Camera 和 P7 十分钟预算继续保留；音频暂缓。

未改 UE 资产、原生插件或已确认的键鼠输入。未提交 Git，未回滚用户改动。
