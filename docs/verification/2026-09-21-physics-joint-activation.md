# 关节预测、角度容差与驱动启用条件

基于 `c383d4f`，继续在 `.` / `main` 实现 Ragdoll 的物理前置条件。
用户未提交的 P4 计划没有改动。实验关节仍只供物理测试场景使用，尚未接入普通角色。
本批补齐部分原生条件，但整链稳定性仍未通过；不能称为完整 Chaos 移植或 Ragdoll 完成。

## 本批修改

`AlsPhysicsJointSet` 预测下一步连接器旋转时，先将角速度乘以
`max(0, 1 - AngularDamp * dt)`，再使用已有的归一化 Euler 四元数积分。
这只是预测，不修改刚体实际速度；Jolt 仍只施加一次真实阻尼。
当前刚体使用 Replace 阻尼模式，资产角阻尼为 20；30 Hz 时预测角速度是原来的三分之一。
预测尚未包含外部角加速度、陀螺项、角速度上限或约束迭代修正，不能说完整积分已等价。

新增纯值逻辑 `AlsJointRowActivation` 并接入适配层：

- 原生角度容差按 `min(1, 3600 * dt²)` 缩放。基准为原生 float `0.001f` rad，
  与现有全部 144 份实际场景记录绑定校验；没有任意调参。30/60 Hz 约为 0.001 rad，
  120 Hz 约为 0.00025 rad。
- 软限制须预测角度超限，且超限量严格大于该时间步容差，才启用。
- 姿态驱动使用已有、经过原生数学校验的 `SwingTwistDriveError` 判断启用，
  不拿限制用的 swing/twist 分解角度代替。锁定轴不启用驱动；非锁定轴需
  “误差超过容差且刚度为正”或者“阻尼为正”。位置/速度开关仍分别控制两个系数。

这些只是每步开始的启用条件。UE 在内部迭代时重新判断限位误差，
当前适配层仍每个 Godot physics tick 判断一次；硬限位仍由 Jolt 自身求解。
实际驱动力仍来自 Jolt 四元数位置电机，不是把 UE 驱动误差公式直接用于求解冲量。

后端更正（后续独立通道批次查明）：虽然 `SpringPart` 的零刚度分支是硬约束，
`SixDOFConstraint` 位置电机会先检查 `HasStiffness()`，零刚度时直接停用，根本不会调用该分支。
因此在当前位置电机路径上，纯阻尼会被丢弃，而不是转成硬速度约束。
因此适配层在合并后 `k == 0 && damping > 0` 时明确抛出不支持异常，避免错误接受纯阻尼。
纯值层保留正确的 UE 语义，不把 Jolt 的限制混进原生启用规则。
新增 `--assert-damping-only-rejected`，对两套角色的孤立关节对齐连接器、清空角速度，
断言这种配置被拒绝；实际 Godot 运行通过。当前资产的正刚度/正阻尼以及 ALS 速度驱动的
正刚度/零阻尼、零刚度/零阻尼均不受这一拒绝影响。纯阻尼的完整运输仍待实现。

## 原生依据与后端差异

本地 UE 5.9 源码：

- `Chaos/Private/Chaos/Joint/PBDJointCachedSolverGaussSeidel.cpp`：
  Init 的时间步容差、ApplyRotationConstraint/Simd 的限位判断、
  InitSwingTwistDrives 的启用条件及独立驱动行。
- `Chaos/Private/Chaos/PBDRigidsEvolutionGBF.cpp`：角阻尼与积分顺序。
- `ChaosCore/Private/Rotation.cpp`：默认归一化 Euler 积分。
  `p.Chaos.ExponentialMapForRotationIntegration` 源码默认 false，项目和 Engine Config
  没有搜到覆盖；本批未重新启动 UE 实测此 CVar，不把默认值写成运行时观测。

Godot 使用官方 4.7.2 Mono `ed1daf0bf`。公开源码固定到这个提交：

