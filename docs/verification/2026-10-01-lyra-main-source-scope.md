# Lyra Start/Cycle 共用 Main 与源同步宿主

2026-10-01，在主目录 `.` 推进；UE 5.8.1 / GASP58 导出，Godot 4.7.2 .NET 运行，保留 ALS 68 skin / 81 logical。本批完成真实 Start/Cycle 根共用一次完整 Main 更新、一个 Lean owner、同一 Linked provider 身份和一次 Sync；尚未完成完整 LocomotionSM 与普通玩法接入。

## 原根与共同 owner

`LyraMainSourceScope` 直接持有完整 Main、Start provider、Cycle provider 与三个独立 Main Lean occurrence。每帧只创建一份完整 Main 候选，Start 取带 offset 方向，Cycle 取不带 offset 方向；两个 Warp 都消费本帧速度/callback alpha，两个 Lean 消费同一 RotationData。没有把此前两个独立 Main 宿主并起来重复更新。

登记顺序保持实际 ApplyAdditive 的 Base→Additive：Start/HipFire → Start Lean，或 Cycle/HipFire → Cycle Lean，再访问下一个根。两根共享同名 Locomotion group；每个源、HipFire 与 Lean occurrence 保留独立身份和历史。收集时将局部 sample 范围变为全局范围，Sync 输出分组重排后按 player identity 回到各 owner。两根同时活跃不要求内部时间相等：2709 帧中两根时间均不同，仍分别与原生时钟/marker 严格一致。

隐藏/reset 也在同一帧事务中准备，保留原实例的 asset/time/marker 与待消费 Linked BlendIn。Commit 先校验 Main、两 provider、两个 Warp、全部 Lean 及共同播放器/样本快照，再发布全部历史。求值前提交、迟到/重复候选、重复 Resolve、坏 epoch/样本、隐藏求值、求值后取消和重试均覆盖。现在活跃根仍要求 Evaluate 后 Commit；没有关闭完整角色的 active update-only。

外部只读 `ReadMainSourceScopeTrace` 使用同一真实 Character/Movement gather、原完整 `BlueprintThreadSafeUpdateAnimation`、同一个真实注册 Linked 实例和共同原生 Sync。校验原 Main Start13/Linked11/Lean12 与 Cycle17/Linked15/Lean16 类型/links/target，再真实 Initialize/CacheBones/Update/Evaluate 两根。权重、相关性、重初始化与遍历顺序是显式状态机边界；没有手算替代原回调，也没有运行整个 LocomotionSM。

## 压缩根通道的修正

联合采集沿用 Start 的压缩等待，全部 78 份 transient 源先完成实际压缩。首轮 Godot 在 `unarmed/30Hz/frame38/Cycle` 的 RootMotion 位置差为 `4.8783126127023524e-8 cm`，超过原 `1e-8 cm` 门槛；同帧源时钟和最终姿态已通过。原因是 Cycle 宿主仍按 RAW 根路径取样。

新增 `main_source_scope_roots.json`，保存 78 份实际 codec、解码 key、独立 TRS 通道和 frame table，702 个原生静态 probe 逐值一致。Cycle provider 通过明确的 compressedRoots 参数使用该资源，骨骼姿态仍走原 RAW 路径。联合全部 6195 个 present RootMotion 的 TRS 差为 0。42 份旧 Start 压缩根与原资源完全相同；没有放宽门槛或从连续 oracle 取运行数据。

旧独立 Cycle exporter 没有调用 WaitOnExistingCompression，它的原 RAW 根配置已由之前夹具验证。把新压缩根直接套给旧夹具时，`frame38/bone49` quaternion 差 `3.3387488635014363e-9`，超过原姿态门槛。旧夹具回归明确保留旧 RAW 配置；联合宿主的实际压缩配置单独完整验收。没有改写旧夹具或把旧 RAW 根对照当成生产压缩配置验收。

## 连续对照与回归

