# Lyra Cycle 原 LayeredBoneBlend 姿态、曲线与属性

2026-10-01，在 `.` 主目录继续实施，源项目为 GASP58，原生端为安装版 UE 5.8.1，运行端为 Godot 4.7.2 .NET。沿用 ALS 网格与 68 根 skin 骨，在 81 根逻辑骨上求值。

本批将上一轮 [Cycle 双源宿主](2026-10-01-lyra-cycle-layer-sources.md) 的共同 Sync 候选接到原 LayeredBoneBlend 的局部姿态、曲线和 authored integer attributes。**输出止于 ConvertLocalToComponent / OrientationWarping 之前；不是完整 Cycle、Main 或生产 Demo 接入。** 目标和其余待办保持开放。

## 实际原生执行与骨架适配

`ReadCycleLayerPoseTrace` 根据原 Cycle 八节点闭包与 native/property 索引翻译定位实际 CDO 的 `FAnimNode_LayeredBoneBlend`，复制其完整配置。保留 mesh-space rotation、local scale、Curve Override、根骨权重策略和原单 child；将其两个输入链接到真实 `GetAnimationPose` 序列叶节点。叶节点使用已有 transient 69 raw / 81 logical ALS 序列和真实 raw、retarget、ForceRootLock 规则，不使用 Godot 输入姿态作原生期望值。

原 UpperBodyMask 从源 BlendProfile 按骨名映射到 transient ALS81 BlendProfile。原 68 skin 权重、weapon_r 与新左手武器空间 VB 保留，11 个 ALS 独有 VB 的未设置权重为零。Godot 加载器另外对照已有遮罩资源与逻辑骨身份；不改网格或 skin 权重。

原生 Evaluate 通过自己的曲线过滤与 `BlendPosesPerBoneFilter` 执行：

- Curve Override 用 child 替换同名 value 和 flags，保留 base-only / child-only 并集；不能按 max bone weight 普通插值。实际扩展 ALS Skeleton 的 linked-bone curve source 表为空，已导出到配置，不能从 Manny 名称猜测。
- 原 `IntegerAnimationAttribute` 虽然源键采用 StepInterpolate，混合时仍是 blendable。实际四个 pelvis 时间码通道采用 Blend；使用原始每骨权重乘积和每次整数截断，不用姿态的 0–1 钳制权重替代。
- 无 relevant child 时，原节点直接返回 base，不能额外经过 mesh-space 转换、normalize 或曲线筛选。

序列时间来自上一轮真实 Cycle 根遍历与共同 Sync oracle，覆盖三个 provider × 30/60/120Hz 九条轨迹的全部 3,528 活跃帧。新的采集器只做节点权重刷新和姿态 Evaluate，不重复执行原 callback/Sync，不冒充新的完整 Linked/Main 连续执行。

另有 72 个原生数据探针：两种属性模式 × 四种 presence × 九种权重，包括阈值、0.5 tie、负值整数和 >1 原始权重。通过实际 `BlendPosesPerBoneFilter` 验证共享/唯一/缺失整数属性和 Override 曲线 value/flags。属性模式在临时探针期间通过 scoped guard 设置并恢复内存中的默认值，没有 SaveConfig 或工程配置修改。

## Godot 宿主与事务

`LyraCycleLayerSourceHost.Resolve` 预校验完整共同 Sync 输出并返回候选 source 状态，尚不发布任何时钟。新 `LyraCycleLayerPoseHost` 在同一个候选内用各 occurrence sampler 取样 base/child，按原节点求值完整 81 骨、曲线及属性，再与被求值的 Sync 状态一致性校验后提交。宿主不新增独立动画时钟，不直接写 Skeleton3D。

图权重以局部 child relevance 判断，仍覆盖外层最终权重极小但实际登记/求值的源。遮罩原始乘积保留给属性，姿态混合在算子内部钳制。策略数组在宿主构造时复制，避免共享配置被运行期写坏。

Godot 场景使用自己的 callback/共同 Sync 结果驱动姿态；每帧先验证源数值与原 native bits，再比较新原生姿态。活跃帧求值后取消并重试，逐值检查 pose/curve/attribute 不变；拒绝旧候选求值、未求值提交、求值后更换 Sync 时间和重复提交，隐藏帧不能产生姿态。原 source 的晚期 HipFire 错误拒绝仍覆盖。

## 精度修正与最终证据

首次姿态比较在 Unarmed/30Hz/frame15/bone3 出现 quaternion `1.4303719908001568e-10`，超过原 `1e-10` 门槛。根据安装版 `Quat.isph::QuatFastLerp` 定位：ISPC 对 double quaternion 仍推导 float 的 DotResult/Bias/`1-Alpha`，旧精确混合器使用 double 补数。

