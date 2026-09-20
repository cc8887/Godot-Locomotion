# Main Save 初始化与首次 Update 分离

日期：2026-09-11。完整性恢复第六十一批。

## 问题和变更

UE `AnimNode_StateMachine.cpp:213` 的 Initialize 建立初始状态并设置 bFirstUpdate；
`:385` 的相关性重置要求已经发生过首次 Update。初始化后因 Slot 抑制而一直未更新
的状态机，不应在首次恢复更新时再次初始化。原 Core 只有 HasUpdated，无法持久
表达这段生命周期。本批只读核对本地 UE 源码，没有修改或启动 UE。

Grounded 和 Detail 状态增加 HasInitialized，提供独立 Initialize，返回有序初始化
记录；不推进时间、不产生来源 Update。首次 Update 保留原先的第一帧转换规则，
不重复初始状态的初始化和进入通知。更新后的相关性间隔仍重新初始化。Grounded
同时检查已初始化但尚未更新状态的 machine kind；首次自动退出不能消费未更新
初始来源的陈旧权重观察。

Main cached graph 提供 InitializeMainSource，保留子 Save 独立快照，重置自身 Main
和 Slot 状态，并将初始化记录交给 owner 消费。实际六缓存整合入口通过 Save 的
Initialize 回调调用它，顺序为 Initialize → CacheBones → Update → Evaluate。
两个入口别名共享同一初始化计数，不重复调用来源。正式 Main 初始 Entry 没有
计时来源或进入通知，夹具对此显式断言，不能把其他有来源的状态当作空节点处理。

120 Hz 的首帧 Slot 完全抑制 Main 时，已初始化状态能够随候选提交；后续首次
Update 不重新初始化 Entry，未访问的 Standing/Detail/Cycle 来源保持 epoch 0。
独立接口测试还覆盖初始化后跨 90 帧首次更新、第一帧跳过混合、子缓存保留和
已提交快照不被候选修改。

## 验证

- 新增 Core 7 项、Import 1 项。Core 状态机专项 38/38，相关 Import 最终 91/91。
- Core 常规最终 1920/1920；按既有范围排除 P5A golden / trace schema 两类，
  不把本结果写成所有项目测试已经通过。
- 最终优化构建零警告、零错误。
- 最终 Main 六缓存实际资源回放 30/60/120 Hz 共 1680 帧，全部六缓存、2356 次
  原始 Standing 骨骼/曲线比较、6 次 Slot 姿势故障拒绝和重试一致通过；活动准备 0 B。
  Save 骨骼刷新各 19 次、状态刷新 56/56/59 次，与前批一致。
- 旧 Main 入口 1680 帧、有序 Standing 初始化 210 帧及 3 次溢出拒绝通过。
- 单/并行 Worker 各 180 帧、各 10 个来源事件通过；结果摘要
  A9DF0647AFC3574C、完整姿势摘要 04D4A5651B87E0E4 保持。并行晚期来源
  事件故障回滚通过，回调泄漏为零，各候选 bank 恢复。日志为
  `artifacts/main-initialize-worker-{single,parallel,rollback}.log`。
- 已跟踪差异及本批涉及的未跟踪文件空白检查通过。

TRX 在 `artifacts/test-results/main-initialize/`；最终为 `core-final.trx`、
`import-final.trx`。Main 日志 `artifacts/main-initialize-final.log`，旧入口
`main-initialize-legacy.log`，Standing `main-initialize-standing.log`。

保留首次 Core 1919/1920 的失败：既有缓存分配测试测得 608 B。独立运行原程序集
通过；发现该类未使用项目已有的 Allocation 隔离集合，已加入同一非并行集合并
显式关闭 tiered compilation 后重跑常规全套，通过。没有修改运行时缓存算法、
预热循环或 0 B 断言；尚不能据此确认那 608 B 的具体分配栈。首次 `core.trx` 与
独立 `cache-allocation-isolated.trx` 均保留。

## 完整性修复顺序与边界

1. 继续向内传播 Standing / Detail / Stop / Cycle 的真实 Save 初始化，移除
   Standing Cycle 外部来源 bootstrap。本批只完成最外层 Main 入口与必要状态模型，
   不代表所有子图 Initialize 都已接通；旧 Prepare 的兼容重置参数也不替代完整传播。
2. 完成 Main Movement、实际 Montage / Slot、放在原生位置的最终惯性化，将更新、
   同步、来源事件、姿势及曲线交给统一生产候选 owner，接入 Demo。
3. 完成最终曲线反馈、播放速率 / Stride / Yaw、动态 Layering / Add / LS / Lean
   和脚部约束，再验证起步、急停、左右换向、斜向与转身。换向时机来自原图条件、
   曲线和过渡，不添加任意延时来代替缺失语义。
4. 继续原 P5A 通用事件 / Sync / ActionPlayer，P5B Overlay / 道具，P5C Mantle /
   Roll / Root Motion，P6 Ragdoll / Get-up / Pose Recovery / 完整 Camera。
5. 原版和 Godot 使用一致输入序列、多个频率和多帧截图检查视觉；最后执行 P7
   十角色全质量、预热后十分钟性能验收与人工验证。音频仍按用户要求延后。

实际资源以 ALS V4 AnimBP 为图与资产依据，Refactored C++ 为相应算法参考；两者
不是一个版本的同一张完整动画图，不能仅以同名变量断言已 1:1。移植完成需要来源
身份、初始化/更新/求值顺序、同步相位、过渡、曲线及最终骨骼结果共同对齐。

本批 Main owner 仍为整合夹具、Slot 姿势为替身，未接通完整 Demo。没有新视觉
截图、原生整图数值回放或十分钟性能采样。之前的平台窗口脚锁失败和短矩阵超预算
仍未关闭；不宣称滑步、交错步、换髋和上身视觉已修复。未改已确认键鼠输入，
未修改原生资产/插件，未提交、回滚或合并用户工作区。
