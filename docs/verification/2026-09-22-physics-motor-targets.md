# Flail 动画关节目标原生对照

## 本批实现

在主目录 `.` 的 main 推进。新增纯 Core `AlsJointMotorTarget.TryEvaluate`：输入 UE 骨骼局部姿态、图形父链及资产原始约束帧，计算 `UpdateRBJointMotors` 的目标四元数。矩阵顺序、无缩放旋转、父链遍历和到根节点的跳过规则按本地引擎实现；不能用创建后修正的 connector 或 COM 帧替代原始帧。调用不分配托管内存，无共享可变缓存。

导出器新增 `-PhysicsMotorTargetsOutput=...`。两套真实模型各采样 ALS_Flail 的五个时刻，调用 UE 的 `SetAllMotorsAngularDriveParams` 和 `UpdateRBJointMotors`，读取实际 Chaos 关节设置；输出局部骨骼姿态、真实目标、profile 目标、启用标志及 K/C。仅操作临时组件，不保存资产。

## 原生证据与范围

- 190 个 motor 样本，其中 180 个启用姿态驱动；新 Core 目标与原生实际目标按四元数符号对齐后，最大分量差 `3.3306690738754696e-16`，断言上限 `1e-12`。
- 原始弹簧 0/12500/25000、阻尼 0/2 的原生有效系数均经过当前引擎 1.5 倍缩放；启用标志独立于系数。这是该引擎配置的观测，不代表任意 UE 配置的常量。
- 参考骨骼名字、父索引、物理资产及启用关节的原始帧与现有 physics asset 输入严格绑定；每模型至少三种不同真实动画姿态。
- 当前两套资产的驱动关节均没有跨中间图形父骨。中间父链的非交换旋转、根跳过和零缩放由独立解析测试覆盖，不能称作真实资产 golden 覆盖。
- 只验证目标计算及导出系数，不证明完整 Flail 物理轨迹或普通角色 Ragdoll 已完成。

新冻结文件 `assets/config/v4_physics_motor_targets.json` 为 658300 字节，SHA256 `C5E087DFBCB2929A61FB355582F9AE8945BAE66570DD9DE4302E9574B2CF8320`；两次冷导出字节一致。旧 joint frame 导出冷复跑仍为 `5843163F656799D55E2BF5B28D04AF700466C043A78DE912B3C70B61DA7B092B`，未更改旧参考。

## 验证

产物目录：`artifacts/physics-motor-targets-20260922/`。

- Core 新增三项测试在 .NET 8/9 通过，包含 2048 次零分配调用。
- Import 定向十项在 .NET 8/9 通过；最新追加资产绑定断言在 .NET 8 单项及 .NET 9 十项通过。180 目标结果相同。原有 coupled 61 例/1464 阶段 DP/DQ/V/W/P 差为零，独立形状 190 端点 P/Q 差零。
- Godot Optimize 构建通过，零警告/错误。本批没有重跑全部测试或运行时矩阵，因为新 helper 尚无生产调用；最近全量来自前批 Core 2878、Import 2477+1 旧 skip。
- 按 UE 插件构建诊断 skill 完成完整 Editor 目标构建与插件审计，fingerprint `7AB92D790C696EB95D9D41A2511BABF54B6EFEC49AF7F0A3EECF6854D017B417`。导出器三份源码与 UE 项目插件镜像一致。
- DataValidation 退出 0，零错误、三个旧警告。普通 Editor PID 24296 输出 `ALS_MOTOR_TARGET_EDITOR_RESTART_OK`，退出 0；仍有两条旧 `Condition failed`，既有间歇退出 AV 未声明修复。

首轮 Import 测试误用 `position` 读取器（实际字段为 `translation`），随后错误假定真实资产存在中间父链；均修正测试并保留失败日志。Core 首轮暴露无效父索引被零缩放提前跳过，现先校验父序再执行原生缩放跳过规则，复跑通过。未放宽数值误差阈值。

## 尚未完成

下一步将最后已提交的动画局部姿态转换为独立 motor targets，结合轴向启用标志和骨盆速度计算驱动强度，接入已有逐物理步 drive 提交。须分离动画目标与物理展示姿态，不能把物理输出反馈冒充 Flail 动画目标。

普通 demo Ragdoll 生命周期、胶囊停用/角色位置跟随、初始速度限制、退出恢复仍未接通。最近落地矩阵仍 9/12，三项 30 Hz 旧失败保留。Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能验收继续保留，不能以本批底层通过代替最终交付。用户 P4 规划修改未纳入提交。
