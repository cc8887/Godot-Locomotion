# 软限制与姿态驱动独立通道

基于 `c943582`，在 `D:\GodotALS` / `main` 继续物理前置实现，用户的 P4 计划修改保持原样。
本批普通落地三个频率均通过，60/120 Hz 高速落地也通过；30 Hz 高速落地仍失败。
实验关节尚未接入普通角色，Ragdoll/Get-up/Pose Recovery 尚未完成。

## 最终实现

删除 `AlsPhysicsJointSet` 中将驱动刚度与限位刚度相加、用两者加权目标角度的做法。
每个逻辑关节现在拥有两个独立的 Jolt 6DOF 约束：

1. 限位通道：保留原有线性锚点、硬角限位，并承担激活后的软角限位电机。
2. 驱动通道：关闭所有硬线性/角限位，仅承担姿态驱动电机，目标直接取原生资产四元数。

两个电机分别保有系数、目标、启用状态和后端累计冲量；改变驱动力不再改写软限制。
限位与驱动仍使用上批的预测角阻尼和时间步容差。每个关节依次创建限位、驱动约束；
限位电机在该 Jolt 约束内部先于点约束求解。但这不保证整个多关节岛的迭代次序与 UE 相同。
全自由 root–pelvis 仍不绑定，因此是 40 刚体、36 个逻辑关节、72 个后端约束。
释放时每个独立 RID 只释放一次，清空句柄，并按原有契约恢复刚体惯量；重复释放安全。

开始时曾将硬约束、软限制、驱动全部分成三个后端约束；该中间版 60 Hz 普通落地
第 59 帧发生 0.200435 m 锚点分离。最终将软限制与锚点放回同一约束，同时保持驱动独立，
恢复它们在同一约束内的耦合。没有用增加迭代次数、提高阻尼或放宽阈值换取通过。
三通道中间版结果保留为 `split-*`，不算最终实现。

新增 Godot `--assert-independent-channels` 实测探针：在同时存在限位与驱动的姿态，
依次设置驱动为 `(75,1.5)`、`(37500,0)`、`(0,0)`，直接回读后端参数。
断言驱动真实变化/停用，且限位的刚度、阻尼、目标和启用标志逐项不变，两个 RID 不同。
两套角色都通过。此探针会临时变更驱动并恢复，不进入普通每帧链路。

回读检查还验证驱动通道没有重复硬约束，限位通道没有丢失资产配置的硬约束。
144 组轨迹回放在每个求解前更新后都检查这些配置。十秒场景检查后端约束数量，
成功释放时断言句柄清空且原始惯量恢复；逐秒日志分别输出 drive_rows 与 limit_rows。

## 原生与 Jolt 依据

UE 5.9 本地 `Chaos/Private/Chaos/Joint/PBDJointCachedSolverGaussSeidel.cpp`：
`ApplyConstraints`、`SolveRotationConstraintSoft`、`ApplyAxisRotationDrive`。
限位与驱动各自拥有独立的系数和 lambda，不应合成一个加权目标。
实际开启 `bSolvePositionLast` 时，顺序是角约束、位置约束、角驱动、位置驱动，
不能仅凭设置名称推断驱动一定早于所有位置约束。

Godot 官方 4.7.2 Mono 固定提交 `ed1daf0bf`：

- [SixDOFConstraint.cpp](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/thirdparty/jolt_physics/Jolt/Physics/Constraints/SixDOFConstraint.cpp)：一个 6DOF 内电机先于角/位置硬约束；每个电机保留自己的 warm-start 冲量。
- [jolt_joint_3d.cpp](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/jolt_physics/joints/jolt_joint_3d.cpp)：公开的 solver priority 在这个后端被忽略，因此没有添加无效的优先级设置。
- [jolt_generic_6dof_joint_3d.cpp](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/jolt_physics/joints/jolt_generic_6dof_joint_3d.cpp)：硬限位开关和电机状态分开配置，目标角使用负 ZYX Euler 运输。

也更正上批报告对零刚度的解释：`SixDOFConstraint` 在位置电机入口检查
`HasStiffness()`，零刚度会停用该电机，并非进入底层 SpringPart 的硬约束分支。
纯阻尼拒绝保留，改为逐通道检查，不能靠另一通道的正刚度掩盖不支持的配置。
`--assert-damping-only-rejected` 双角色实测仍通过。

查阅遵循 agent-reach 的 GitHub 路径；gh 未登录，使用固定提交的公开源码页面替代读取。
没有修改登录状态、下载新项目副本或改变 UE 插件。

## 最终回归

