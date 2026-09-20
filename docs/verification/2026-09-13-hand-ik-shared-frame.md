# 原图双手 IK 与真实组合验证

日期：2026-09-13。第一百二十六批，承接外层 Aim/脊柱。

## 实现与来源

`AlsHandIkCompiler` 校验 ALS V4 原 AnimGraph 的实际节点身份与连线：
TwoWayBlend_6 → LocalToComponent_1 → 左 TwoBoneIK_5（26）→
右 TwoBoneIK_2（24）→ ComponentToLocal_1 → Foot IK。

左右手目标分别为 `VB RHS_ik_hand_l`、`VB LHS_ik_hand_r`，BoneSpace、
零偏移；肘部目标是各自 hand 的 ParentBoneSpace 零偏移。原节点不拉伸、
允许 twist、采用目标旋转，Alpha 来自 UpdateLayerValues 输出的变量。
它们等于上一已提交曲线中的 Enable_HandIK 与对应 Layering_Arm 的乘积。

直接核查本地 UE 5.9：`AnimationCore/Private/TwoBoneIK.cpp`、
`AnimGraphRuntime/Private/BoneControllers/AnimNode_TwoBoneIK.cpp`、
`Engine/Public/BonePose.h`、Core 的 Vector/UnrealMath 实现。

新增 `AlsTwoBoneIk` 在 UE 轴系和厘米单位中求解，使用当前组件姿势的骨长，
保留原不可达目标、重合目标、共线肘部参考和反向旋转处理。没有添加原版
不存在的内侧可达距离钳制。Godot/UE 的位置和四元数转换放在调用边界。

新增 `AlsComponentPose` 保留惰性组件空间缓存。先左后右，在同一缓存中
求值；第一只手可能改变另一只手关联目标的祖先，不能预先冻结两只手的
目标。写回骨链前，将已缓存的后代恢复到局部空间。部分权重先完整求解，
再把骨链转为局部姿势混合；最后转换输出时遵循原归一化顺序。

`AlsHandIkRuntime` 使用每角色预分配缓冲，不推进来源时钟或发出事件，
保留输入曲线及 presence。已在 `OverlaySharedFrameSmoke --hand-ik`
中接到实际上身输出后，准备、求值、验证、提交、取消均与上游组合一致。
最终手部输出曲线反馈到下一帧；冷启动仍为空曲线，不人为填 IK 权重。

## 验证结果

- Debug 优化构建 0 警告、0 错误。
- 新增 15 项编译/求解/顺序/部分权重/回滚/零分配测试通过；连同 Aim、
  LayerBlending、BasePoses 回归共 204 项通过，1 个既有普通 Editor 检查跳过。
  手部热循环 2,000 次零分配；不代表整个动画系统已达到最终性能预算。
- 30/60/120 Hz 真实资产组合共 1,260 帧，逐帧取消和重试一致。
  左手相关 828 帧、右手相关 105 帧、部分权重共 156 次、双手隐藏 327 帧。
  手部控制改变 5,477 个逐帧局部骨骼结果；这是执行覆盖计数，不是视觉质量指标。
- 新增 10 次手部入口晚期失败，上游完整求值后仍未泄漏已提交状态；
  原 26 次 Overlay、11 次 LayerBlending、11 次 Aim 失败检查保持。
  全组合 33 个来源事件，其中 6 个蒙太奇资产事件、3 个状态通知/播放请求。
- 原 `--aim-layer` 1,260 帧回归通过。生产 single/parallel 各 600 帧通过：
  result=`EAAF62E4D0A80A76`、fullPose=`EE519FBE375F4A2B`、
  root=`A4F6C26CBAB8A0E7`，28 个事件，lag/stale 为 0。

初次故障断言误将 BasePoses 的当前候选 State 与上一已提交状态比较，
现明确暴露并读取 CommittedState，检查没有删除。初始场景右手相关帧为 0：
Rifle 和到达的 Bow 状态没有覆盖右手控制，因此在最后半秒增加原 Barrel
Overlay，用其原始曲线覆盖右手；没有强制填曲线或取消右手覆盖要求。

最终证据：

- `artifacts/test-results/hand-ik-verified-final.trx`
- `artifacts/hand-ik-shared-verified.log`
- `artifacts/hand-ik-aim-regression.log`
- `artifacts/hand-ik-worker-single.log`
- `artifacts/hand-ik-worker-parallel.log`

`hand-ik-shared-frame.log`、`hand-ik-shared-diagnostic.log` 为早期断言失败记录；
`hand-ik-shared-final.log`、`hand-ik-coverage-diagnostic.log` 为右手覆盖不足的
中间记录，均不代表最终结果。

## 完成边界与下一项

受控真实组合现在到达 Foot IK 输入，但默认 Demo 尚未改为完整上身输出。
生产仍为 75/109 BaseLayer；223/257 的完整上身来源绑定仅在组合入口验证。
本批没有新增独立 UE 手部/最终全图输出探针，没有人工视觉验收。
不能据此宣称双臂、侧身、换髋、交错步或起步滑步已经修复。

继续原 P3/P4 的最终根初始化/重入、缓存生命周期与统一生产帧所有者，
完成 Worker/Demo 最终姿势和曲线发布，再闭合 Foot IK/Foot Lock/pelvis/
平台及 UE/Godot 同输入、同脚相位多帧对照。当前移动视觉问题在这一阶段验收，
不推迟到 Mantle 或 Ragdoll 后。

原 P5A 剩余通用事件/动作、P5B Overlay/道具玩法、P5C Mantle/Roll/Root Motion、
P6 Ragdoll/Get-up/Pose Recovery/完整 Camera、P7 十分钟预算均继续保留。
已知全 Core 23 项失败、Import 分配稳定性及 p95 2.559ms > 2.5ms 未关闭。
音频按用户要求暂缓。
