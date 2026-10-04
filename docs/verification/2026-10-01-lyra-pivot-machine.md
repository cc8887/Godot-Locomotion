# 原 PivotSM 双源调度与惯性 Sync

2026-10-01，在主目录继续推进 Lyra 移植，源为 GASP58 / 安装版 UE 5.8.1，Godot 4.7.2 .NET。沿用 ALS 模型、68 skin / 81 logical。上一批显式源访问已提升为原 PivotSM 规则驱动的双状态源调度；本批关闭机器 Update、真实子图初始化/访问、回调与共同 Sync 的受控原生对照。完整双 Warp/外层 HipFire 姿态、Main Pivot 状态根和普通 Demo 仍未接入。

## 实际机器执行

`LyraPivotLayerGraph` 校验原16节点闭包、编译机器59、源61/67、共同delegate72、初始PivotA、maxTransitions=1、reinitRelevant与skipFirst标志、两条0.4f FastFeet惯性过渡。新工厂 `CreateMachine` 构造 `LyraPivotMachineRuntime` 和两个独立原源，保留三 Provider 的实际距离资源绑定。

机器输入只包含外层 Active/Weight/Initialize 和 Main/Movement 观察。PivotA/B 的活跃、权重、顺序、初始化和惯性 Sync 标志由机器决定，运行时不读取原生期望输出。Shared StartingAcceleration/TimeAtStop/StrideAlpha 与 Main.LastPivotTime 的源回写仍由共同 pair 持有。状态、源与共享字段一次预校验后提交，隐藏帧和 update-only 可提交必要历史，不求值或发布骨骼。

本机 UE 的关键顺序：

- 首次或外部 Initialize 清机器过渡/elapsed，进入PivotA，清该源 CachedBlendWeight 并初始化子图；保留 NodeRelevancy 的访问历史。隐藏帧的显式 Initialize 也生效。
- Update 的相关性重置按实际物理更新 counter 间隔判断，至少跳过一次访问后自动重入；单纯极小权重仍有机器 Update，不等价于隐藏。
- 先执行原转移规则，再进入目标状态并请求0.4f/FastFeet/HermiteCubic；目标源清 cached weight / Initialize，不清历史相关性观察。
- `bSkipFirstUpdateTransition` **不跳过首帧选边或惯性请求**。它在选择后丢弃首帧过渡记录，因此该目标子图首帧没有惯性 Sync Scope。以后惯性过渡立即只访问目标，带 Scope。
- 子源 BecomeRelevant/Update 与一次共同 Sync 仍按原顺序执行；elapsed 在子图更新后以原 float delta 累加。

Source pair 新增入口清 cached weight 和来自外层的 inertial-scope 参数，默认值保持旧受控源入口行为。Machine 自己不建立动画时钟或额外 Sync。请求保留原 BlendProfile 路径、模式和 duration，后续完整外层求值仍须实际消费它们。

## 原生捕获

`ReadPivotMachineTrace` 在独立 GamePreview 创建真实 Character/Movement、登记的组件、原 Main 和原 Linked provider；直接更新原 PivotSM。两个实际 StateResult 内置观察探针，转发原 Result 链的 Initialize/CacheBones/Update，记录真实子图访问，不替换原规则、回调或两套 Warp 更新。退出前恢复原链接。

输入不提供 `sources/order`。原属性访问批次、反向规则、预测与 source tick 都执行当前 UE 原函数；共同原生 Sync 只有一次。Native Mesh 是采集宿主的 Manny，序列绑定为实际 ALS 目标；本批不进行姿态 Evaluate，因此不以它证明 ALS 骨架上的完整 Warping 姿态。

两套不可变矩阵分别为普通隐藏/重入和首帧反向重入；各含三个 Provider×30/60/120Hz×6秒。第二套在保留 provider 加速度历史时重新初始化机器，使首帧规则实际成立，补齐第一套的覆盖缺口。

