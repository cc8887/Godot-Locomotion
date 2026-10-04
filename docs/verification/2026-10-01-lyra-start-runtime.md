# Lyra 原 Start Layer 完整根与压缩 RootMotion

2026-10-01，在 `.` 主目录实施；源为 GASP58 / 本机 UE 5.8.1，运行端为 Godot 4.7.2 .NET。沿用 ALS 模型、68 skin / 81 logical。本批完成原 `FullBody_StartState` provider 根；完整 Lyra 移植继续开放。

## 原始 Layer 闭包

外部只读 exporter 将 Cycle 专用图读取器扩展为按实际 Layer 名读取编译根，旧入口保持原行为。九个 provider 的 Start 闭包均为八节点：Root44 → ComponentToLocal38 → Stride37 → Orientation40 → LocalToComponent39 → LayeredBlend42，Base 为 Start43，child 为 HipFire41。编译 property 顺序、链接、节点类型与来源 callback 均由 Godot loader 校验。

真实 Main/Linked 实例保留原绑定，实例内资源替换为 transient ALS81。原 Start 根执行 Initialize/CacheBones/Update，Start 与独立 HipFire 登记到同一 Sync Scope，只运行一次 Sync，再从原根 Evaluate。没有使用单源输出替代 provider 根。

Start 的 BecomeRelevant/Update 延续上一批原 evaluator；姿态读取 Sync 后 InternalTime，Start alpha 读取原 ExplicitTime。HipFire child 先于 Base 更新；只在 blend weight 超过原 1e-5 阈值时登记，保留其独立 source/Marker/Delta。Warp 的角度来自 Main **带 offset** 的方向，Stride alpha 来自本次 Start callback，不能复用 Cycle 的无 offset 角度。

`LyraStartLayerPoseHost` 在同一候选中持有 source、HipFire、Orientation filter 与 Stride spring。Prepare 不发布历史，Evaluate 依次执行 mesh-space 层混合、typed metadata、原双 FCSPose Warp；Commit 先校验完整同步快照。隐藏帧、初始化、求值前提交、过期候选、晚到 HipFire epoch、求值后取消/重试均覆盖。活跃根仍要求 Evaluate 后提交；本批没有关闭整个角色的 update-only。

ALS 缺少的 Manny spine_04/05 映到 spine_03 并去重，仍用已验证的 ALS81 mask 与双 Warp 适配。Start 的静态 blend 与 Warp 参数只有在逐项校验与原 Cycle 相同后才复用政策；源时钟和动态引脚各自持有。

## 压缩 RootMotion 与 RAW 姿态

首次完整根对照发现第 2 帧根位置差 `1.951765357432573e-6 cm`，超过原 `1e-8 cm` 门槛。距离曲线编辑后的异步压缩完成，RootMotion provider 的 `ExtractRootMotion` 开始读取压缩骨数据；RequiredBones 的 RAW 姿态开关不影响这一路径。

稳定后的 42 条 transient Sequence 使用 39 个 PerTrack、2 个 RemoveLinearKeys、1 个 Bitwise codec，并非 ACL。最终 exporter 只接受这三类 legacy codec，在 UE 离线解出三个独立压缩通道的关键帧和原 frame table；同时保留实际压缩 payload、格式、offset、flags 与采样帧率。不是把连续 oracle 输出用作运行资源。全部 2141 个压缩 channel keys 中有一个变量时间通道，rotation/scale 在本批资源中都是单关键帧。

Godot 的 `LyraCompressedRootBank` 读取不可变关键帧，按原 FFrameTime 精度、相对时间的 float 收窄及 legacy TimeToIndex 选帧，然后进行原 binary32 插值。区间累计复用原 RootMotion interval 的正反向/循环规则；移除 reference root、scale 设置和 typed RootMotion presence 沿用原语义。姿态继续读取 RAW 动画。这批运行不依赖 UE 或 native DLL。

