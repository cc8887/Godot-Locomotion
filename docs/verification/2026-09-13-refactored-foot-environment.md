# Refactored 脚部环境查询与骨盆弹簧

日期：2026-09-13，第一百六十七批。原 P4 完整脚部链的组成部分，尚未接入 Demo。

## 实现与原图约束

新增 `AlsRigSpringModel`、`AlsPelvisRigModel` 和 `AlsFootTraceRigModel`。
前者对应实际 `FRigUnit_SpringInterpV2`，保留零 delta 初始化、外部 Current、
Force、目标速度与历史有效性；它与脚部 `UAlsMath` 的初始化封装不同。
共享弹簧计算内核提取到 `AlsRefactoredSpring`，原脚部模型仍保留自身封装。

骨盆图先取两脚偏移最小值，clamp 到 [-30,40] cm，乘 Amount 后送入
Strength=2、CriticalDamping=1 的独立弹簧，再乘 Amount 后写入骨盆 Z。
两次乘权重均为原图连接，不能删去一次或用 V4 VInterp 替代。

地面查询拆成主线程射线描述和纯观察值消费者：从目标 XY 的 VM Z=50
到 Z=-80，射线不随目标动画 Z 上下移动。命中法线按逆旋转和逆缩放进入 VM，
原节点不再归一化；坡度判定与 height/normal.Z−height 补偿使用该结果。
禁用、未命中、不可行走分别遵循实际节点的零偏移/向上法线输出。
此处尚未改变 Godot 的真实物理采集时机和层过滤。

新增正式配置 `assets/config/refactored_foot_environment_inputs.json`，从原生
CR_Als 对象导出提取 RefreshPelvisOffset、RefreshFootOffset、TraceFootOffset。
`AlsFootEnvironmentCompiler` 检查节点实现、全部连线、引脚类型/方向、注入变量、
骨骼/空间/曲线/查询通道与受支持的弹簧选项，数值设置读入不可变定义。
精度变更（float→double）和丢失乘权重连接会被拒绝。该合同仅覆盖三个环境函数。

原图还明确：

- 查询权重来自 `FootLeftIk` / `FootRightIk`，启用条件为 >=0.0001。
- 外层 RigInput 脚变换无效时跳过查询和变量赋值，不能误实现为全部历史归零。
- `FootHeight` 是注入变量，显示的 13.5 不是运行时配置值。
- PrepareForExecution 从 `foot_l` 的 Initial Global Translation.Z 初始化脚高，
  从 `thigh_l` 到 `foot_l` 的 Initial Global 链长初始化 LegLength。
  ChainLength 原节点逐段累加并转 float；需要依据实际参考骨架求值。
- 原顶层普通执行顺序为 DiagonalScaling → SpineRotation → FootOffset →
  PelvisOffset → FootIk → HandIk；还受 IsGameWorld 执行分支约束。

检查脚本新增顶层 RigVM 图和变量声明输出，见
`artifacts/refactored-foot-graph-167.json`。初始化尚未编译进生产定义，完整
RefreshFootIk/ApplyFootIk、PoseMoving 资产映射和生命周期合同继续开放。

## 原生对照与回归

新增实际节点探针 `AlsFootEnvironmentProbe.cpp`，无资产保存。
弹簧 12 组、1440 帧，覆盖 30/60/120 Hz、零 delta、重置、外部 Current、
初始化开关、欠/过/临界阻尼、Force 与目标速度；另用临时物理世界和真实
Box 碰撞执行 48 组 FootOffsetTrace，覆盖坡度、旋转、非均匀缩放、未命中与禁用。
36 组查询命中，消费者接受 16 组，拒绝 32 组。

冷启动、普通 Editor 和正式夹具三份输出逐字节相同，SHA256：
`5248D2CD81E4F21C29165D3266C98373376A8237E19332A0359E96FAF0CD5B47`。
夹具：`tests/Als.Core.Tests/Fixtures/FootIk/native_foot_environment.json`。

| 实测项 | 最大误差 |
| --- | ---: |
| 弹簧结果 | 7.62939453125e-6 cm |
| 弹簧速度 | 5.340576171875e-5 cm/s |
| 骨盆偏移 | 7.62939453125e-6 cm |
| 射线端点、偏移、法线 | 0 |

Core 相关 Release 65/65 通过，包含 1440 次环境弹簧同帧重试，以及此前
脚部节点、Rig IK、旋转和 based lock 的回归；Import 合同 8/8 通过。
日志为 `artifacts/foot-environment-native-167-tests.log` 与
`artifacts/foot-environment-config-167-typed-tests.log`，TRX 位于 artifacts/tests。
Godot Debug 优化构建零警告、零错误。没有接入新生产路径，因此未声称新视觉结果。

完整 UE Editor 目标构建与审计通过；BuildId
`bb1d152a-059d-48ae-9835-6ec3c8eea157`，fingerprint
`268AAAC3EF0750484B60BD444F30B7FB67D84DF232C413A2D578E4ABEB57FE03`。
冷启动和普通 Editor 导出/退出均为零；普通 Editor 日志仍有已有 AutomationTest
启动报错，不能称整份日志无错误。DataValidation 退出零，0 error(s)、3 warning(s)。
探针与头文件的项目/工具源码副本一致。含 ALS 依赖的隔离插件包构建成功、
ExitCode=0，DLL/PDB/modules 齐全，包后项目审计 AUDIT_PASS；产物位于
`artifacts/unreal/foot-environment-167-package/`，未部署打包 DLL。打包时 UBA
因低内存终止一个编译进程后自行重试成功，日志保留，没有另起重复构建。

首次和第二次原生探针退出 3：重复调用 InitializeNewWorld，导致 WorldSettings
对象冲突；bInSkipInitWorld 不会阻止 CreateWorld 自身初始化。已改为只在
CreateWorld 中传入 InitializationValues，保留两次失败日志。配置解析首次
1 失败/6 通过，原因是 DefaultValue 前缀截取多留一个引号；修正后 7/7，
增加引脚精度校验后 8/8。未放宽测试阈值。

## 下一步与验收边界

把初始化、完整腿部函数、曲线版本适配及分支历史一起编译成帧输入合同，
再将已验证的查询、骨盆、位置/膝/双骨 IK/脚踝节点按原顺序接进候选/提交状态。
之后做真实移动、支撑窗口、平台、30/60/120 Hz 和多帧截图回归。

第 616 帧约 41.100025° 的旧视觉失败尚未关闭；默认完整入口、P3/P4
整角色效果验收以及 P5A–P7 均未完成，音频继续暂缓。
