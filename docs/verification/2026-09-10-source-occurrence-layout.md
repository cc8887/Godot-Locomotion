# 第二十九批：源图播放身份布局 v3

## 结果与边界

在既有 `AlsP5OccurrenceLayoutCompiler` 中增加源图布局编译入口，没有新增生产时钟、
事件队列或 P5 facade。当前 Cycle/Detail/Stop 的 37 个播放器、59 个样本已能分配
全局 occurrence handles，和仍保留的 Idle/Crouch/Air、Turn/Rotate 双 bank、动作通道
组成同一张布局。新增 Core 校验和借用视图供下一步主运行时绑定使用。

这只是布局编译完成，不是 P5 运行时或 Demo 接线完成。旧主快照和编译器仍明确拒绝
v3；实际 Demo 仍使用七个 Cycle 来源、临时 MovingWeight，没有完整 Detail/Stop。
本批没有新视觉捕获，不能宣称起步滑移、交错步或上身已经修复。

## 身份与范围

| 当前模块 | 物理槽位 | 身份规则 |
| --- | ---: | --- |
| 保留的旧 Base | 9 | Standing Idle、Crouch Idle/四向、三段空中；保留原 binding 索引 |
| 新图持续播放样本 | 47 | compiled node index + sample index |
| Stop 固定时间求值器 | 12 | 独立 SourceEvaluator，不等同于普通播放器 |
| Turn / Rotate | 16 / 8 | 保留 Godot 当前物理双 bank，不假称原版有相同 bank 数 |
| Transition | 1 | 原通道 |
| ActionMontage / ActionSequence | 1 / 1 | 按动作配置动态分配 |

当前组合共 95 个槽位、41 个 authority groups。数量来自配置，额外动作段会增加槽位，
不把 95 冻结成最终完整 ALS 的总量。旧 Standing 的 13 个移动槽被来源表替换，
不是同时保留两套身份。SyncMappings 仅保留四个旧 Crouch 来源的映射；新源的 Sync
成员是播放器，而非将 BlendSpace 的每个样本展开成独立组成员。

每个新播放器拥有独立 authority group，同一 BlendSpace 的样本共享该播放器的组。
同一 AnimationId 被不同状态使用时不得合并；缓存读写节点不会另建播放身份。
此处 authority 分配不代表已实现 UE 通知筛选策略，不能直接沿用旧 Timeline 的
winner/filter 即宣布原生等价。SourceEvaluator 的类型区分也不是事件分发已经接通。

整图清单的 233 个资产来源、277 个静态样本不是物理槽位总数。本布局明确记录另外
196 个尚未绑定到新来源表的原生 compiled node indices。它们有些已有旧模块近似，
不能把“未绑定”一概称为完全没有实现，也不能将这些旧模块当作已完成原生对账。

## 实现与防错

- Import/Core 枚举追加 SourceSample=7、SourceEvaluator=8；旧 1..6 数值不变。
  layout v2 仍拒绝新类型，Core 合同独立允许 SourceGraphVersion=3。
- `CompileSourceAware` 验证骨架、源快照完整标识、动画集摘要及原生覆盖，匹配六套
  Walk/Run BlendSpace 和 Sprint，并核对被替换的 Standing 动画集合。
- `ValidateSourceAware` 重编译并逐字段对账布局、来源映射、旧 Sync 映射、未绑定清单；
  不允许只改 digest 或数量绕过真实来源校验。
- `AlsP5SourceOccurrenceContract` 校验样本归属、compiled node/sample/animation 对应、
  handle 唯一性、播放器 authority 隔离，以及新来源与旧通道不共享 authority。
- `CreateCoreView/CreateSourceView` 借用私有不可变数组，10000 次创建零分配。
  公开数组为防御性副本，record `with { Entries = ... }` 同时刷新 Core 表。
- 旧动作通道分配抽为同编译器内部公共逻辑，v2 的条目顺序和 authority 不变。

