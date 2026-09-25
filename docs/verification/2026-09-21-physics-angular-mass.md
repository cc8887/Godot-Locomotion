# 预测角轴、局部惯量系数与睡眠修复

基于 `826a1bb`，继续在 `${env:GODOT_ALS_ROOT}` / `main` 推进，保留用户未提交的 P4 计划修改。
本批修复停用电机与外部投影的睡眠行为，并增加原生角系数计算的实验接入。
组合实验九项整链七项通过，默认实现仍是八项通过；均未完成整链验收。
Ragdoll/Get-up/Pose Recovery 仍未接入普通角色。

## 实现

新增诊断参数 `--native-angular-mass`，**默认关闭**。该开关只改变传给 Jolt 电机的系数计算：

1. 从当前质量与已做刚体级调节的主惯量计算逆量；冻结体取零。
2. 当资产启用 MassConditioning 时，调用已有原生对照通过的 `AlsJointMassConditioning`。
   MinParentMassRatio=0.2f、MaxInertiaRatio=5 作为具名参考常量，与 144 个原生场景的设置逐项绑定。
3. 按阻尼后的角速度预测刚体与连接器姿态，以预测质量坐标系构造世界逆惯量。
4. 驱动取预测子连接器轴；Twist 限位取预测 twist 轴，两个摆动限位取去掉 twist 后的 pyramid 轴。
5. 分别计算驱动与限位的有效惯量，转换 Acceleration 模式 K/C；Force 模式仍按 cm²→m² 换算。

局部调节值不写回共享刚体，不改变另一个关节或碰撞接触实际使用的刚体质量/惯量。
**这里没有替换 Jolt 内部的约束轴、有效质量、累计冲量和两端刚体响应。**
尤其是原生的局部惯量既参与系数计算，也参与实际修正；目前只接入前者，不能称完整局部质量适配。
这正是保持实验开关、不作为默认修复的原因。原生轴和质量计算正确，不保证这种系数运输整体等价。

本批同时实施两个独立的睡眠修复：

- 完全停用的电机只初始化一次目标，之后保留目标参数；重新启用时先刷新目标再求解。
  Godot/Jolt 修改 equilibrium 会唤醒两端，即使电机未启用，因此不应每帧更新无效目标。
- 若整个实验刚体集合的动态体均已睡眠，外部投影不写回变换和速度。
  这里读取后端真实睡眠状态，不按自设速度阈值强制休眠，也不修改接触或验收阈值。

新增 `--assert-dormant-targets` 真实后端探针：验证停用时参数不变且保持睡眠，
全睡眠状态下投影不移动刚体，限位重新激活时刷新目标并唤醒。两套角色通过。
初版探针在第一次物理回调检查睡眠，彼时 Jolt 刚体尚在 pending 队列，误报重新激活不唤醒；
最终等待一个完整后端步骤后再检查。早期失败日志保留，不作为最终结果。

## 依据

UE 5.9 本地源码 `Chaos/Joint/PBDJointCachedSolverGaussSeidel.cpp`：
`Init()` 的局部质量调节，`InitRotationConstraintsSimd()` 的 pyramid 轴，
`InitSwingTwistDrives()` 的子连接器轴，`SolveRotationConstraintDelta()` 的实际响应。
驱动软行与限位的旋转修正方式也不完全相同，不能只统一写一个角弹簧目标。

通过 agent-reach 的 GitHub 路径查阅官方 Godot 固定提交 `ed1daf0bf`；
gh 未登录，使用公开源码页面读取，没有新建项目副本或改变登录状态。

- [6DOF 目标更新](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/jolt_physics/joints/jolt_generic_6dof_joint_3d.cpp)：目标及弹簧参数更新均会请求唤醒。
- [刚体写回](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/jolt_physics/objects/jolt_body_3d.cpp)：变换/速度更新也请求唤醒；睡眠查询返回实际 active 状态。
- [空间队列](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/jolt_physics/spaces/jolt_space_3d.cpp)：新建刚体先进入 pending 队列，再加入物理世界，不能在首次回调假定已经完成插入。

这些源码影响了睡眠修复及探针时序。不能据此推断额外唤醒就是当前整链抖动的根因：
修复前后 60 Hz 高速工况预算相同，该问题仍然存在。

## 完整矩阵

组合方案同时开启 `--native-angular-mass --native-projection`。
保持既有十秒门槛：全程锚点 <0.2 m，末秒角超限 <0.1 rad，接触工况末秒速度 <0.2 m/s。
仍使用原始资产、计算惯量和 ALS 速度驱动，没有改阻尼、迭代次数、地面或阈值。

