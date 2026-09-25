# cached 线性约束与连续关节轨迹

本批直接在 `${env:GODOT_ALS_ROOT}` 的 `main` 补齐三轴锁定线性位置/速度约束，组合上一批角约束，
并完成真实 ALS 资产的 144 组无接触、保持唤醒的十二帧连续轨迹对照。
这是 Core 关节求解与积分链路的验收，尚未替换 Godot 的 Jolt 求解，也未接入普通角色。

## 实现范围

- `AlsCachedLinearJoint` 缓存预测质心坐标下的 connector 臂长、世界惯量、关节局部质量、
  三根轴及位置累计量。三轴锁定 SIMD 同时累加；scalar 逐轴更新。
- 线性速度约束复用位置阶段的缓存。保留原生 gate：SIMD 只要任意位置 lambda 激活，
  即求解三轴；scalar 分别按各轴累计量启用。软角限制不再在速度阶段重复施加阻尼。
- `AlsCachedAngularJoint.SolveVelocities` 补硬角限制/锁定的零恢复系数速度修正。
  世界惯量张量计算抽出供线性和角度两者使用，原 516 组角度对照仍通过。
- `AlsCachedJoint` 实现 ALS 顺序：角限位 → 线性锚点 → 角驱动；随后将 DP/DQ 换算为
  隐式速度，再依次处理硬角速度和线性速度。迭代刚度为 1，`bSolvePositionLast=true`。
- `AlsRigidBodyIntegration` 保留 native 的线性 drag、归一化 Euler 四元数积分，以及
  particle actor 四元数/速度 float 存储、质心与 solver 计算 double 的边界。
- 修正 `AlsLockedLinearProjection.Correct` 的零 DQ 分支：原生此时保留输入四元数，
  不额外归一化。为这个浮点精度边界添加测试；原投影对照通过。

已覆盖的完整无接触步顺序：预测 actor/COM → 缓存全部行 → 8 次位置迭代 →
DP/DQ 隐式速度 → 2 次速度迭代 → 应用位置修正并清空增量 → 缓存线性投影 →
1 次投影和 0.1/dt 速度贡献 → actor 存储与下一帧。

固定父级在 native 中使用 actor 坐标；动态子级使用 COM 坐标，connector 需要相应转换。
这里没有按参考结果修改输出，也没有每帧重新读取原生姿态作为下一帧输入。

限制仍明确存在：无接触/重力/陀螺力矩/速度限幅，未实现物理岛睡眠/唤醒；
无 SLERP drive、非零驱动速度目标、有限扭矩、shock propagation、非单位 parent mass scale、
线性 Free/Limited 组合、非零 restitution。普通资产三轴线性锁定符合本批契约；
自由 root 不能当锁定关节接入。连续轨迹比较位置、旋转和两类速度，未证明 force/torque、
断裂事件、网络同步或完整角色接触等价。

## 两层原生证据

新增 `-PhysicsJointStepOutput=绝对路径`：通过 native cached container solver 执行 8 次位置迭代、
SetImplicitVelocity 和 2 次速度迭代，逐阶段观测。288 组为 3 Hz × 4 端点动态模式 ×
质量调节开关 × 两种旋转/种子增量配置 × SIMD 开关 × Free/软 Limited/硬 Locked 角模式。
两端有非零臂长、各向异性惯量及初始速度，部分用例含外部写入 DP/DQ。
数据中的角模式 0/1/2 与 UE 枚举一致。此探针不包含投影、碰撞或真实时间积分。

| 288 组逐阶段最大误差 | 观测值 | 门槛 |
| --- | ---: | ---: |
| DP，cm | 1.7233887e-6 | 2e-5 |
| DQ，rad | 8.4293697e-7 | 3e-6 |
| V，cm/s | 0.0003429835 | 0.003 |
| W，rad/s | 0.00015206246 | 0.0005 |

新增 `-PhysicsAwakeSolverOutput=绝对路径` 复用原 144 组真实 PhysicsAsset scene 导出流程，
仅在导出作用域关闭 `p.Chaos.Solver.Sleep.Enabled`，RAII 恢复原值；资产不保存。
参考根节点明确 `sleepEnabled=false`。两模型、spine_02/neck_01、三个频率、三个轴、正负 0.6 rad、
资产/高速驱动，共 144 组，每组从 frame 0 连算至 frame 12。

| 144 组 × 12 帧连续轨迹最大误差 | 观测值 | 门槛 |
| --- | ---: | ---: |
| 位置，cm | 1.0180348408383202e-6 | 2e-5 |
| 旋转，rad | 9.424321830774485e-8 | 1e-6 |
| 线速度，cm/s | 5.4505726e-6 | 1e-4 |
| 角速度，rad/s | 1.9073486e-6 | 2e-5 |

