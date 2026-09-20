# Refactored 脚部控制链完整性与首个缺失组件

日期：2026-09-13，第一百六十四批。承接用户按移植完整性继续推进的要求。

## 结论与原规划归属

这些缺口属于原 P4 的 Foot IK / Foot Lock / pelvis correction，不是新增玩法。
与停止动画有关的通知/播放属于 P5A 前置依赖，上一批已接共同 Slot。
原计划的完整脚部约束尚未全部完成，因此当前不能称为完整照搬 ALS。

本项目资产/动画图以 V4 为基准；基座脚锁借用 Refactored C++。当前
`AlsFootIkRuntime` 在使用 Based Final 后仍执行 V4 的 Offset、pelvis、
虚拟膝骨和 TwoBoneIK，不能把这条混合链当作 Refactored Control Rig 的等价实现。

## 原始证据

只读 UE 导出 `artifacts/refactored-foot-settings-164/` 中的 AIS_Als_Default、
AB_Als 和 CR_Als；冷启动日志记录 `assets=3 assets_saved=0`。
实际 CDO 设置为 useFootIkBones=true、MovingSmoothSpeedThreshold=150、
AllowFootLock=true、大腿限制 90°、脚掌限制 40°，与当前脚锁参数相符。
本批没有修改 UE 资产或 C++ 插件。

`tools/diagnostics/inspect_refactored_foot_graph.mjs` 现支持 UTF-16LE BOM 和
完整嵌套引脚/注入变量路径，报告为 `artifacts/refactored-foot-graph-164.json`。
只有顶层 DefaultValue 会遗漏实际变量来源及子引脚参数。

| 原图处理 | 已核实的实际合同 | 当前状态 |
|---|---|---|
| 膝盖目标 | CalculatePoleVector；方向乘 40 cm；ExponentialDecayVector 半衰期 0.05 s | Based 适配仍使用 V4 虚拟骨/固定偏移，待迁移 |
| 脚部位置 | ApplyFootOffsetLocation；频率 12、阻尼比 2、目标速度量 0；插值后最大腿长比例 0.99 | 未接入 Based 控制链 |
| 骨盆至脚最小距离 | PoseMoving 驱动 double Lerp，20→50 cm | 本批增加图参数映射；位置组件未完成 |
| TwoBoneIKSimplePerItem | TargetRotation 同时连入 Effector.Rotation；bEnableStretch=false；向子骨传播 | 当前仍是 V4 TwoBoneIK，需核对并替换对应语义 |
| 脚踝后处理 | IK 后读取 calf/foot 当前旋转；参考骨架转入 calf 空间；法线平滑后再约束 | 本批增加独立候选状态组件，尚未接 Demo |
| 旋转约束参数 | Swing1=[-20,40]；Twist=[0,0]；Swing2 的两端随 PoseMoving 从 [-15,5] 变为 [0,0]；半衰期 0.1 s | 已按导出图实现映射，尚未做严格导入编译及整图消费 |
| 写回权重 | Set Transform 在旋转节点之后写回；真实 FootIK 曲线参与权重 | 待与当前基于 Update 历史的权重入口区分并整合 |

原 AB_Als 根顺序为 Grounded → Transition Slot → Locomotion → PostLocomotion
Slot → Layering → Head → CR_Als → Ragdolling → Output。未发现位于 Control Rig
之后的最终惯性化节点，不能凭猜测加最终惯性化来解释当前脚部突转。

脚踝后处理读取的“当前旋转”是 IK 已处理后的旋转，而且 IK 已接收
TargetRotation。因此本批发现并不证明一个脚踝角度限制就能消除第 616 帧问题；
不得将测试中的任意 current/target 差异误当成真实图的输入关系。

## 已实现与验证

新增 `src/Als.Core/Locomotion/AlsFootOffsetRotationModel.cs`：

- 原始 calf-space 参考旋转缓存、首次直接初始化法线、后续法线平滑。
- 按 UAlsMath 的 float InvExpApprox 系数计算平滑，未复用不同的 Pow 公式。
- 原版 FindBetweenVectors 的零向量/反向分支、UE 5.9 双精度 Rotator 奇异点。
- 将当前动画角度纳入限制上下界，允许已有动画姿势超出常规区间。
- 角度限制使用 float，姿态运算使用 double；返回纯候选状态，调用者可整帧提交/丢弃。
- PoseMoving 的 double Lerp 不额外 clamp；图参数与可配置 Rig Unit 输入分离。

24 项专项覆盖各轴、已有超限动画、反序区间、初始/当前小腿空间、缓存重置、
30/60/120 Hz 法线历史与同帧重试、零 delta、反向法线和旋转奇异点。
与既有 BasedFootLockModel 合并后 Release 46/46，通过日志为：

- `artifacts/tests/foot-offset-rotation-164.trx`
- `artifacts/tests/foot-offset-related-164.trx`
- `artifacts/foot-offset-rotation-164-build.log`：Godot Debug 优化构建，零警告、零错误。

这些是源码对照、解析几何/状态行为测试和构建验证；尚无本组件的 UE 实际
Rig Unit 数值探针、整图结果对照，也没有生产接线或本批移动截图。
原 V4 路径 720 帧通过及 Based 第 616 帧 41.100025° 失败沿用上一批证据，
没有重跑、放宽阈值或宣称视觉问题修复。

## 继续执行的依赖顺序

1. 导出/严格编译完整 RefreshFootIk/ApplyFootIk 合同，补足位置弹簧、腿长、
   膝目标及真实曲线来源。建立原生 Rig Unit/原图对照，明确 V4 与 Refactored 的适配。
2. 将这些状态纳入完整帧事务，按原顺序接入 Based 路径，保留独立 Target/Final
   历史及初始化/相关性语义，验证晚期失败后的重试不改变平滑状态。
3. 重新进行实际起步、A↔D 换向、停步、转身、斜坡和移动平台测试，记录支撑
   接触窗口与骨骼/曲线/相位并做多帧截图。通过后才启用默认完整动画入口。
4. 继续原 P5A 通用通知/动作消费者、P5B 全 Overlay/道具玩法、P5C
   Mantle/Roll/Root Motion、P6 Ragdoll/Get-up/Pose Recovery/完整 Camera，最后 P7。

完整 P3/P4 动作与接触验收、P5A 至 P7 均未关闭；音频仍暂缓。
