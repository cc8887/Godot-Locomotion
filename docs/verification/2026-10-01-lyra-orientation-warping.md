# Lyra OrientationWarping 与 ALS 脊柱适配

2026-10-01，直接在主目录实施。沿用 GASP58 原资产、安装版 UE 5.8.1、Godot 4.7.2 .NET 和 ALS 68 skin / 81 logical 布局。在上一批 [Sequence RootMotion 属性](2026-10-01-lyra-root-motion.md) 后接原 OrientationWarping，当前输出为 `AfterOrientation` 局部组件；Stride、完整 Main 输入/遍历和生产入口尚未接入，整体移植目标保持开放。

## ALS 资源适配

继续使用原 ALS 人物模型、skin、骨架与逻辑控制骨，不修改或保存源 Skeleton/Sequence。原 Orientation CDO 的脊柱列表是 `spine_01/02/03/04/05` 加 `ik_hand_root`。ALS 没有 04/05，显式映射到 `spine_03` 后去重，得到三节 spine 加独立 hand root 分支。实际 UE 节点在 transient ALS81 hierarchy 上重新 CacheBones；Godot 按原祖先分支算法得到三节 spine 各 1/3、独立 `ik_hand_root` 为 1。

这证明三节 ALS 脊柱可以运行该原节点，但分配范围与五节 Manny 不同；对照基准明确采用同一 ALS 适配，不能据此声称两种人物外形完全一致。IK foot root/左右 IK feet 使用原 CDO 骨名，最终 skin 仍为原 68 骨。

## 原节点语义与候选宿主

实际配置为 Graph / Z / ComponentTransform，minimum root speed 80 cm/s、方向反转门槛 90°、distributed alpha 0.75、rotation interp 10、counter interp 45、max correction 180°、max counter compensation 45°。global weight scaling 关闭，alpha 为原 Float 路径；三 provider 的 prediction asset 均为空、time 为 0。

新增 `AlsOrientationWarping` 按安装版源码实现方向的组件空间转换、根运动方向反转、低速门控、上一 root rotation 补偿、float 插值和原组件骨写入顺序。RootMotion 属性的 translation 按目标角旋转；pose 使用插值后的角并施加实际 alpha，两者不能混为一个结果。alpha 过滤不推进节点更新历史；跨隐藏/过滤更新间隙按原 update counter 重置。原 Reset 只清 First/Angle/Direction，保留 RootRotation/CounterTarget，已按此实现。

`AlsComponentPose.SetComponent` 对应原 `FCSPose::SetComponentSpaceTransform` 的直接写入，保留已缓存的后代；原其它 controller 的 Apply/LocalBlend 行为保留。骨骼转换为 local 后随 typed root 属性一起输出。

`LyraCycleLayerPoseHost` 显式启用 generated root + Orientation controller，在真实共同 Sync 候选后采样原 Cycle/HipFire，再做 LayeredBlend 和 Warp。Orientation 历史为候选值，source 预校验/提交成功后才发布。每帧求值后取消重试、隐藏、旧/重复/未求值、不同 source clock/root delta 等操作保持原事务门禁；取消不改变已提交 Warp history。没有新增 Skeleton3D writer 或独立时钟。

## UE 原生采集

外部 exporter 使用实际 provider-enabled Sequence leaves → 原 LayeredBoneBlend → 原 local-to-component converter → 复制实际继承 CDO 的 OrientationWarping → 原 component-to-local converter。节点调用实际 UpdateInternal/Evaluate，并保留跨帧状态、组件变换、delta 和 update counter；补齐真实空 `FAnimationUpdateSharedContext` 以满足节点调试消息路径。

Source previous/delta 来自上一批真实共同 Sync oracle；本批方向/组件相对旋转/alpha 是明确的受控节点输入，没有执行完整 Main 的 compiled exposed handler。因此这是新原生 Warp 连续输出对照，不能称为完整 Main 原生连续验收。

Unarmed/Pistol/Rifle × 30/60/120Hz 九条轨迹，共 3,780 物理帧、3,528 活跃帧，包含：

- 1,401 帧部分 alpha、651 帧 alpha=0、1,749 帧 world direction、2,571 帧非单位组件相对旋转。
- 252 隐藏帧、18 次显式重初始化、原曲线/双源/低权重/不同 delta；角度覆盖反转门槛两侧、正负跨 180° 和 360°/720°。
- 相较 Warp 前，2,661 帧姿态、23,632 个骨结果和 2,395 帧 root translation 实际改变。独立 verifier 对这组变化数量设门禁，防止旁路成为假通过。

