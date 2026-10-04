# Lyra Main Lean 原播放器、共同 Sync 与首次生成验证

2026-10-01，在 `.` 主目录继续实现。源为 GASP58/本机 UE 5.8.1，运行端为 Godot 4.7.2 .NET，沿用 ALS 模型及 68 根蒙皮骨、81 根动画逻辑骨。本批关闭 Main 三个 Lean 源播放器组件，并修复上一批首次重定向的压缩 ensure；完整 Main、普通生产入口及整个 Lyra 支线继续开放。

## 原节点连续对照

外部 exporter 的 `ReadMainLeanTrace` 构造真实 `ABP_Mannequin_Base` 实例，取得原编译节点 22/16/12（property 80/86/90），分别执行原 FPoseLink Initialize/CacheBones/Update。原 exposed handler 将 Main 的 `AdditiveLeanAngle` double 窄化到 float pin；只在临时实例上将资产指针替换为保留原配置、包含三个 ALS81 目标序列的 transient BlendSpace。全部活跃源登记后执行一次 native Sync，再用原 BlendSpacePlayer Evaluate 取得局部 additive 输出。没有执行完整 Main 状态基底、ApplyAdditive 或最终骨骼控制。

三节点各自持有 normalized clock、pin、cached graph weight、delta record、sample 顺序/weight/weightRate/clock；资产共享不意味着这些历史共享。原 DoNotSync 节点进入同一更新批次中的 independent 分支；三个样本原/目标 rate 均为 1，原/目标 Marker 数均为 0，不人为加入组或 Marker。原 player 的 persistent triangulation index 在本次实际 native 范围中始终为 -1。

三条 30/60/120Hz 各 10 秒轨迹包含不同活跃分支、重叠权重、隐藏保留、隐藏初始化、反序遍历和小于源权重门槛的上下文。输入是受控角度/相关性快照，不是实际 Character → UpdateRotationData 连续执行。覆盖如下：

| 项目 | 数量 |
|---|---:|
| 物理帧 | 2100 |
| 活跃节点更新 / sample 更新 | 2121 / 6155 |
| 三个平滑样本同时保留的更新 | 1944 |
| 隐藏但保留 sample 历史的节点观察 | 3255 |
| 非首次隐藏初始化 / 极小上下文权重更新 | 6 / 21 |
| 多分支重叠物理帧 | 189 |
| 活跃输出骨数 | 171801 |

两次独立成功 native 采集的 JSON 语义相同，保留首次资源字节。最新 native 进程正常退出 0，唯一标志为 `LYRA_MAIN_LEAN_RUNTIME_NATIVE_OK traces=3 frames=2100 nodes=3 assets_saved=0`，没有 LogPython/LogOutputDevice Error、ensure 或 assert。项目既有插件/平台 SDK 等警告保留；不把这次源组件采集写成完整 Editor/玩法或全项目无警告。

## Godot 实现与提交边界

`AlsBlendSpaceWeightSmoothing` 按原 sample 顺序匹配、保留淡出项并追加新项，执行 speed=3 的 EaseInOut critical damping；归一化权重时不归一化变化率。`LyraMainLeanSourceHost` 在 Prepare 保留候选，CollectAtCommonSync 计算平滑并登记三节点，外层调用现有 `AlsSyncRuntime.TryEvaluateAssetSyncBatch` 一次后 Resolve，再独立取样/混合。只有 Commit 发布历史，Cancel 不修改已提交值；允许只更新而不求值。

同一 ALS81 bank 的 237 源和共同 curve/attribute 布局继续复用。每个 player occurrence 有三个独立 sampler 与一个私有 scratch；不同节点可并行求值，同一节点的重入被拒绝。输出保留原局部 additive，不直接写 Skeleton3D。当前组件的 Prepare/Resolve/Commit 由宿主串行协调；完整角色事务及最终唯一蒙皮发布仍须在 Main 接入时完成。

每帧先求值/重复求值/并行 occurrence 求值，再取消并检查历史，重试后提交；另一个宿主每 17 帧求值一次，验证 update-only 的时钟与平滑历史不依赖姿态求值。旧候选、错误 epoch、缺失时钟、外来 sample、隐藏求值、短输出和重复提交等 23685 次坏操作均拒绝。

```text
LYRA_MAIN_LEAN_RUNTIME_GODOT_OK frames=2100 ticks=2121 sampleTicks=6155
tripleSamples=1944 rejected=23685 positionCm=9.900923867263842E-15
quaternion=2.624561232697792E-16 scale=0 clocksAndWeights=exactBits
retry=true updateOnly=true parallelOccurrences=true
stage=MainLeanSources production=false
```

节点/sample 的已比较 float 权重、变化率、时钟及 delta 逐位一致。姿态门槛保持 position `1e-8 cm`、quaternion `1e-10`、scale `1e-12`，native 输出只作断言，没有驱动生产计算。Lean 输出曲线/属性为空；没有据此声称原序列 Notify 已完成提取或消费者已接入。

