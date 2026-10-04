# Lyra Start/Cycle/Stop 共用 Main 与源同步

2026-10-01，在 `.` 主目录实施，UE 5.8.1 / GASP58 只读导出，Godot 4.7.2 .NET 运行。保留 ALS 68 skin / 81 logical。本批将已验证的 Stop provider 接入既有共同 source owner，与 Start/Cycle 共用一次完整 Main 更新、一个 Linked provider 身份、一个 Lean owner和一次 Sync；尚未完成整个 LocomotionSM 或生产玩法替换。

## 共同 owner 与分组

`LyraMainSourceScope` 新增可选 Stop 分支。每帧只准备一份 Main 候选；三个 provider 从同一份本帧 Main 状态取得站蹲、ADS、方向、速度/输入标志。Stop 另外读取实际 CharacterMovement 的 LastUpdateVelocity 和摩擦/减速快照，保持原停止预测语义。

Start/Cycle 继续共同使用 Locomotion group0；Stop 使用独立 Stop group1。HipFire 和两条 Main Lean occurrence 保留独立源身份及 DoNotSync 方法。来源登记按实际 root order 访问，每个 ApplyAdditive 仍是 Base/provider（含 Linked 请求）→自己的 Lean；Stop 只有四节点 provider，没有新增 Lean 或 Warp。禁止把旧独立 Stop 的默认 group0 直接接入这个共同 owner，九个对应的错误绑定被拒绝。

只向共同 Sync 提交一个播放器/样本集合。局部样本范围转换为全局范围，输出按 player identity 返回各 owner。Stop 的独立时钟、HipFire、隐藏/reset、ExplicitTime/InternalTime 和 marker/delta 历史没有合并进 Start/Cycle。取消包含所有 source、两套 Warp、Lean、Main 和待消费 Linked 请求；Commit 在任何历史发布之前预校验全部依赖和 Sync 快照。

当前活跃分支仍要求 Evaluate 后 Commit；本批没有增加整角色 active update-only 支持。外部最终骨骼 writer 未改。

## 原生遍历与模式反馈

新增 `ReadMainSourceScopeStopTrace`，在同一个实际注册 Character/Movement/Main/Linked 实例上执行完整 `BlueprintThreadSafeUpdateAnimation`，访问实际 Main Start ApplyAdditive13→Linked11/Lean12、Cycle ApplyAdditive17→Linked15/Lean16，以及 Stop StateResult18→Linked19。两条同名 Locomotion 源和独立 Stop 源在同一个原生 Sync pass 中 tick，然后真实 Evaluate；没有把几个独立进程或独立 Main 的结果拼在一起。

Stop StateResult 的真实 `UpdateStopState` 读取实际机器上下文。当前 state2/3、上一 Stop weight以及 root order/权重/相关性是显式机器观察边界。状态回调的 RootYaw 模式在本帧图之后记录；下一帧采集直接保留实际 Main 上的模式，交给原完整 Main 更新消费，**不会按预设输入再次覆盖模式**。Godot 同样将 post-graph 模式与 source 历史原子提交，并逐帧检查下一次 Main 输入与已提交模式相同。

本批的 Start/Cycle 入口仍是 ApplyAdditive13/17，未包含外层 StateResult10/14 的完整回调与实际状态机遍历。特别是原 `UpdateStartState` 的 Hold 模式仍须在下一阶段接入；这份 Stop 模式反馈对照不能当作完整状态根或整机验收。也未执行实际状态选边、机器给出的权重栈或最终状态混合姿态。

## 原生探针修正与资源保护

首轮新增 Stop 记录误用 `FAnimNode_SequenceEvaluator::GetAccumulatedTime()`。本机 header 明确将该方法覆写为返回 **ExplicitTime**，不是 Sync 使用的 InternalTimeAccumulator。Godot 在 `joint/0/frame1/prepared` 拒绝对照：首轮 native 记录 `.8000000715`，真实更新前内部时钟为上一 tick 的 `.7666667104`。

改为与既有 Start 探针相同的 `FNodeAccess::Time`，读取实际内部字段；before/prepared/time 均修正，ExplicitTime 仍单独记录。完整首轮三个 JSON、UE 日志和失败 Godot 日志归档为 `artifacts/lyra-analysis/main-source-stop-*-explicit-time-diagnostic.*` 或对应 first 日志，未作为验收。只修探针，不改 UE 回调、Core 算法、夹具期望公式或误差门槛。最初构建还有局部 Marker 名遮蔽，修正并保留失败日志。