修正后的两次独立 UE 进程均退出 0，新资源语义一致并保留文件原字节。最终日志无 Python Error/ensure/assert；既有项目插件/GameplayTag 警告保留。489 个源包哈希未变，所有新增 Skeleton/Sequence 为 transient，`assets_saved=0`。仅构建外部 exporter，没有修改 GASP58 工程配置、源码或引擎源码。

## 最终验证

Godot 用自己的原 source callback/共同 Sync 输出驱动 Warp，不读取期望姿态作为运行输入。每个活跃帧完整比较 81 骨、曲线、authored integer attributes 和 generated RootMotion，再取消重试；共拒绝 24,129 次无效操作。

```text
LYRA_CYCLE_LAYER_POSE_GODOT_OK frames=3528 bones=285768 curves=105 attributes=14112
dataProbes=72 positionCm=1.0772985490388227E-13
quaternion=9.354905487120434E-16 scale=0 retry=true stage=AfterOrientation

LYRA_ORIENTATION_GODOT_OK frames=3528 bones=285768 rootPresent=3255
rootPositionCm=0 rootQuaternion=0 rootScale=0 retry=true
stage=AfterOrientation production=false
```

保持原 `1e-8 cm / 1e-10 quaternion / 1e-12 scale` 门槛，没有放宽或在比较前 normalize root quaternion。独立 verifier 还确认 Warp 没改变曲线、整数属性、root presence/rotation/scale/身份，alpha=0 保留原 root 属性。

Core 7 项新测试与既有 RigTwoBoneIK 2 项通过；既有 Standing 原生连续对照 6 项通过，均无失败/跳过。Debug 与 Release Optimize 均 0 错误、0 警告。原 RootMotion 场景、authored pose 场景和 source 隐藏重初始化 3,780 帧回归通过。旧 Demo 在 60Hz 跑完 870 物理帧、六次换层、871 次 pose 提交；这验证旧入口回归，不能作为新 Warp 生产接入证据。上述 Godot 场景全部退出 0、无 ERROR/WARNING。本批没有新渲染、视觉/性能或全量测试。

| 新资源 | SHA-256 | 字节数 |
|---|---|---:|
| orientation_policy.json | `3a6b258ed52dfc37dbff128b87a58f94fe1fc7e04c324e79999ca3cb698e300c` | 1,460 |
| orientation_requests.json | `7b974c47cf8b14409a3081bee52cdc414b91e48f5e925d5727f32c02abb5a985` | 749,295 |
| orientation_native_v2.json | `09cb6dde65da42cb04f41500e2a2b47e05a43ffde03daf9ed8587e026dd36f71` | 53,643,610 |

资源位于 ignored `assets/generated/lyra_als/`。证据在 `artifacts/lyra-analysis/`：`orientation-godot-final.log`、`orientation-root-regression.log`、`orientation-pose-regression.log`、`orientation-hidden-regression.log`、`orientation-demo-60.log`、`orientation-core-final.trx`、`orientation-standing-native.trx`、`orientation-debug-final.log`、`orientation-optimize.log`、`orientation-reader-build-shared.log`、`orientation-export-corrected.log`、`orientation-export-repeat.log`、`orientation-ue-corrected-full.log`、`orientation-ue-full.log`、`orientation-verification.json`。

## 失败保留与剩余范围

首 native 构建使用不存在的带 weight UpdateContext 构造器，修为 FractionalWeight；日志 `orientation-reader-build-first*.log` 保留。首 UE 采集因缺 SharedContext 触发 ensure 并退出 1；其 `orientation_native.json` 及 `orientation-export-first.log` / `orientation-ue-shared-context-first.log` 保留，不计通过。正式读取修正后的 `orientation_native_v2.json`。首次 Core 编译捕获 ReadOnlySpan 导致 CS9108，改捕获本地长度后通过，`orientation-debug-first.log` 保留。

仅验收当前原 CDO 的 Graph/Z/ComponentTransform 和无 prediction asset 路径。其它轴/Manual/自定义 WarpingSpace/未来预测资源未声明原生验收。方向和组件控制值仍需由原 Main 更新链提供；Stride、其它 Layer/Main、partial marker sets、Main 三个 Lean、统一 Notify/Montage、FootPlacement/LegIK、生产宿主与完整主图原生/视觉/性能保持开放。

复跑：构建外部 exporter → `scripts/export-lyra-orientation.ps1` → `tools/verify_lyra_orientation.py` → `scenes/tests/lyra_orientation_warping_smoke.tscn`。
