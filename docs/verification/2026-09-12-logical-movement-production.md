# 第 106 批：Main Movement 的完整逻辑姿势进入 Demo

日期：2026-09-12。工作区：`D:/GodotALS-p5a-events-actions`。

## 结果与范围

默认 Worker/P4 Demo 的 Main Movement/BaseLayer 已切换至第 104/105 批验证过的
UE 原始来源。站立、停止、蹲伏、跳跃、坠落、落地、Detail、Lean、Turn Slot
和 BaseLayer Action Slot 在内部使用 79 根逻辑骨骼，包含 11 根虚拟骨骼。
Godot 实体模型保持 68 骨；只在最终写回进行名称映射投影。

这是原 P3/P4 完整性补完，不是完整 ALS 最终动画图验收。默认 Demo 当前最终
反馈仍止于 BaseLayer。BasePoses/Overlay/Aim/LayerBlending 外层尚未全部接线，
脚部最终约束、上身观感、起步滑步与换髋视觉仍未关闭。没有 commit/revert。

## 实现

- `AlsMovementGraphDefinition.RawSources` 在主线程加载经过哈希和来源闭包验证的
  76 资产源库，RuntimeContext 将同一实例交给各 Worker。75 player / 109 sample
  的身份、同步、通知、候选提交和失败回滚保留。独立诊断图使用相同正式来源，
  不回退到 FBX 动画采样。
- `AlsMovementPoseSources` 校验实体骨名称和父链，提供原生 79 骨参考姿势、父链
  与 Godot→logical 映射。`AlsMovementAnimationSource` 只保留一次采样结果，
  姿势和曲线共用同一时间；不会推进播放时间或创建播放身份。
- 所有移动缓存、状态混合、惯性化和 Slot 缓冲区均使用逻辑姿势。QuickFeet 与
  ChangeDirection 增加明确的逻辑骨掩码；未配置的虚拟骨保留默认混合权重。
  Stop 的分支掩码按原生逻辑父链包括脚部 Offset / knee target，不根据目标
  名称错误地归入 `VB foot_target_l/r`。
- 蹲姿 DiagonalScale 使用当前 FBX 骨空间常量 `(1.4, 1.4, 1)`，与旧 canonical
  空间 `(1.4, 1, 1.4)` 明确区分。原物理配置接口保留给既有消费者。
- Detail/Lean/Landing 使用源采样器已计算的 local/mesh additive，去除第二次
  参考姿势和参考曲线相减。曲线从来源、缓存、状态机、Slot 到 BaseLayer 保留
  presence，图内 ModifyCurve 才明确插入名称，排除派生根轨道冒充源曲线。
- Standing 保存同步后的实际 sample 秒数，姿势、曲线、Idle 都直接消费它；
  归一化 Times 仅保留用于诊断兼容。状态机曲线使用 Scale/Accumulate，
  TwoWayBlend 使用 Lerp，不能用同一个端点算法替换两者。
- 原生对照发现 DataModel 的 double 结束时间与 float-backed SequencePlayLength
  可以略有差异。通用提取层将原时间交给原生取键等价实现，不额外截断或循环；
  来源时钟和 `SampleSourceSeconds` 独立校验有限值、范围及资产长度。
- `AlsProductionMovementRuntime` 区分逻辑输出写回与实体回滚，后者直接恢复
  写回前的 68 骨快照。旧 AnimationPlayer Pose 输出也按同一映射投影，不能
  将虚拟骨作为 Godot 骨下标写入。

## 验证

日志位于 `artifacts/logical-movement-*.log`。

| 检查 | 结果 |
| --- | --- |
| Godot C# 构建 | 0 警告、0 错误，`build-final.log` |
| Core 相关回归 | 108/108，包含新的曲线 presence 检查 |
| Import 相关回归 | 340/340；补充实际 Stop 虚拟骨掩码断言后专项 18/18 |
| 原始来源 UE 对照 | 76 资产、1,648 姿势、130,192 骨、2,812 曲线值通过 |
| 附加来源 UE 对照 | 21 资产、1,092 姿势、86,268 骨、832 曲线值通过 |
| 新生产来源接口 | 上述夹具中普通 307 / 附加 273 个实际提取上下文直接对照 UE；先读曲线再读姿势，79 骨与曲线 presence 通过 |
| 来源采样并行/分配回归 | 两种模式各 4 所有者，单/并行各 36,480 采样位值一致；原生等价底层每种模式热采样 4,864 次分配 0 |
| 完整 BaseLayer 输入映射 | 30/60/120 Hz，共 3,360 帧，12 次晚期故障、24 次非法提交检查及重试通过；明确断言共享源库、79 逻辑骨/68 实体骨 |
| Roll Action Slot | 1,050 帧/1,050 次重试，354 帧完整 Roll 姿势，28 次晚期故障通过；此项验证组合和事务，源采样正确性由 UE 夹具独立验证 |
| 外围曲线反馈接口 | 1,050 帧/重试，45 次晚期故障，821 帧反馈不同于局部输出，420 帧蹲姿非零 Yaw 通过 |
| 实际 Worker/Demo | single/parallel 各 600 帧，姿势/事件/结果一致，lag/stale=0；包含跳跃、直接坠落、落地、蹲伏与回滚 |
| 实际渲染移动 | 720 帧/120 截图，552 移动帧、58 转身帧，成功退出；已查看连续图 |
| Git 格式检查 | `git diff --check` 通过；既有 CRLF 提示不属于 diff 错误 |

两种生产模式的最终摘要：

```
result    EAAF62E4D0A80A76
full_pose 3103E3B355BF1F3B
root      A4F6C26CBAB8A0E7
events    28
```

相对于第 105 批，完整姿势摘要已变化，角色根摘要相同；不能继续要求旧 FBX
采样的姿势摘要不变。这里只证明新入口确实执行、双模式一致及回滚有效，不
将摘要一致视为完整 AnimBP 与 UE 的最终姿势等价证明。组件 smoke 中历史
`demo=not_connected` 标签描述其夹具运行方式，真实 Demo 接线以 Worker 回归为准。

## 视觉发现与后续

捕获目录：`artifacts/logical-movement-visual-106`。连续截图中没有发现突然全腿
翻转，但双臂仍收拢；不能据此关闭上身问题。诊断记录换髋启动帧 309/485、
等待帧 42、最多 2 个活动过渡；这说明存在条件等待，不代表换向视觉已经验收。
低位脚起步水平位移峰值约 7.03 cm/帧，仅为旧诊断代理；它没有确定接触/支撑
窗口，不能作为真实脚锁误差或合格阈值。

下一项先把已完成的 BasePoses owner 与完整 79 骨移动输入一起接到真实
Overlay/Aim/LayerBlending，逐项补源资产、节点更新和 Slot 所有权；将上一帧
反馈迁移为外围最终层输出。然后闭合 Foot IK/Lock/pelvis/平台，执行源图条件、
多相位左右换向、起步和上身的 UE/人工整链验收。以上属于原 P3/P4 修复，
不会推迟到 Mantle 或 Ragdoll 之后。P5A 剩余项、全部 Overlay/道具玩法、
P5C/P6/P7 继续保留，音频按用户要求暂缓。