| 检查 | 结果与范围 |
|---|---|
| Unarmed/Pistol/Rifle × 30/60/120Hz | 3780 物理帧，Start/Cycle 各 3150 输出，共 6300 姿态 / 510300 骨 |
| 共同活跃/顺序/隐藏 | 2709 两根同帧活跃，1782 帧 Cycle 先遍历，189 空 bank；两根独立 reset/隐藏重入 |
| Main/源/Warp/Lean | Main 标量/spring/flags、源时钟/marker、引脚及 Lean 权重/变化率/时钟逐位一致；Main 向量最大 1.3642420526593924e-12 cm |
| 最终姿态 | Start 最大位置 2.2695695994463617e-13 cm / quaternion 1.7461764767496646e-15；Cycle 2.109680835400689e-13 cm / 1.7111937024914371e-15；scale 0 |
| 数据属性 | 3150 曲线值、25200 整数属性、6195 present / 105 absent RootMotion；RootMotion TRS 差 0，存在性/标志/typed identity 保留 |
| Lean 时钟 | 7560 occurrence clock checks / 14709 sample checks；活跃 Start/Cycle sample ticks 为 6771/6300，检查数另含隐藏历史 |
| 惯性请求 | 81 条 Cycle 换源 `.2` 秒 + 两根初次绑定 18 条 `.15` 秒；顺序/生命周期一致，未验收外层惯性姿态 |
| 事务拒绝 | 23373 次；每帧 prepare-cancel-retry、求值后取消重试，失败不发布任何分支历史 |
| 原单根配置回归 | 7560 帧、7200 姿态、583200 骨，7078 present RootMotion 差 0；Main/Lean 时钟严格一致 |
| 旧入口/导出 | 60Hz Rifle Demo 870 帧 / 871 姿态 / 6 次换层；共享 exporter 的旧 Main Start 复导通过 |
| 构建 | 外部 UE exporter 成功；最终 Debug / ExportRelease Optimize 均 0 错误 / 0 警告，运行配置为 Debug |

原门槛保持 `1e-8 cm / 1e-10 quaternion / 1e-12 scale`。最终三个 Godot 进程退出 0、有完整成功标志，无 ERROR/WARNING。联合 trace 多次独立导出一致；新增压缩根有两个独立进程复导，三份新资源语义相同并保留原字节。492 个包、依赖 JSON、原 234 加三条 Lean raw 源及旧九 fixture 字节保持。

两次最终联合 UE commandlet 均报告 **0 errors / 1889 warnings**，无 Python Error/ensure/assert。旧 Main Start 复导为 0 errors / 1319 warnings，GameplayTag/临时压缩依赖等警告保留。没有保存 UE Skeleton/Sequence/AnimBP、修改引擎源码、打包或新渲染验收。

首次原生构建有局部 Marker 变量重名，已改名；两次采样配置失败均保留原日志。证据位于 `artifacts/lyra-analysis/main-source-scope-*`，汇总为 `main-source-scope-final-verification.json`。旧 Main Start wrapper 的固定 full log 被本次回归刷新，上一批汇总对应日志哈希同步更新并注明刷新原因，首个独立采集日志保留；旧资源字节未变。

## 复跑与剩余工作

资源在 ignored `assets/generated/lyra_als/main_source_scope_{requests,native,roots}.json`，依赖原 Start/Cycle、Main/Lean 导出，只有代码检出不足以运行。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-main-source-scope.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_main_source_scope.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_source_scope_smoke.tscn `
  -- --lyra-main-source-scope-joint
```

本批只关闭 **Start/Cycle 共同 Main/source 宿主**。没有真实 LocomotionSM 选边/权重栈或最终状态混合输出；未覆盖所有 provider、Linked 热换类、统一 Notify/Montage、最终 FootPlacement/LegIK、Godot live gather、普通 Demo 替换及完整整链/视觉/并行/打包/性能。继续把 Stop/Pivot/Air/Idle 真正根源库存纳入同一 owner，再接实际机器遍历，避免继续沿用旧单 phase/global HipFire 时钟。ALS R2–R7、用户未提交修改和暂缓项保留。
