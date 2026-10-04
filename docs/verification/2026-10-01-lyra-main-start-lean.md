# Lyra Main Start 原 Linked 调用与 Lean 共同求值

2026-10-01，在 `.` 主目录实施。源项目为 GASP58，本机验证引擎为 UE 5.8.1，运行端为 Godot 4.7.2 .NET；沿用 ALS 68 skin / 81 logical。本批接通 Main 的 Start `ApplyAdditive13 → Linked11 + Lean12` 与真实 Start provider，完整 Lyra 移植继续开放。

## 实际编译根与输入

外部只读 exporter 新增 `ReadMainStartLeanTrace`，校验实际 ApplyAdditive、LinkedAnimLayer、BlendSpacePlayer 类型、Base/Additive LinkID、`FullBody_StartState` 名称与 target instance。原 Main 和 Linked Layer proxy 使用同一 transient ALS81 布局；原 Main Lean BlendSpace 的三个样本替换为此前验证的 ALS additive transient Sequence。原 AnimBP、Skeleton、Sequence 均不保存。

每帧通过实际 Character/Movement PropertyAccess 采集输入，执行原完整 `BlueprintThreadSafeUpdateAnimation`，随后从 Main Start 根执行 Initialize/CacheBones/Update/Evaluate。Base 分支真实遍历 Linked Start 的原八节点、SequenceEvaluator/距离匹配、HipFire、LayeredBlend、Orientation 和 Stride；Lean 分支随后登记，两个分支共用一次原生 Sync。

Godot 的 `LyraMainStartLeanHost` 在同一候选中计算完整 Main 更新，直接给 Start 和 Lean 传入新值。Start 的 Orientation 使用 **带 offset** 的方向，Stride 使用 DisplacementSpeed 与本帧 callback alpha；Lean 使用本帧 AdditiveLeanAngle，无第二份旋转差分。UE 的 cardinal enum 与本地枚举顺序不同，已显式转换。原 Start 只在 BecomeRelevant 时选资源，之后保留当前资源，不能每帧按新方向换源。

原 Start Layer 的 42 份实际压缩 root payload、probe 和 key/frame table，以及 36 份 UniformIndexable 距离曲线，均与上一批资源逐值一致。姿态仍使用 RAW 源通道，RootMotion 使用原压缩根通道，两种路径保持各自原语义；没有从连续 oracle 读取运行姿态或根运动。

## 共同同步、求值与历史

宿主先登记真实 Start/HipFire 源，再登记 Lean，交给外层统一 Sync。共同 Evaluate 将实际 Start 的 81 骨与原曲线、整数属性、generated RootMotion 交给 local ApplyAdditive；metadata 保留值、身份、标志和存在性。

Commit 在发布任何历史前校验 Main、Start、两个 Warp、Lean 与整份 Sync 播放器/样本快照。隐藏帧可提交更新；当前活跃根仍要求求值后提交。逐帧取消、重复求值、过期候选、不同 epoch/Sync 输出、求值前提交和迟到 Lean sample 故障均验收。失败与取消不会发布 Main/源/Warp/Lean 历史，重试与 clean 路径一致。

首次真实 Linked child update 后产生 BlendIn，请求来自原编译签名。九条轨迹各一条 `0.15000000596046448` 秒，之后不重复；只有共同提交活跃根后才消费，隐藏和取消保留。此处仅验收请求产生与生命周期，外层惯性姿态及热换类 BlendOut/BlendIn 仍待。

## 连续对照与回归

