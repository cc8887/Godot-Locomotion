# 原生线性投影与 Godot 实验接入

基于 `a399958`，直接在 `D:\GodotALS` / `main` 实现。用户的 P4 计划修改保持原样。
本批完成线性投影算法的原生数据对照，并增加可选的整链测试接入；**默认不启用投影**。
开启后锚点分离改善，但九项整链仅六项通过，较默认实现的八项通过存在退化。
实验物理链仍未接入普通角色，不能称 Ragdoll/Get-up/Pose Recovery 已完成。

## 原生参考与算法

新增 `-PhysicsProjectionOutput=<新的绝对路径>` 导出模式。
导出器通过 `FPBDJointConstraints::CreateSceneSolver()` 和虚函数接口，实际调用 Chaos DLL
中的 cached joint solver；不在导出器内重写待验证的投影公式，不保存 UE 资产。
这是隔离投影阶段的合成关节参考，不是完整物理场景轨迹；完整场景仍使用已有 144 组参考。

独立容器必须显式 `SetUseLinearSolver(true)`；只设置关节 `bUseLinearSolver` 不够。
初次导出遗漏此容器标记，结果没有被采纳；修正后重建、两次冷启动重导，最终数据为：

- `assets/config/v4_physics_projection_reference.json`，547807 bytes，262 组。
- SHA256：`45723123CC6F2F1F330D5F47BC574D807AA81E30701C2EEE6BFFC8FEBE6C01A4`。
- 30/60/120 Hz × 三轴 × 七个位移 × 有无旋转 × 有无父级累计修正，共 252 组。
- 另有 10 组控制：停用投影、线性 alpha=0/0.5、停用 teleport、静态子刚体。
- 运行时观察速度修正 alpha=0.1、指数旋转积分关闭。

`AlsLockedLinearProjection` 实现三轴线性锁定、角投影 alpha=0 的 cached SIMD 分支：
先缓存全链，投影时父级逆质量为零；子级使用最小主逆惯量构造球形惯量。
位置超差按连接器三个轴分别判断，严格大于 5 cm 才 teleport，不使用整体距离阈值。
三轴从同一个累计误差状态并行计算修正，然后累加位移和旋转。
最后一轮按累计位移/旋转增加速度，旋转用归一化 Euler 四元数增量。

静态子刚体需在 teleport 之前跳过：原生 `InitProjection()` 已令父级不可移动，
随后容器 `RequiresSolve()` 判定两端均不可动。不能只照着 `ApplyTeleports()` 函数体推断行为。
未实现其他线性运动模式、非零角投影、指数旋转积分；实验适配器对不支持的关节配置显式拒绝。
原生 cached solver 自身未实现 TeleportAngle，不能把该字段当作现成角度恢复功能。

参考比较覆盖父/子累计修正、线速度/角速度以及最终位置/旋转。
位移 delta 容差 2e-5 cm、旋转 delta 2e-6 rad，速度容差 2e-4 cm/s、2e-5 rad/s；
最终位置容差 3e-5 cm，四元数 dot 残差容差 1e-10。262 组全部通过。
另有五项 Core 语义回归，覆盖严格阈值、链式传播、停用、静态子级和热身后零托管分配。

## Godot 接入与限制

`AlsPhysicsJointSet.Project(dt)` 在 Jolt 本步结果上缓存所有刚体和关节，
按原关节顺序累计修正，最后统一写回动态刚体的位置、旋转和速度。
使用既有质量、已计算的主惯量和资产投影参数；长度从厘米转换为米。
不修改冻结刚体；自由 root–pelvis 不绑定。缓冲区在构造时分配并复用。

仅两个诊断场景通过 `--native-projection` 开启；普通角色与默认诊断行为未切换。
回放第零帧保留原始参考状态，不提前施加投影。
这是 **Jolt 接触求解后的外部修正**，没有嵌入同一物理岛的内部求解阶段；
正确的独立公式不代表完整接触/驱动/限位耦合已经等价。

## 十秒整链结果

保持原门槛：最大锚点分离 <0.2 m，末一秒最大角超限 <0.1 rad，
接触工况末一秒最大线速度 <0.2 m/s。没有增加迭代数、阻尼或放宽阈值。
两套角色合计 40 刚体、36 逻辑关节、72 个后端约束，开启 ALS 速度驱动和计算惯量。

