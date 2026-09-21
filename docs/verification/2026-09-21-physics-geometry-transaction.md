# 几何查询事务生命周期

本批在主目录 main 推进运行时接入的事务前置条件。尚未把 AlsPolygonManifold 接到 AlsGodotContactQuery，未改变普通 demo 的后端，也未重跑十二项整链；最新整链仍为 9/12。

## 接入边界

IAlsContactGeometrySource 新增 StageCommit、PublishCommit、Abort、Reset 默认回调，旧无状态 provider 保持兼容。AlsWorldContacts 在整次求解的现有事务中驱动它们：

- PrepareStep 在 registry 锁内先于查询和流形恢复；即使部分准备失败，也会执行 Abort。
- 全部接触历史准备完后才执行几何 StageCommit；只有它成功才允许发布。
- PublishCommit 与既有接触历史一起发布，早于 island 身体状态发布。
- 查询/求解/准备失败调用 Abort，显式重复 Abort 不重复调用 provider；释放 registry 锁。
- Reset 只在没有待提交步骤时调用，清除几何持久状态，随后重置接触历史。

所有可能失败的工作必须在 PrepareStep、Query 或 StageCommit 中完成。PublishCommit 与 Abort 的契约是不得抛出；这是参与者原子发布的约束，不代表 owner 能回滚任意恶意 provider 已发布的副作用。Abort 的 finally 仍会释放 registry 锁。

流形恢复可能完全跳过 Query，被过滤的步骤也可能没有任何接触对；两者仍执行准备和提交回调，provider 不能以“是否查询过”决定步骤是否结束。

AlsContactTrace 此前没有转发 PrepareStep；本批补齐上下文转发，并转发所有新生命周期，避免启用诊断改变求解语义。

## 验证

AlsGeometryTransactionTests 使用真实 AlsPolygonManifold/GJK 缓存和两刚体 island，不仅是回调计数：

- 在已有成功缓存后，分别注入部分 PrepareStep、完成 GJK/流形后的 Query、StageCommit 失败。
- 检查已提交 witness/weights、身体状态与 epoch 未改变，registry 解锁。
- 撤销故障后的重试与无故障对照身体及缓存一致。
- 显式 stage 后 abort 保留原缓存，reset 清空；pending 时拒绝 reset。
- 被过滤的空步骤与恢复流形跳过 Query 的步骤仍结束几何事务。

Godot PhysicsCoreContactSmoke 新增 geometry_transaction_checks=3，通过真实 AlsContactTrace 和 AlsWorldContacts 验证上下文、成功提交、查询失败回滚与 reset 转发。60 Hz 实际运行退出 0：三场景各 60 步，545 接触精度、9 几何、5 流形、5 睡眠及 3 新事务检查通过，动态动量误差 0。原报告中的 ordinary_character_connected 和 chaos_narrow_phase_parity 仍为 false。

Godot 优化构建 0 error/0 warning。日志和报告在 artifacts/physics-geometry-transaction-20260921/。本批未修改 UE 插件，未重新运行 UE；上一批普通 Editor 退出 0xC0000005 和两条旧 Condition failed 未解决。未修改 Import 代码或原生数值算法，未重跑 Import 全量。

Core Release 固定 JIT、集合串行全量 2798 项通过、退出 0；按既定约定排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests。既有零分配回归仍通过。

## 下一步

给查询绑定保留原生 cooked 拓扑、源尺寸与缩放；在 registry revision/身体 generation 及步骤生命周期下持有 GJK 缓存。原始 pair 次序必须在旧 interior-face 换序前保留，并把运动状态、pair margin 与分离 cull 接入。事务接口已具备，但运行时缓存实现和 polygon 接管仍未完成。之后继续整链三项旧失败、普通 Ragdoll/Get-up/Pose Recovery，以及总清单中的 Mantle、完整 Camera 和十分钟性能预算。
