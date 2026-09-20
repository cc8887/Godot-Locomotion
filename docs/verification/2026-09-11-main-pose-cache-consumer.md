# Main/BaseLayer 实际姿势缓存消费

日期：2026-09-11。第五十八批。工作区 `D:\GodotALS-p5a-events-actions`，
基线仍为 `d6b45e3` 加既有未提交工作；没有提交、回退或覆盖其他改动。

## 本批关闭的缺口

新增可复用 `AlsMainGroundedPoseEvaluation`，消费第五十六、五十七批的实际 Main
更新结果与已经完成的共享来源时间。六个 BaseLayer Save 的最终骨骼和曲线通过
现有 `AlsPoseCacheEvaluation` 按读取求值，同帧同作用域内不重复求值：

| Save | 实际输入 | 下游缓存读取 |
| --- | --- | --- |
| 100 | Main / Grounded Slot | Main 状态 1 读 Standing，状态 2 读 Crouching |
| 99 | Standing | Moving 读 Detail；Stop 按实际 Stop 状态读取 Detail |
| 33 | Standing Detail | 按实际 Detail 状态读取 Standing Cycles |
| 32 | Standing Cycles | 已同步的真实循环来源 |
| 30 | Crouching | Moving / Stop 的 compiled 110 / 122 读 Cycles |
| 31 | Crouching Cycles | 已有完整 Stride → DiagonalScale → Lean 姿势组件 |

Standing 的原始求姿势与惯性化历史拆开。原 Controller 入口继续调用同一份原始
组合后处理历史；新 Main 缓存入口只消费原始姿势，不提前把站姿单独平滑。
Main 之后应在原生 Main Movement 外层统一执行惯性化，这部分尚未实现。
没有为了拼接 Main 新建来源时钟或修改已有共享同步结果。

缓存数据包含骨骼及全部当前来源曲线，Main 的 BasePose_CLF 反馈从实际最终输出
读取。Standing 的输出也写回候选帧，避免下一帧观察仍消费旧的占位姿势曲线。
Source / Slot / Total 权重继续分别传递。Slot 无贡献时直接求源姿势；有贡献时
只在 SourceWeight 相关时求 Main，并要求外部提供 Slot 姿势 owner。缺少该 owner
明确失败，不能偷偷退回源姿势。整合测试使用明确的参考姿势 Slot 替身；这不等于
已经实现 Montage 的资产混合、注册、时间或通知。

## 原生依据

本批只读本地 UE 源码与既有正式导出，没有修改 UE 插件或重新导出资产。

- `AnimNode_StateMachine.cpp:948`、`:1038`、`:1582`：标准中断栈先求首个
  From，再依次求各 To；不能因为 alpha 为零就跳过求值。重复状态的缓存读取去重。
- 同文件 `:283`、`:299`、`:1265`：CacheBones 的状态相关性、计数与状态初始化
  路径分别受控。不能用每帧无条件初始化所有缓存替代；本批没有虚构这段生命周期。
- `AnimNode_Slot.cpp:95`：无 Slot 权重时透传；有权重时按 SourceWeight 判断
  是否求源，随后交给 SlotEvaluatePose。
- 正式 `v4_pose_cache_graph.json` 中 BaseLayer 的顺序为 Main Movement
  StateMachine_9 → Inertialization_0 → Slot_1 → Root。Grounded Slot_0 位于
  Main_11 与 Save_5 之间，不能与这个最终 Slot 混为一个节点。

原图曲线/转换行为仍以现有 ALS V4 资产为准；与固定 ALS-Refactored 版本对照时
保留语义来源，不把 HipOrientation_Bias 和 HipsDirectionLock 当作变量改名。

## 验证

最终优化构建成功，零警告、零错误。

`main_grounded_update_smoke.tscn -- --cached-poses`：

| 频率 | 路线帧 | 原始 Standing 骨骼/曲线对照次数 | Slot 故障拒绝 | 活动准备分配 |
| --- | ---: | ---: | ---: | ---: |
| 30 Hz | 240 | 422 | 2 | 0 B |
| 60 Hz | 480 | 694 | 2 | 0 B |
| 120 Hz | 960 | 1240 | 2 | 0 B |

