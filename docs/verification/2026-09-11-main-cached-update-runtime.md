# Main/BaseLayer 共享缓存更新验证

日期：2026-09-11。第五十六批，工作区 `../GodotALS-p5a-events-actions`。
未提交、回滚或合并；保留既有未提交修改。未改 UE 插件、资产和已确认键鼠输入。

## 完整性修复位置

本批属于原完整性恢复计划的基础移动/缓存更新补项，不是新增玩法。资产和公式一致
不足以保证相同姿势：共享缓存选取的更新上下文、状态初始化、来源时钟、Slot 相关性、
最终曲线和分层顺序也必须一致。仅有独立组件通过，不能宣称 Demo 已接入或视觉等价。

## 实现

- 新增严格 Main 缓存编译器，读取现有正式源图与缓存表。外部读取 248/302 -> Save 100，
  Slot 102 -> Main 214，Main 218 -> Standing 99、221 -> Crouching 30；全局顺序
  为 100、99、33、32、30、31。验证 Slot 名称/策略及已导出 editor/native 生命周期回调。
  外层 Main Movement 只有 compiled inventory，没有本范围内图体，不凭空假设图体存在。
- Main 统一排空上述队列；Standing 内部增加 BeginShared/UpdateSharedCache/EndShared，
  原生产入口继续复用相同实现。Crouching 不提前排空其 Moving/Stop 共享缓存。
- 保留最大权重、同权首次优先、跳过回调、祖先状态、惯性化标记和 root-motion 权重。
  Save 初始化是否实际发生由外部生命周期 owner 返回，不因读取次数重置所有缓存。
- 新增 Slot 来源更新组件。来源低于等于 1e-5 且未要求始终更新时跳过；相关来源最低
  使用 2e-5 权重。来源权重下降或 Slot 满权时按默认原生策略标记 inactive；该标记
  穿过 WithState/WithWeight/WithInertialization。additive Slot 可同时来源=1、Slot=1，
  TotalNodeWeight 可大于 1，不能把来源权重硬算成 1-SlotWeight。
- Standing 活跃性和 Main 来源收集保留 Slot inactive。未接入 Main 的旧入口默认行为不变。
- 候选机器状态分别保存，最后组装整图结果，避免多次整图 record 复制叠加过渡缓冲栈占用。
  失败后可以从相同 committed 状态重试，没有扩大线程栈、降低容量或放宽断言。

原生依据为本地 UE 的 `AnimNode_Slot.cpp`、`AnimNode_StateMachine.cpp`、
`AnimNodeBase.h`、`AnimTypes.h`。本批没有运行新的 UE 原生整图探针。

## 验证

日志及 TRX：`artifacts/test-results/main-cached-graph/`。

| 范围 | 结果 |
| --- | --- |
| 新 Slot Core 专项 | 19 项，包含在常规全套中；Release 19/19 |
| 新 Main 编译/调度专项 | 27 项，包括 13 种 editor/native 数据变异拒绝 |
| Main 与既有 Standing 专项 | Debug 36/36，Release 36/36 |
| Core 常规 | 1910/1910，按原约定不含 P5aGolden/TraceSchema |
| Import 全套 | 1128/1128 |
| Main 调度夹具 | 30/60/120 Hz 共 1050 帧，每帧候选重试一致 |
| 默认线程栈/分配 | 900 帧，前 300 帧热身，后 600 帧 0 B |
| Godot 构建 | 0 警告、0 错误 |
| Standing 实际生产 smoke | Standing 5040、Detail 1890、Pivot 5040、Sprint 1260 帧通过；活动 0 B |
| 既有共享来源 smoke | split 840、mixed 1260、13522 贡献、38 事件、27 拒绝、Main 姿势 1260 帧通过 |
| 蹲姿来源 owner | 1671 帧、29 事件、104 共享读取、15 拒绝、0 B 通过 |
| 单/并行 Worker | 各 180 帧、10 事件，旧结果/姿势摘要不变 |
| 并行晚期来源事件失败 | 无回调泄漏，Sync/角色/Controller/姿势提交回滚通过 |
| P4 pose 脚本 | 姿势/动画图、双模式脚部组件和晚期事务回滚通过，活动分配 0 B |
| Demo 300 帧路线 | 失败；平台锁定门禁未关闭，详见下文 |

Godot 日志前缀：`artifacts/main-cached-`。生产 result 摘要
`A9DF0647AFC3574C`，full pose `04D4A5651B87E0E4`，与前批一致。
短时回归不是 P7 十分钟 Release 性能验收，也没有新的移动截图/人工视觉验收。
本批未重跑 P4 matrix 和聚合入口；P4 受控脚部组件通过不能替代 Demo 平台路线通过。

## 首错与修正

1. `main-first.trx`：5 通过、4 失败。测试遗漏实际 ChangeStance delegate 和一次
   共享 Cycle 跳过回调。改为读取原始曲线并保留全部回调，不使用线性替代。
2. `main-second.trx`：测试主机 Stack overflow 中止。整图大值复制叠加默认线程栈，
   改用分别持有机器候选状态；`main-third.trx` 22/22 后新增默认栈连续更新检查。
3. `main-fourth.trx`：严格回调校验误假设外层 Main Movement 图体也在局部导出内。
   改为完整 compiled inventory 必检、已有 editor 图体同检。不存在的 editor 图体
   变异测试移除，保留该外层 reader 的实际 compiled 回调变异拒绝。最终 `main-fifth.trx`
   36/36，全套 Import 与 Release 均通过；未改原始导出数据。

## 保持打开

- Main 当前是可复用共享 Update 组件，**尚未接入 Demo**。测试自动退出时间/规则由
  夹具提供；既有 shared-source smoke 的混合权重仍是受控输入，不是新 Main owner。
- 将 Main 更新结果直接供给 Godot Standing/Detail 阶段，避免再次 Prepare；再统一
  Crouching/Main 来源初始化、Sync/Notify、CacheBones/Evaluate 和最终候选提交。
- Save 生命周期测试在此只使用初始化计数；完整缓存姿势、Montage Slot 注册/混合、
  惯性化请求最终消费与通用状态事件未整体闭合。旧 Standing 位掩码消费者仍有待迁移。
- Main 之后继续原曲线反馈、动态 Layering/Add/LS/Lean/IK mask、完整脚部约束，以及
  P5A 事务/动作、P5B Overlay/道具、P5C Mantle/Roll/Root Motion、P6、P7。音频暂缓。
- 本批 Demo 仍在 85--96 帧失败：`locks=0`，`platformWindowCurves=<1,1,0,0>`，
  sprintEnd `(-3.999,1.0001398,-7.932024)`，brakeEnd `(-3.972002,1.00092,-9.286164)`。
  与前批一致，不强制脚锁、不改路线/阈值，也不将其直接归因为 IK 求解器。
