# Lyra 胶囊地面查询与高度策略

本批将原 Shooter 的地面查询、边缘支撑和高度间隙策略接入普通玩家与 NPC 共用的移动服务。原 ALS 人物、69 raw / 81 logical 骨架和十四入口的 ItemAnimLayers 实例继续使用。地面专项通过；原生查询还有三个接触点精度差异，完整同输入物理轨迹仍未通过，目标保持 active。

此前同输入基线见 [物理轨迹](2026-10-03-lyra-character-trajectory.md)，资源和 Interface/Layer 边界见 [人物与接口](2026-10-03-lyra-als-interface-review.md)。本批没有重新导出动画，也没有修改原 UE 角色源码、项目配置或资产。

## 新原生证据

本批扩展自有 `LyraWholeMainOracle` 插件，使用本机 UE 5.8 完整 Editor 构建两版探针，分别进行只读命令行采集。没有改动引擎。`package-cmc-floor-v1` 的初版44项记录保留；最终 `package-cmc-floor-v2` 与 `cmc60-floor-v2` 提供144项真实 `K2_ComputeFloorDist` / `K2_FindFloor` 查询。

矩阵为站立/蹲伏半高90/65厘米、24个位置、Compute/FindWalking/FindFalling三种查询。位置包含间隙区间两侧、地面缺失、墙边、初始穿透和地面箱体边缘。三个 Provider 的查询与 CDO 完全一致；此前250组地面、175组空中计算，以及各 Provider 前60帧真实物理输入记录保持逐项相同。两次 UE 采集退出0、没有保存资产；旧 Condition/PostLoad 警告保留，不称 UE 零警告。

新增不可变文件：

- `assets/generated/lyra_als/character_floor_v1.json`，SHA256 `33AE82F1ADEE0B9F315B09B531441885F6FBFF963FD3964EA66505B0C8B3ACA2`。
- `artifacts/lyra-analysis/character-floor-v1-reference.json`，SHA256 `98E07ACA1A37E951EA27E11948460013FE59A903570FEE17A4431A818697E18E`。

生成器 `tools/export_lyra_character_floor.py` 拒绝覆盖已有文件。运行资产被 Git 忽略，依赖当前 `character_motor_v2.json` 的字节哈希；仅有源码的检出不能直接运行 Lyra Demo。

原配置为圆底胶囊、每帧检查地面、允许站立和蹲伏走出边缘，PerchRadiusThreshold=6cm、PerchAdditionalHeight=40cm。地面间隙常量实际导出值来自 `UCharacterMovementComponent`；1.9–2.4cm区间内保持高度，超界才调整到平均2.15cm。

## 普通运行接入

`LyraCharacterFloorProbe` 使用真实 Godot/Jolt 查询执行原短胶囊收缩、边缘重试、射线回退和 Perch 判定。初始穿透与无有效阻挡区分，LineTrace 保留原 Sweep 的时间、位置和接触点语义。实际 CastMotion 的毫米级括区用真实 GetRestInfo 收窄，未把原生结果写入查询或角色。

原 UE Box 使用直角几何。Godot Box 的默认 margin 会圆化边缘，因此原生查询及同输入轨迹夹具将 BoxShape3D.Margin 设为0。运行中其它场景的实际几何保留。对初始穿透的直角箱体，按实际几何与 Chaos CoreSegment 的支撑端点规则统一接触点；其它形状仍使用实际物理查询。

共用服务现在持有独立的 Grounded 状态，由真实地面/边缘支撑和着陆接触决定，并供蹲伏、跳跃、玩家输入和 Main 观察共同使用。一次 MoveAndSlide 后，同一物理 owner 执行实际高度安全扫掠；阻挡会限制高度调整。最终物理凭据包含 Grounded、查询结果、高度修正及包含修正的总位移。动画取消/重试复用已发布凭据，未再执行高度调整。

初始化查询先于蹲伏，恢复原起点中心92cm的区间内高度。GroundInfo 在 Walking 时使用当前真实地面结果；空中从胶囊中心向下追踪100000cm加半高。新增悬空夹具发现旧未命中分支返回 double.MaxValue、转 float 后成为无穷大，现按原 Lyra 默认 GroundTraceDistance 返回有限100000cm。

首次尝试把空中切为 Godot Floating 模式，60Hz最大竖直差扩大到约12.23cm。精确4.7.2源码显示 Floating 首次滑动把投影方向归一化并保留剩余长度；该模式切换已撤回。原 MoveAndSlide 的空中碰撞剩余时间积分仍待替换。

## 查询差异

144项实际查询首轮有36项差异。修正夹具圆角后降为9项，统一真实箱体支撑点后降为3项；原比较门槛保留，报告与进程继续明确失败。

最终三项均为蹲伏胶囊在原地面边缘外10cm位置的 Compute/FindWalking/FindFalling：原接触点为 `(10000.009765625, 0, -0.0029506683349609375)` cm，Godot点为实际箱体边界 `(10000,0,0)` cm，欧氏差 `0.0102016604cm`，超原 `0.01cm` 门槛。没有用录制点替换实际点，也没有放宽门槛。所有分类标志、可走法线均一致，最大距离差 `0.0015022755cm`、时间比例差 `2.9742718e-5`；这不代表完整查询精度验收通过。无有效地面的深穿透原始点/法线也保留在诊断中。