Core 视图沿用可信 Import 输出的合同，不逐帧重算完整来源摘要，也不认证任意伪造
Span。布局 digest 仍是版本和条目摘要，来源语义由 SourceStamp 与精确映射校验共同
约束；后续主快照必须纳入这些来源信息，不能只使用旧 layout digest 代替完整绑定。

## 验证

新增 21 项 Import 测试，覆盖真实布局、共享资产/Sync 的独立身份、缓存不分配、
九类布局漂移、四类重算摘要后的别名错误、不可变视图/零分配、旧运行时拒绝、
错误源快照、额外动作段与 Standing 动画替换。

复查时新增反例：清除旧 Sync 声明并将一个 Standing 动画改为 Idle，原布局编译器
错误接受。`source-layout-standing-red.trx` 保存该失败；补齐替换集合校验后全套通过，
没有放宽断言或调整容差。

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除 P5aGolden/P5aTraceSchema | 1733/1733 |
| Import 完整 Debug，最终版 | 724/724 |
| Import 布局/清单/旧主绑定 Release | 93/93 |
| Core P5 合同 Debug / Release | 各 49/49 |
| Godot 优化 Debug 构建 | 0 错误、0 警告 |

TRX：`artifacts/test-results/source-layout/`。首轮 Import 全套为 723/723，新增上述
反例修复后最终为 724/724。

额外启动旧 P5 Family7、Family10、Family11 三个 Golden 用例，其中 Family11
`CanonicalBoundariesAndPhysicalTimelinesRemainSeparateWithExactExclusions` 通过
（86.56 秒）。包含全量 Schema 变异矩阵的该次运行在约 365 秒后由本批主动停止，
没有等待剩余两项完成。`source-layout-legacy-golden.trx` 保留整次 Failed/Aborted
及一个 Passed 结果；测试工具报“测试主机进程崩溃”是主动终止的表现，不是观察到
产品崩溃。其余两项没有结论，不声称 Golden/Schema 全套通过，也不将中止当作通过。

真实资源回归：StandingCycle 的 15216 次源时间、5040 帧移动入口检查通过；Detail
71400 个骨骼检查，GroundedCache 1260 帧/85680 个骨骼检查通过，组件仍标记
`demo=not_connected`，缓存上游仍为夹具。日志 `artifacts/source-layout-*.log`。

Cycle single/parallel 各 180 帧一致，result=`C658034A39C5917B`，完整姿势
`076E345A0151A012` 与前批相同；两种模式晚期失败注入验证 source_sync、输入、
Controller、姿势和 P4 banks 一起恢复。故障注入帧的预期错误诊断不算意外失败。

完整 `verify-p4-pose.ps1` 通过图/曲线/转身、双模式脚部与晚期事务、姿势门禁。
拓扑/活动姿势各 10000 次零分配，活动 4781.965 ms，不代替 P7 性能预算。
相机和输入 SHA-256 与前批一致，未修改 UE 插件或重复导出资源，未 commit/revert。
`git diff --check` 通过，只有既有 LF/CRLF 提示；本批测试进程已结束。

## 下一步

1. 在现有 `AlsP5CoreRuntimeBindingCompiler/Snapshot` 增加正式源图版本：用本布局
   解析来源、事件绑定、Sync 元数据及完整摘要，保留旧 v2 验证路径；不能仅删拒绝条件。
2. 将 source Tick delta 通知窗口、队列生命周期、NotifyMode/权重筛选、状态事件及
   每来源候选历史纳入已有 P5 Gather/Worker/Commit；同帧失败必须连同姿势一起回滚。
3. 接真实 Main/Slot 上游、完整 Standing/Detail/Stop、Lean/Sprint Impulse 和惯性化，
   替换 Demo 的临时 MovingWeight；逐阶段核对原生状态/时间/曲线/骨骼和多帧画面。
4. 继续动态 Layering/Add/LS/YawOffset 与手部 IK、P5B 全 Overlay/道具、P5C 动作、
   P6 物理恢复/完整相机及 P7 最终预算。音频仍暂缓，基础移动和人工视觉验收不关闭。
