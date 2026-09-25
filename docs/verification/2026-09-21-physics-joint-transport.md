# Godot 关节绑定与弹簧适配：单关节通过，整链落地未验收

本批基于 `721e23d`，直接在 `${env:GODOT_ALS_ROOT}` / `main` 实现，没有建立项目副本。
用户未提交的 P4 计划修改保留，未纳入本批。

## 当前实现

`AlsPhysicsJointSet` 将原生父/子约束坐标系转换到刚体的主惯量/质心坐标系，
创建 Godot Generic6DOF 关节。两套资产共 40 个刚体，绑定 36 个关节；
两个 root–pelvis 六轴自由且无驱动的约束不绑定，不能把骨盆固定到 root。
沿用原生碰撞排除表和约束碰撞开关，线性 Locked 及角度 Locked 使用后端硬约束。
所有 Godot 对象操作在 Main，关节 RID 必须先于刚体释放，重复 Dispose 安全。

软角限制和 TwistAndSwing drive 使用 Jolt 内部角弹簧，使其与硬约束、接触在
同一物理求解过程中处理。Acceleration 系数依据当前轴的有效惯量换算，
Force 系数按 kg·cm² 到 kg·m² 转换；目标角从原生 swing/twist 表示转换成
Godot 所需的负 ZYX Euler equilibrium。参数相同则不重复写入，避免无谓唤醒。
运行测试会读取 PhysicsServer 的实际参数和开关，核对后端是否接受配置。

这里修正了调研中的一项错误判断：C# XML 文档未列出角弹簧枚举，
不等于 C# API 没有它。对实际 GodotSharp.dll 反射、编译和引擎运行回读确认，
`AngularSpringStiffness/Damping/EquilibriumPoint` 与 `EnableAngularSpring`
都是公共 API，无需私有枚举强转或修改引擎。
它们与 Jolt 忽略的旧 `AngularLimitSoftness/Damping/Restitution` 参数不同。
依据固定引擎提交 `ed1daf0bf` 的
[Generic6DOF 实现](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/modules/jolt_physics/joints/jolt_generic_6dof_joint_3d.cpp)及
[公共 PhysicsServer 枚举](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/servers/physics_3d/physics_server_3d.h)。

另新增纯值数学工具 `AlsJointSpringRow`，表达 cached Chaos 单行软约束的
隐式刚度、阻尼、有效惯量、累计 lambda 与力矩上限语义。
lambda 是位置/角度冲量，不是速度冲量。当前 Godot adapter 不调用此工具，
不能把单行公式测试说成 Chaos 整体求解器或后端受力等价测试。

## 与原生行为仍有差异

当前是实验 adapter，只由 `physics_joint_set_smoke.tscn` 使用，普通 Demo
尚未接入真实 Ragdoll。整链稳定性未过关，不能标记 P5C/P6 或 Ragdoll 完成。

- Jolt motor 使用目标四元数误差，而 Chaos TwistAndSwing drive 使用不同的
  分解/投影表达式。当前将 drive 和活动软限制合并成加权目标、合计 K/C；
  这不等价于原生独立约束行。软限制目前通过预测/当前角度触发双向弹簧，
  未复刻完整单边约束的每次迭代激活、累计冲量和退出行为。
- Chaos 投影、质量调节、接触距离/恢复系数以及全局 solver overrides
  尚未完成对应适配。原生设置保留在编译结果中，没有声称全部生效。
- 有限线性运动、SLERP drive、shock propagation、非默认 parent mass scale、
  非零目标角速度/力矩上限等模式显式拒绝。当前测试资产不需要这些模式。
- 默认测试使用导出的资产 drive；`--als-drives` 才按 ALS-Refactored
  `RefreshRagdolling` 的速度公式更新刚度：
  `25000 * clamp(pelvisSpeedMps / 10, 0, 1) * 1.5`，阻尼为 0。
  1.5 是当前 UE 有效 drive stiffness scale，不是任意调参。
  这只验证驱动更新公式，尚未接入完整 Ragdoll 状态机、速度限制或拉回行为。

之前尝试在 C# 侧施加角速度修正、再交给 Jolt 的原型存在明显整链超限，
相关失败日志保留；提交实现改用后端内部弹簧。两者结果不可混用。

## 单关节验证

引擎为官方 Godot 4.7.2 Mono `ed1daf0bf`，所有运行均在主目录。
Mannequin / AnimMan 各取实际 spine_01–spine_02 刚体、原生质量/惯量/形状及
约束，固定父体、关闭重力和接触，绕父连接坐标轴扰动子体 ±0.6 rad。
父体使用 Static 固定支撑，避免创建位置到目标位置的运动学速度污染测试。

30/60/120 Hz × 三轴 × 正负方向，共 18 次十秒运行，每次同时检查两套资产。
最终修正碰撞隔离后的复验全部退出 0；最后整整一秒的线速度与角度超限均为 0。

| Hz | 次数 | 最大锚点分离 | 最后一秒最大线速度 | 最后一秒最大超限 |
| --- | ---: | ---: | ---: | ---: |
| 30 | 6 | 3.403 mm | 0 m/s | 0 rad |
| 60 | 6 | 3.210 mm | 0 m/s | 0 rad |
| 120 | 6 | 2.900 mm | 0 m/s | 0 rad |

