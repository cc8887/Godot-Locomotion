# Foot IK 原图补导出与骨盆输入函数

日期：2026-09-13。第一百三十批，承接完整动画链修复。

## 新证据与修复方向

此前 `v4_layering_inputs.json` 没有最终 Foot IK 函数图。本批从实际 ALS V4
AnimBP 补导出 `v4_foot_ik_inputs.json`：Foot IK、UpdateFootIK、SetFootLocking、
SetFootLockOffsets、SetFootOffsets、SetPelvisIKOffset、ResetIKOffsets，以及
EventGraph / UpdateGraph / 两个曲线辅助函数，共 11 个正式图。

原节点链为：局部转组件，左/右 ik_foot 锁定替换，左/右虚拟脚目标的世界
空间偏移，骨盆世界空间偏移，左/右虚拟膝目标偏移，左/右 TwoBoneIK，
组件转局部。具体差异已经确认：

- 脚锁替换 `ik_foot_l/r` 的组件空间平移和旋转，之后才在
  `VB ik_foot_l/r_Offset` 上应用世界空间地形偏移。
- 膝盖通过 `VB ik_knee_target_l/r` 定向，骨骼空间偏移分别为 UE 厘米
  `(20,30,0)` 与 `(-20,-30,0)`。
- 左/右腿 TwoBoneIK 允许伸展，MaxStretchScale 为 1.5，未覆盖的
  StartStretchRatio 原生默认值为 1；取得虚拟脚目标的旋转。
- 启用曲线用于虚拟脚偏移、膝目标和腿 IK。UE
  `AnimNode_SkeletalControlBase.cpp` 在 Update 阶段读取 AnimInstance 曲线，
  再对 ActualAlpha 做 0..1 钳制。后续接线必须核对上一完成曲线的生命周期，
  不能因为最终姿势使用完整曲线，就直接用本帧求值后的曲线替代 Update 输入。
- 现有 `AlsFootPlacementModel` 和 `AlsComponentPoseModifier.ApplyLeg` 采用
  自定义世界目标约束、不可伸展的链长夹取、当前膝盖/绑定极向量，以及
  半衰期骨盆平滑。它们是已有可运行实现，但不是以上 V4 原图节点链。

本批未直接把这条链的一部分混入旧约束路径。完整替换仍需脚锁/地形输入、
原生伸展求解、组件空间控制顺序及真实 Gather/Worker 事务一起接通。

## 已实现的骨盆函数

新增 `AlsPelvisIkInputModel` 与 `AlsPelvisIkInputCompiler`，后者核对原图的
数据连线、条件、执行顺序、函数与属性所有权、常量及默认值，读取实际
曲线名和插值速度。`AlsMovementGraphDefinition.Load` 已编译并持有此模型。

`SetPelvisIKOffset` 的行为：

1. 两次 GetCurveValue 的 float 结果先提升为 Blueprint double，求均值写入
   PelvisAlpha。属性函数不钳制曲线或均值；最终骨骼控制的 alpha 钳制是另一步。
2. Alpha 不大于零时，立即把 PelvisOffset 清零。
3. Alpha 为正时，比较左右目标的 UE Z；选择较低目标的整个向量。等高时
   选择右目标，保留其 X/Y，并非只取最小高度。
4. 比较目标与当前 Offset 的 Z，上行使用 10，下行/等高使用 15，执行
   原生 VInterpTo。先比较厘米空间距离平方与 `1e-4f`，否则使用 float
   `Clamp(DeltaTime * InterpSpeed,0,1)` 权重。这不是指数/半衰期插值。

模型保留 UE 世界轴、厘米和 double 向量，最终节点适配处再转换轴和单位。
状态只有函数实际写入的 Alpha 与 Offset；PelvisTarget 属于函数局部变量。
函数从调用方传入的已提交状态计算候选，没有内部时钟或隐藏历史。

