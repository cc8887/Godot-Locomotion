# 逐形状包围盒筛选与接触连续性

实现位于主目录 `D:/GodotALS/main`。本批补齐非 CCD / 非 MACD、启用 bounds checks 的有界球/胶囊/盒/凸包路径；不是完整 native midphase 或普通角色 Ragdoll 验收。

## 原生依据与实现

本机 UE `Chaos/Private/Chaos/Collision/ParticlePairMidPhase.cpp` 的 `FSingleShapePairCollisionDetector::DoBoundsOverlap` 与 `Chaos/Public/Chaos/Collision/CollisionUtil.h` 的 `CalculateImplicitBoundsTestFlags`：

1. 球球不做 AABB，按世界包围盒中心距离和 float 半径之和加 cull 检查；恰好相接保留。
2. 其他有界组合先将第一侧世界 AABB 按 cull 对称扩张，与第二侧相交检查。
3. 上一帧没有有效接触时，为每个非球端执行 OBB-to-AABB：将另一端实际几何的 bounds 变换到该端局部空间、扩张 cull，再与该端局部 bounds 比较。不能将两盒已有世界 AABB 再逆变换，也不能每帧无条件执行 OBB。
4. 上一帧有有效接触时，仅跳过上述 OBB，仍执行 AABB 或球球距离检查。

Core 新增 `AlsShapeBoundsGeometry`，统一 polygon 局部八角变换和 quadratic 端点/半径的紧包围盒。Godot 绑定真实 primitive、wrapper bounds 和明确的场景几何；每步保存 shape world pose/bounds，先 whole-particle gate，再逐 shape gate，之后才可恢复 manifold。

WorldContacts 保存已提交的 last-active epoch 和完整 pair identity；只有连续上一帧且身份相符时传入 true。Abort 不推进该状态，缺席步骤、零有效点、body generation/shape revision 变化及 Reset 均不会误认为连续接触。Trace 转发该状态。形状筛选失败不直接销毁 GJK，仍遵循上批 whole-particle 分离才退役的规则。

## 证据边界

本批是本机源码移植加解析几何/生命周期测试，没有新增 UE 独立 shape-bounds golden。尚未从运行中导出 bounds-check flags、各方向 OBB 判定及真实约束 last-used epoch。不能据本批自测声称逐位 native 等价，下一阶段仍需要这些独立参考。

真实胶囊仍沿用 float endpoint0/axis/height 重建末端；原生独立 endpoint1 的末位一致性待验证。未覆盖 triangle mesh、heightfield、levelset、probe、CCD/MACD、bounds checks 关闭分支或通用多岛睡眠。

## 验证

- Godot 优化构建通过，0 warning / 0 error。
- Core Release 固定 JIT 串行 **2859 通过**，沿用既有两个排除类；随后仅新增空查询/shape revision 连续性测试，最终定向10项在 .NET8 与 LatestMajor roll-forward 均通过。生产代码与全量通过时相同，未把最后追加的一项冒充全量已重跑。新增覆盖合计四个解析几何测试和两个连续性测试。
- 三频率接触 smoke 通过，增加实际 Godot binding + trace 的旋转细长盒筛选、上一帧接触跳过 OBB、反向端点检查，native_polygon_checks=76；60 Hz 场景接触 smoke 通过。
- 普通30 Hz 两模型各捕获60步，共120个完成步，与旧 UE 完整世界参考比较。`differences.json` 的 samples 数组与上一批 `physics-bounds-20260922/extended-differences.json` **完全相同**。因此本批没有改善该样例的早期速度误差，也不能归因其休眠失败已修复。

- 完整矩阵仍 **8/12**：高速30、全部60、高速/平移/旋转120通过；普通/平移/旋转30以及普通120休眠失败保留。八个成功报告相较上批只有 native_polygon_queries，及其中三个场景的 native_cached_pairs 变化，其他字段逐项相同；筛选减少了查询，但未改变这些场景的运动、接触和休眠结果。

证据目录 `artifacts/physics-shape-bounds-20260922/`；普通入口仍未接入实验后端，用户 P4 文档保持原样。本批未修改 Import 或 UE exporter、没有新 UE 构建/重启，旧 Editor AV / Condition failed 未修复。

下一步优先补独立原生 bounds/flags/last-used 参考，并对普通120休眠回归以及30 Hz第40–60步逐渐积累的误差进行同阶段比较。之后继续普通 Ragdoll/Get-up/Pose Recovery、Mantle、完整 Camera 和最终十分钟性能预算，不收缩完整 ALS 目标。
