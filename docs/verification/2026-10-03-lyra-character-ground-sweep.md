# Lyra 地面胶囊扫掠与 StepUp

本批将原 `MoveAlongFloor` / `StepUp` 的地面移动顺序接入普通玩家与 NPC 共用服务。ALS 人物继续使用68 skin / 69 raw / 81 logical 骨架，十四个 typed Layer 入口和装备共享实例沿用现有实现。本批没有重新导出人物或动画。完整运动等价和完整移植目标仍然开放。

前置地面查询与高度带见 [地面策略](2026-10-03-lyra-character-floor.md)，资源和 Interface / Layer 方案见 [人物与接口复核](2026-10-03-lyra-als-interface-review.md)。

## 原生证据

扩展自有 `LyraWholeMainOracle` 探针，通过保护成员访问调用本机 UE 5.8 原 `SafeMoveUpdatedComponent`、`MoveAlongFloor` 和 `AdjustFloorHeight`。实际 `MoveAlongFloor` 包含原 `StepUp`。原引擎与 Lyra 角色源码未修改。

完整 Editor 构建 `package-cmc-ground-sweep-v1` 成功，6项构建动作，16.82秒。只读采集 `cmc60-ground-sweep-v1` 使用三个实际 Provider，各60物理帧；采集完成后移出暂存插件。两个临时台阶 Actor 在查询后销毁，胶囊、组件变换、速度、模式、CurrentFloor 与 bJustTeleported 恢复。三个 Provider 的旧144项地面查询、250项地面速度、175项空中内核以及各60帧物理输入前缀逐项保持相同。

新增32项矩阵：站立/蹲伏半高90/65cm，SafeMove/MoveAlongFloor两种调用，分别覆盖自由移动、正面撞墙、斜向撞墙、短位移、带上移请求、下移请求、30cm台阶和55cm障碍。三个 Provider 的32项结果完全相同。这里的 ground 参考由实际 MoveAlongFloor、FindFloor/StepDown 及 AdjustFloorHeight 产生，再按真实组件位移计算速度；**不是完整 PhysWalking 的时间分步、速度更新或剩余时间调度参考**。

不可变参考 `artifacts/lyra-analysis/character-ground-v1-reference.json`，SHA256 `54B09BBA927DD5A2CA914325B49A645276C566FCEEDE02B3BDDE8B759ED97A5E`。`tools/export_lyra_character_ground.py` 拒绝覆盖。运行继续依赖现有被忽略的 `character_motor_v2.json`、`character_floor_v1.json` 和动画资源，没有新增运行资产格式。

## 共用服务接入

采集实际退出0，原 UE Condition/PostLoad 警告保留，没有称 UE 零警告。

`LyraCharacterSweep` 用实际 Jolt 胶囊 CastMotion / GetRestInfo 查询，排除自身 RID，以真实接触收窄时间。原 PrimitiveComponent 的 hit-time pullback 单独应用于移动比例，保留原始接触位置和比例。实际箱体的 opposing face 由物理服务器形状与变换求得；没有注入录制位置、法线或命中比例。查询本身不移动 owner。

`LyraCharacterGroundMovement` 在同一 owner 中处理地面坡面投影、第一命中、剩余坡面、StepUp、滑动和第二面调整。StepUp 按上移→前移/滑动→下移→高度/边缘/可走地面检查执行，失败恢复组件变换。成功后按实际 FindFloor 执行原高度带调整。撞墙后的速度来自最终组件实际位移除以帧时长；无碰撞路径继续保留原速度内核的 double 历史。

普通玩家与 NPC 默认地面移动使用这个 owner。空中仍走既有 MoveAndSlide / 地面接触路径，着陆剩余时间、顶点分步和完整 PhysFalling 尚未替换。独立 Root Motion 物理夹具也保留其既有调用路径；生产 Root/Warp/Emote 的现有场景回归另行验证，不将它们称为新增 root 台阶专项。

物理凭据新增 GroundSweepApplied、Stepped、StepReverted 和实际接触列表，墙体验证消费此列表。地面路径由本实现执行查询，不能再以 CharacterBody3D 的旧 IsOnWall 缓存作为新步骤的证据。动画取消重试复用已发布凭据，不重复扫掠、回退、高度调整或 root 旋转。

穿透恢复目前使用实际 Jolt BodyTestMotion recovery；原 CMC 的完整 MTD、拉回、限制和 bJustTeleported 生命周期仍是未完成边界。`lyra_can_step_up=false` 是 Godot 场景的 StepUp 开关，不能视为完整 UE component/actor CanStepUp / CanBeBase 的移植。移动基座、所有坡面/角落、多接触和复杂地形仍需扩大原生与玩法覆盖。

## 实际台阶玩法

新增五角色场景通过普通场景服务推进：站立与蹲姿30cm台阶、55cm高障碍、30cm台阶上的低屋顶，以及关闭 StepUp 的30cm台阶。每频2秒，每帧动画取消重试并断言组件变换、物理凭据和移动次数未改变。

