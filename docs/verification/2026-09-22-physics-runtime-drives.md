# Ragdoll 前置：逐步关节驱动与回放切频边界

本批在 `.` 的 main 继续实现。新增运行时关节驱动提交，允许每个物理步更新目标、刚度和阻尼；同时定位并修复了原生对子测试在切换物理频率时的偶发失败。普通 demo 尚未接入 Ragdoll，本批没有把被动落地测试当作完整 Flail 物理动画。

## 为什么这是角色接入的前置

本地 Refactored C++ `Plugins/ALS/Source/ALS/Private/AlsCharacter_Actions.cpp` 中，StartRagdollingImplementation 开启 `bUpdateJointsFromAnimation`，RefreshRagdolling 根据骨盆速度设置角驱动强度。UE `PhysAnim.cpp::UpdateRBJointMotors` 从动画骨骼、图形父骨至物理父骨的链及原始约束参考帧计算目标。旧 Core 的角设置在创建后固定，直接启用物理会缺失该行为。

本批补上的是最终已解析驱动参数进入 solver 的运行时边界，未跳过动画目标计算：

- `AlsIslandAngularDrive` 使用原生 connector-relative 目标和 X twist / Y swing2 / Z swing1 系数。输入有序、唯一，可以只更新部分关节；省略项保留已提交设置。禁用通道由 owner 传 0 系数。
- `AlsJointIsland` 预分配候选关节数组，每步建立新行并重置 lambda。新驱动与身体状态一起提交，Gather、observer 或接触 StageCommit 失败均不发布候选设置。观察器看到本步候选，公开 JointDefinitionAt 仍返回已提交设置。
- 只更新驱动目标和 K/C，不更改 connector、限位、质量调节、拓扑或投影。不能通过该接口把 connectivity-only joint 静默升级为求解关节。现有零目标角速度、无限 torque、swing/twist 范围不变，SLERP 仍不支持。
- 按本地 `FJointConstraintPhysicsProxy::PushStateOnPhysicsThread` → `FPBDRigidsEvolutionGBF::SetJointConstraintSettings` 行为，单纯参数更新不隐式唤醒粒子。休眠时可提交设置，身体/接触历史继续保持；需要唤醒由 owner 单独 RequestWake。
- `AlsCoreJointHost` 的无接触、外力及场景接触入口均可转交同一步的驱动输入，没有新增第二个积分 owner。

五项新 Core 测试覆盖：目标换向、刚度归零/切换阻尼、跨步不残留行状态、失败回滚与重试、非法批次、connectivity-only 拒绝、休眠更新与显式唤醒、输入复制及零分配。36 个连续步与基于上一步状态独立创建的已验证 solver 对照；这部分不称为新的 UE 动态动画 golden。

## 真实 Godot 驱动提交验证

原生对子回放增加 `--runtime-drives`：创建时清零驱动 K/C 和目标，每步从该案例原生**输入设置**经 host 提交，输出仍仅与独立原生样本比较。144 例各 12 步，共 1,728 次提交全部通过，固定 15 FPS catch-up 和普通调度两次报告一致。固定参数路径也通过；除提交计数外，与运行时路径的报告字段相同。

最大误差：P `5.684341886080802e-14` cm，角度 `6.664001874625056e-8` rad，V `4.547473508864641e-13` cm/s，W `5.684341886080802e-14` rad/s。未放宽原有门槛。

此测试验证运行时参数进入真正的 Godot/Core 路径；每个原生案例内部的目标仍固定，不能据此宣称 Flail 目标计算、速度缩放、开启动作或 Get-up 已完成。

## 偶发切频错误的根因与修复

初次新回放在 case 72 / frame 0 收到 120 Hz 的 dt，但下一案例要求 30 Hz。进一步在 `--fixed-fps 15` 下，修复前稳定于 case 12 / frame 0 失败：actual `0.03333333333333333`，expected `0.01666666753590107`，configured Hz 为 60。

当前运行日志给出的 Godot 提交 `ed1daf0bf` 的 [Main::iteration 源码](https://github.com/godotengine/godot/blob/ed1daf0bf/main/main.cpp#L4577) 在进入 catch-up 循环前读取一次物理步长，同一批所有回调共用这个值；idle process 在整批物理循环之后。因此在某个物理回调末尾改 Engine.PhysicsTicksPerSecond，并不能改变本批剩余回调的 dt。

回放现在在首次启动及实际切频时停用自身物理处理，于下一 idle `_Process` 设置新频率并重新启用。下一案例只在新主循环批次开始；没有忽略步长不匹配的有效模拟帧，没有改 dt 值，没有减少任何案例或案例内的 12 步。也没有使用可能在旧批次子步之间运行的 CallDeferred 来猜测边界。

修复后固定 15 FPS 的固定驱动与运行时驱动两种模式都完整通过，报告记录 12 个频率启动边界。普通调度运行时模式也完整通过。已证明上述 catch-up 切频故障的先红后绿，不将其扩展为所有可能引擎时序问题均已消失。

使用 agent-reach 技能查阅源码时，GitHub CLI 未登录、公共网页后端 TLS 失败，随后使用网页工具直接读取同一官方提交；未修改账号配置或引擎源码。

## 回归与剩余工作

- Core Release 固定 JIT、既定两个 P5A 类过滤、串行：2,878 通过。初次定向构建的 `Math.Abs` 命名空间歧义已修为 `System.Math.Abs`，首次失败日志保留。
- Import Release 固定 JIT、串行：2,477 通过，1 项原有 skip。
- .NET 9：驱动/kinematic/sleep/observer 25 项；coupled 与形状 9 项通过。已有 61 例、1,464 阶段同输入求解五通道零误差及 190 独立形状端点零差继续成立。
- Godot Optimize 构建 0 warning / 0 error；三频率接触 smoke 和 60 Hz scene smoke 通过。切频修正后再次验证普通 120 Hz：A635/M1004 睡，末秒 V/W0；与上一提交报告所有既有字段一致，仅增加运行时提交/切频诊断字段。
- 本批未重跑完整 12 场景矩阵，最近完整矩阵仍是上一批 9/12，三个 30 Hz 休眠失败继续保留。普通 120 本批没有回归。
- 没有修改或启动 UE 原生模块，也没有重新导出冻结资产；上一批 Editor 退出 AV 和旧 Condition 仍未解决。

本批产物：`artifacts/physics-runtime-drives-20260922/`。重点为 `core.log`、`import.log`、`core-net9.log`、`import-net9.log`、`rate-before.log`、`rate-after.json`、`native-after.json`、`runtime-pairs-final.json`、`normal-120-final.json`。`runtime-pairs.log` 是首次切频失败，不能当最终通过报告；`run_checks.ps1` 为首轮脚本，最终切频修正后的两种对子和普通 120 使用独立产物。

下一步必须继续补齐实际 `UpdateRBJointMotors` 的动画父链与约束帧目标、原生 drive scale 及骨盆速度强度映射，然后在普通角色中接入最后已提交动画姿态 → 物理初始化、禁用胶囊移动、Main 物理步、物理姿态展示和恢复快照。动画目标姿态与物理输出姿态必须分开保存，不能把物理姿态再当下一帧 Flail 目标。角色位置跟随地面 sweep、初始八帧速度限制、Get-up / Pose Recovery、Mantle、完整相机与十分钟性能预算仍属于原目标。

用户 P4 规划文件保持原始 SHA256 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`，不纳入提交。