42 × 9 个独立 `ExtractRootTrackTransform` 采样点全部精确同；该探针只在 smoke 比较，运行采样器不读取 probes。仅当前静态根旋转资源已验收，多关键帧压缩根旋转尚未专项验收。

## 验证与边界

| 检查 | 结果 |
|---|---|
| Unarmed/Pistol/Rifle × 30/60/120Hz | 3780 物理帧，3672 输出 / 108 隐藏；逐帧取消重试 |
| 完整 Start 最终姿态 | 297432 骨；最大位置 `1.1191048088221578e-13 cm`、quaternion `8.74438297520381e-16`、scale `3.8459253727671276e-16`，保持原 `1e-8 / 1e-10 / 1e-12` 门槛 |
| 数据与 RootMotion | 3774 条实际曲线 / 14688 整数属性；3672 root present（1247 identity、2425 非零），RootMotion TRS 差全部 0 |
| 源与 Warp 引脚 | 216 Setup、1584 次保留选源、152 显式/同步时间不同；时钟、Marker、alpha、引脚逐位一致 |
| HipFire 与生命周期 | 2268 source ticks，包括 204 个实际图权重不超过 1e-5 的 tick；13500 次无效操作拒绝 |
| 资源 | 42 compressed root、36 UniformIndexable distance、九个原 Start graph；492 个源/目标包 SHA-256 保持 |
| 回归 | 原 Main Cycle + Lean、Start 单源；60Hz Rifle Demo 870 物理帧 / 871 姿态 / 6 次换层通过 |
| 测试与构建 | Core 根区间/Warp/Sync 33、Import Standing 原生六组通过；Debug / ExportRelease Optimize 均 0 错误 0 警告；最终外部 exporter 编译成功 |

两次包含完整 legacy root resource 的独立 UE 进程正常退出，重复导出严格要求 JSON 语义一致并保留原文件字节。最终 Godot 日志退出 0、有成功标志、无 ERROR/WARNING。UE commandlet 显示 0 errors / 1761 warnings，包含 GameplayTags、transient 压缩依赖 ConditionalPostLoad 和插件加载警告；不声称 UE 零警告。没有修改或保存 Skeleton/Sequence/AnimBP，没有修改引擎源码。

首编译属性名错误、C++ `ExtractRootTrackTransform` 缺参数、raw root 超门槛及拒绝 ACL codec 的失败均保留。当前三类 codec 解码修正后通过；未删 case 或放宽阈值。未使用的 ACL 二进制实验已归档到 `artifacts/lyra-analysis/start-runtime-unused-acl`，运行源码中已去除该实验。

汇总：`artifacts/lyra-analysis/start-runtime-final-verification.json`；独立资源检查：`start-runtime-resource-verification.json`。本批只关闭 **Start provider 根**，Main Start ApplyAdditive/Lean、真实 LocomotionSM 权重与多状态 source 遍历、Stop/Pivot/Air、外层惯性、统一 Notify/Montage、最终 FootPlacement/LegIK、Godot gather 与生产/整链验收均开放。没有新增渲染、人工玩法矩阵、多角色并行、打包或全量测试。原 ALS R2–R7、用户未提交修改及暂缓项保留。

## 复跑

资源在 ignored `assets/generated/lyra_als`，只有代码检出不能运行。依赖已有逻辑源、Start source、Cycle blend/Warp 政策和 Main Cycle fixture；本批运行资源为 `start_layer_graph.json`、`start_runtime_requests.json`、`start_runtime_distance.json`、`start_runtime_roots_v2.json` 和 `start_runtime_native_v3.json`。旧 fixture 用于独立 verifier 的资源/行为不变检查，不要重新格式化。

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-start-runtime.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_start_runtime.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_start_runtime_smoke.tscn
```

下一步接原 Main Start ApplyAdditive13 的真实 Linked11 与 Lean12，使完整 Start provider 输出和 Main 更新共同遍历、同步、提交，再向原 LocomotionSM 和其余 provider 推进。原先此处的 11/10 编号错误，已按实际编译图校正。
