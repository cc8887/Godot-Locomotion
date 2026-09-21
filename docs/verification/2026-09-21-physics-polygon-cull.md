# Polygon 分离距离与接触激活

在 `D:\GodotALS` 的 `main` 继续实现，没有创建副本。当前仅实验物理后端接入，普通角色没有切换。

## 本批实现

- 从既有原生 `v4_physics_cull_reference.json` 严格读取实际 detector：基础距离 3 cm、速度倍率 1、额外扩展最多 3 cm、逆参考尺寸 .01f、最小缩放 1。复用已通过 324 个原生 midphase 对照的计算器，不把 CVar 的 -1 override 当实际距离。
- 从既有惯量观察读取完整 particle 的 actor-local bounds，使用最大完整边长，而不是单个 shape、COM 旋转后尺寸或扩大后的关节惯量 extents。48 个完整/隔离身体映射验证；40 个完整链身体都小于 100 cm，因此尺寸缩放为 1。
- 显式 polygon 查询和恢复使用同一个当帧 cull。动态身体取步前速度，kinematic 取本帧指定速度，静态为零；静态和 kinematic 的 bounds 不参与动态尺寸缩放。缺少 previous、缺少身体 bounds 或 generation 失效时拒绝，不默默退回猜测参数。
- 新接触同恢复接触一样，仅在最小有效 Phi 不大于 cull 时激活；等号保留。超出范围的流形清空，下一帧重新查询。禁用点不参与最小值，空流形不激活，失败步骤不发布几何或摩擦历史。
- 新增可选 NativePhi，保留窄相在局部接触点降为 float 前计算的原始 float Phi；避免从已经舍入的点重算后改变边界判断。恢复时重新计算当前 Phi。旧几何源未提供时仍从点重算。
- 旧 raw/scaled/box/nonzero-margin 四套共 **24624** 个原生 polygon 对照新增 NativePhi 精确相等断言；点和法向原有精确相等断言保留。

## 验证与实际边界

日志与报告：`artifacts/physics-polygon-cull-20260921/`。

- Godot 优化构建通过，0 警告/错误。
- 30/60/120 Hz 接触 smoke 均通过：各 551 精度、9 几何、68 polygon、3 事务、5 流形、5 睡眠检查。新增 9 个 polygon 检查覆盖分离发现/拒绝、速度和尺寸来源、generation 失效及回滚。
- Release 固定 JIT、串行 Core **2819** 通过（按既有规则排除 AlsP5aGoldenTests/AlsP5aTraceSchemaTests）；Import 全量 **2406** 通过、1 既有跳过，退出码均 0。首轮 Core 新测试误在 Commit 前读 LastActivePairs 失败，修正测试读取提交后统计，没有更改激活判据。

整链采用原有时间、睡眠持续一秒和几何门槛，结果 **8/12**，比上批 9/12 新增一项回归：

| 场景 | 30 Hz | 60 Hz | 120 Hz |
| --- | --- | --- | --- |
| 普通落地 | 休眠失败 | 通过 | 通过 |
| 高速落地 | 第 9 帧穿地 | 通过 | Mannequin 休眠失败（本批回归） |
| 平台平移 | 通过 | 通过 | 通过 |
| 平台旋转 | 休眠失败 | 通过 | 通过 |

高速 30 Hz 仍是 AnimMan 身体 19，第 9 帧 z=-73.3190865986863 cm，与上批相同。既有第 7/8 帧已经检测到向上的地板接触，分离 cull 未解决该失败。高速 120 Hz AnimMan 第 246 帧睡眠，但 Mannequin 未睡；最后速度 .98504567 cm/s、角速度 .12640052 rad/s，最大锚点误差 .85565599 cm、关节超限 .00465021 rad。不能把参考算法对照或专项测试通过写成轨迹/稳定性通过。

## 后续

- 本批仅显式绑定的 box/convex 对接入分离距离。球、胶囊及混合对仍走旧 overlap 路径；CCD/MACD、完整 midphase 退役、原生 collision tolerance 传输仍未完成。
- 继续核对原始 Gather 输入与历史。源码 `PBDCollisionConstraint.cpp` 的 Setup 用双方 InitialOverlapDepenetrationVelocity 和 0 的最大值，当前整链 Gather 默认 -1。已有同 Gather 后输入对照不能证明这个设置正确；应观察实际身体/constraint/solver 参数后传输，不能直接靠改数值碰运气。未在本批擅自更改该参数。
- 继续关闭上述四项整链失败，核对其他 primitive 实际几何，然后接普通 Ragdoll owner、pelvis 胶囊/相机、Get-up/Pose Recovery；Mantle、完整 Camera 和十分钟性能预算等总目标仍未完成。
- 本批无 UE 插件修改/新导出，未重跑 UE 构建/重启门禁。上一批普通 Editor 退出访问冲突和两条旧 Condition failed 没有修复。