此处“接入”仅指正式数据编译。Worker 仍使用旧脚部处理，新骨盆模型还没有
被正式每帧调用；不能把模型测试描述为 Demo 脚部行为已经替换。

## 构建、导出与验证

按照 `ue-diagnosing-plugin-build-load` 技能执行完整 Editor 目标构建与插件
审计。首次 UBT AppHost 误用系统 .NET，缺少 10.0；在本次命令环境中设置
引擎自带 `DotNet/10.0/win-x64` 后重试成功，没有安装或修改系统运行时。
完整目标、插件 DLL/manifest/receipt/BuildId 一致性检查通过，构建状态
fingerprint=`9A73FAE64658B708AF6F3DCFF0A6B733D994DAE2607EB9628B4E27F785441C35`。

使用现有原生插件、两个独立冷启动 Python commandlet 完成正式/重复导出，
均 exit 0，日志显示 AlsGodotExporter 正常加载，导出 `assets_saved=0`。
没有修改 UE 插件源码、描述符或 UE 资产。初次探索产物另含一个自动生成
图，正式导出已按 11 个必需图精确选择并校验。

两次完整原始 JSON 的字节摘要不同：UE 自动生成的 BreakVector 引脚 GUID
会变化。通过 `inspect-native-graph.ps1` 解析为节点/属性/默认值及
`节点名.引脚名` 连线后，Foot IK、SetPelvisIKOffset、SetFootLocking、
SetFootLockOffsets、SetFootOffsets、ResetIKOffsets、UpdateFootIK 七个图
均无差异；CDO 默认文本和 24 组原生数学样本一致。没有改写原始 GUID
以伪造字节级一致，也没有把全部 EventGraph 的语义等价性当作本批已证明。

- 原生 `MathLibrary.VInterpTo` 导出 24 组样本：30/60/120 Hz、10/15 速度、
  上下行及厘米空间小距离阈值两侧。模型结果与样本小数点后 12 位一致。
- 新 Import 专项 12 项通过；改用另一次冷启动导出文件，再次 12 项通过。
  包含函数连接/运算更改拒绝、全向量与等高选择、无预先钳制、关闭清零、
  上下行速度、输入拒绝与同一已提交状态重复计算。
- Debug 优化构建通过，0 警告、0 错误。
- 真实分层生产 parallel 600 帧通过：result=`946C9F387EA43F26`、
  fullPose=`6B39A65B68F3FAE3`、root=`A4F6C26CBAB8A0E7`，事件 28，
  lag/stale 0。摘要保持是预期结果：新输入模型尚未替换旧脚部消费者。

主要证据：`artifacts/foot-ik-input-export.log`、
`artifacts/foot-ik-input-export-repeat.log`、`artifacts/foot-ik-inputs-repeat.json`、
`artifacts/test-results/pelvis-native-input.trx`、
`artifacts/test-results/pelvis-native-input-repeat.trx`、
`artifacts/pelvis-input-worker-parallel.log`。

原生数学样本验证 VInterpTo，编译器核对 Blueprint 连线；本批没有新增
完整 SetPelvisIKOffset ProcessEvent 或最终 Foot IK 姿势的独立 UE 探针。

## 继续工作

下一项沿新正式原图完成 SetFootLocking / SetFootLockOffsets、SetFootOffsets /
ResetIKOffsets 与 UpdateFootIK 调度，补腿 IK 的原生伸展及虚拟骨控制链。
随后接入真实脚部物理查询、上一完成曲线、组件变换和统一候选提交，替换
旧脚部后处理；完成平台、支撑脚、左右反向和起步的 UE 多帧/人工验收。

普通分支根生命周期、默认 Demo 切换仍未完成；原 P5A 剩余项至 P7 保持，
音频暂缓。当前视觉问题、全 Core 既有 23 项失败、Import 分配稳定性和
p95 2.559ms 超过 2.5ms 的既有问题均未在本批关闭。
