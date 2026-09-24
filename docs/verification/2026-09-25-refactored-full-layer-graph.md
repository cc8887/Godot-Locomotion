# 完整 Refactored Layering 图编译

在 `.` 的 `main` 实施。新增 `AlsRefactoredLayerGraphCompiler`，将实际 Layering 的 94 个编译节点转成共享 LayerBlending 图定义；保留原生节点身份、11 缓存顺序及所有运行分支，排除清单中 12 个未编译编辑节点。

## 接入范围

- 两 linked 输入 Locomotion Input/Overlay Input，以及真实 Stand/Crouch 的两个 SequenceEvaluator 输入；基础资源和固定帧策略经过已有 BasePoseCompiler 校验。
- 完整 local/mesh dynamic additive、additive application、TwoWay、MultiWay、骨骼分层、缓存、曲线尾部与 root。
- Head/Spine/ArmLeft/ArmRight/Pelvis/Legs/Curves 七个 Slot 保留实际标签和宿主回调，不伪造现有 V4 Montage bank 已支持全部新 Slot。
- 每条普通 pose edge 同时验证原生 compiled linkId/sourceLinkId 和编辑图连接（穿过 reroute）；UseCache 由清单编译器交叉验证实际保存节点。
- 原生 PropertyBindings 的 LayeringState 属性映射至 Refactored 输入；PoseState.StandingAmount/CrouchingAmount 映射过去已提交曲线中的 PoseStanding/PoseCrouching。本地 `AlsAnimationInstance.cpp::RefreshPose` 确认这两项读取原值、缺失为零，无额外 clamp。
- 校验实际 alpha scale/clamp/interpolation、LOD、子节点重置、Slot 源更新、mesh/root-space、curve mode 和 callback 等支持边界，未知或未消费的属性绑定拒绝。
- 七个实际 LayeredBlend 的 79 骨骼静态过滤表，共 553 个来源索引与权重，与 UE 导出的 perBoneBlendWeights 逐值一致。保留真实四个手部虚拟骨名称，不用 V4 别名替代。

输出是完整 linked Layering 图定义，宿主仍需提供 linked 输入、基础姿态和真实 Slot 回调；不包含外围 Head/View、Control Rig、Ragdoll 图。

## 验证

- 实际图全节点/输入/7 Slot/两个 MultiWay 资产索引与 PoseState 绑定闭包检查通过。
- 真实基础姿态及原始 79 骨架布局驱动整个共享 runtime。受控 linked 输入在站立/蹲姿及 layer amount 0/1 间切换；Slot passthrough 和全部 Slot 覆盖两场景，各连续 120 帧，每帧 Cancel 后同身份重试、比较完整 pose/curve 后 Commit。覆盖场景实际访问七个 Slot，并验证覆盖时不求值其源。
- 最终曲线的姿态控制和六 Slot 曲线 presence/value 断言通过：透传按原生尾部重置为零，覆盖按 Slot 输出为 .5。
- 错 edge、修改 scale、always-update-source、root-space、回调及篡改原生骨骼过滤表均拒绝。
- Import Release `AlsRefactoredLayer|AlsRefactoredBasePose|AlsLayerBlending` 最终 **63 通过、1 既有条件跳过、0 失败**，含新增 9 项。跳过项 `NormalEditorRepeatsTheConsumedGraphAndInputSemantics` 缺 `ALS_LAYERING_REPEAT_FILE`。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 warning、0 error。没有 Core 生产改动、UE 插件/资产重导、全量测试或新 Godot 场景验收。

首次全部 Slot 覆盖测试失败，诊断定位为测试 Sink 的 dangling else：Pose 曲线错误地写到 Overlay 分支，Locomotion 没写。补大括号后通过；生产 runtime 未因此改动或放宽断言。临时 trace 已撤除，失败与诊断 TRX 保留于 `artifacts/refactored-full-layer/`，最终以 `verified.trx` 为准。早期 `first.trx`/`final.trx` 的通过不覆盖此修正后的完整测试范围。

## 后续与边界

当前已把整个真实 Layering 图跑通，但测试的 linked inputs/Slot provider 是受控夹具，不是完整 UE AnimInstance 或普通 Demo。接下来需要完整 Layering 原生运行逐帧对照、生产宿主 Refactored 骨/曲线布局适配与区域 Slot 分发，再接 Head/View 和 Mantle。不能用静态过滤一致或本批组合回归宣称视觉 1:1。

既有物理稳定性 9/12、Flail 0/3、复杂相机碰撞、Mantle 探测/motion 生命周期和最终十分钟预算仍未关闭。用户 project.godot/P4 规划/头颈诊断/uid 保留；头颈拉伸、道具物理、音频继续暂缓。
