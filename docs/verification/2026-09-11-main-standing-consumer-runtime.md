# Main 更新到 Godot 来源/姿势消费验证

日期：2026-09-11。第五十七批，分支 `feature/p5a-events-actions`。
延续完整性恢复计划，没有提交、回滚、修改 UE 资产/插件或已确认键鼠控制。

## 实现

- StandingStateGraph 新增 Observe/Consume，CycleDetailGraph 和 StandingCycleGraph
  提供对应入口。Controller 原路径与 Main 共享路径复用 Stop 派生来源及方向/Stride
  阶段；消费外部更新不会再次推进 Standing/Stop/Detail 状态机。
- Core Standing 更新保留 StopContext，消费阶段使用缓存赢家的 Delta。新增测试
  将首个低权重路径 .03 秒与后续高权重路径 .01 秒区分，核对祖先、inactive、
  root-motion 权重及实际 Stop 记录权重。
- 按本地 UE `AnimNode_MultiWayBlend.cpp` 的局部 Alpha 门控修正零权重更新：
  Core CycleCacheWeights 不再直接拒绝零全局权重；Godot 调用者单独判断子图是否
  真正被访问。旧测试中“零全局权重即无来源”的预期改为原生局部相关语义。
- Main 没有访问 Standing 时，收集入口不初始化或提交站姿来源；父级非活跃状态
  可以抑制来源活跃标志，Main 来源也保留父级惯性同步请求。
- MainGroundedSourceCollector 改用现有 CreateCoreView，避免 Players/Samples
  防御性复制属性在观察/收集热路径分配数组；不移除导入配置的不可变性保护。

## 新整合回放

入口：`scenes/tests/main_grounded_update_smoke.tscn`。
最终日志：`artifacts/main-consumer-observation-verified.log`。

Main 真实状态规则/缓存调度 -> Standing 消费更新 + Crouching 主状态/Cycles 来源
-> 一次共享 Sync -> 实际站姿/蹲姿/Main 姿势组合。Main 自动退出读取前帧共享来源
时间，BasePose_CLF 反馈读取 Main 曲线，不用目标 Stance 直接替代来源姿势。

| Hz | 帧数 | Standing 更新 | Crouching 更新 | 双 Cycles | 来源事件 | 零权重来源帧 | Slot 抑制 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 30 | 240 | 136 | 90 | 30 | 16 | 14 | 5 |
| 60 | 480 | 272 | 179 | 58 | 11 | 28 | 9 |
| 120 | 960 | 545 | 476 | 236 | 11 | 55 | 19 |

每帧候选重试的来源时间/epoch/历史、最终源姿势、Main 状态及事件数量一致。
Main 覆盖 Standing、Crouching、两个站蹲转换资产状态；本夹具未覆盖 From Roll。
蹲姿覆盖 Idle/Moving/Stop/Rotate Left，Rotate Right 由已有主状态回归覆盖。
包含首帧完全抑制、零权重上下文和父级惯性同步标记；共 3 次过期消费拒绝。
三档各两处、每处热身 30 次/测量 120 次重复 Prepare，受测路径分配为 0 B。
测量含输入观察、Main 调度、来源收集、Sync、姿势和事件准备，不是十分钟预算。

## 回归

TRX：`artifacts/test-results/main-consumer/`。Godot 日志前缀 `artifacts/main-consumer-`。

- Core 常规 1910/1910，保留原 P5aGolden/TraceSchema 排除范围；Cycle 专项 13/13，Release 13/13。
- Import 首轮 1128/1128；新增 Stop 上下文测试后最终全套 1129/1129；Main/Standing Release 37/37。
- 旧包装入口、拆分入口和新消费入口 840 帧对照通过；共享批次原 1260 混合帧、
  13522 贡献、38 事件、27 拒绝、1260 Main 姿势帧和 6 组片尾检查保持。
- 实际 Standing 5040、Detail 1890、Pivot 5040、Sprint 1260 帧通过，活动 0 B。
- 单/并行 Worker 各 180 帧、10 来源事件通过；晚期来源事件失败无回调泄漏，候选回滚通过。
- P4 pose 脚本的姿势、动画图、双模式脚部和晚期事务回滚通过，活动 0 B。
- 构建零警告/零错误。旧 result `A9DF0647AFC3574C`、full pose `04D4A5651B87E0E4` 保持。

P4 matrix 本批失败。首次脚本执行的 1/single、1/parallel、10/single 通过；10/parallel
的 Gather+Commit p95=1150 us、Worker p95=3002 us、total p99=5135 us，超出原门槛。
确认先前测试进程已终止后，按相同优化构建/tiering=0/120 热身/600 帧单独复测该格，
`artifacts/main-consumer-matrix-isolated.log` 仍失败：1015/2502/3971 us。
Worker 门槛为 2500 us，不能把 2502 us 四舍五入为通过。结果/姿势摘要均与前三格一致，
没有线程、代际或结果差异。该脚本使用旧 p3 profile，未进入新增 Main 整合路径；
这不足以确认或排除共享帧布局等变化的影响，性能原因尚未确定。未扩大阈值、缩小
负载或以再次取样直到通过作为修复。最终 P7 性能目标保持。

Demo 300 帧路线复测 `artifacts/main-consumer-demo.log` 仍在平台窗口 85--96 失败：
locks=0，IK/FootLock=(1,1,0,0)，sprintEnd=(-3.999,1.0001398,-7.932024)，
brakeEnd=(-3.972002,1.00092,-9.286164)。与前批相同，没有改路线、曲线或锁脚阈值。

## 首错记录

1. 新夹具遍历计数参数 int/short 编译不匹配，改为有界测试帧的 checked short 转换。
2. 首次回放只在移动中切换姿态，没有进入两个站蹲转换资产状态，覆盖断言失败。
   增加静止切换段，同时保留移动切换段，不更改源规则/过渡时长。
3. 120 Hz 首帧错误地假设目标 Crouching 必定直接进入蹲姿缓存。原图还读取前帧
   BasePose_CLF；改用完全抑制 Slot 的确定场景验证未访问来源，不强制修改反馈曲线。
4. 入口对照用内置 Equals 比较含 InlineArray 的 Stop 结构被 Godot 运行时拒绝。
   改为比较实际派生左右选择器及惯性时长，并继续逐骨/曲线对照最终结果。
5. 新受测整合路径首次产生 56640 B；分段日志确认全部来自 Main 来源收集阶段。
   根因是配置数组属性的防御性复制；使用只读运行时视图后相关观察/收集和全段均 0 B。
6. 父级惯性断言首次误用字段名，编译失败；随后一次启动仍使用旧程序集，不作为
   该断言通过证据。修正为 RequestedInertialization，成功构建后重新回放；最终
   observation-verified 日志包含实际新程序集与扩大的零分配测量。

## 未完成边界

这不是最终 Main/Demo owner。外层 Save 只使用初始化计数；完整 CacheBones/Evaluate、
缓存上下文作用域、Montage/Slot 输出、外层惯性化及状态事件统一提交仍未闭合。
现有 Standing/Detail 内部惯性化仍使用旧生产 owner，不能视为全图原生放置已确认。

回放的上游权重、速度/方向、蹲姿 rate=.75/stride=1、Lean/Yaw 和 Slot 权重属于受控
输入。源资产、缓存权重和来源时间真实，不等于 UE 完整 AnimBP 逐帧轨迹已经一致。
没有新移动截图、人工视觉验收或十分钟性能采样，不关闭滑步、交错步、上身和平台门禁。
下一步继续完整外层姿势缓存与生产提交，再补最终曲线、动态上身、完整脚部和原 P5A-P7。