共同资源是114份原 Start/Cycle/Stop/HipFire。新增 `main_source_stop_roots.json` 保存实际 transient 压缩根 codec与1026个静态 probe。原78份共同根、42份 Stop 根及六份重叠 HipFire 的 codec逐份相同；距离 buffer 继续使用已验证的原 Start/Stop静态数据。姿态仍按原 RAW 数据求值，运行实现不读取连续 oracle 作为输出。

两个修正后的独立 UE 进程复导语义相同，保留第一次生成文件的原字节。492个 `.uasset` 与依赖 JSON、旧共同宿主/Stop资源哈希保持；汇总附完整旧文件清单。没有保存 UE Skeleton/Sequence/AnimBP、修改引擎或把旧 ignored资源覆盖为新资源。

## 验证结果

| 检查 | 结果与范围 |
|---|---|
| Provider与频率 | Unarmed/Pistol/Rifle ×30/60/120Hz，共3780物理帧 |
| 姿态与骨 | Start3150、Cycle3150、Stop2805，共9105姿态 / 737505骨 |
| 交叠/隐藏 | 2079三根共同活跃、63 Stop单独活跃、126全部隐藏，覆盖六种 root order与独立reset |
| Main/时钟 | Main标量/spring/flags、Stop回调模式、三源/HipFire/Lean时钟及marker/delta逐位同；Main向量最大 `1.3642420526593924e-12 cm` |
| Stop模式反馈 | 1884活跃帧设置Accumulate，921活跃帧混出；1881个下一帧 Main更新实际消费上一图Accumulate模式 |
| 最终姿态误差 | Start位置 `2.4293912300207066e-13 cm` / quaternion `1.7538800720617396e-15`；Cycle `2.109680835400689e-13 cm` / `1.7111937024914371e-15`；Stop `9.099386826154683e-14 cm` / `8.280938314956994e-16`；scale0 |
| Root/data | 9000 present / 105 absent RootMotion，TRS差0；36420整数属性、6228曲线值，typed identity/存在性保持 |
| Lean | 7560 occurrence clock checks、14709 sample checks，逐位同；Stop没有对应Lean occurrence |
| 惯性请求 | 81条Cycle换源 `.2` 秒与三个根首次Linked共27条 `.15` 秒，计108；顺序/取消/消费生命周期一致，未验收外层惯性姿态 |
| 事务与绑定 | 24537坏操作拒绝、每帧 prepare-cancel-retry及求值后取消重试；另9个Stop误绑Locomotion group被拒绝 |
| 回归 | 原共同两根3780帧/6300姿态、原Main Stop3780帧/3672姿态通过；70项相关Core测试0失败0跳过 |
| 构建 | UE外部 exporter成功；最终Debug与ExportRelease Optimize均0警告/0错误，Godot验证使用Debug |

原姿态门槛保持 `1e-8 cm / 1e-10 quaternion / 1e-12 scale`，1026个静态 compressed root probe精确相同。两个修正后的 UE commandlet均退出0，summary为 **0 errors / 2202 warnings**，无 Python Error/ensure/assert；原压缩依赖、GameplayTag等warning保留。最终Godot联合进程及回归退出0、成功标志齐全，无ERROR/WARNING。

证据为 `artifacts/lyra-analysis/main-source-stop-*`，汇总 `main-source-stop-final-verification.json`。没有本批普通Demo、渲染、人工矩阵、多角色、并行、打包或性能验收。上一批短资源切换恰逢物理零delta的UE Notify ensure诊断仍未关闭。

## 复跑与剩余项

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-main-source-stop.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject'
python .\tools\verify_lyra_main_source_stop.py
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_main_source_stop_smoke.tscn
```

本批只关闭三分支共同 source/Main owner与Stop状态模式反馈。继续接真实Start10/Cycle14等状态根回调、Pivot/Air/Idle源库存、LocomotionSM遍历/选边和最终状态混合；再统一Notify/Montage、外层惯性、最终FootPlacement/LegIK、Godot live gather、生产入口和完整视觉/并行/打包/性能验收。当前 groupIDs是已知Locomotion/Stop两个组，完整 native通知派发/TMap排序仍需统一队列验收。ALS R2–R7、用户未提交修改和暂缓项保留。
