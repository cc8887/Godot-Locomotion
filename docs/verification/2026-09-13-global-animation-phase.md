# 全局动画属性与普通姿势图分阶段准备

第一百三十八批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`。

## 问题与完成内容

第 137 批已统一根来源批次，但全局属性仍藏在 BaseLayer 的准备方法中：
外层分层所有者先进入 BaseLayer，再通过回调准备外层图，最后继续来源
更新。这使外层根不能在全局属性准备好之后独立选择子图访问方式。

本批将实际 `AlsBaseLayerFrameRuntime` 拆成两个阶段：

1. `PrepareGlobalFromFrame` 准备 Montage/Action 候选、Jump、Aim、地面/
   空中属性、Idle/Turn 和普通图所需输入。它不需要姿势上下文，不初始化
   或更新移动子图，不推进共享来源，不求值姿势。
2. `PrepareGraph` 接收外层选好的同帧上下文，再绑定 Slot 姿势输入、更新
   BaseLayer 与来源、完成通知队列。之后沿用 Evaluate/ValidateCommit/
   Commit 或 Discard，所有候选依然随最终帧统一发布。

`AlsLayeredAnimationFrameRuntime` 的生产入口已经按此顺序调用：先全局
候选，再根选择与普通分层准备，再普通来源图。移除原
`IAlsBaseLayerOuterGraph` 回调接口；默认 BaseLayer 的组合入口也复用
这两个阶段，没有复制公式或另开运行时。原受控输入入口保持独立模式。

阶段检查拒绝尚未完成全局准备就进入来源图、重复全局/图准备、不同帧
或 delta 的图上下文，以及仅有全局属性时求值、提交或读取来源时钟/
事件。全局阶段可取消并以相同输入重试；允许外层在其后失败，最终姿势
尚未成功时不会发布属性、Montage 身份/时钟或通知历史。

直接只读核对本机 UE 5.9 `AnimInstance.cpp`：第 769 行 UpdateMontage、
第 797 行 BlueprintUpdateAnimation、第 827 行 ParallelUpdateAnimation
的顺序。保持既有 Jump 读历史 Speed、Montage 冻结求值数据与 Blueprint
请求的内部顺序。本批未启动 UE，也未新增 UE 最终图运行时对照。

## 验证

最终优化 Debug 构建 0 错误、0 警告；本批仅修改三个已有 Godot 文件，
没有新增脚本 UID。未修改 Core/Import 算法，因此没有重复其单元专项。

| 检查 | 结果 | 日志（`artifacts/` 下） |
| --- | --- | --- |
| 两阶段全局候选与原组合入口 | 30/60/120 Hz，3,360 帧，每帧全局取消重试 | `global-phase-base.log` |
| 原受控 BaseLayer 路径 | 3,360 帧，271 隐藏帧、15 次重入、6 次晚期失败 | `global-phase-controlled.log` |
| 真实分层输入 | 1,260 帧，每帧取消重试，10 次求值失败 | `global-phase-layered.log` |
| 真实动作片段/Slot | 1,050 帧及重试、372 隐藏帧、28 次晚期失败 | `global-phase-actions.log` |
| 原生脚部生产单线程 | 960 帧通过 | `global-phase-production-single.log` |
| 原生脚部生产并行 | 960 帧通过 | `global-phase-production-parallel.log` |
| 默认 BaseLayer 生产路径 | 并行 600 帧，75/109，28 个事件，lag/stale=0 | `global-phase-default-production.log` |
| 晚期姿势失败 | 全局/来源/控制器/姿势一起回滚 | `global-phase-late-transaction.log` |
| 晚期来源事件失败 | 回滚通过、回调泄漏 0 | `global-phase-late-events.log` |

两阶段专项覆盖 302 个 Slot 遮蔽来源帧、18 次来源重入和 12 次晚期
失败。每帧先仅准备全局候选，检查已提交属性、状态机、来源和 Montage
没有改变，验证非法阶段调用，再取消并重试。第一次完整求值使用拆分
入口，后续重试使用组合入口，姿势、曲线、来源时钟、事件和请求相同。
这是 Slot 遮蔽来源的覆盖，不是外层根隐藏整个普通子图的验收。

真实分层回放包含空中 168 帧、蹲伏 423 帧、32 个事件，确认 Aim 每帧
只更新一次，Overlay 消费同帧属性，下一帧读取最终曲线。动作回放使用
真实 Roll 姿势和曲线，包含接受/替换/取消/完成；不消费 Root Motion，
也不代表 Roll gameplay 已启用。

原生脚部生产两模式保持第 137 批结果：result=`B289A6FB5130DBB7`、
fullPose=`7B82A91E8A09C723`、root=`DB5B813964D3479C`、sampledPose=
`6804D603D2523040`，224/258，37 个事件，lag/stale=0；按旧身份布局
归一化结果仍为 `D898A6B5BD5DE295`。没有将这些摘要解释为视觉修复完成。

默认 BaseLayer 生产回放 result=`12082FF817D9EDE2`、fullPose=
`93800FF5F8744E91`，使用组合入口完成同样的全局/图两阶段。上述进程
均已正常退出；本批修改文件空白检查通过。

## 后续边界

本批完成实际生产全局/来源阶段拆分，还未完成外层根不访问整个普通
子图时的提交路径。当前全局映射仍限定原地面/空中观测，真实 Ragdoll
状态应按原 UpdateGraph 分支保留地面/空中属性，不能仅用 Floor 标志
冒充。下一项需补这一显式状态门控、普通分支隐藏事务和重新相关时的
初始化/CacheBones 传播，再连接根混合调度。

P3/P4 的平台、UE 同输入支撑脚/上身/换髋多帧与人工验收仍未完成，
默认 Demo 仍为 BaseLayer。完整 P6 物理玩法不是这些修复的前置条件。
原 P5A 剩余项、P5B Overlay/道具、P5C Mantle/Roll/Root Motion、P6
恢复/Camera、P7 十分钟性能与既有 Core 23 项失败、Import 分配不稳定、
旧 p95=2.559ms 超过 2.5ms 均保持未完成；音频暂缓。

没有 commit、revert 或 merge，保留既有工作区修改。
