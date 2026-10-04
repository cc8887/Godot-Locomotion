# Lyra Main Cycle 原 Linked 调用与 Lean 共同求值

2026-10-01，在 `.` 主目录实施。资源源于 GASP58 / 本机 UE 5.8.1，运行端为 Godot 4.7.2 .NET；沿用 ALS 68 skin / 81 logical。接通原 Main 的 Cycle `ApplyAdditive17 → Linked15 + Lean16`，基底来自真实 Linked Cycle 输出。完整 Lyra 移植继续开放。

## 原根与共同事务

外部只读 exporter 新增 `ReadMainCycleLeanTrace`。它校验实际编译节点类型、Base/Additive LinkID、`FullBody_CycleState` 名称及 target instance，保留原 Linked Base 链路；没有替换为显式源姿态。原 Main 与 Layer proxy 都绑定同一 transient ALS81 骨布局。原 BlendSpace 的三个样本替换为已验证的 ALS81 additive transient Sequence，原资源不保存。

实际 Update 顺序为 Base 的 Linked Cycle → Lean，两个分支进入同一 Sync Scope，只运行一次 Sync，再从 Main ApplyAdditive 根求值。Cycle 包含原 source callback、HipFire、Orientation 与 Stride。每个活跃帧只求值一次原根；没有为了采集基底再次求值 Warp。与上一批独立 Cycle oracle 比较时，基础链的源时钟、引脚、Main 更新和 metadata 均相同。

`LyraMainCycleLeanHost` 共同持有完整 Main 更新、真实 Cycle 与 Lean 候选。Lean 直接消费本帧 Main 已计算的 RotationData，没有第二份旋转差分。两个分支按原遍历顺序登记源，再由外层共同 Sync。求值将真实 Cycle 的 81 骨、曲线、属性及 RootMotion 交给原 local additive 算子；输出保留 metadata 的值、标志、身份和存在性。

共同 Commit 先校验所有子图、同步播放器及样本快照，再发布 Main、Cycle、Warp 与 Lean 历史。取消、重复/迟到提交、求值后取消及迟到 Lean 样本故障均拒绝发布。当前活跃根仍需 Evaluate 后 Commit；隐藏根可提交更新。没有关闭完整角色的活跃 update-only。

## Linked 惯性请求与权重

真实 LinkedAnimGraph 在 child update 后消费 pending BlendOut/BlendIn。首次绑定的九条轨迹各多一条 `0.15000000596046448` 秒 BlendIn，位于 Cycle 换源的 `.2` 秒请求之后；之后没有重复。宿主读取实际编译签名的 BlendIn，并随共同候选提交消费，取消/隐藏保留待消费请求。

本批验证初次绑定及其消费顺序。没有实现完整热换类的 BlendOut/BlendIn 或外层惯性接收器；108 条请求的捕获不等于惯性姿态验收。

原请求有 `weight=1.1`，真实节点原样缓存。Lean Prepare 先前只允许 0–1，已改为有限非负图上下文权重，与 Cycle 保持同一边界。sample blend 权重及 ApplyAdditive 的 alpha 仍按原算子处理，没有钳制输入或修改原生门槛。

## 连续对照与回归

| 检查 | 结果与范围 |
|---|---|
| Unarmed/Pistol/Rifle × 30/60/120Hz | 3780 帧，3528 活跃、252 隐藏；每帧取消/重试 |
| 完整 Main、Lean 缓存与源时钟 | 标量、spring、flags、Lean 权重/变化率与时钟逐位一致；Main 向量最大 1.3642420526593924e-12 cm |
| Main Cycle 最终输出 | 285768 骨；最大位置 1.5629921469055528e-13 cm、单位 quaternion 1.3791477125662915e-15、scale 0；原门槛 1e-8 cm / 1e-10 / 1e-12 |
| 曲线、整数属性与 RootMotion | 曲线全部缺失且逐项检查存在性；14112 整数属性，3406 root present / 122 absent，RootMotion TRS 差 0 |
| Lean 实际执行 | 7527 原生 sample ticks，480 帧含三个样本；全部 3528 帧相较独立 Cycle 有字节值差异，Godot 3519 帧超过 smoke 的姿态变化门槛 |
| Linked/源惯性 | 108 条请求，其中 9 条初次绑定 BlendIn，时长和顺序逐位一致 |
| 无效操作 | 38997 次拒绝，包括求值前提交、过期候选、改变 Cycle 时钟/区间、迟到 Lean sample，以及取消重试 |
| 回归 | 旧完整 Main→Cycle、Lean runtime、显式 Lean composition；60Hz Rifle Demo 870 物理帧/871 姿态/6 次换层通过 |
| 构建 | 外部 UE exporter 成功；Debug 与 ExportRelease Optimize .NET 均 0 错误/0 警告；运行使用 Debug |

最终五份 Godot 日志退出 0、有成功标志、无 ERROR/WARNING。UE 两个独立进程正常退出 0，无 Python Error/ensure/assert；第二次导出严格检查语义相同并保留文件字节。独立 verifier 校验全部 492 个源/目标包与 JSON 字节依赖；上一批五份 Main fixture 与原汇总中的 SHA-256 一致。加载扩展 bank 校验原 234 加三条 Lean raw 文件。本批没有修改或保存 UE Skeleton/Sequence/AnimBP，也没有修改引擎源码。

汇总见 `artifacts/lyra-analysis/main-cycle-lean-final-verification.json`，包含两份新 fixture、11 份通过日志、旧五 fixture 字节、原生覆盖与剩余边界；独立资源检查为 `main-cycle-lean-resource-verification.json`。

首次 smoke 的三次失败保留：共同库存追加 Lean 后 mask 表仍只有 Cycle 长度；缺失首次 Linked BlendIn 请求；旧 Lean 入口拒绝原 `weight=1.1`。分别修正库存、原待消费历史和输入边界，未删 case、改期望值或放宽姿态/时钟门槛。最终通过日志及所有失败位于 `artifacts/lyra-analysis/main-cycle-lean-*`。

## 复跑与剩余工作

资源为 ignored `assets/generated/lyra_als/main_cycle_lean_requests.json` 和 `main_cycle_lean_native.json`；仅代码检出不足以运行。依赖此前 Main update、真实 Cycle、逻辑源及 Main Lean 导出。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-main-cycle-lean.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_main_cycle_lean.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_cycle_lean_smoke.tscn
```

本批关闭 Main **Cycle 状态根**的 Linked 与 Lean 合并。relevance、图权重、HipFire 和上一图 RootYaw mode 仍为明确输入边界，尚未来自真实 LocomotionSM 遍历。Start/Pivot 及其它入口的实际源遍历、完整状态权重、外层惯性、统一 Notify/Montage、最终 FootPlacement/LegIK、Godot gather、生产 Demo 替换及连续整链/视觉/性能仍待。没有新增渲染、人工玩法矩阵、多角色并行、打包或 UE PIE 线程调度验收；ALS R2–R7、用户修改与暂缓项保留。