| 计数 | 普通矩阵 | 首帧重入矩阵 |
| --- | ---: | ---: |
| 物理帧 | 3780 | 3780 |
| 实际活跃源访问 | 3528 | 3528 |
| 状态切换/0.4f请求 | 810 | 810 |
| 隐藏后自动初始化 | 81 | 81 |
| 子图初始化 | 927 | 1035 |
| 首帧选边、无惯性 Scope | 0 | 108 |
| 带惯性 Scope | 810 | 702 |
| Setup | 927 | 927 |
| DistanceMatch / Advance | 2277 / 1251 | 2277 / 1251 |
| 显式隐藏初始化 | 9 | 9 |
| 坏操作拒绝 | 25227 | 25227 |

状态和elapsed、初始/目标子图初始化计数、访问顺序/权重、原惯性请求profile/mode/duration、预测位置/距离、共享字段、源选择/显式与内部时间、Marker/Delta/leader均按原 float/double 位模式一致。保持原门槛，不放宽精度。

每帧 prepare-cancel-retry，对求值之前的旧状态与源逐项确认没有发布。额外注入子源绑定失败，发生于机器已完成选择/初始化计算之后；修复依赖后同身份重试与干净宿主完全一致。测试还拒绝跨机器候选、旧候选、缺失、坏epoch/sample/marker/非有限时间及重复提交，机器和源不发布半帧状态。

## 构建、回归与字节保护

- 最终机器两套 smoke 同一进程正常退出0，无Godot ERROR/WARNING，唯一两个成功标记。
- 原双源3780帧回归仍为7236活跃/3564双源/36资源/29493拒绝，逐位通过。
- Main/Start/Cycle/Stop共同历史3780帧、9105姿态、737505骨回归通过，原门槛保持；仍非完整Main机器。
- 最终 Debug 与 ExportRelease Optimize 均0错误/0警告；Godot运行验证使用Debug。
- 最终外部 UE exporter 构建成功；普通机器、重入机器和旧双源复导均正常退出0。机器矩阵的0错误/既有Footstep GameplayTag与toolset等警告保留。
- 每份机器捕获保护508个UE包，第一份保护608份之前JSON，第二份保护611份之前JSON；新增六份JSON保留在ignored `assets/generated/lyra_als/`，旧双源资源字节保持。
- 重复旧双源导出现在复用原 manifest 的保护集合，避免后来新增JSON导致旧不可变fixture manifest变化；旧native数据没有重写。

本批没有变更UE原工程C++/配置/旧资产或保存新uasset；只构建外部采集插件、执行只读原生捕获。没有渲染、人工、全量、十分钟、性能或打包验收。

## 失败记录

首机器捕获在脚本成功并写出数据后，于Python shutdown发生access violation，进程退出3，不能算成功。日志 `pivot-machine-ue-shutdown-failed.log` 保留。随后补齐 transient 探针原链接恢复，同时移除脚本跨退出保留的UE绑定方法引用；修订后的机器复导与首帧重入、旧双源进程均正常退出。两项修改的独立因果贡献未单独隔离，不宣称引擎Python退出问题已全面修复。

第一份Godot轨迹的逐帧比较已全过，但最终覆盖检查失败，因为它没有首帧规则成立的样本；日志 `pivot-machine-godot-first.log` 保留。新增第二套独立fixture覆盖108次，不覆盖或放宽第一份数据。

## 复跑与下一步

```powershell
.\scripts\build-gasp58-exporter.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -ExternalOnly
.\scripts\export-lyra-pivot-source.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -Machine
.\scripts\export-lyra-pivot-source.ps1 -EngineRoot '../UE_5.8' `
  -UnrealProject '..\GASP58\GASP58.uproject' -Machine -Reentry
.\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . scenes/tests/lyra_pivot_machine_smoke.tscn
python tools/verify_lyra_pivot_machine.py
```

最终摘要 `artifacts/lyra-analysis/pivot-machine-final-verification.json`。验收器还需要上述命名的回归/构建日志，单条smoke不会产生完整交付证据。

下一步让此机器驱动两套独立Warp求值历史，再按原拓扑在外层混HipFire；随后接Main Pivot StateResult的方向锁存/LastPivotTime更新/Lean和共同角色Scope。Idle/Air、完整LocomotionSM、Layer热换类和统一Notify/Montage、最终足部、普通Demo与整链验收继续开放。本批没有关闭完整Pivot或整个移植目标，ALS R2–R7和用户未提交修改保留。
