# Lyra Sequence RootMotion 属性与 Cycle 混合

2026-10-01，在主目录 `.` 继续实施，源为 GASP58，原生端为安装版 UE 5.8.1，运行端为 Godot 4.7.2 .NET。沿用 ALS 68 skin / 81 logical 布局。

本批在上一轮 [Cycle authored pose](2026-10-01-lyra-cycle-layer-pose.md) 上增加实际 Sequence provider 的 `RootMotionDelta` Transform 属性，并在原 LayeredBoneBlend 中混合。输出仍止于 Orientation/Stride Warp 之前；没有替换生产 Demo、执行完整 Main 或关闭整体 Lyra 移植目标。

## 原生行为与运行实现

安装版 `AnimSequence.cpp::GetAnimationPose` 在 Sequence `HasRootMotion()` 且 provider 可用时生成属性，独立于角色是否从普通序列驱动胶囊。`AnimationWarpingRuntimeModule.cpp::SampleRootMotion` 从真实 `FDeltaTimeRecord.GetPrevious()/Delta` 提取区间，即使 delta=0 也加入单位变换属性。禁用 RootMotion 的源则没有该属性，不能用单位值代替 absence。

实际属性身份为 `RootMotionDelta`、root、namespace=bone、`/Script/Engine.TransformAnimationAttribute`，当前原配置采用 Blend。新只读 policy 保留身份与实际 operator，并依赖原 logical catalog/calibration 和 Cycle mask policy 的字节哈希；运行期不读取原生期望姿态作为输入。

`AlsRawRootMotionIntervalSampler` 复用现有 Actions `AlsRawRootMotionSampler` 的未锁定绝对根轨道入口，增加显式 native RoundSubframe 取样与直接区间入口。原 SampleAbsolute 单参数 API 和既有 Extract(start,end) 行为保留。新 provider 区间使用一次 FFrameTime 转换，越界 key 为 identity，Step 采用 nearest frame；不能借用普通 GetBonePose 的时间往返、越界夹紧和 Step floor。

区间先按根参考姿态逆变换转换到组件空间，再求相对变换。按实际 metadata 处理 normalized root scale。推进遵循 native binary32 previous/current/delta，严格超出边界才结束，正反循环逐段累积，累积运动 scale 归一为 1。不使用 ForceRootLock 后的根姿态或整段平均位移。

`LyraRootMotionAttribute` 保留 presence，支持原 per-bone Transform Blend/Override：共享属性使用原始每骨权重、最短旋转累积及 normalize；唯一属性从默认单位变换 Interpolate；full-weight 快捷路径和 base-first tie 保留，未拿 pose 的 clamp 权重代替属性权重。

`LyraCycleLayerPoseHost` 可显式启用 generated 属性，在同一真实共同 Sync 候选后采样 Cycle/HipFire previous/delta，按原 root mask 混合并与姿态一同提交。取消后清除可读输出，隐藏帧不产生 pose/属性；求值后换 root delta 的提交被拒绝。无独立时钟或 Skeleton3D writer；原 authored-only 验证入口继续可用。

## 实际 UE 采集

外部 exporter `ReadRootMotionTrace` 对 189 条 nonadditive transient ALS81 Sequence 执行实际 raw `GetBoneTransform`、`ExtractRootTrackTransform`、`ExtractRootMotion` 和已注册 provider。每条 16 个区间，共 3,024 项，覆盖零 delta、内部正向、正反单次/多次循环、非循环边界夹紧、恰在起止、恰走完整段和极小正反 delta。raw 和 provider 的根轨道输出逐值一致，证明本批 transient interval 没有偷偷走另一套压缩轨道。

Cycle 原节点采集扩展为 provider-enabled Sequence leaves，将此前真实根遍历/共同 Sync oracle 的 previous/delta 送入 ExtractContext，再由实际 LayeredBoneBlend Evaluate。三个 provider × 30/60/120Hz 九条轨迹的全部 3,528 活跃帧均采集；未重复执行 callback/Sync，不能冒充新的完整 Main 连续 oracle。

新增 72 个真实 `BlendPosesPerBoneFilter` Transform 属性探针：Blend/Override × 四种 presence × 九种原始权重，包含零/阈值/0.5 tie/>1、不同方向和符号的 quaternion、不同非均匀 scale。默认 operator 仅在 scoped guard 中临时改内存并恢复，没有 SaveConfig 或资产保存。

两次修正后的独立 UE 进程均正常退出 0，资源语义一致且原字节保留。最终原生日志没有 Python Error/ensure/assert；源工程既有 GameplayTag/插件警告保留。所有 Sequence/Skeleton/Mask 为 transient；`assets_saved=0`，未改 UE 引擎或 GASP58 C++/工程配置。