| 工况 | Hz | 最大锚点 m | 末秒速度 m/s | 末秒超限 rad | 结果 |
| --- | ---: | ---: | ---: | ---: | --- |
| 普通落地 | 30 | 0.027338 | 0.102738 | 0.119804 | 角超限失败 |
| 普通落地 | 60 | 0.034946 | 0.084168 | 0.026684 | 通过 |
| 普通落地 | 120 | 0.011141 | 0.100293 | 0.017049 | 通过 |
| 无接触 | 30 | 0.013670 | 不作门槛 | 0.000588 | 通过 |
| 无接触 | 60 | 0.009541 | 不作门槛 | 0.000550 | 通过 |
| 无接触 | 120 | 0.004369 | 不作门槛 | 0.000449 | 通过 |
| 高速落地 | 30 | 0.102700 | 0.261725 | 0.094244 | 速度失败 |
| 高速落地 | 60 | 0.047447 | 0.402834 | 0.065383 | 速度失败 |
| 高速落地 | 120 | 0.018414 | 0.122215 | 0.010630 | 通过 |

30 Hz 高速锚点从上一批第 21 帧 0.290387 m 失败变为全程最大 0.102700 m，
但末秒速度不合格；普通 30 Hz 与高速 60 Hz 则从通过变成失败。
高速 30/60 Hz 瞬态最大角超限约 1.097/1.140 rad，仍不能交付观感验收。

18 次单关节十秒回归（3 频率 × 3 轴 × ±0.6 rad，每次两套角色）通过，
包含释放后句柄清空和原惯量恢复。不能用这些孤立对子通过代替整链通过。

144 组完整原生轨迹对照最大位置偏差从 0.011906 m 降至 0.001189 m，
但最大旋转偏差从 0.394426 rad 增至 0.449575 rad。
按每组最大旋转误差统计：21 组改善、93 组变差、30 组差值不超过 1e-6 rad。
仍为 `parity_asserted=false`。软限制向内释放第 3 帧跨零回归通过，
该用例最大旋转偏差 0.122111 rad，最终 0.016943 rad。

## 构建及可复测证据

UE 完整 Editor 目标构建和跨插件审计通过，输入指纹：
`F2EEE9E838C0343A6172E2F2B6F19F9FC191209642C34291FFE35AE6BA03C632`。
BuildId 为 `186ff094-6861-4ab2-95dd-e0889004ba00`。
两次最终冷导出退出 0、字节一致；普通 Editor 冷重启加载导出器反射类，标记成功并退出 0。
Editor 日志仍有既有引擎素材缺失提示，不能称无警告启动。
DataValidation 退出 0，0 error / 3 warning（旧 AI 组件、导航版本及相应资产提示）。
本批没有修改 UE 内容资产，也没有执行打包构建。

优化 Godot 构建通过，0 warning / 0 error。Release Core：2621 通过，
按既有方式排除旧 P5A Golden/TraceSchema。Release Import：2333 通过、1 项既有跳过。
最终构建的九项整链复跑与首轮每项预算输出完全一致，仍是六通过、三失败。

本地日志目录 `artifacts/physics-projection-20260921/`：
`native-linear*` 为最终原生导出，`native-projection*` 为未采纳的早期错误分支结果；
`chain-*` 为首轮整链，`final-*` 为最终构建整链；`pair-*`、`replay.*`、`release.*` 为回归。
失败日志保留，九项批运行退出码 3，不能计为全部通过。

```powershell
Set-Location D:/GodotALS
$projectionEngine = 'F:/下载/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
& $projectionEngine --headless --path D:/GodotALS scenes/tests/physics_joint_set_smoke.tscn -- --hz=30 --als-drives --high-drop --native-projection
& $projectionEngine --headless --path D:/GodotALS scenes/tests/physics_joint_reference_replay.tscn -- --computed-body-conditioning --native-projection --report=D:/GodotALS/artifacts/projection-replay-new.json
```

## 后续顺序

下一批优先对齐角限位/驱动的独立预测轴、关节局部质量惯量，以及投影与接触的执行阶段。
需用本批三个失败工况和 144 组参考反证改动，不能仅追求锚点误差更小。
同时检查外部写回与停用电机目标更新是否导致额外唤醒；目前未将其认定为已证实的根因。
整链稳定性与观感验收通过后才接普通角色 Ragdoll/Get-up/Pose Recovery。
Mantle、完整 ALS Camera 和最终十分钟性能预算仍在总清单内。
