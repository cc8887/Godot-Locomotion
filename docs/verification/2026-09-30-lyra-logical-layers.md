# Lyra 81 骨 Layer 与双手控制运行接入

本批继续在主目录实施，Godot 4.7.2 .NET / 本机 UE 5.8.1。独立 `lyra_unarmed_demo.tscn` 已默认消费 69 raw / 81 logical 源动画，经过当前已实现的共同 Layer 和双手控制，最终仅发布原 ALS 68 根蒙皮骨。使用原 ALS Mannequin 网格、骨序和 skin 权重。

已关闭本批的 **81 骨跨层姿态传递、CopyBone/左手 IK 运行接入和有限场景验证**。这是独立 Lyra 示例的阶段交付；完整 Lyra 主图、连续原生整图对照、人工玩法矩阵和性能验收仍开放。

## 资源与骨架

沿用 [逻辑源资源](2026-09-30-lyra-logical-controls.md) 的 234 条源：189 普通动画和 45 AimOffset 样本。前 68 个逻辑索引映射原 skin；weapon 在索引 68，新左手武器空间 VB 在索引 80。两个控制通道参与完整姿态求值，最终不写入 skin。

VB 在源关键帧阶段生成，随后独立插值/混合。不能在 Aiming 或最终骨骼姿态之后重新 FK 构造左手目标。HipFire/上身遮罩保留 Manny 原控制骨权重；LeftFingers 控制骨权重为零，原 ALS 独有虚拟骨没有 Manny 遮罩条目，权重为零。

最终独立复核：485 个 GASP58 `.uasset` 包、原 11 个依赖 JSON、全部 234 个 clip 的 SHA-256 一致。此前 calibration/catalog/sampling/native 文件字节哈希均未改变。新增 `logical_controls/hand_chain_native.json` 及其 48 条输入请求绑定字节哈希；不得统一格式化这些文件。导出仅读取源资产、创建 transient Skeleton 和节点，保存资产数为零。

资源仍被 Git 忽略；仅检出代码不足以运行。已有逻辑源导出步骤见上一验证记录；新增手部 oracle 步骤是先构建外部 exporter，运行 `lyra_logical_layers_smoke.tscn -- --write-logical-hand-requests`，再运行 `scripts/export-lyra-logical-hand-chain.ps1`。普通资源加载同时核对原生 RootYaw 节点设置，故需保留本批 oracle。

## 运行求值顺序

```text
完整 raw 源采样/既有播放过渡，输出 81 local TRS
  → 非 Idle HipFire（mesh-space 上身遮罩）
  → LeftHandPose（local 遮罩）
  → Aiming（Relaxed/ADS Mesh Space additive）
  → FullBodyAdditives × 0.65
  → RootYaw（native 根骨 Z 轴）
  → Hand IK Retargeting
  → CopyBone：VB IK_Hand_L_weaponSpace → ik_hand_l
  → Right TwoBoneIK
  → Left TwoBoneIK（取目标旋转）
  → native→Godot 局部 TRS 转换，只发布 skin 68
```

`LyraLogicalSourcePlayback` 复用当前 Motion 宿主的已选择播放时间，当前源不新增独立时钟；退出源拥有独立 occurrence sampler/scratch 和过渡时间。新源中断过渡时保留既有退出源权重。当前过渡沿用示例的线性 crossfade，并非原 UE 图惯性化。移动源根位移沿用现有 in-place/胶囊移动策略。

`AnimationPlayer` 仍负责既有时间适配、镜像和可见基础姿态恢复；最终 Layer 输入来自 raw 81 骨源。每个 pose 入口写入独立输出缓冲，最终只有一个 skin 发布点。前帧发布姿态在下一更新前恢复，拒绝短缓冲或输入/输出别名。

CopyBone 无条件执行，复制组件空间位置与旋转，不复制 scale。左右 TwoBoneIK 使用原 Joint Target/End Effector BoneSpace 设置；左手 `bTakeRotationFromEffectorSpace=true`。Unarmed 的原 CDO 禁用双手 IK，Pistol/Rifle 按 `DisableHandIK` 与 `DisableRHandIK/DisableLHandIK` 曲线门控。左手姿态遮罩禁用量与左手 IK 禁用量保持不同字段。

## Animation Interface 与 Linked Layer 边界

沿用 [Linked Layer 合同](2026-09-30-lyra-linked-layer-contracts.md)：编译 `ALI_ItemAnimLayers` 的 typed 姿态/参数入口，14 个原调用节点共同绑定 `ItemAnimLayers` 实例。HipFire、LeftHandPose、Aiming、FullBodyAdditives 和手部控制由该共同实例持有。Aiming 的两个参数保留 double 合同；资源 bank 只持有不可变资源，实例持有可变层状态。

当前 Unarmed/Pistol/Rifle 共同实例配置 81 骨算子。相同 Animation Class 重绑返回原实例，保留 AirIdentity、权重和可见姿态；不同 class 创建新实例并按既有换层规则重新初始化。读取 CDO 时按 class path 查找，避免用显示名称代替绑定身份。

这里没有关闭原 Linked Layer 全部执行语义。移动源时钟仍由示例主 Motion 宿主持有，尚未迁入原各 Linked 节点/共同 owner 的完整状态图和 Sync Scope。完整曲线/attribute 容器、多源通知、Montage/Slot、图级惯性化及取消重试事务仍待。Shotgun/Feminine 目前仅左手参考姿态，未具备完整移动/Aiming 资源和玩法验证。