## 验证结果

最终 Godot 使用自己的 source callback/共同 Sync 输出驱动新属性，逐帧求值后取消重试；坏/旧/重复/未求值、不同 Sync 时间、不同 root 区间提交均拒绝，共 24,129 次无效操作拒绝。区间、属性位置/原始 quaternion/scale 使用原严格门槛 `1e-8 cm / 1e-10 / 1e-12`，未放宽阈值，也未在比较前 normalize root quaternion 隐藏误差。

```text
LYRA_ROOT_MOTION_GODOT_OK sequences=189 ranges=3024 probes=72 frames=3528
present=3255 identity=299 positionCm=0 quaternion=0 scale=0 retry=true
stage=ProviderPreWarp production=false
```

完整 81 骨、曲线、authored integer attributes 同时通过原对照：285,768 骨结果、105 曲线、14,112 整数属性，最大 position `1.0772985490388227e-13 cm`、quaternion `9.354905487120434e-16`、scale 0。独立 verifier 还直接确认 provider-enabled UE 输出的 authored pose/curve/attributes 与上一轮原生 fixture 完全相同。

Core 新区间边界/Step/absence 测试及既有 RootMotion/Mantle absolute root 测试共 28 项通过，既有 Standing 原生连续对照六项通过，均无失败或跳过。Debug 与 Release Optimize 均 0 错误、0 警告。原 authored-only 九条轨迹和隐藏重初始化 3,780 帧回归通过。既有三层 Demo 在 60Hz 跑完 870 物理帧、六次换层、871 次姿态提交；这是旧生产入口回归，不证明新 provider 宿主已接生产。上述 Godot 运行均退出 0，无 ERROR/WARNING。

独立资源 verifier 复核 489 个包、234 clips、此前原生 pose fixture 的依赖链，新 189 源/3,024 区间/全部九条轨迹/presence/probes 和 policy identity；所有旧生成 JSON 字节保持不变。新资源在 ignored `assets/generated/lyra_als/`，仅代码检出不能运行。

| 新资源 | SHA-256 | 字节数 |
|---|---|---:|
| root_motion_policy.json | `dd97aa88745b47cf2485a215cb689744b93166008513fc28bd6c50b6102e83c1` | 462 |
| root_motion_native.json | `698f030bb3d24f42d61963f09705061bb9d67d9ff49443eead5cfe7cf0a05b83` | 56,177,658 |

证据在 `artifacts/lyra-analysis/`：`root-motion-godot-final.log`、`root-motion-authored-regression.log`、`root-motion-hidden-regression.log`、`root-motion-demo-60.log`、`root-motion-core.trx`、`root-motion-standing-native.trx`、`root-motion-debug-final.log`、`root-motion-optimize.log`、`root-motion-export-corrected.log`、`root-motion-export-repeat.log`、`root-motion-ue-full.log`、`root-motion-verification.json`。

## 保留失败与范围

首个实际采集在 StackAttributeContainer 创建时缺失 MemStack scope，UE 断言退出 3，不计通过；增加函数/逐区间 FMemMark 后重采，保留 `root-motion-export-first.log` 和 `root-motion-ue-memstack-first.log`。首 C# 编译出现 Core.Math 名称解析；修正后发现与现有 Actions 根取样器重名，随后复用现有入口并给区间推进独立类名；失败日志保留。首 Standing 过滤器没有匹配任何测试，零测试不算通过，其 log/TRX 改名为 `root-motion-standing-native-no-match.*` 保留。

原生 range 库存限于现有 189 条非 additive 源；本批不声明 additive RootMotion、所有稀疏根轨道/非默认根参考或未来 provider 已完成原生验收。当前 raw root 三类 channel 均为完整 key count，已由 verifier 检查。Step 的新差异有独立 Core 边界测试，本批实际源区间仍沿原 metadata。

下一步将此 typed 属性接入原 OrientationWarping / StrideWarping 图，保留图级权重、组件空间转换、真实 delta 与节点历史，验证 ALS 脊柱映射和脚定义。其它 Layer/Main、partial marker sets、Main 三个 Lean、统一 Notify/Montage、完整 FootPlacement/LegIK、生产宿主替换、完整主图连续原生/视觉/性能仍开放；本批没有新渲染或人工验收。

复跑：构建外部 exporter → `scripts/export-lyra-root-motion.ps1` → `tools/verify_lyra_root_motion.py` → `scenes/tests/lyra_root_motion_smoke.tscn`。