仍用既有十秒门槛：全程锚点分离 <0.2 m；最后完整一秒最大超限 <0.1 rad；
接触落地最后一秒最大线速度 <0.2 m/s。无接触自由落体不要求静止。
均使用从原生几何/拓扑计算的刚体惯量与 ALS 速度驱动。

| 工况 | Hz | 全程最大锚点分离 m | 末秒最大线速度 m/s | 末秒最大超限 rad | 结果 |
| --- | ---: | ---: | ---: | ---: | --- |
| 普通落地 | 30 | 0.110305 | 0.131961 | 0.096568 | 通过 |
| 普通落地 | 60 | 0.094716 | 0.083103 | 0.039997 | 通过 |
| 普通落地 | 120 | 0.022991 | 0.112071 | 0.011910 | 通过 |
| 无接触自由落体 | 30 | 0.030063 | 不作门槛 | 0.006650 | 通过 |
| 无接触自由落体 | 60 | 0.017137 | 不作门槛 | 0.000275 | 通过 |
| 无接触自由落体 | 120 | 0.010502 | 不作门槛 | 0.000340 | 通过 |
| 十米高速倾斜落地 | 30 | 0.290387 | 未到末秒 | 未到末秒 | 第 21 帧锚点失败 |
| 十米高速倾斜落地 | 60 | 0.134023 | 0.141210 | 0.056761 | 通过 |
| 十米高速倾斜落地 | 120 | 0.067181 | 0.063547 | 0.021475 | 通过 |

相对上批，普通 60/120 Hz、高速 60/120 Hz 从失败变为通过。
30 Hz 普通落地末秒超限从 0.055520 增至 0.096568 rad，距离门槛很近；独立复跑取得同样结果，
这仍不能证明对所有初始姿态都有足够余量。30 Hz 无接触末秒超限也从 0.000393 增至 0.006650 rad。
60 Hz 普通/高速落地全程最大角超限分别为 0.880568/0.927586 rad，仍有明显瞬态偏差；
末秒收敛通过不等于全过程观感已经可交付。

144 组原生轨迹对照两次完成，其中最终一次加入每步通道回读；结果一致，退出 0。
最大旋转偏差从 0.432737 降到 0.394426 rad，86 组改善、6 组变差、52 组差值不超过 1e-6 rad。
最大位置偏差从 0.011692 增至 0.011906 m。仍明确 `parity_asserted=false`。
软限制向内释放的第 3 帧跨零回归通过；该用例最大旋转偏差 0.125327、最终 0.017847 rad。

18 次十秒单关节回归（30/60/120 Hz × 三轴 × ±0.6 rad，每次两套角色）全部通过，
末秒最大速度和超限均为 0，释放后的句柄清空与原惯量恢复检查通过。
30 Hz 矩阵运行命令的总退出码为 1，来自已列明的高速落地失败，不能把整个批命令记成通过。

优化 Godot 构建通过，0 warning / 0 error。Core/Import 源码和数据均未改，本批主要验证
真实 Godot 后端运输、求解及生命周期，没有把上批 Core/Import 的通过数冒充新跑结果。
没有进行 UE 资产重导出、UE 重编译或普通角色人工观感验收。

本地证据目录 `artifacts/physics-independent-channels-20260921/`：
`two-channel-chain-*`、`final-chain-30-repeat.log`、`final-flight-*`、`final-high-*`、
`final-replay.*`、`final-release.*`、`independent-channels.log`、`damping-only-rejected.log`、`pair-*.log`。
`split-*` 是被替换的三通道中间版，保留其失败记录。

```powershell
Set-Location D:/GodotALS
$godotAlsEngine = 'F:/下载/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
& $godotAlsEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --assert-independent-channels
& $godotAlsEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --hz=60 --als-drives --high-drop
& $godotAlsEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --hz=30 --als-drives --high-drop
```

## 剩余缺口

独立后端电机不等于完整移植 Chaos 约束行。Jolt 仍在速度求解器中使用当前姿态、
自己的四元数误差与 warm start；限位和驱动仍取同一个当前子连接器轴，
没有完整使用原生预测后的各自轴和关节局部惯量，也没有原生的逐迭代限位再判断。
此外仍缺原生投影与接触耦合。

下一步优先针对 30 Hz 高速落地的锚点分离，对齐原生线性投影和关节局部质量/惯量；
同时保留 144 组轨迹与其他八个整链工况作为回归。还需检查停用限位通道的目标更新是否
造成多余的刚体唤醒。不能把九项中八项通过改写成整链门槛全部通过。
随后继续 Ragdoll/Get-up/Pose Recovery；Mantle、完整 ALS Camera、最终十分钟性能预算仍在总清单中。
