# 空中移动控制器归 Core

本批将实际生产 `LyraCharacterAirMovement` 的完整碰撞控制迁入纯 .NET `AlsCharacterAirMovement`。保留 ALS 人物、Pistol/Rifle、原移动服务和动画链，继续复用既有 `AlsCharacterFalling`、`AlsCharacterVelocity`、`AlsDoubleVector.SafeNormal` 及上一批 Core 地面控制器。

## 生产路径和边界

Core 处理空中子步与迭代预算、跳跃顶点分段/余量返还、重力和终端速度、空中控制限制、坡面滑动与防止向上加速、两墙响应、零时间侧向脱困、perch 随机脱困、着地几何判定及着地后的 walking remainder。落地余量继续经过实际 Core 地面控制器，站蹲速度设置和 RootMotion 覆盖规则保持。接触顺序、恢复穿透标记、每帧清理及随机状态仍属于同一物理角色；动画取消重试不重复移动胶囊。

Core 接受 typed 命中、floor、adjustment 与 world 查询接口，碰撞载荷保持不透明。物理边界为 Y-up 米/float，ALS 积分保留 Z-up 厘米/double；方向转换按分量执行，位移差先在 float 位置边界相减，再转厘米。没有替换为 SIMD Dot/Normalize 或给碰撞结果建立新的动画时钟。

Godot 适配器保留对象 owner、实际 capsule sweep/floor 查询、碰撞载荷转换、当前位置读取和物理写入。角色资源设置、half height、装备/root 输入继续由原生产服务提供；Core 不引用 Godot、Node、Rid 或 JSON。着地支持和碰撞来自当前真实物理世界，未回放录制的物理观察。

原通知队列的 FRandomStream 分数生成已提取为 Core `AlsRandomStream.NextFraction`，通知队列和空中脱困共用同一实现；保留原 seed 更新和浮点位构造。已有原生通知队列 fixture 回归通过，脱困只在原分支消耗两次随机数，角色 seed 独立。

## 验证

Core 最终 40 项通过，新增空中控制器 23 项，0 失败/0 跳过。覆盖自由下落/midpoint、顶点返还及禁用、迭代预算/终端速度、RootMotion、站蹲着地余量、失去支持时停止 walking、四类不合法着地、edge sweep/line fallback、墙面/双墙、恢复穿透后的速度保留、连续帧清理、perch 随机脱困、零时间侧移、坡面不得向上加速和输入拒绝；另含既有下落/速度/原生通知随机队列回归。首轮 v1 37 项通过后补充三个碰撞分支测试，最终 v2 40 项；审计预检纠正新增数 25 应为 23，没有修改 C# 或运行结果。

Debug 与 ExportRelease 构建各 0 警告/0 错误。两种构建各完成完整 Main＋Rig 7560 帧/7296 姿态、真实 Godot Rig 物理 2520 帧/2484 姿态、普通 ALS 1700 帧和普通十角色 480 帧/4800 蒙皮发布。

实际 Jolt 角色移动场景三频各 6 角色、4 秒，30/60/120Hz 分别 720/1440/2880 次移动及同数动画取消重试，每构建共 5040 次。真实 jump apex/落地、midpoint/end velocity、站蹲净空/恢复站立和墙面切向运动通过。三份完整报告与前批及 Debug/Optimize 相同。

原空中 query 场景每构建 112 个实际 Jolt 查询，20 次 apex split、28 次 landing、6 次多接触，mismatches=0；完整 112 行报告同两种构建及迁移前结果，原阈值保持，没有回放物理观察。这是指定 query 的原参考对照，不等于所有 UE/Jolt 世界轨迹逐位等价。

普通地形三频每构建共 6300 次最终蒙皮发布，台阶/斜坡/落差/跳跃/站蹲/ADS、Pistol/Rifle 通过；三频完整报告与两种构建及前批相同。十角色和真实 Rig 物理完整报告亦同前批。

最终 `artifacts/lyra-analysis/air-move-core-v3-audit.json` 通过：22 个成功 Godot 进程、8 份本批源码与 4661 份保护基线（含 870 份 Lyra 资产 JSON）保持哈希、前批证据保持。三轮 Optimize 验证后六份 Debug DLL/PDB 均恢复。实现/测试/构建/运行为 v2，v3 仅纠正审计计数，冻结 v2 保留；没有重测已通过的相同构建。

没有 UE 修改/启动/重导、资产 JSON 格式化、提交推送、新 GPU、全量 managed、十分钟、性能或跨平台验收。实体键鼠没有重试：此前 computer-use `GetCursorPos 0x80070005` 拒绝没有恢复证据，自动逻辑输入不作为实体键鼠通过证明。

## 剩余工作

完整地面与空中移动控制已归 Core。下一步将 `LyraCharacterFloorProbe` 的 Compute/Find/高度调整决策、`LyraCharacterSweep` 的穿透恢复顺序和 `LyraSceneMovementService.SetCrouching` 的净空/回退决策迁入 Core；Godot 保留实际 shape/ray/overlap 查询、对象所有权及 capsule/model 写入。

之后收尾普通生产路径的 Core 归属和实体键鼠验收。URO、额外 Provider、完整 UE 调度/私有字段还原、物理世界逐位等价及既有暂缓项继续后移，完整目标仍开放。