| 检查 | 结果与范围 |
|---|---|
| Unarmed/Pistol/Rifle × 30/60/120Hz | 3780 物理帧、3672 输出、108 隐藏，每帧取消/重试 |
| Main 更新、源与 Warp pins | 标量、spring、flags、源时钟/marker/explicit time 与动态 pins 逐位一致；Main 向量最大 1.3642420526593924e-12 cm |
| Main Start 最终姿态 | 297432 骨；最大位置 1.0497051549900617e-13 cm、单位 quaternion 1.0148829165269725e-15、scale 0；原门槛 1e-8 cm / 1e-10 / 1e-12 |
| 曲线与属性 | 3672 曲线值和 14688 整数属性严格通过；3672 RootMotion present，3103 移动 / 569 identity，RootMotion TRS 差 0 |
| 源选择与时钟 | 216 Setup、21 个实际 Start 资源；516 帧保留资产与当前期望选择不同，81 帧 explicit/internal 时钟不同，HipFire 2268 ticks |
| Lean | 权重、变化率与时钟逐位一致；3780 clock checks / 7713 sample checks，7506 活跃 sample ticks / 180 三样本帧；后两项只计活跃帧，前两项含隐藏历史检查 |
| 带 offset 分支 | 255 活跃帧方向与 NoOffset 分支不同；1872 Lean pin 变化、21 个不同 Lean pin；3654 输出超过 smoke 的姿态变化门槛 |
| Linked 与事务 | 9 条首次 BlendIn；22464 次无效操作拒绝，求值后取消和迟到样本失败后重试通过 |
| 回归 | 旧 Main Cycle 3780 帧 / 285768 骨、旧完整 Start 3780 帧 / 297432 骨；共享 exporter 的旧 UE Start 复导；60Hz Rifle Demo 870 物理帧 / 871 姿态 / 6 次换层 |
| 构建 | 外部 UE exporter 成功；Debug 和 ExportRelease Optimize .NET 均 0 错误 / 0 警告，运行使用 Debug |

两次 Main Start UE 独立进程退出 0，语义比较一致并保留新 fixture 原字节。492 个源/目标包及全部 JSON 依赖哈希通过；旧 Start 五份与 Main Cycle 两份 fixture 字节保持。两次 Main Start UE commandlet 均报告 **0 errors / 1319 warnings**，没有 Python Error/ensure/assert；日志包含 GameplayTag 等既有/临时资产路径警告，不应表述为 UE 零警告。最终 Godot 回归日志退出 0、有完整成功标志，无 ERROR/WARNING。

汇总为 `artifacts/lyra-analysis/main-start-lean-final-verification.json`，独立资源检查为 `main-start-lean-resource-verification.json`。首次失败保留：此前文档把原 Main Start 错写为 ApplyAdditive11/Linked10，实际编译类型门禁拒绝空 trace；改为真实 13/11/12，并修正上一批文档。首次 smoke 构建调用不存在的 ReadComponent helper，改为读取明确 component TRS。未改算法期望、删除覆盖或放宽门槛；失败日志位于 `main-start-lean-ue-first-failure.log` 与 `main-start-lean-debug-smoke.log`。

## 复跑与剩余边界

资源为 ignored `assets/generated/lyra_als/main_start_lean_requests.json` 与 `main_start_lean_native.json`，依赖原 Start、Main update 和 Lean 导出。仅代码检出不足以运行。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-main-start-lean.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_main_start_lean.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_start_lean_smoke.tscn
```

本批关闭 Main **Start 状态根**的 Linked 与 Lean 联合执行。graph relevance/weight、Layer HipFireWeight、上一图 RootYaw mode 与 relative rotation 仍为明确输入边界，尚未来自完整 LocomotionSM。现有 Main Start/Cycle 宿主各自持有 Main 更新，用于各根连续验收；完整 Main 必须统一角色 Main owner，仅更新一次，再遍历实际活跃状态，不能并用这些宿主重复更新 Main。

后续继续 Stop/Pivot/Air 等真实 provider 根、完整状态机与共享 Main/source traversal，再统一 Notify/Montage、外层惯性、最终 FootPlacement/LegIK、Godot 实际 gather 与生产接入。完整活跃 update-only、连续整链、渲染/人工观感、多角色并行、打包与性能仍未验收。ALS R2–R7、用户未提交修改和暂缓项保留。