## 首次生成 ensure 的修复

检查本机 `IKRetargetBatchOperation.cpp`：批量操作先重映射引用，再在 Convert 后由 `FAdditiveRetargetSettings.RestoreOnAsset` 恢复旧的 additive 配置，因而可能把 Manny 的 `RefPoseSeq` 带回 ALS 目标。上一批首次生成触发的 SkeletonToCompactPose ensure 由此定位到跨骨架基底；现有资产重复导出成功不能证明首次生成已修复。

新目标生成改为 `retain_additive_flags=False`，先得到普通 ALS 目标，再仅对新目标恢复原 LocalSpace / AnimFrame / frame0，最后设置 ALS Center 为 `ref_pose_seq`。最后设置引用是必要的：`AnimSequence.cpp` 的 PostEditChange 在 AdditiveType 为 None 时会清空它。Center 保持自引用；现有生产目标不修改。

使用全新 `/Game/GodotLyraRetarget/Verification/MainLeanCold_20261001A` 生成并保存三个专用验证目标。该进程正常退出 0，无 ensure/assert/Python Error，24 组 79 logical / 68 skin 姿态与现有生产目标的 position/quaternion/scale 差均为 0。492 个保护的源/生产目标包全部哈希不变。**本批在 GASP58 额外保存了三个验证资产，492 是保护闭包数量，不是项目资产总数。**没有更改 UE 源码、GASP58 插件或配置。

`main-lean-cold-proof.json` 保存三份验证包哈希和逐项结果。复跑脚本支持 `-TestName MainLeanCold_<新名称>`，限定在上述 Verification 根目录；已有证明或目标会拒绝覆盖。默认名称的验证已完成，重复运行需选择新名称，并会新增三个验证资产。

## 回归、资源与失败记录

最终 Debug 和 Release Optimize 构建均 0 错误/0 警告。Core Sync/Independent/BlendSpaceTiming 相关 79 项通过，0 失败/跳过；旧 Lean 静态 24/33 与原 936 源采样、Cycle runtime 3528 活跃帧/285768 骨及 root TRS 差 0、独立 Demo 60Hz 870 物理帧/六次换层/871 次姿态发布回归通过。最终四份 Godot 日志均退出 0 且没有 ERROR/WARNING。并行 occurrence scratch 最后修订后再次构建 Debug/Optimize，并重跑全部本批 runtime 轨迹；未重复不受该修订影响的旧组件门禁。

`tools/verify_lyra_main_lean_runtime.py` 验证 492 包、旧 234 clip、源配置、float playLength、请求/native/policy 哈希与覆盖。新增的三个 ignored JSON 为 `runtime_requests.json`、`runtime_native.json`、`runtime_policies.json`，连同静态资源共十个文件，已有字节哈希依赖不格式化。native SHA-256 为 `c14e505ff32bd616fd965d913be2bea0694c5f44a164c7edf09377a73e9e9758`；完整哈希/大小和门禁结果见 `artifacts/lyra-analysis/main-lean-runtime-resource-verification.json`、`main-lean-runtime-final-verification.json`。

首次 Core 构建的 Math 命名空间冲突、构建失败后误启动旧 Godot 程序集、BlendSpace MarkerRecord 与 Core Sequence-only 合同冲突、Python Marker 反射名、float playLength 误比较原 double、冷生成的属性设置次序失败均保留日志，随后按真实边界修正。30Hz weightRate 曾相差一个 ULP，定位为 native BlendSpace InvExp 的展开乘加顺序与旧 Rig Horner 求值树不同；新平滑组件采用原展开顺序，旧 Rig 已验证算法保持原样。没有放宽阈值或改 native 期望值。

最终主要证据：`main-lean-runtime-ue-full.log`、`main-lean-runtime-godot-parallel-final.log`、`main-lean-runtime-release-parallel-final.log`、`main-lean-sync-regression.trx`、`main-lean-cold-ue-full.log`、`main-lean-cold-proof.json`。首失败日志及两次成功 native 采集日志均保留在 `artifacts/lyra-analysis/`。

```powershell
.\scripts\export-lyra-main-lean-runtime.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python tools/verify_lyra_main_lean_runtime.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_lean_runtime_smoke.tscn
```

后续是原 Character/Main 更新与回调顺序、Lean ApplyAdditive 及完整 Main 根遍历，接其余 Start/Stop/Pivot/空中/Idle provider、共同源 Sync/Notify/Montage 和最终 FootPlacement/LegIK，再替换生产宿主并做全链原生/视觉/玩法/性能验证。本批没有新增渲染/人工观感/全量/性能验收，不关闭完整 Lyra 或 ALS R2–R7，用户暂缓项继续保留。
