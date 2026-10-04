# Lyra Layer 输入姿态缓冲与最终发布

2026-09-30，在主目录 `.` 实施，Godot 4.7.2 .NET，仍使用 ALS Mannequin 的 68 根物理骨。本批将已实现的层后处理改为显式输入/输出姿态，并接独立 Lyra Demo；完整主图、曲线/属性、共享 Sync/Notify 和 IK 链没有关闭。

## 实现范围

`ILyraAimingLayer` 新增 `EvaluatePose(InputPose, AimYaw, AimPitch, Weight, OutputPose)`。HipFire、LeftHand、RootYaw 与 Hand Retarget 同样接收只读输入和独立输出，不在这些方法内读取或写入 Skeleton3D 的姿态。全部入口在权重为零或旁路时仍检查完整骨数和缓冲重叠，防止权重变化后才暴露别名写入。

`FullBodyAdditives` 的编译签名没有输入姿态，因此返回独立 additive 缓冲：位置/缩放为零，旋转为 identity。保留原 Identity→AirIdentity、落地边为 false 的状态行为。它不再在生产求值中自行读取骨架并输出绝对姿态。

`LyraLayerPosePipeline` 执行当前已实现部分：

```text
AnimationPlayer 移动输入
→ 非 Idle 的 HipFire
→ LeftHand
→ Aiming
→ ApplyAdditive(FullBodyAdditives, 0.65)
→ RootYaw
→ SkeletalControls 首个 Hand Retarget 节点
→ 一次最终层姿态发布
```

原主图快照 `..\GASP58\Saved\BP2DSL\Exports\20260913-134530-535215\Lyra\AnimBP2Lisp\Game\Characters\Heroes\Mannequin\Animations\ABP_Mannequin_Base.animlang` 的 404–430 行确认 Aiming→Additives→RootYaw→SkeletalControls 的顺序及 `0.650000`。原实现将 Additives 放在 RootYaw 之后，本批纠正。当前源 additive 是 identity，不能从可见姿态推断顺序或权重正确，因此另用非 identity 受控输入检查 0.65 的平移/缩放效果。原主图 `.uasset` 哈希与上批编译契约导出相同，见最终验证 JSON；本批没有重新启动 UE 或导出原图。

宿主下一帧开始或换类时只恢复一份完整的层前基底，避免前一帧瞄准/根骨/手部修正进入 AnimationPlayer 的稀疏轨道或 crossfade。同类重绑仍在恢复之前返回，保持可见姿态和同一 ItemAnimLayers 实例。源 AnimationPlayer 自身仍直接采样到 Skeleton3D；“一次发布”只指本批层后处理，不代表整个动画系统已改为独立缓冲求值。

原 `Apply/RestoreBase` 方法保留为组件测试适配器，调用同一缓冲算子。生产 Motion 不再逐层调用它们。既有 `AppliedFrames` 统计保留活跃求值含义，另加 `PoseCommitFrames` 检查宿主最终发布次数。

## 验证

最终 .NET 构建 0 警告/0 错误，Godot editor import 退出 0。12 个运行日志全部退出 0，各一个预期成功标记，无 Godot ERROR/WARNING；清单见 `artifacts/lyra-analysis/pose-buffers-final-verification.json`。

- 新 `lyra_pose_buffers_smoke.tscn`：Unarmed/Pistol/Rifle ×30/60/120Hz，各两秒，共 1260 帧。故意将 Skeleton3D 的 68 骨可见位置偏移后，仍以独立 InputPose 求值，检查输入和骨架逐值不变，再与现有骨架适配器结果对照。测试覆盖 HipFire 权重、瞄准混合、站蹲、空地与手部禁用量变化；完整发布及重复恢复逐值返回源基底。30 个完整大小/重叠/旁路/NaN 拒绝通过。
- 原 Unarmed AimOffset 静态 UE oracle：16 组仍通过；最终叠加位置差最大约 `3.93e-6 cm`、旋转差 `3.67e-7 rad`。Pistol/Rifle 目录和原权重输入通过。
- Hand Retarget 324 组原生对照仍通过，Godot 位置差最大 `1.19e-7 m`；五配置左手遮罩组件 2100 帧、RootYaw 组件回归通过。
- 原实际 UE 八步绑定 oracle 的 Godot 回归通过：14 hook、四 owner、四次同类复用、六坏合同拒绝。
- 独立 Rifle 换层场景 30/60/120Hz 分别 435/870/1740 物理帧，最终层发布 436/871/1741 次（包含初始化），均与 Additives/LeftHand/Hand Retarget 求值次数相同；六次换层、六次 Cycle 换源、47 条 Rifle 通知、同类重绑及真实落地保留 AirIdentity 均通过。Pistol 60Hz 场景回归通过。

构建/import 日志为 `pose-buffers-build-final.log`、`pose-buffers-import.log`；12 个运行日志和各自成功标记列在最终验证 JSON 中。最终缓冲测试为 `pose-buffers-smoke-final.log`，首轮日志同时保留。本批首轮构建/测试成功，未改变原生门槛。

## 保持开放

这里的缓冲只有 68 骨 local TRS，没有完整 curve/attribute 容器。两个 Disable 曲线仍采用既有上一源采样反馈；尚非最终混合曲线、Montage 或跨源反馈。Aiming 编译参数仍记录为 double，实际 Godot 采集/取样仍为 float。

各移动入口仍是现有单播放器和部分规则，HipFire 也未变成完整移动层拓扑；UpperbodyLowerbodySplit、原 Slot/Montage、惯性化、跨图共享 Sync/角色 Notify、层切换完整取消事务、Warping、武器空间虚拟目标、双手求解和 FootPlant 均待接入。缓冲求值会推进当前层状态/计数，不承诺失败后的整角色回滚。

本批未新增完整 UE 主图连续 oracle、实际渲染截图、人工或性能验收，也未修改 UE 工程/资产和普通 ALS Demo。原 ALS R2–R7 与用户暂缓项保持开放。
