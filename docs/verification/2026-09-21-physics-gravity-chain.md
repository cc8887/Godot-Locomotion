# 重力、外力入口与真实资产整链接触

本批在主目录 `D:\GodotALS` / `main` 推进 Core 物理后端。
两套导出角色的关节、身体和真实碰撞形状现可在重力下共同求解，
普通及高速落地六项探针通过。普通角色 Ragdoll 尚未启用。

## 积分与输入契约

对照本地 UE 5.9：

- `Engine/Source/Runtime/Experimental/Chaos/Private/Chaos/PBDRigidsEvolutionGBF.cpp`，
  `Integrate` 中先收集 ForceRules，再累计 acceleration、impulse velocity，最后施加 drag。
  `bApplyDragBeforeVelocityIntegration` 默认 false；true 时先 drag 再累计输入。
- `Engine/Source/Runtime/Experimental/Chaos/Public/Chaos/PerParticleGravity.h`，
  根据 GravityEnabled 将重力组向量加到 acceleration；加速度不再次乘质量。

`AlsBodyStepForces` 显式接收世界坐标加速度、角加速度、线/角冲量速度增量，单位为
cm/s²、rad/s²、cm/s、rad/s。它不是未解析的力/力矩；调用方需先转换，
连续加速度每步提供，单次冲量只提供一次，island 不暗中缓存输入。

`AlsJointIsland.Step` 接收当前重力、可选的一身体一条输入、contacts 和 drag 顺序。
只积分动态身体，遵循各身体 GravityEnabled；固定身体不移动。
先验证输入，再 Gather、共同迭代、Stage 和统一发布；非有限数或速度存储溢出不发布部分状态。
保留 `StepForceFree`，明确传入零重力和零外力。
维持现有 float 速度/actor quaternion 存储边界以及 double COM 积分。
尚无陀螺力矩、速度上限、运动 kinematic、CCD 或睡眠；本批没有新增 UE 原生外力轨迹导出，
证据为源码顺序、解析测试、已有原生回放和真实 Godot 落地，不声称新外力路径已经原生逐帧对照。

## 资产与 Godot 接入

`AlsPhysicsContactShapes` 从已有导出数据创建球、盒、胶囊和凸包。
查询形状使用 native `(Y,Z,-X)` 到 Godot 世界的映射，不能混用 FBX 骨架 `(X,-Y,Z)` 基底。
盒体尺寸相应置换；native Z 轴胶囊直接对应 Godot Y；凸包 scale 烘入顶点，形状变换保持刚性。
保留 CollisionEnabled 和资产 disabled body-pair table，不通过关闭自碰撞换取通过。
当前碰撞模式和非零 RestOffset 等不支持项显式拒绝。

代理 host 更名为 `AlsCoreJointHost`，允许资产身体前缀后附加环境身体。
Core 唯一负责积分，Godot 身体保持 Freeze + Static 和双向 collision filter 为零；
只把最终 COM 变换发布到代理，再检查骨架 local pose 重建。
新增接触不通过 Jolt 动态积分后再改写姿态。

探针使用两套角色独立 query space：总计 40 个资产身体、2 个地面身体、36 个约束、45 个查询形状。
角色彼此不碰撞；该探针不证明多角色交互已经实现。
使用导出默认材质与同材质地面，static friction 按原生 max(dynamic, static) 解析为约 0.7，
dynamic/velocity friction 约 0.7、restitution 约 0.3。检查同材质、默认 combine、gravity group 0、
无 gyroscopic torque，不把它伪装成通用材质组合器。

## 整链中发现并修复的校验错误

首次三个频率均被 `Contact basis must be orthonormal` 中止。
60 Hz 的诊断在第 77 帧记录 N·U = 1.2000732e-5。
UE `PBDCollisionContainerSolver.cpp` 的 Gather 使用 float：
`sliding = velocity - dot(velocity, normal) * normal`，随后归一化 sliding 为 U、cross(N,U) 为 V。
近法向速度的相减会放大舍入残差，因此额外要求严格 N·U < 1e-5 超出了这段原生代码的保证。

保留原生计算，不重新正交化/平滑轴。`GatherGeometry` 使用内部构造路径，仍检查有限数、
N/U 单位长度及 V=cross(N,U)，但不额外拒绝此种消减残差；直接调用接触行的公开 API
继续要求正交基。新增近法向速度测试覆盖旧断言确实失败、Gather 接受且求解有限、
公开 API 仍拒绝非正交输入。已有原生几何/接触阶段参考回放继续通过。

首次该测试把 .NET Vector3.Normalize 的除法与原生倒数乘法作逐位相等断言，差 1 ULP；
改为 1e-7 向量距离检查，未修改被验证的运行时计算。相关失败日志保留。

## Godot 十秒落地结果

