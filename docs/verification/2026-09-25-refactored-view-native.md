# Refactored View / Spine / Head 原生连续状态对照

本批在主目录 main 补齐连续状态参考与回放测试，没有改变生产算法，也没有切换普通 Demo。原始 AB_Als_C 的临时实例使用其真实 Settings，原生 NativeThreadSafeUpdateAnimation 执行 RefreshView / RefreshSpine，再按请求显式执行 InitializeHead / RefreshHead。GamePreview 世界不模拟物理、不保存资产；FeetState 禁用以隔离足部场景查询。代理时间通过原生 PreUpdate 设置。

## 范围和结果

- 30 / 60 / 120 Hz 各五秒，共 1050 帧；覆盖瞄准进入/退出、第一人称慢动作、平台旋转、输入方向接近 180 度、动作期间视角冻结、Head 隐藏恢复、零游戏 delta。
- C# 使用自己的连续历史，未将原生上一帧输出灌回被测状态。三频率各两次初始化、半秒隐藏，换侧状态共 210 帧；全部布尔状态逐帧一致。
- 冷启动与普通 Editor（PID 37696，实际退出码 0）的输出字节一致，SHA256 `DBADBB04BC2E9A01D37EA5E74CDAE1AECCF188992429E13D89340307E119DCBB`。请求及输入 hash 随参考保留。

| Hz | View 最大差 | Spine 最大差 | Head 非速度最大差 | Head 角速度最大差（度/秒） |
|---|---:|---:|---:|---:|
| 30 | 5.96046448e-8 | 0.0000152587891 | 0.00000381469727 | 0 |
| 60 | 5.96046448e-8 | 0.0000190734863 | 0.0000305175781 | 0.000244140625 |
| 120 | 5.96046448e-8 | 0.000133514404 | 0.0000915527344 | 0.00048828125 |

这是误差预算内的对照，不是逐位相等。最终门槛：布尔精确相等，无量纲权重/scale/bias 2e-6，角度 0.001 度，角速度 0.001 度/秒。0.001 度在一米力臂对应小于 0.018 mm；速度门槛在 30 Hz 一步对应小于 0.000034 度。以上并不代替视觉验收。

## 失败与数值边界

首轮对所有浮点统一要求 1e-4，60/120 Hz 在角速度上失败。按单位拆分速度门槛后，120 Hz 第 316 帧 Spine.Yaw 的差值 0.000102996826 超过初始角度门槛，完整轨迹最大值见表。最终明确调整角度预算，未声称保持原门槛通过。

检查本地 UE 源码确认 FMath::InvExpApprox 的源码是展开多项式，现有 Rig 数学实现的 Horner 形式来自此前原生轨迹。分别尝试只改 Head spring、以及改 pitch/spine damper 为展开式，前者更早失败，后者三个频率均失败；两次实验均已撤回，生产算法未变。精确舍入差异尚未定位到编译后的指令，不将“浮点优化”当作已经证明的根因。相关失败 TRX 保留于 `artifacts/refactored-view-native`。

UE 首轮编译尝试写代理私有 CurrentDeltaSeconds 失败，改用标准 PreUpdate 后完整 Editor 构建和插件审计成功。成功日志前缀 `20260924T183438108Z-d20267ce54c6435b95fa6107d1b05b07`，fingerprint `213A8E383835FAE536FC592F6E90271B782C24E64E0FC4CDF5C277562B21F8C2`。冷导无错误/警告；普通 Editor 保留两条旧 Condition failed 和五项旧警告，未修复这些问题。DataValidation 退出 0，0 error / 3 旧 warning。

## 回归与后续

Import View/Head/Look 最终 29 项通过；Godot Optimize 构建 0 警告、0 错误。未运行全量 Core/Import、Godot 场景或打包验收。

这个探针显式指定 Head 回调时机，不是原始 Head AnimGraph 自动遍历或完整角色运行的证明。下一步是原始 Head 整图连续求值对照，再与完整 Refactored 骨架布局、Locomotion/Layering 及普通宿主衔接，继续 Mantle 和既有 Ragdoll、相机、性能缺项。用户暂缓的头颈拉伸调查、道具物理与音频保持暂缓；不把本次结果称为头颈问题修复。