Core 的精确 mesh-space 混合增加显式 `singleRotationAlpha` 选择；原六参数 API 和默认 double 算法保留，Cycle 选择本机原生 ISPC 路径。没有改变阈值或源时间。修正后：

```text
LYRA_CYCLE_LAYER_SOURCES_GODOT_OK frames=3780 cycle_ticks=3528 hipfire_ticks=2205
hidden=252 tiny_weighted=225 inertia=108 rejected=20601 exact_bits=true
LYRA_CYCLE_LAYER_POSE_GODOT_OK frames=3528 bones=285768 curves=105 attributes=14112
dataProbes=72 positionCm=1.0772985490388227E-13
quaternion=9.354905487120434E-16 scale=0 retry=true
stage=AuthoredPreWarp generatedRootMotion=false production=false
```

姿态门槛仍是 position `1e-8 cm`、quaternion `1e-10`、scale `1e-12`；曲线 presence/flags/value bits 和属性身份/值精确比较。原节点 bypass、部分/全权重、超范围局部权重、换资源、隐藏与重新初始化均沿原轨迹输入执行。

两次修正后的独立 UE 进程导出均退出 0，已有文件 byte hash 相同。复核 489 个源包、234 clip 和依赖资源哈希一致。新验证器绑定 policy/native fixture、所有依赖、九轨迹全部活跃帧及 72 探针，不能仅靠成功标记通过。

最终相关回归：Core mesh-space / layered blend 30 项、既有 Standing 原生连续对照 6 项全部通过，无失败或跳过；Debug 与 Release Optimize 构建均为 0 错误、0 警告。最终程序集另跑隐藏后重初始化九条源轨迹，共 3,780 帧、5,985 次无效操作拒绝，时钟与原生 bits 精确一致。现有三层换武器 Demo 在 60Hz 跑完 870 个物理帧、六次层切换和 871 次姿态提交；这些 Godot 运行均退出 0，日志无 ERROR/WARNING。Demo 使用既有生产入口，不能作为新 Cycle pose 宿主已接生产的证据。

证据位于 `artifacts/lyra-analysis/`：`cycle-pose-godot-final.log`、`cycle-pose-hidden-source-regression.log`、`cycle-pose-demo-final-60.log`、`cycle-pose-core.trx`、`cycle-pose-standing-native.trx`、`cycle-pose-debug-final.log`、`cycle-pose-optimize.log` 和 `cycle-pose-verification.json`。

| 新资源 | SHA-256 | 字节数 |
|---|---|---:|
| cycle_layer_pose_policy.json | `2ce3d61dd49fd0a9c268310a7ce8b0c67439dff3ad908bfd41a0dba0e8f0bc7c` | 3,520 |
| cycle_layer_pose_native_v2.json | `b4c025f690f915ae44d9a739a858faf6d520429465551609eb5b2436a02738be` | 53,714,618 |

这些资源位于 ignored `assets/generated/lyra_als/`，只有代码检出仍不能运行。复跑：构建外部 exporter → `scripts/export-lyra-cycle-pose.ps1` → `tools/verify_lyra_cycle_pose.py` → `scenes/tests/lyra_cycle_layer_pose_smoke.tscn`。

## 保留失败与运行边界

首次原生构建错误涉及 proxy 私有 Skeleton 和 const class interface；后续 probe 构建错误涉及 curve Find、嵌套 enum 和 protected notify API，均已修正，完整 UBT 日志分别保留。首个采集宿主直接写 SkinnedAsset 触发 `KnownSkinnedAsset` ensure，虽然生成姿态，进程退出 1，**不计通过**；已改为禁用组件动画后使用正式 SetSkeletalMeshAsset。首个原生文件与日志保留，新正式 oracle 使用 `_v2`，未覆盖旧生成文件。最终原生采集没有 Python Error/ensure/assert，源项目既有 GameplayTag 和插件警告保留。

本批原生和 Godot 求值明确关闭 generated Transform RootMotion 属性，当前输出只含 authored 数据。安装版 Sequence GetAnimationPose 在 HasRootMotion 时由 provider 根据真实 DeltaTimeRecord 生成 RootMotion 属性；这不等于角色是否从普通序列消费根运动。下一步必须实现该属性的原生区间取样、混合及 Orientation/Stride Warp 消费，不能用全段平均速度或单位变换替代。

完整的曲线/attribute 跨 Main 层混合、其它原始子图、partial marker sets、Main 三个 Lean、角色级 Notify/Montage、完整 FootPlacement/LegIK、生产宿主替换和全主图连续原生/视觉/性能验收仍开放。现有示例全局 HipFire、单 phase 时钟和线性 crossfade 尚未被本批替换；普通 Demo 回归只证明旧入口没有回退。