共 1680 路线帧。每种频率都覆盖六个缓存生产者、站蹲中断混合、零全局权重、
满 Slot 来源抑制；两个 Main 读取身份及第三次重读完全相同，不增加求值次数。
修改调用者输出缓冲不污染缓存。同帧候选重试保持骨骼、同步历史与事件数量一致。
2356 次原始站姿对照复用旧直接组合路径作为交叉检查；不以新缓存结果自行生成预期。
对照次数包含候选重试及分配测量重放，不是额外的独立路线帧。

6 次故障覆盖缺少 Slot owner、求完源后 Slot 抛错。错误使缓存候选失效；关闭作用域、
从已提交状态重新准备后，来源、姿势和事件与故障前的有效候选一致。
新路径未应用站姿局部惯性化，但本批没有完整 UE AnimBP 最终骨骼轨迹用于数值对照。
分配测量含直接组合交叉检查，不能作为最终多角色帧时预算。

日志：`artifacts/main-cache-verified.log`。
保留早期 `main-cache-first.log`、`main-cache-raw-comparison.log` 的成功组件运行，
最终结果以 verified 为准。初次 C# 构建因误用来源帧 Identity、InlineArray 属性
不能直接取 Span 等接口错误失败；改为真实更新身份、固定时间数组及局部帧写回后
构建通过。未在失败构建后启动旧程序集充当本批验证。

回归：

- Core 姿势缓存专项 35/35；Import Main / Standing / cache 编译专项 47/47。
  TRX 位于 `artifacts/test-results/main-pose-cache/`。
- 原 Main 整合入口 1680 帧通过，状态覆盖、同步与事件计数保持；
  `artifacts/main-cache-legacy.log`。
- Standing 5040、Detail 1890、Pivot 5040、Sprint 1260 帧通过，活动分配 0 B；
  `artifacts/main-cache-standing.log`。
- 共享来源 840 帧旧/拆分/消费入口对照、1260 帧混合来源、38 事件与 27 次拒绝
  保持；`artifacts/main-cache-shared.log`。
- 单/并行 Worker 各 180 帧、各 10 个来源事件通过，结果摘要仍为
  `A9DF0647AFC3574C`，完整姿势摘要仍为 `04D4A5651B87E0E4`。
  并行 late_source_event 故障回滚通过，来源事件回调泄漏为零，runtime/controller/
  pose/P4 banks 恢复；`artifacts/main-cache-worker-single.log`、
  `main-cache-worker-parallel.log`、`main-cache-worker-rollback.log`。
- 已跟踪差异及本批涉及的未跟踪文件空白检查通过；本批没有新增 Git 提交。

## 未关闭项与接续顺序

本批是实际资源上的可复用 Evaluate 消费组件，仍不是完整 Main/Demo 事务 owner。
其调用方负责 Initialize、状态级条件 CacheBones、作用域及提交；外层 CacheBones
还没有完整执行，测试保持固定骨架。内层 Crouching Cycles 已有的生命周期仍运行。
Standing 的有序重复初始化消费者迁移、Main Movement 上游状态、最终 Slot、惯性化
请求归属及失败时整条生产事务回滚仍需补齐。不能把本批的外层缓存求值称为完整
Save 生命周期、原生状态节点所有副作用或最终动画图已等价。

下一步先整合这些生命周期与最终姿势所有权，再接入 Demo；之后补最终曲线驱动的
RotationScale、Stride/播放速率、YawOffset 与动态 Layering/Add/LS/Lean/IK mask，
再完成 Foot IK、Foot Lock、pelvis / thigh 修正。P5A 的完整 Notify/State/Sync/
ActionPlayer、P5B Overlay 与道具/手部 IK、P5C Mantle/Roll/Root Motion、P6
Ragdoll/Get-up/Pose Recovery/完整 Camera 和 P7 最终性能/人工验收全部保留。

没有新移动截图、完整原生图回放、全套 P4 或十分钟性能采样。第五十七批的 Demo
平台脚锁失败与 10 角色并行短矩阵超预算仍保持未通过；本批没有重跑这些门禁，
不以组件通过关闭它们，也不宣称起步滑移、交错步或上身外观已修复。