场景 `scenes/tests/physics_core_joint_replay.tscn`，Godot 4.7.2 Mono `ed1daf0bf`。
`--chains --drop` 将整个资产姿态旋转 0.35 rad，并抬高 100 cm，初始水平速度 100 cm/s。
加 `--high-drop` 改为抬高 300 cm、初始下落速度 1000 cm/s。
重力为 -980 cm/s²，8 次位置/2 次速度迭代，保留导出惯量、阻尼和关节配置，未开启睡眠。
地面是 40×1×40 m 盒体，顶面为 Z=0。这个厚度与旧 Jolt 场景未必相同，
因此新路径通过不能关闭旧 Jolt 故障，更不能证明 CCD 已实现。

| 模式 / Hz | 最大锚点偏差 cm | 末秒最大线速度 cm/s | 末秒最大角速度 rad/s | 末秒限位超出 rad | 全程限位超出 rad |
| --- | --- | --- | --- | --- | --- |
| 普通 30 | 1.484643 | 7.021260 | 1.245426 | 0.056217 | 0.336728 |
| 普通 60 | 0.930077 | 13.838384 | 0.735960 | 0.072447 | 0.250048 |
| 普通 120 | 0.376753 | 2.502304 | 0.272859 | 0.027884 | 0.124898 |
| 高速 30 | 4.840667 | 12.815650 | 0.854553 | 0.080834 | 0.726441 |
| 高速 60 | 1.739599 | 7.323903 | 0.538181 | 0.057610 | 0.506028 |
| 高速 120 | 0.914909 | 0.816971 | 0.069661 | 0.067435 | 0.263509 |

门槛：锚点始终 <10 cm、身体原点 Z>-25 cm（粗略穿地检查，不是表面穿透深度）、
第二秒 pelvis 明显下落（排除被 free root 锚住）、9–10 秒每帧每个动态身体的最大线速度 <20 cm/s、
角限位超出 <0.1 rad。角速度仅记录，没有据此宣称静止；表中数值仍提示需睡眠和观感检查。
每帧检查骨架和冻结代理变换，一致性最大位置差不超过 1.458e-6 m。
六轮各有数万接触点，进程退出 0，最终日志无 Godot ERROR/WARNING。

命令模板（report 必须指定新的绝对路径）：

```powershell
& 'F:/下载/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path D:/GodotALS res://scenes/tests/physics_core_joint_replay.tscn -- --chains --drop --hz=60 --report=D:/GodotALS/artifacts/physics-gravity-chain-20260921/drop-60-final.json
```

## 回归与产物

新增 7 项 forces 测试和 1 项近法向 Gather 测试。覆盖 drag 两种顺序、30/60/120 Hz 半隐式
自由落体、固定身体/重力开关、冲量不残留、无效长度/非有限值/溢出回滚，以及连续 2048 步零分配。
首次测试编译发现 `Math` 被 Core.Math 命名空间遮蔽，改为 System.Math；保留初次失败日志。

Godot 优化构建通过，0 warning / 0 error。
原有 144 组原生关节对子 × 12 帧通过，最大位置 1.020660e-6 cm、旋转 3.576279e-7 rad、
线速度 4.722374e-5 cm/s、角速度 8.397188e-6 rad/s，数值与前批一致。
原有 60 Hz 三场景真实接触探针及六项几何/失效/主线程检查通过，动态对动量差 0。
Import Release 固定 JIT 全量 **2359 通过、1 既有条件跳过**。
Core 全量首轮 **2682 通过、1 失败**：未改动的 Montage 重复替换零分配测试测得 6216 字节；
同一固定 JIT 配置下独立复跑及全量复跑通过，最终 **2683 通过**，原因尚未定位。
Core 两轮保留既有 P5A Golden/TraceSchema 过滤；两套回归使用 Release，
DOTNET_TieredCompilation=0、COMPlus_TieredCompilation=0，没有为消除失败改动旧测试。

全部产物在 `artifacts/physics-gravity-chain-20260921/`：
普通最终 `drop-{30,60,120}-final.log/json`；高速 `high-{30,60,120}.log/json`；
`pairs.log/json`、`contact-regression.log/json`、`godot-build-guards.log`、
`core-full.log/core.trx`、`core-allocation-repeat.log`、`core-full-repeat.log/core-repeat.trx`、
`import-full.log/import.trx`，以及早期编译/正交断言失败日志。
高速之后增加的仅为材质/重力组输入拒绝检查；最终普通三频率复跑验证这些资产满足契约。
本批没有改 UE 插件、重导 JSON、改普通入口或覆盖用户 P4 规划修改。

## 下一步与边界

先推进睡眠/唤醒与持续接触生命周期，使落地后可靠静止；继续补原生完整重力/接触轨迹对照、
移动 kinematic、真实场景接入、CCD 和性能结构，再接普通角色 Ragdoll、Get-up/Pose Recovery。
窄相仍是 Jolt，不能声称完整 Chaos 1:1；无接触的原生阶段通过不能外推为落地轨迹等价。
本批是 headless 数值/变换验证，没有多帧画面验收或完整 Ragdoll gameplay。
Mantle、完整 Camera 和最终十分钟性能预算仍在总目标中，均未以本批探针代替。
