# 地面移动控制器归 Core

本批按完整目标核对实际生产链后，发现`LyraCharacterGroundMovement`仍包含引擎层MoveAlongFloor/StepUp/Slide控制。迁移完整控制器，而不只抽取投影公式；ALS模型、Pistol/Rifle、原速度积分与动画链继续复用。

## 生产路径与边界

纯.NET `AlsCharacterGroundMovement`处理斜坡投影、接触顺序、穿透后的滑动、两墙响应、台阶up/forward/down顺序、edge/高度/坡度/顶棚拒绝、失败回滚、末次floor adjustment，以及实际位移重建水平速度。RootMotion和恢复穿透时继续保留原end velocity。逐帧contacts/teleport标记仍在同一物理owner内重置；动画取消重试不再次移动胶囊。

Core接受typed命中/floor接口和宿主world接口；世界坐标为Y-up米、float。完整变换检查点类型由宿主提供并保持不透明，Core决定何时Capture/Restore；Godot实际使用原`Transform3D`，没有通过TRS重建破坏原basis或scale。Core可由不依赖Godot的测试world运行，不读取Node、Rid、Collider或metadata。

Godot `LyraCharacterGroundMovement`保留对象owner识别、Godot/System.Numerics向量逐分量转换，以及真实floor/sweep/AfterSweep和Transform3D写入。`lyra_can_step_up`、PhysicsBody/CharacterBody判断留在命中适配器；floor结果只暴露Core所需的几何值。原射线/capsule查询、shape配置及世界资源继续由Godot负责。

float物理边界的Dot/Cross/长度/Normalize保留原scalar运算次序，避免更换为SIMD reduction或倒数乘法。只读检查本机GodotSharp Vector3/Mathf方法IL，确认Normalized按各分量除以MathF.Sqrt长度。没有修改Godot引擎，也没有UE启动/修改/导出。

速度/摩擦/加速度仍用既有ALS Core；`LyraSceneMovementService`、`LyraRootMovementMotor`和空中着地后的walking remainder实际经过新Core控制器。

## 验证

最终v2 Core24项通过（新增19）：平地、sweep斜坡/line bypass、完整台阶、up/forward/down穿透、edge/高度/不可行走/缺失floor七类回滚、不允许StepUp、Root/恢复穿透的速度保留、双墙/平行墙、缺失地面、连续帧清理、参数拒绝及opaque检查点。另含既有GroundMovementPrediction和CharacterVelocity回归；0失败/0跳过。首轮v1为Core.Math与System.Math命名冲突，修为显式alias后通过，失败编译日志保留。

Debug和ExportRelease构建均0警告/0错误。两种构建各完成完整Main＋Rig7560帧/7296姿态、真实Godot Rig物理2520帧/2484姿态、普通ALS1700帧、十角色480帧/4800蒙皮发布及三频台阶。

实际Jolt台阶场景每频5角色、2秒：30/60/120Hz分别300/600/1200次移动及同数逐帧动画重试，每构建共2100次。站立/蹲伏低台阶、过高台阶拒绝、低顶棚阻挡/回滚和关闭StepUp的metadata策略通过。Core单测中的checkpoint还包含位置以外的marker，用来验证Core恢复完整宿主状态而不是只恢复位置。

上述三份台阶、十角色和真实Rig物理完整报告与两种构建及各自前批相同，包含逐帧数据；没有以新计数或近似阈值替代原结果。普通Jolt地形三频各450/900/1800帧、每帧两角色，每构建6300最终蒙皮发布；台阶/斜坡/落差/跳跃/站蹲/ADS和Pistol/Rifle通过，完整三频报告也与两构建及前批相同。

最终`artifacts/lyra-analysis/ground-move-core-v2-audit.json`通过：20个成功Godot进程，7份本批源码与4657份保护基线（含870份Lyra资产JSON）保持冻结哈希，前批证据保持，六份Debug DLL/PDB在两轮Optimize切换后恢复。实施、Core测试、构建及运行均为v2；首轮v1命名冲突编译日志保留。没有UE启动/修改/重导、资产JSON格式化、提交推送、新GPU、全量managed、十分钟、性能或跨平台验收。实体输入未重试；此前computer-use `GetCursorPos 0x80070005`拒绝没有恢复证据，自动逻辑输入不作为实体键鼠通过证明。

## 完整目标中的剩余归属

| 生产能力 | 当前通用实现 | 剩余工作 |
| --- | --- | --- |
| 起步/停止/Pivot | Core DistanceMatching、GroundMovementPrediction与原源采样 | 当前Lyra图规则/距离资源绑定留在Lyra |
| Cycle方向/步幅 | Core OrientationWarping、StrideWarping、BlendSpace与同步 | 原资源和图连接留在Lyra |
| Rig/脚部/姿态 | Core IK、Hierarchy、Memory、Traversal、PoseAdapter与ALS姿态/曲线/属性 | 当前资产布局适配留在Lyra |
| Animation Interface/Layer | Core合同、绑定/多实例执行、阶段/缓存/通知容器 | 14个Lyra接口声明和武器图绑定留在Lyra |
| 地面碰撞控制 | 本批完整Core控制器，原ALS速度积分 | FloorProbe判定、Sweep穿透恢复仍在Godot层 |
| 空中碰撞/蹲伏 | 已共用Core CharacterFalling积分 | AirMovement子步/滑动/着地控制、SetCrouching决策仍需迁移 |
| 普通Demo验收 | 既有两武器、实际物理/地形及GPU样本 | 实体键鼠尚未验收 |

表中Core调用与剩余实现均按当前代码检查；它不是全UE兼容或所有私有字段还原的验收。下一步优先完整空中控制器，再floor/穿透/蹲伏决策，之后收尾生产Core归属和实体键鼠。URO、额外Provider、完整UE调度/物理逐位等价及既有暂缓项继续在后续路线，完整目标仍开放。