原带睡眠参考保留，包含 84 个子级休眠样本；其全部初始条件、资产质量/惯量、关节设置、
频率、迭代参数与新参考逐项相同。新参考所有帧保持唤醒。
没有拿原参考的 awake 标志驱动 Core，也未把测试关闭睡眠带进 Godot 生产入口。

首次连续回放原睡眠参考时，case 1/frame 11 位置差 0、角度差 5.96e-8 rad，速度差
0.010126363 cm/s / 0.0022923006 rad/s。原生此帧进入睡眠并清零速度，暴露出物理岛状态
不属于当前独立求解器的范围。保留失败日志，再独立导出 awake reference 验证求解链路；
睡眠系统仍需后续实现，不能称为已修复或跳过整个目标。

原生 JSON 均通过冷导出重复哈希检查：

| 文件 | 字节 | SHA256 |
| --- | ---: | --- |
| v4_physics_joint_step_reference.json | 6070718 | `5DFD6EEA433405D17EF7590AF393BFAD7D99EEE93108201E6DD5C05208C78F57` |
| v4_physics_awake_solver_reference.json | 3901506 | `C96AB7C45EF673ED2FB4AF5C557298EA2AF7E41698FF15209B0CF02D46F37909` |

原默认导出也重跑验证，仍与旧 `v4_physics_joint_solver_reference.json` 字节一致，SHA256
`18243B9F6F948C65216D73DC17FF95DD5C8E6422E508C58FF87811819464D0FD`。

## 验收及失败记录

Core 新增五项语义测试覆盖逆质量分配/线动量、SIMD 与 scalar 速度 gate、隐式速度、COM 与
float 存储边界、2048 次组合关节构造/8+2 迭代零托管分配。针对性 15 项通过。
全量 Release Core **2631 通过**（既有 P5A Golden/TraceSchema 排除），Import **2337 通过、
1 既有条件跳过**。两套全量使用子进程固定 JIT 配置，不改变用户环境。
Godot 优化构建 0 warning / 0 error。原 262 组投影、516 组角约束对照继续通过。

首轮 288 组测试将默认最大扭矩误认为只允许 0；原生 `float.MaxValue` 同样表示无限制，
调整为明确接受这两种原生无限制表示，未更改求解结果或放宽其数值门槛。

UE 遵循 `ue-diagnosing-plugin-build-load`：完整 Editor 目标构建、所有适用项目插件闭包审计
通过。最终 fingerprint `5FA482323DA496335FC79C59D83D24F70FFA25E572CE4476A86B18E4761EE196`，
BuildId `186ff094-6861-4ab2-95dd-e0889004ba00`。未复制二进制或改 BuildId，未执行打包构建。
普通 Editor 重启成功加载 exporter 反射类，输出专用标记并退出 0；仍有既有
Condition failed、材质及导航提示，不称无错误日志启动。
DataValidation 退出 0，0 error / 3 既有 warning。
Godot 原有可选投影路径的 60 Hz、Y 轴 -0.6 rad 单关节十秒探针通过（两模型）：
最大锚点 6.525135e-5 m、末秒速度 2.1330993e-6 m/s、末秒角超限 0，退出 0。
这个探针保护共用零 DQ 修正的兼容性，不是新 Core 求解器的 Godot 接入证明。

产物统一位于 `artifacts/physics-joint-step-20260921/`：`native-joint-step*`、`native-awake*`、
`native-default-repeat.json`、`trajectory-first.log`（睡眠边界失败）、`trajectory-final.log`、
`rows-test.log`（默认无限扭矩表示断言失败）、`rows-final.log`、全量回归与引擎日志。

## 下一步

下一阶段进入 Godot 物理世界接入：确认可用后端能否在同一迭代内共享关节与接触的 DP/DQ、
速度及局部质量响应；普通 Jolt 6DOF 参数不足以表达这一契约。所需原生模块和构建入口必须
放在主仓库，不能另造 demo 副本。接入之后重新运行九项整链与真实场景对照，并实现物理岛
睡眠/唤醒。只有整链、接触和观感通过，才继续普通角色的 Ragdoll/Get-up/Pose Recovery。

本批 Core 对照通过不能关闭旧 Jolt 高频/低频整链失败，也不能替代十分钟性能验收。
Mantle、完整 ALS Camera 和剩余角色流程继续属于总目标。用户未提交的 P4 规划文件保留，
未保存 UE 内容资产，未推送远端。