30/60/120Hz 分别300/600/1200角色物理帧，累计2100移动和同数重试，每个构建各一套。站立/蹲姿实际登阶，55cm障碍拒绝，低屋顶限制完整登阶并产生回退。圆胶囊在屋顶下会在边缘局部抬升，实际上端始终不越过2.05m屋顶底面。

StepUp 开关只约束 StepUp。禁用角色在30Hz边缘局部抬升，60/120Hz可通过可走边缘/高度修正抵达台阶顶部，所有频率实际 Stepped 数均为0。这些数据保留在报告；**本批没有对应的原 UE 禁用组件/低屋顶完整轨迹，因此不声称这两个玩法轨迹原生等价**。

首次夹具错误要求屋顶与禁用角色完全不抬升，60Hz和30Hz断言失败。后续断言改为实际净空、是否执行 StepUp、回退与物理凭据；原失败日志保留。原生32项和完整同输入轨迹的所有精度门槛未改变。

## 原生差异与验收范围

32项最终诊断仍有8项失败：两种半高各三项 ground 速度差（正面墙、斜向墙、55cm障碍）及一项30cm边缘 raw contact normal 差。最大位置误差 `3.9478842253e-5cm`，最大速度差 `0.00168902355cm/s`，最大时间比例差 `1.6391277313e-6`，最大法线差 `0.000728577774`，地面距离报告最大差 `5.4836273193e-6cm`。速度门槛仍为0.001cm/s、法线0.0001，诊断返回1并明确 comparisonPassed=false。

初版累加扫掠位移的速度最大差为 `0.00326258617cm/s`；按原 PhysWalking 使用实际组件位移后降至上述值，但未通过。Jolt 的台阶边缘法线与原 Chaos GJK 法线不同，未将实际法线替换为原记录。

最终同输入连续轨迹仍有下列差异。30/60Hz首个碰撞发生在空中，本批地面替换没有改变该处结果；120Hz新增地面扫掠已生效，但完整空中阶段仍未通过。

| 频率 | 帧数 | 未通过帧 | Grounded差异帧 | 最大位置差cm |
| --- | ---: | ---: | ---: | ---: |
| 30Hz | 240 | 120 | 1 | 14.7219222468 |
| 60Hz | 480 | 240 | 1 | 7.7952958809 |
| 120Hz | 960 | 481 | 1 | 4.0219680101 |

120Hz第479帧的首个地面撞墙，实际位置相对原生只差约 `0.0000600124cm`；实际速度 `(393.7797308,17.0459732,0)` cm/s 对原生 `(393.7979391,17.0509328,0)`，欧氏差 `0.0188716799cm/s`，仍超门槛。旧地面路径把该帧正面速度变为0，本批修复该行为，但没有将它计为原生验收通过。所有三频实际加速度精确相同，姿态标志相同，每频仍有一帧着陆 Grounded 差。

矩阵、连续轨迹和完整性审计证据：

Debug 与实际 ExportRelease Optimize 构建均0警告/0错误；每构建九项原场景加三项地面、三项台阶，累计30个最终玩法进程退出0。另两套144项地面诊断、两套32项扫掠诊断及六条连续轨迹均正常完成并按失败判定退出1，最终40进程无 Godot ERROR/WARNING。物理/普通多角色、地面、台阶、Warp、Emote和完整轨迹报告在两构建逐项相同；四套优化验证脚本各备份并恢复六个Debug DLL/PDB，最终恢复哈希一致。

最终独立审计通过，保护868份旧JSON、709个原资产包、9配置及3原角色源码。首轮审计误比较大小写不同的SHA字符串，第二轮未还原JSON的Single时间值；修正按原类型/字节哈希校验后通过，没有改运行报告、原生数据或门槛。两次失败与最终 `character-ground-v1-integrity3.log` 均保留。

- `character-motor-{debug,optimize}-cmc-ground-v1-final3-verification.json`：原九项场景。
- `character-floor-{debug,optimize}-cmc-ground-v1-final3-verification.json`：三频八角色地面专项及144项诊断。
- `character-ground-{debug,optimize}-cmc-ground-v1-final3-verification.json`：三频五角色台阶专项及32项诊断。
- `character-trajectory-{debug,optimize}-cmc-ground-v1-final3-verification.json`：三频实际同输入完整轨迹。
- `character-ground-v1-integrity.json`：原文件保护、探针/程序集及报告哈希、原生前缀保持、独立重算位置/速度/时间/法线/分类和轨迹门槛。

只关闭本批共用地面扫掠/StepUp 接入和明确的玩法矩阵。原生32项、旧地面三项接触点差、整个同输入物理轨迹仍未关闭；通用 Layer 的多 Group / default / self / None / Unlink、Shotgun/Feminine完整图、近景握持、材质、复杂地形、独立导出和性能也保持开放。没有本批 GPU、全量 managed、十分钟或人工全矩阵验收；音频、道具物理和头颈专项继续暂缓。