Z 轴负向扰动受原始约束坐标偏置影响，本次始终未超限；它检查限内驱动，
不能说 18 组都从限外恢复。测试目前没有独立的 drive 目标姿态误差门槛，
也没有角速度验收门槛，不能仅凭上述结果推断完整动态等价。

负对照：60 Hz、X 轴 +0.6 rad，禁用软求解。刚体可以睡眠、速度为 0，
但持续超限 0.425467 rad，测试如预期退出 1，证明“静止”不能替代限幅验收。

## 整链诊断与失败

60 Hz、完整 40 刚体/36 约束、启用 ALS 速度驱动、三米落地，
最后一秒逐 tick 观测，门槛保持线速度 <0.2 m/s、超限 <0.1 rad。

| 场景 | 全程最大锚点分离 | 最后一秒最大线速度 | 最后一秒最大超限 | 结果 |
| --- | ---: | ---: | ---: | --- |
| 地面 + 自身碰撞 | 99.454 mm | 0.363421 m/s | 0.033442 rad | 速度失败 |
| 仅地面接触 | 99.593 mm | 0.317672 m/s | 0.039129 rad | 速度失败 |

前者最后一秒的线速度峰值来自 AnimMan 的 spine_02，
全部动态体最大角速度为 3.299158 rad/s。十秒整的瞬时线速度约 0.051651 m/s，
不能用这个较低值掩盖最后一秒的尖峰。去掉自身碰撞后仍失败，
因此目前证据不足以归因于肢体之间的碰撞。

无接触测试最初仅把 body mask 设为 0，仍可能被地面的 mask 匹配，
出现 frame 51 锚点分离超过 20 cm 的失败。修正为 layer 和 mask 都为 0 后，
60 Hz 十秒自由落体整链通过：最大锚点分离 16.885 mm，最后一秒最大超限
0.000336 rad。自由落体仍受重力，速度持续增加，该隔离用例不要求落地静止。
旧 `backend-flight-als-60.log` 等结果不应称为有效的无接触诊断。

随后补跑最终隔离配置的 30/60/120 Hz，发现低 Hz 仍有独立问题：

| 自由落体 Hz | 全程最大锚点分离 | 最后一秒最大超限 | 最后一秒最大角速度 | 结果 |
| --- | ---: | ---: | ---: | --- |
| 30 | 44.524 mm | 0.251030 rad | 16.05453 rad/s | 超限失败 |
| 60 | 16.885 mm | 0.000336 rad | 0.003696 rad/s | 通过现有门槛 |
| 120 | 10.040 mm | 0.000471 rad | 0.007286 rad/s | 通过现有门槛 |

30 Hz 在十秒整仍记录 neck_01 的 Locked Y 超限 0.12544 rad；
最后一秒逐 tick 的最大超限更高。60/120 Hz 的隔离通过不能推断
只有地面接触才会出问题，30 Hz 还需要先处理整链求解稳定性。

完整落地尚未验收 30/120 Hz、高速倾斜、动画姿态交接及生命周期切换。
不放宽阈值、不把默认测试改成只测单关节，也不将这份结果称为完整 Ragdoll 验收。

## 构建、回归与复现

- 优化 Godot 构建：0 warning / 0 error。
- Core Release：2601 通过、0 失败；依项目规则排除
  `AlsP5aGoldenTests` / `AlsP5aTraceSchemaTests`。新增单行公式测试 5 项，
  覆盖 30/60/120 Hz、累计修正、Force/Acceleration、阻尼和非法/溢出参数。
- `p3_demo_input_smoke.tscn`：11 个输入映射、12 个方向组合、镜头基底、俯仰、
  瞄准、清理、HUD 通过；Overlay 输入 13 个选择与同帧稳定性通过。
  这不是窗口内人工键鼠或完整动画观感验收。
- Import 未修改，本批没有重跑全库；上批结果不能冒充本批重跑。
- 本批没有修改 UE 导出资产，没有构建 UE，也没有最终十分钟性能验收。

日志/TRX 位于 `artifacts/physics-joints-20260921/`（本地诊断产物，不纳入 Git）：
`final-pair-*`、`final-negative-control.log`、`final-chain-als-60.log`、
`backend-floor-only-als-60.log`、`final-flight-*`、`demo-input-smoke.log`、
`core-release.trx`。失败日志完整保留。

```powershell
Set-Location .
$godotAlsEngine = 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
dotnet build GodotALS.csproj -p:Optimize=true --no-restore
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --hz=60 --pair --pair-axis=0 --pair-angle=0.6
# 预期失败的负对照
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --hz=60 --pair --no-soft-solve
# 当前仍失败的落地门槛
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --hz=60 --als-drives
# 整链接触隔离
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --hz=60 --als-drives --no-contact
```

## 下一步

先补原生 Chaos 步进下的单关节受力/轨迹参考和实际 solver overrides，
对照当前 Jolt 的驱动误差、单边限制与接触耦合，先修复 30 Hz 无接触整链
超限，再修复整链落地抖动并完成
30/60/120 Hz 与高速落地检查。之后才接 Main 角色物理所有权、
动画姿势交接、Ragdoll/Get-up/Pose Recovery。
原规划的 Mantle、完整 ALS Camera 和最终十分钟性能预算仍在后续清单中。
