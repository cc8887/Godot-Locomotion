# 地面探测与高度调整归 Core

本批将实际生产 `LyraCharacterFloorProbe` 的完整 Compute/Find/高度调整决策迁入纯 .NET `AlsCharacterFloorProbe<TCollider>`。继续使用 ALS 人物、Pistol/Rifle、现有 ALS 积分和上一批 Core 地面/空中控制器；普通 Lyra 玩家、NPC 和 RootMovement 的真实 floor 路径已调用新 Core。

## 生产路径和边界

Core 处理短胶囊查询参数、edge/穿透后的缩半径重试、floor distance 下界和坡度判定、射线回退、保留原 sweep 点/时间/位置/trace 但替换 normal/collider、perch 支撑重查/拒绝/恢复、平均地面高度带、Initialize/AfterMove/AfterSweep 的 Grounded 判定和修正顺序。AfterMove 保留实际滑动接触几何与上升速度门控；walking 使用原 predicted height；ApplyFloorSnap 后仍朝先前计算的绝对高度目标修正。

Core 的 typed floor result 复用既有 `IAlsCharacterGroundFloor`，adjustment 复用 `IAlsCharacterFloorAdjustment`。opaque Collider 泛型在 Godot 为原 Rid，在纯 .NET 测试为整数 ID；Core 不引用 Godot、Node、PhysicsServer、Rid、Collider 对象或 JSON。几何仍为 Y-up 米/float，标量平方长度和运算顺序保持。

`AlsCharacterSweepMath.PullBackFraction` 提取原 PrimitiveComponent 回拉算法，供 Core 高度修正和实际 capsule sweep 共用。高度修正保留 backend travel 中的真实 depenetration，仅回拉请求运动对应部分；不将实际位移替换为请求 fraction 的近似值。

Godot 保留物理 owner/线程检查、真实 capsule/ray/height sweep、实际碰撞 normal 与形状/box 载荷适配、Jolt cast bracket 的真实 overlap 精化、ApplyFloorSnap 的临时设置恢复及位置写入。所有查询仍来自当前物理世界；没有保存/回放 floor position、contact 或 hit 来替代物理查询。Lyra 设置类保留 CDO JSON/schema/hash 检查及单位转换，Average/TraceDistance 由 Core 设置提供。

## 验证

最终 Core 76 项通过，新增 floor 32 项，原地面 19、空中 23、速度/下落原生 fixture 2 项，0 失败/0 跳过。新测试覆盖短胶囊、edge/穿透重试、line 保留几何/替换表面、四种 line 拒绝、缺失 floor/零查询半径、perch 拒绝/恢复/三种跳过、地面带的包含边界、Snap 后绝对高度目标、初始化上升/无支持拒绝、高度碰撞 travel 的恢复与回拉、line 高度门控、上升/下降接触、三种无效 slide 接触、walking predicted height 及非法参数先于物理操作拒绝。

首轮 v1 75 通过/1 失败是高度碰撞预期字面量 `0.9028285f`；独立重算原 float 标量步骤得到 `0.9028284549713135`，修正为 `0.90282845f` 后最终 v2 全过。原六位小数比较门槛、算法及原生阈值没有改变，失败 TRX/日志保留。

Debug 和 ExportRelease 构建各 0 警告/0 错误。两种构建各完成完整 Main＋Rig 7560 帧/7296 姿态、真实 Godot Rig 物理 2520 帧/2484 姿态、普通 ALS 1700 帧和普通十角色 480 帧/4800 蒙皮发布。十角色和真实 Rig 物理完整报告与前批相同。

三频 floor 实际 Jolt 场景各 8 角色、0.6 秒，30/60/120Hz 分别 144/288/576 次移动及同数动画重试，每构建共 1008 次。普通高度带、站蹲后净空、高度修正受真实顶棚阻挡及 perch 支撑拒绝通过；三个完整报告包含逐帧位置/grounded/floor movement，均同 Debug/Optimize 及迁移前结果。

原 floor query 每构建 144 次，实际物理查询 2250、perch 重查 8 次，没有修改 actor。原生参考对照保留已有 3 项差异，maxValidPointCm 约 0.01020166，原 0.01cm 门槛保持；比较结果仍为 false，两个诊断进程按原协议退出 1。完整 scalar/144 行报告只除去本次 evidence tag 后同迁移前及两种构建；本批没有把它们作为 native floor 验收通过，也没有通过改容差或跳过行消除旧差异。

空中每构建 112 个实际 query、20 次 apex split、28 次 landing、6 次多接触，0 mismatch，完整报告同上一批。普通地形三频每构建共 6300 次最终蒙皮发布，台阶/斜坡/落差/跳跃/站蹲/ADS、Pistol/Rifle 通过，完整报告同两构建及上一批。

最终 `artifacts/lyra-analysis/floor-probe-core-v2-audit.json` 通过：22 个成功 Godot 进程和 2 个保留旧差异的预期诊断（共 24），7 份本批源码/4667 份保护基线（含 870 份 Lyra 资产 JSON）保持冻结哈希，前批证据保持。四轮 Optimize 后六份 Debug DLL/PDB 恢复，源码和历史文档内容审计通过。实现/测试/构建/运行为 v2，v1 字面量失败证据保留。

没有 UE 修改/启动/重导、资产 JSON 格式化、提交推送、新 GPU、全量 managed、十分钟、性能或跨平台验收。实体键鼠没有重试；此前 computer-use `GetCursorPos 0x80070005` 拒绝没有恢复证据，自动逻辑输入不作为实体键鼠通过证明。

## 剩余工作

完整地面、空中与 floor 决策已归 Core。下一步迁移 Sweep 的穿透恢复顺序/调整限幅和 SetCrouching 的净空/回退决策；Godot 保留实际 shape/ray/overlap 查询、物理 owner 和 capsule/model 写入。之后完成普通生产路径 Core 归属核对和实体键鼠验收。

本批只关闭上述通用 floor 决策及公共回拉复用。完整 UE 调度/私有字段还原、物理世界逐位等价、URO、额外 Provider 和既有暂缓项继续后移，完整目标仍开放。
