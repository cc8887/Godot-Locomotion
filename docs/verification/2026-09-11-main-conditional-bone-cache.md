# Main / BaseLayer 条件 CacheBones

日期：2026-09-11。第六十批。工作区 `ARCHIVED_P5A_WORKTREE_PATH`，保留既有
未提交改动；本批没有 Git 提交、回退、UE 插件修改或资源导出。

## 实现

新增 Core `AlsMainGroundedBoneCache`，与实际 `AlsPoseCacheEvaluation` 候选绑定。
它保存 Main、Standing、Stop、Detail、Crouching 的 31 个状态骨骼缓存计数，并按
既有 compiled 读取身份传播到六个共享 Save。没有建立第二份姿势缓存或来源时钟。

调用链已接到 Main 整合流程：候选开始时复制已提交计数、执行入口 CacheBones；
消费状态更新中的初始化记录；姿势消费者在访问每个实际状态前再次执行条件检查。
因此全图 CacheBones 的正权重遍历与 EvaluateState 的零 alpha 路径分别处理。
Main / Standing / Stop / Detail 的现有更新回调直接驱动该组件，Crouching 新增
初始化之后、来源更新之前的观察回调，并由共享 Main owner 转发。

保留以下原生语义：

- 检查 traversal counter 与 global frame 两者；相同短计数、不同全局帧仍刷新，
  计数回退同样受支持，不假设计数永远递增。
- 先记录状态计数再进入子图；同一计数下重复状态或同一个 Save 的不同读取不重复刷新。
- 普通 CacheBones 遍历状态局部权重大于零的分支，不拿全局图权重作替代。
- 整机相关性重置、实际 Save 来源初始化重置状态计数；首次 Update 不再次抹掉
  此前已经完成 CacheBones 的初始状态。Core 将首次初始化与首次更新合并返回，
  所以需要区分“尚未 Update 的初始快照”与“已 Update 后再次初始化”。
- Standing 的 Stop 是状态内子机，初始化该状态需清理 Stop 的状态计数；Detail
  则属于独立 Save，不能一起无条件重置。
- Save Initialize 本身不被错误转换成 Save 骨骼/姿势缓存全部失效；仍使用现有
  Save 生命周期的独立计数。
- 骨骼刷新失败使候选不可用；姿势失败也使绑定的骨骼候选不可提交。重试从已提交
  对象复制，成功后与姿势缓存成对交换。帧身份相同的另一候选也不能混用 owner。

本批沿用前两批已只读核对的 UE `AnimNode_StateMachine.cpp:213`、`:283`、
`:299`、`:1265`、`:1582` 和 `v4_pose_cache_graph.json` 的六缓存归属。
没有新增原生完整图数值探针。

## 验证

最终优化构建零警告、零错误。新增骨骼缓存专项 15 项，涵盖六缓存传播、入口别名、
首次更新、counter/global frame 独立变化、计数回退、真实 Save 初始化后的状态
计数、相关性间隔重置、失败重试、conduit / 越界拒绝与同帧不同候选隔离。
Import 相关回归最终 90/90，Core 缓存与 Detail 专项 51/51。
TRX：`artifacts/test-results/main-bones/`。
最终 Import 报告为 `main-bones-verified.trx`。已跟踪差异与本批涉及的未跟踪文件
空白检查通过。

Main 实际资源整合回放：

| 频率 | 路线帧 | Save 骨骼刷新 | 状态骨骼刷新 | 刷新计数变化 |
| --- | ---: | ---: | ---: | ---: |
| 30 Hz | 240 | 19 | 56 | 3 |
| 60 Hz | 480 | 19 | 56 | 3 |
| 120 Hz | 960 | 19 | 59 | 3 |

计数为各成功提交路线帧的合计，不包含重试与分配测量重放。每帧重复准备会检查
骨骼刷新计数一致；6 次缺失/失败 Slot 的重试同时检查骨骼计数、来源、姿势与事件。
共 1680 路线帧，全部六缓存、2356 次原始 Standing 骨骼/曲线对照通过，活动准备
0 B。最终日志 `artifacts/main-bones-verified.log`；早期 `main-bones-first.log`
和 `main-bones-final.log` 保留，最后一次增加了 Slot 失败后骨骼计数重试断言。

旧 Main 入口 1680 帧通过，`artifacts/main-bones-legacy.log`。
上一批有序初始化 210 帧、3 次溢出拒绝保持，`main-bones-initialization.log`。
单/并行 Worker 各 180 帧、各 10 个来源事件通过；结果摘要
`A9DF0647AFC3574C`、完整姿势摘要 `04D4A5651B87E0E4` 保持。
晚期来源事件回滚通过，回调泄漏为零，runtime/result/controller/pose/P4 banks 恢复。
日志 `main-bones-worker-single.log`、`main-bones-worker-parallel.log`、
`main-bones-worker-rollback.log` 均位于 `artifacts/`。

## 边界与下一步

这是可复用的条件骨骼缓存调度及整合接线，不是动态骨架/LOD 重映射已实现。
整合使用固定 Godot 骨架，刷新回调核对已有映射的骨数；实际节点映射由已建立的
姿势组件持有。计数变化用于检查缓存失效语义，没有改变骨架布局或声称已覆盖 UE
不同 LOD 的骨集合。Crouching Cycles 内部已有 CacheBones 随同一计数进入其组件。

完整外层 Save Initialize 的源图传播仍待实现；当前初始化回调只在现有 owner
实际转发时调用，不能用本批组件替代缺失的初始化调用。Standing Cycle 外部来源
仍有 bootstrap，需要纳入其真实 Save 初始化。后续继续该初始化所有权及 Main
Movement、最终 Slot/惯性化、统一生产提交，再接完整 Main/Demo；最终曲线、动态
上身、完整脚部约束与原 P5A-P7 全部保留。Slot 姿势仍为明确的测试替身。

本批未重跑全套 Core/Import、Standing 长回放、完整 P4、平台路线或性能矩阵，
没有新移动截图、完整原生图回放或十分钟性能采样。之前的平台脚锁失败和短矩阵
超预算继续保持未通过，滑步、交错步、上身以及最终人工验收仍未关闭。
