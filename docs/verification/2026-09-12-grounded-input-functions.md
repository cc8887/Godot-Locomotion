# 地面方向、倾斜与步态输入函数（第八十六批）

继续地面 UpdateMovementValues 的依赖补全。本批新增五项正式图检查与 Core
计算：CalculateVelocityBlend、InterpVelocityBlend、CalculateRelativeAccelerationAmount、
CalculateDiagonalScaleAmount、CalculateWalkRunBlend。生产共享定义已加载这些
函数；它们尚未替换 Standing 的旧输入更新，因此不宣称 Demo 视觉问题已修复。

## 与源实现对齐的行为

- VelocityBlend 先对完整速度 SafeNormal，再逆角色旋转，除以局部 XYZ 绝对值
  之和，拆为 F/B/L/R。Godot 局部 +X 为右、-Z 为前；竖直分量参与分母。
  源 Normal 容差 0.1 是 cm/s 的平方，需按平方单位换算，不能当作 0.1 m/s。
- 四方向分别 FInterpTo，不做二次归一化，也不在全零时补 Forward=1。起步
  权重从零增长；反向时旧方向衰减、新方向增长。是否执行由外层 ShouldMove
  决定，无状态函数本身不根据零速度偷偷保留旧值。
- RelativeAcceleration 使用 Acceleration 与 Velocity 的完整三维点积。
  点积 >0 用最大加速度，否则（包括零）用最大制动减速度。先限制向量长度，
  再除以该上限，最后转角色局部空间。输出保留三维量，之后全局更新才映射 Lean。
- DiagonalScale 采原生曲线的 abs(F+B)，输入是尚未归一化的权重和。
- Walking 的 WalkRunBlend=0，Running/Sprinting=1；未知契约枚举明确拒绝。

本地 UE 源依据：KismetMathLibrary.inl 的 Normal/Divide_VectorFloat，及
Core/Public/Math/Vector.h 的 GetSafeNormal/GetClampedToMaxSize。Divide_VectorFloat
零分母返回零；GetClampedToMaxSize 对小于 1e-4 cm/s² 的上限返回零。
新函数使用数值 Godot 输入和旋转，不含 Godot 对象、来源采样或可变角色历史。

导入器检查五个图的节点闭包（28/11/28/7/5）、表达式、局部变量归属、赋值
顺序、分支去向和枚举返回值；不只按函数名宣称算法一致。扩展已有表达式读取器，
增加三分量求和、完整向量运算、方向结构与组件属性，并保留原有空中/Lean 验证。

## 验证

专项共 47/47：新增地面 22 项，已有输入函数 12 项、空中更新 13 项。

- 起步在 30/60/120 Hz 的前五帧与独立几何递推解比较；权重和小于 1，左右
  反向保留两个方向，同一已提交状态重复计算相同结果。此处为函数级验证，
  不等于全局输入事务或实际脚部视觉验证。
- 平面四方向、斜向、纯竖直、带竖直分量斜坡、旋转、归一化容差两侧。
- 加速/制动、零点积、完整三维长度限制、不同分母、零/极小上限。
- Diagonal 非负域读取此前正式导出的 UE 曲线求值样本作为期望值。
- 16 项图变异拒绝，覆盖归一化容差、遗漏竖直分量、局部赋值顺序、分量符号、
  插值算法/通道、加速判断/分支/组件、制动分母、Diagonal、步态和额外节点。

Godot 构建零警告/零错误。生产 single/parallel 各 180 帧终态退出码 0，结果
摘要 `21E164D829153157`、完整姿势摘要 `CF9225D4DE9B2C8B` 一致，各 10 个
来源事件、14 个预测快照；日志明确标记 ground_formulas=5、frame_adapter=not_connected。
回归日志分别为
`artifacts/ground-functions-worker-single.log` 与
`artifacts/ground-functions-worker-parallel.log`。

没有新增完整 UE 动画输入逐帧 oracle，没有人工截图、性能预算或完整曲线历史
验收。旧 StandingCycle 的二次归一化尚在生产路径；新函数已准备好，但需统一
输入所有者后一起替换，避免两套时钟/历史共同驱动方向权重。

## 下一项依赖与接线

CalculateStrideBlend 和 CalculateStandingPlayRate 的正式结构化图包含宏端口，
但宏属性为空，未包含宏本体。已有 UE 编辑器只读导出
`artifacts/movement-input-functions-editor.json` 的 nativeText 确认三处引用均为
ALS_AnimBP:GetAnimCurve_Clamped，GraphGuid=D8A28103432B8B42CDE369B4904E61E5。
不能只根据 Name/Bias/ClampMin/ClampMax 引脚假定宏实现。

下一步补正式宏引用/宏本体及地面结构默认值，编译完整步幅、播放倍率和
UpdateMovementValues 更新顺序/ShouldMove 门控；接入 BaseLayer 候选全局历史，
让 Standing 和 Crouching 读取同一结果，再将完整输出接入 Worker/Demo。
继续最终 YawOffset、上身分层和 IK/脚锁验收后，推进剩余原 P5A–P7。

本批未更改 UE 插件/资产、未启动 UE，未提交、回退或合并现有工作树。
