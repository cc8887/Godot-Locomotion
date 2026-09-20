# 嵌套移动状态机的实际 Update 遍历传播

第一百四十一批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`。

## 本批修复

承接第 140 批外层 Main Movement/BaseLayer 生命周期，补上内部 Main、
Standing、Stop、Locomotion Detail、Standing Cycle、Crouching、Crouching
Cycle/Direction 与 Jump 的动画 Update 计数传播。这属于 P3/P4 所需的
P5A 前置运行时补完，没有改为固定输入等待或额外姿势补偿。

AlsPoseUpdateContext 增加可选 UpdateCounter。显式 BaseLayer 入口写入
外层实际计数，Weight、Inactive、State、惯性化消息和缓存读取保留该值。
同一缓存遍历拒绝混合不同 Update 计数或混合有/无计数的读取；不能让低权重
读取携带另一帧更新上下文。无共享消息上下文的路径也保留实例更新计数。

地面通用状态机和 Detail 的候选状态保存各自 LastUpdateCounter，并用
WasSynchronizedCounter 判断相关性：当前计数或落后一个更新均视为连续，
包括 short 回绕和跳过 -1；忽略 GlobalFrame 差异。LastUpdateSerial 仍保留
帧身份，用于既有事务/骨骼缓存观测校验，不再在显式路径兼任相关性判断。
未显式传计数的旧入口保留旧行为；已经更新的实例拒绝静默更换计数所有权。

Standing Cycle 独立生命周期同样使用实际 Update 计数；未访问不推进历史，
相关性重入重置外层方向但不无条件重置 Sprint Save。另将生产 Sprint
初始化计数从固定零改为实际外层 Initialization，保留独立缓存初始化语义。

直接只读核对本机 UE 5.9 的 `Engine/Public/Animation/AnimTypes.h`
FGraphTraversalCounter::WasSynchronizedCounter，以及
`Engine/Private/Animation/AnimNode_StateMachine.cpp` 的 Update 相关性门控。
没有新增 UE 执行结果、重新导出资源、启动 UE 或修改原生插件。

## 验证证据

优化 Debug 构建 0 警告、0 错误。专项测试：

| 检查 | 结果 | 证据（artifacts/test-results） |
| --- | --- | --- |
| 地面/Detail 状态机、缓存上下文、Standing Cycle 生命周期 | 68/68 | nested-traversal-core.trx |
| 正式 Main/Standing/Crouching/Jump 图与运行时回归 | 152/152 | nested-traversal-import-verified.trx |
| 新增 Jump、Crouching 根显式计数检查 | 2/2 | nested-traversal-jump-crouch.trx |

测试覆盖帧编号 1→1001 而计数连续时不重新初始化；帧编号只加一但计数
跳过更新时重入；short.MaxValue 回绕、-2→0、同计数不同 GlobalFrame；
过渡、初始化日志、跳跃来源不重复初始化及取消重试。正式 Crouching
Cycle 测试确认其 Direction 子状态机和来源上下文收到同一计数。

初次 Import 运行有一条新测试错误地要求整个图恰好只有一个跳过的缓存
读取；正式图内部过渡也会产生重复读取，实际为两个。断言已改为验证根
重复读取确实产生跳过，内部遍历仍正常；原日志不计为通过证据。

真实资源 30/60/120 Hz 共 1,050 帧通过：651 帧隐藏、6 次恢复、每帧
取消重试；检查 1,134 次嵌套更新，实际覆盖 Main/Standing/Stop/Detail/
Standing Cycle，三次 FrameId 跳变未误重置内部图。额外保留第 140 批
初始化/隐藏 CacheBones/计数回绕检查。日志
`artifacts/nested-traversal-unvisited-final.log`。这段输入未覆盖 Crouching
与 Jump 的所有重入组合，它们由上述正式图专项和生产移动回放补充。
补充 Sprint 初始化计数逐帧断言后的最终 1,050 帧亦通过，日志
`artifacts/nested-traversal-sprint-init-final.log`。

生产 single/parallel 各 960 帧通过，日志
`artifacts/nested-traversal-production-single.log`、
`artifacts/nested-traversal-production-parallel.log`，与上一批同摘要：
result=B289A6FB5130DBB7、fullPose=7B82A91E8A09C723、
sampledPose=6804D603D2523040、root=DB5B813964D3479C；224/258，
37 事件，lag/stale=0。Sprint 初始化传递补充后执行了 single 和未访问
回归；普通连续路径结果仍相同。

旧全局拆分/组合入口 3,360 帧、302 帧 Slot 隐藏、18 次重入和 12 次
晚期故障通过：`artifacts/nested-traversal-legacy.log`。实际脚部生产
晚期姿势/来源事件失败均回滚，事件回调泄漏 0：
`artifacts/nested-traversal-late_transaction.log`、
`artifacts/nested-traversal-late_source_event.log`。

这些是组件、真实来源和生产接线证据，不是 UE 最终图或人工视觉验收。

## 下一步与保持未完成

内部移动状态机以 FrameId 代替 Update 计数的已识别调用已补齐。整图
生命周期仍不能据此宣布完成：最终根生产入口仍拒绝实际 Ragdoll 混合
分支，外层分层/Aim/手脚与完整根访问边界还须一起闭合并验证。下一步
继续最终根调度所需的普通分支与上层图隐藏/初始化/恢复传播。

默认 Demo 仍为 BaseLayer；新分层/脚链专项入口不等于默认入口完成。
继续 P3/P4 起步、换髋、上身、平台/支撑脚的同输入 UE 多帧及人工验收，
通过后切换默认入口。完整 P6 物理 Ragdoll 玩法不作为这些视觉修复前置。

原 P5A 其余通用事件/动作、P5B Overlay/道具玩法、P5C Mantle/Roll/
Root Motion、P6 物理恢复/完整 Camera、P7 十分钟性能验收保持范围。
既有 Core 23 项失败、Import 分配不稳定和旧 p95=2.559ms 超过 2.5ms
未在本批关闭；没有全套测试/性能或最终视觉通过声明。音频暂缓。
未 commit、revert 或 merge，保留原有工作区改动。