## 原生证据与运行验证

新增 UE 原生 helper 从当前 provider class 的继承 CDO 复制实际四个手部节点，把 BoneReference 按名字绑定 transient ALS 81 骨 Skeleton。在 **同一个 FCSPose** 内依原顺序执行并保存每阶段全部 81 根骨。FKWeight 使用当前 provider 设置，局部混合继续调用原引擎实现。

48 条输入来自 Godot 实际 81 骨层输出的手控前姿态：三配置共 12 姿态，alpha 0 / 0.25 / 0.5 / 1。逐阶段比较 48 × 4 × 81，包含 16 个有效左手部分权重样本；门槛保持 position `1e-8 cm`、单位 quaternion `1e-10`、scale `1e-12`。

```text
LYRA_LOGICAL_HAND_CHAIN_OK cases=48 stages=4 logical=81 partial=16
positionCm=5.728578676879116E-14
quaternion=3.0103685668981314E-15 scale=0
native=oneFCSPose left=takeRotation
```

这证明所测输入的四节点手控整链，不是 UE 原主图从 locomotion 输入到最终姿态的连续 oracle。936 组独立原生 raw 源采样回归仍通过，位置最大 `1.4163191318420816e-13 cm`；Layer 上游混合本批未新增 UE 全图 oracle。

| 验证 | 结果与范围 |
|---|---|
| 完整逻辑层受控 smoke | 三配置 × 30/60/120Hz，共 1260 帧；117 混合帧、45 坏缓冲拒绝，81 骨输入隔离/最终 68 骨发布/恢复通过 |
| 原 Linked 绑定回归 | 实际 UE 八步生命周期对应 Godot，14 hooks / 四 owner / 四同类复用 / 六坏合同拒绝通过 |
| 原 68 骨缓冲回归 | 1260 帧 / 30 坏缓冲拒绝通过，保留局部兼容入口 |
| Pistol ADS 回归 | 60Hz 移动中混合与通知通过，47 个中间权重帧 |
| RootYaw 世界朝向 | 60Hz Idle/Start/Cycle/Stop 通过；120Hz 站立重复转身 180°、30Hz 蹲姿左转 90°通过 |
| Debug / Release Optimize | 均 0 错误 / 0 警告 |

同一角色六次 Unarmed/Pistol/Rifle 换层、蹲伏/空中/ADS/转身、同类重绑及落地 AirIdentity 保留的实际场景：

| Hz | 物理帧 | 完整 source/Copy/左右 IK 求值帧 | 双手有效应用帧 | 源过渡帧 |
|---:|---:|---:|---:|---:|
| 30 | 435 | 436 | 391 | 157 |
| 60 | 870 | 871 | 780 | 335 |
| 120 | 1740 | 1741 | 1560 | 694 |

最终 14 份指定通过日志 marker 核对完成；Godot 最终日志无 ERROR/WARNING。受控 smoke 的 skin 发布允许 `2e-5 cm` 的 float 消费边界，与上面的原生 double 手控门槛分开；没有改原生误差阈值。

60Hz 实际 GPU 渲染另跑 870 物理帧，五张 Cycle/Crouch/ADS/Jump/Turn 图全部逐张检查：ALS 人物和双臂姿态可见、未见骨骼爆开。画面没有实际武器网格，因此未验收武器接触、握持或附着。

证据位于 `artifacts/lyra-analysis/`：`logical-hand-chain-smoke-first.log`、`logical-hand-chain-export-first.log`、`logical-layers-smoke-final.log`、三频率 Rifle 日志及五张 `logical-layers-rifle-runtime-*.png`；最终哈希/日志/构建汇总为 `logical-layers-final-verification.json`。

## 修正与保留失败

首次编译出现数组 `CopyTo` 重载歧义，改为显式 Span；保留 `logical-layers-build-hands.log`，最终构建通过。

首次正确采用 native 根骨 Z 轴后，旧 RootYaw 朝向门禁失败：门禁把 Godot world Y 旋转直接乘到 Skeleton local quaternion，漏掉导入模型父节点的实际坐标基变换。实际父节点基为 `X=(0,0,1), Y=(1,0,0), Z=(0,1,0)`；native local Z 对应世界 Y。修正观察为 `Skeleton.GlobalTransform` 的旋转乘 local root，原角度阈值 0.03 保留。

生产根骨旋转使用 native Z。当前 main AnimBP 原 RotateRootBone CDO 只读导出确认 MeshToComponent 为 identity、RootMotion attribute 旋转关闭，加入加载门禁。首次失败日志 `logical-layers-root-yaw-first.log` 保留；当时停止仅本次持续报错的测试进程。修正后的 world-facing 和 90°/180°转身运行通过，未修改 oracle 或提高容差。

## 下一步与未验收范围

继续迁移原 Main/Linked 状态图、源播放器 owner/Sync/Notify、原生惯性化和完整距离匹配，再接 Stride/Orientation Warping、FootPlacement/LegIK、Montage/Root Motion、最终曲线与 attribute。需要从同一输入采集 UE 连续主图姿态/曲线/通知并逐帧比较。

本批未做完整人工矩阵、十分钟稳定性或性能验收，未接实际武器网格，也未关闭原 ALS R2–R7。UE 工程配置和已有资产没有修改；外部 exporter C++ helper 本批构建后参与只读导出。
