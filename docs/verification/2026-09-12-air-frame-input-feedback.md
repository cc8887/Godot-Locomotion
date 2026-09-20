# 空中真实帧输入与已提交曲线反馈（第八十五批）

继续 P3 主移动输入与 P5A 候选提交。此批完成可复用 BaseLayer 所有者的空中
输入适配，不代表完整 Main/BaseLayer 姿势已替换生产 Worker/Demo 的入口。

## 来源、计算与提交

`AlsInAirUpdateCompiler` 严格检查正式 `v4_movement_runtime_inputs.json` 的
UpdateInAirValues：15 个非注释节点、14 条连接、变量/函数归属，以及顺序
FallSpeed -> LandPrediction -> LeanAmount。源 UpdateGraph 在空中分支更新
这些全局变量，不以 BaseLayer 内部来源节点是否被 Slot 隐藏作为更新条件。

`AlsInAirAnimationInputModel` 从实际 FrameInput 读取速度和角色旋转，将世界
速度转到角色局部空间，使用已编译曲线/公式和上一已提交全局 Lean 插值。
不使用视角旋转或逆组件缩放。落地预测读取同帧主线程胶囊查询快照。

`AlsAnimationInputFeedback` 按名称提取 Mask_LandPrediction，并保留曲线存在
标志。调用方必须提供上一已提交最终动画帧；所有者检查角色、代次和帧身份，
拒绝过期、未来或其他角色的反馈。该特定 GetCurveValue 消费者在曲线不存在
时读零，不将此规则扩展到其他曲线。冷启动不接受未提交掩码。

BaseLayer 的 PrepareFromFrame 在判断来源相关性之前计算全局候选输入。
隐藏来源时，节点历史和来源时钟仍冻结，全局空中输入继续更新；最终姿势和
两个子所有者验证/提交后才接受全局输入。取消、最终 Slot 失败均不污染已提交
Lean 或帧身份，同帧重试可复现。一个所有者不混用旧手工输入与新映射入口。

地面 Lean、其他地面输入和 JumpPlayRate 仍由调用方提供。初始 Lean 沿用零，
本批未新增源 CDO 结构默认值导出证明。最终曲线快照仍由外层最终姿势所有者
提供，不把 BaseLayer 中间曲线自动当作生产完整图的最终历史。

## 验证

- Import 专项 13/13：原生曲线样本、局部速度/缩放/视角、30/60/120 Hz 插值、
  曲线存在性与身份，以及源图连接/顺序/归属变异拒绝。
- Core 契约/Exchange 24/24，新增数值结构不含托管引用。
- Godot 构建成功，零警告、零错误。
- BaseLayer 映射入口 3360 帧：隐藏期间全局更新 145 帧，上一已提交掩码
  消费 14 帧，12 次最终 Slot 故障、24 次守卫检查及每帧取消/重试通过。
  旧入口独立 3360 帧回归通过。映射测试的物理、地面输入和 Slot 为受控夹具。
- 真实 Motor 在 30/60/120 Hz 分别产生 16/34/68 个空中输入/非零 Lean 帧；
  胶囊查询帧 13/28/55，非零预测帧 9/20/39。输入初始水平速度为 3.5 m/s，
  与上一批静止水平速度场景的预测帧数不直接比较。此场景反馈为受控缺失曲线，
  没有最终动画图。各频率 15 项几何检查通过，最大命中比例误差 8.970499E-06。
- 生产 single/parallel 各 180 帧通过，结果摘要 `21E164D829153157`、完整姿势
  摘要 `CF9225D4DE9B2C8B` 一致，各有 14 个已提交预测查询、10 个来源事件。
  这是现有 Standing 路径回归；生产日志仍明确标记 frame_adapter=not_connected。

日志：`artifacts/base-layer-air-input-runtime.log`、
`artifacts/base-layer-air-input-regression.log`、`artifacts/air-input-motor-30.log`、
`artifacts/air-input-motor-60.log`、`artifacts/air-input-motor-120.log`、
`artifacts/air-input-worker-single.log`、`artifacts/air-input-worker-parallel.log`。

本批未更改 UE 插件/资产、未启动 UE，未提交、回退或合并现有工作树。
没有测量整个新增输入所有者的分配预算，也没有进行人工截图或十分钟性能验收。

## 下一项：补齐地面全局输入，再接 Demo

核对发现，源 CalculateVelocityBlend 先归一化完整速度，再转角色局部空间，
按绝对 XYZ 分量之和缩放，拆分四方向；UpdateMovementValues 插值四个权重后
不再归一化。现有 StandingCycle.AdvanceDirection 使用水平二维输入、零速度
保留旧值、零权重回退 Forward，并对插值结果再次归一化。这是已确认的实现
差异，可能影响起步权重增长及斜坡；尚未证明它单独造成当前全部视觉问题。

应继续编译/实现 UpdateMovementValues 的完整顺序：VelocityBlend、Diagonal、
RelativeAcceleration/Lean、WalkRunBlend、StrideBlend 和播放倍率，补齐所需
默认值、真实运动输入与最终曲线历史；避免 Standing 图和全局输入各自推进
一份竞争历史。随后将统一 BaseLayer 输出接入 Worker/Demo，闭合最终 YawOffset、
动态 Layering/Add/LS、Aim/上身和 Foot IK/Lock/pelvis，再做实际移动多帧验收。

真实 Montage/ActionPlayer、隐藏 NotifyState 的正向 End 覆盖、完整最终曲线
消费者仍未完成。滑步、交错步/换髋、双臂姿态和平台问题保持开放；完整 Overlay/
道具、Mantle/Roll/Root Motion、Ragdoll/Get-up、完整 Camera 与 P7 继续原计划。