| 工况 | Hz | 最大锚点 m | 末秒速度 m/s | 末秒超限 rad | 结果 |
| --- | ---: | ---: | ---: | ---: | --- |
| 普通落地 | 30 | 0.027514 | 0.108523 | 0.039886 | 通过 |
| 普通落地 | 60 | 0.033641 | 0.443923 | 0.095346 | 速度失败 |
| 普通落地 | 120 | 0.010026 | 0.149593 | 0.017649 | 通过 |
| 无接触 | 30 | 0.013685 | 不作门槛 | 0.039790 | 通过 |
| 无接触 | 60 | 0.009541 | 不作门槛 | 0.000663 | 通过 |
| 无接触 | 120 | 0.004369 | 不作门槛 | 0.000525 | 通过 |
| 高速落地 | 30 | 0.095589 | 0.180139 | 0.070164 | 通过 |
| 高速落地 | 60 | 0.044172 | 0.333732 | 0.067906 | 速度失败 |
| 高速落地 | 120 | 0.018259 | 0.111926 | 0.009841 | 通过 |

相对上一批单独投影的六项通过，30 Hz 普通与高速从失败变为通过，但 60 Hz 普通从通过变成失败。
30 Hz 无接触末秒角超限从 0.000588 增至 0.039790 rad；尽管仍通过，退化不能忽略。
60 Hz 高速瞬态最大角超限约 1.140 rad，尚未做普通角色观感验收。
单独启用角系数、不开投影的隔离结果同样不适合默认启用：普通 30 Hz 角超限失败，
高速 30 Hz 第 21 帧锚点 0.295118 m 失败，高速 60 Hz 通过。

默认配置重跑九项，仍为八项通过、30 Hz 高速第 21 帧锚点 0.290387 m 失败。
默认 144 组轨迹 JSON 与 `a399958` 结果字节一致，SHA256：
`5ADC61CA2F0E5E418CE2DD96ABE90CEBFC7BC609338A160B3B059B937B17F1D5`。

组合 144 组轨迹相对单独投影：5 组旋转改善、4 组变差、135 组差值不超过 1e-6 rad；
最大旋转偏差仍为 0.449575 rad，最大位置偏差仍为 0.001189 m，`parity_asserted=false`。
组合 JSON SHA256：`82414C1AD6D81349077D2A80A3208F1D382934B516EDBFB601F6F303D3664EB3`。
不能把最大值相同说成 144 组完全未变。

## 回归与交付边界

优化 Godot 构建 0 warning / 0 error。Release 针对性 Core 24 项、Import 45 项通过，
其中覆盖已有 912 组角度/轴、152 组质量调节、262 组投影原生参考。
本批未重新宣称上一批全量 2621/2333 项为新跑结果；未修改 UE 插件、未重新构建 UE 或重导资产。
独立驱动通道、纯阻尼拒绝、睡眠探针、软限位第 3 帧跨零均通过。
18 次十秒单关节矩阵全部通过，包括三个频率、三个轴、正负扰动和两套角色，检查句柄释放与原惯量恢复。

本地证据 `artifacts/physics-angular-mass-20260921/`：`final-false-*` 是默认，
`final-true-*` 是组合方案；`chain-*` 为早期隔离结果，`dormant-*` 为睡眠探针，
`pair-*` 为单关节，`final-replay-*` 与 `final-release.*` 为轨迹回归。
两个完整矩阵进程退出码分别为 1、2，必须保留对应失败。

```powershell
Set-Location D:/GodotALS
$jointMassEngine = 'F:/下载/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
& $jointMassEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --assert-dormant-targets --native-angular-mass
& $jointMassEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --hz=30 --als-drives --high-drop --native-angular-mass --native-projection
& $jointMassEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --hz=60 --als-drives --high-drop --native-angular-mass --native-projection
```

## 下一步

停止把“原生系数 + 通用 6DOF 电机”等同于原生约束。下一批应实现/验证约束行层面的接口：
独立预测轴、每端局部惯量修正、限位/驱动各自累计量和每步重置，以及与接触共同求解的阶段。
先检查固定 Godot/Jolt 接口能否完整表达这些数据；若需要原生模块，源码和可重现构建脚本
仍放主仓库管理，不再开一个独立 Demo 项目。严禁将局部调节后的惯量直接覆盖共享刚体。
之后以两个 60 Hz 失败工况、30 Hz 已改善工况及 144 组轨迹验证，不按频率切换调参方案。
整链和观感通过后再继续普通角色 Ragdoll/Get-up/Pose Recovery；Mantle、完整 ALS Camera、
最终十分钟性能预算仍属总目标，未完成。