## 实际玩法专项

新增八角色场景直接走普通共用服务：六种初始地面间隙、屋顶阻挡和边缘支撑不足。三频各0.6秒，站立→蹲伏→起身，实际检查区间内高度保持、区间外回平均高度、胶囊底面保持、上移遇屋顶停止、边缘拒绝后落下，以及未命中地面的有限 GroundDistance。每帧完整动画取消重试，不重复物理步骤。

原六角色物理场景另逐帧检查服务、物理凭据和 Main 的 Grounded 一致，并检查平地实际间隙在原区间内；原起跳中点/帧末速度、净空、撞墙切向保留等断言保留。

该专项属于 Godot 中的实际策略与阻挡验证；屋顶等新增玩法输入没有新增完整 UE 连续轨迹对照。斜坡、StepUp、双墙、移动平台和复杂地形仍未验收。

## 验证与开放项

最终标签 `cmc-floor-v1-final`，验证工具为 `scripts/verify-lyra-character-floor.ps1`、既有 motor/trajectory 脚本和 `tools/verify_lyra_character_floor.py`。查询诊断及完整轨迹不满足门槛时均退出1；数据采集完成与比较通过分别报告。

Debug 与实际 ExportRelease Optimize 各通过九项既有场景和三项新增地面专项，共24个进程退出0、没有Godot ERROR/WARNING。既有三频六角色物理、三频普通十角色、60Hz Root/Warp/Emote全部保留；新增三频八角色专项共2016次实际移动及2016次动画重试。物理/普通、Warp/Emote和新增地面报告在两构建中逐项相同。

两个构建的144项查询均保留相同三项失败；完整三频轨迹各1680次移动和重试也相同，八个诊断进程正常保存数据、没有Godot ERROR/WARNING，但因原门槛失败退出1。这些诊断没有计入24项通过数。

最终构建0错误0警告；三轮Optimize验证各自逐文件恢复六个Debug文件。独立审计 `artifacts/lyra-analysis/character-floor-v1-integrity.json` 通过，逐帧重新计算查询与轨迹误差，核对当前两种程序集、探针源/包镜像与原生物理前缀。原867 JSON、709 UE包、9配置及3份角色源码哈希保持；原V2配置、425组内核采集和旧夹具保持。旧审计的源码/程序集哈希属于各自历史，不用于声称当前构建相同。首轮审计误按UTF8读取PowerShell生成的UTF16场景日志，修正BOM识别后通过，原失败日志保留。

额外Debug真实渲染在RTX5080上运行普通十角色480物理帧，退出0且没有Godot ERROR/WARNING，七张FramePostDraw截图及帧身份完整。七图合览，另检查站立、蹲伏、跳跃原图，没有看到整体姿态破坏。当前为远景检查，近景握持、足部毫米级视觉精度及人工观感仍未验收。补充哈希记录为 `character-floor-v1-render-integrity.json`，同时确认当前源文件及六Debug文件保持。没有重跑managed全量或十分钟性能矩阵。

![普通十角色七状态](../../artifacts/lyra-analysis/cmc-floor-v1-render60-contact.png)

同输入诊断保留原位置0.01cm、速度0.001cm/s门槛，三频各八秒，完整结果如下。蹲伏标志全同，加速度差为零，每频仍有一帧落地状态差。

| Hz | 比较帧 / 未通过帧 | 最初四秒通过帧 | 最大位置差 cm | 最大平面差 cm | 最大竖直差 cm | 最大速度差 cm/s |
|---|---:|---:|---:|---:|---:|---:|
| 30 | 240 / 120 | 120 / 120 | 14.72192225 | 0.70544593 | 14.72187061 | 480.00064607 |
| 60 | 480 / 240 | 240 / 240 | 7.79529588 | 0.62472699 | 7.79499180 | 496.33494883 |
| 120 | 960 / 481 | 479 / 480 | 3.87096972 | 0.17882980 | 3.87062848 | 496.33471313 |

恢复初始地面间隙后，原先1680帧全部失败的诊断现在有839帧通过；不称完整误差收敛，最大空中竖直差反而增加。120Hz第479帧首次地面撞墙已经出现差异：原速度约393.798cm/s、实际速度投影为零，原间隙因地面碰撞流程变为2.15cm、当前仍保持2cm。本机 `PhysWalking` 在碰撞后按实际位移/时间重建速度；原 MoveAlongFloor/StepUp 流程尚未迁移。该帧与后续空中碰撞、顶点分步和落地时间退款继续开放。

编译类型错误、默认圆角差异、Floating 轨迹、悬空动画失败均保留日志。一次初步 Debug 矩阵与构建重叠，已中断并另存 invalidated 记录，排除该轮的部分结果。

后续继续原命中回退、空中碰撞剩余时间、顶点与落地时间分步，再 StepUp/多接触与完整地形。通用多Group、无组调用点实例、default/self/Unlink、近景握持以及其余开放项保持；本批不关闭完整移植目标。
