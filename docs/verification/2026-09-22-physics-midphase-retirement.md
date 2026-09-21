# 身体分离后的碰撞缓存退役

本批直接在 `D:/GodotALS/main` 推进，未修改普通 demo 入口，未修改用户 P4 文档。

## 已确认的遗漏

本机 UE 源码 `Chaos/Private/Chaos/Collision/CollisionConstraintAllocator.cpp:274` 的 `PruneExpiredMidPhases` 会销毁本帧未使用且不在睡眠中的 particle-pair midphase 及其 collision constraints。

`ParticlePairMidPhase.cpp:487` 明确区分两种情况：单个 shape pair 分离时保留 collision，直到所属 particle pair 不再重叠才销毁。`PBDCollisionConstraint.cpp:791` 的 `ResetManifold` 只清理接触点，不清理 GJK warm-start 数据；销毁 constraint 才清除这一对象持有的搜索缓存。

上一批已在包围盒分离时跳过几何及摩擦恢复，但 `AlsPolygonQueryCache` 只有显式 Release，运行时没有调用。这会让过期身体对保留旧 GJK 数据。补齐该遗漏符合原生生命周期，但不等于已经证明它导致休眠回归。

## 实现

- Core `RetireSeparatedPairs` 处理当前 owner 已有的缓存条目，按完整身体包围盒判断分离；缺失形状、shape revision/body generation 变化同样退役。
- 在 Godot `PrepareBounds` 完成 union 和动态扩张后调用。使用 staged/proposed cache，整步失败时 Abort 恢复已提交缓存；成功发布才变更缓存计数。
- 身体包围盒仍相交时保留缓存，包括当前没有 Query、仅恢复 manifold 的步骤。包围盒刚好相接也保留。
- 当前 fixed-island owner 在整岛睡眠时不运行 Gather，因此不会因静止时省略查询而清除睡眠中的缓存。尚未实现通用多岛/逐约束睡眠的 native midphase owner，不作扩大声明。

## 验证及结果

- Core 定向 11 项通过，其中新增三项覆盖提交/回滚/重入冷启动、接触边界/无 Query 保留、移除与无效输入。Release 固定 JIT 串行全量 **2854 通过**（沿用既有两个排除类）；LatestMajor roll-forward 定向 11 项通过。
- Godot 优化构建 0 warning / 0 error。30/60/120 Hz 接触 smoke 通过，新增五项通过 trace 包装的真实 query 生命周期检查；native polygon checks 从 68 增至 73。
- 十二项完整矩阵仍为 **8/12**。普通/平移/旋转30，以及普通120的休眠失败仍在。普通120 Mannequin末秒 V2.22706985 cm/s / W0.26983485 rad/s，AnimMan687帧睡；本批未解决该回归。
- 八个成功报告逐字段对比上一批：高速30、普通60仅 native_cached_pairs 从10降至9，其他字段不变；其余六个成功报告所有字段不变。说明清理确实生效，但不能把它当作轨迹或睡眠改善。
- 未改 Import 或 UE exporter，没有新 UE 原生导出/构建/重启，也未重跑 Import 全量。旧 Editor AV 和两条 Condition failed 未修复。证据在 `artifacts/physics-midphase-retirement-20260922/`。

## 后续缺口

继续源码检查确认：`FSingleShapePairCollisionDetector::DoBoundsOverlap` 在 narrow phase / manifold restore 前还执行逐形状筛选。普通组合先做按 cull 扩张的 AABB 检查；球球走中心距离；非球参与两侧 OBB-to-AABB 检查，后者只在上一帧没有有效接触时运行。该层目前尚未移植，不能把整个身体 bounds gate 称为完整 broad/midphase。

下一阶段优先补齐逐形状规则及 last-used epoch，并用独立原生参考核验 flags/边界/转换，避免无条件每帧 OBB 筛选或错误退役 GJK。仍需解决四项休眠失败、校验胶囊端点末位与独立 bounds 参考，再接普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 和最终十分钟性能预算。完整 ALS 目标继续保留。