- [MotionProperties.inl](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/thirdparty/jolt_physics/Jolt/Physics/Body/MotionProperties.inl)：后端同样先按时间步施加角阻尼。
- [SixDOFConstraint.cpp](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/thirdparty/jolt_physics/Jolt/Physics/Constraints/SixDOFConstraint.cpp)：电机使用当前连接器姿态和投影后的四元数误差，并保留 warm-start 冲量。
- [SpringPart.h](https://raw.githubusercontent.com/godotengine/godot/ed1daf0bf/thirdparty/jolt_physics/Jolt/Physics/Constraints/ConstraintPart/SpringPart.h)：底层零刚度分支忽略阻尼；但此位置电机的调用入口会先将它停用，见上文更正。

按 agent-reach 技能查公开源码时，本机 agent-reach 命令不可用，gh 也未登录；
使用公开源码页面读取作为替代，没有安装工具、更改凭据或下载新的项目副本。

## 结果

所有阈值保持不变：全程锚点分离 <0.2 m，最后完整一秒的最大超限 <0.1 rad，
接触落地另要求最后一秒最大线速度 <0.2 m/s。无接触自由落体不要求停住。
40 个刚体、36 个关节，启用 ALS 速度驱动，使用上批的计算惯量。

| 工况 | Hz | 全程最大锚点分离 m | 末秒最大线速度 m/s | 末秒最大超限 rad | 结果 |
| --- | ---: | ---: | ---: | ---: | --- |
| 普通落地 | 30 | 0.107891 | 0.117747 | 0.055520 | 通过 |
| 普通落地 | 60 | 0.086434 | 0.363889 | 0.075726 | 速度失败 |
| 普通落地 | 120 | 0.021735 | 0.394192 | 0.097770 | 速度失败 |
| 无接触自由落体 | 30 | 0.030234 | 不作门槛 | 0.000393 | 通过 |
| 无接触自由落体 | 60 | 0.017275 | 不作门槛 | 0.000447 | 通过 |
| 无接触自由落体 | 120 | 0.010555 | 不作门槛 | 0.000316 | 通过 |
| 十米高速倾斜落地 | 30 | 0.298651 | 未到末秒 | 未到末秒 | 第 21 帧锚点失败 |
| 十米高速倾斜落地 | 60 | 0.181822 | 0.386435 | 0.122909 | 速度/超限失败 |
| 十米高速倾斜落地 | 120 | 0.084115 | 0.521337 | 0.081855 | 速度失败 |

相对 `c383d4f`，30 Hz 普通落地从超限失败变为通过，无接触 30 Hz 的末秒超限从
0.017455 降到 0.000393 rad；但 120 Hz 普通落地从通过回退为速度失败。
60 Hz 高速落地最大锚点分离也从 0.099679 增加到 0.181822 m。
保留这些回退，不选择性报告通过项，也不移除已验证的原生惯量来换取通过。

144 组原生轨迹对照完成、退出 0，但不断言后端等价。最大旋转偏差从
0.403482 增至 0.432737 rad，最大位置偏差仍为 0.011692 m。
按每组最大旋转偏差比较，31 组改善、18 组变差、95 组差值不超过 1e-6 rad。
代表性的软限制向内释放用例仍在第 3 帧跨零，`--assert-limit-release` 通过；
该用例最大旋转偏差 0.125327 rad，最终偏差 0.017847 rad。

另保留“只改阻尼预测、尚未加启用条件”的中间对照：普通落地 30 Hz 通过、
60/120 Hz 失败，120 Hz 末秒速度 0.728188 m/s；轨迹最大偏差也是 0.432737 rad。
最终启用条件相对此中间版的 144 组对照为 14 改善、7 变差、123 近似不变。
这些中间日志仅用于隔离改动效果，不代表最终代码的验证结果。

## 构建与回归

- 优化 Godot 构建通过，0 warning / 0 error。首次新增纯值类误用了 Import 层枚举，
  构建失败；改为接收是否允许驱动的布尔值，保持 Core 不依赖 Import，后续构建通过。
- Core Release：2616 通过、0 失败，包含新增的 7 项启用规则测试，按项目规则排除
  两组外部 P5A trace 测试。覆盖跨频率小误差、纯阻尼/锁定轴、半周摆动时
  原生驱动误差与限位角不同，以及非法时间步。
- Import Release：2332 通过、0 失败、1 项已有条件跳过。
  本次未复现上批的零分配测试波动，不据此宣称原因已经解决。
- 纯阻尼拒绝 Godot 测试通过，覆盖 Mannequin 和 AnimMan。
- 18 次十秒单关节回归（30/60/120 Hz × 三轴 × ±0.6 rad，每次两套角色）全部退出 0，
  末秒最大速度、超限均为 0；包含后端惯量回读、关节释放后的原惯量恢复。
- 九个整链工况和 144 组轨迹完整保留上述通过/失败结果。
- 本批没有改动 UE 插件或导出资产，没有重跑 UE 构建、DataValidation、普通 Demo 人工观感
  或最终十分钟性能验收；前批记录不能冒充本批验证。

本地日志与 TRX：`artifacts/physics-joint-activation-20260921/`，包括
`drag-*` 中间对照、`activation-chain-*`、`activation-flight-*`、`activation-high-*`、
`activation-replay.*`、`activation-release.*`、`damping-only-rejected.log`、
`core-release.trx`、`import-release.trx`、`activation-unit.trx`、`pair-*.log`。

```powershell
Set-Location .
$godotAlsEngine = 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --hz=30 --als-drives
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --hz=120 --als-drives
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_set_smoke.tscn -- --assert-damping-only-rejected
# report 必须使用新文件名
& $godotAlsEngine --headless --path . scenes/tests/physics_joint_reference_replay.tscn -- --computed-body-conditioning --assert-limit-release --report=./artifacts/joint-release-new.json
```

## 下一步

停止把补齐启用条件当作整个求解器已等价。优先处理软限制与姿态驱动的独立求解行，
包括各自的误差、轴、累计冲量与迭代时的限位判定；保留固定父体的原生轨迹对照来评估结果。
关节局部质量/惯量不能写回共享刚体的全局惯量，必须在约束内部使用。
再对齐投影与接触耦合，复验三个频率的普通/高速落地，不靠提高阻尼或放宽门槛掩盖失败。

整链稳定后继续角色姿态交接、Ragdoll、Get-up、Pose Recovery。
Mantle、完整 ALS Camera 和最终十分钟性能预算仍在总清单中，本批没有取消这些工作。
