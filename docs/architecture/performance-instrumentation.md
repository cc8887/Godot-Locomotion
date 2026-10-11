# 启动性能埋点

Godot-Locomotion 性能埋点使用独立于 IDE、面向本地与云端 CI 的跨平台命令行技术栈：C# EventSource + Stopwatch 埋点、dotnet-trace/EventPipe 采集、Microsoft.Diagnostics.Tracing.TraceEvent 解析并输出 JSON/CSV 汇总与基线对比，Perfetto Trace Processor 作为可选 SQL 查询工具。

## 开关与职责

`src/Als.Core/Diagnostics/AlsPerformanceTrace.cs` 是纯 .NET 埋点入口，provider 为 `GodotAls-Performance`，Startup keyword 为 `0x1`，level 为 Informational（4）。普通启动默认不记录；只有 EventPipe/ETW 采集器或 EventListener 启用该 provider 才创建计时 scope。禁用路径预热后不分配，首次访问 EventSource 本身有一次初始化成本。

Godot/Import 在已有同步加载和构建调用两侧记录 scope，不改变资源选择、哈希门禁、动画时钟、图求值或提交/取消事务。Godot 资源调用涉及引擎原生代码：scope 记录整个调用的墙钟耗时，托管分配只统计当前 C# 线程，不包括引擎原生分配。

scope 使用当前线程的父链，继承角色 ID、generation、active/spare、资源路径和动画 ID；角色 ID/generation 用 `long` 无损承载现有 `uint`，未知值为 `-1`。不要跨 `await` 或线程传递 scope。进入 worker 回调时应显式提供角色身份。`Complete` 只在操作成功后调用；异常和未完成的退出仍发送结束事件，`completed=0`。

## 当前覆盖

| 路径 | phase |
| --- | --- |
| 普通 Demo 场景加载、实例化、配置 | `demo.bootstrap`、`demo.scene_load`、`demo.scene_instantiate`、`demo.ready`、`demo.configuration` |
| 编译动画设置读取、摘要与反序列化 | `animation_set.resource_load`、`animation_set.definition`、`animation_set.payload_hash`、`animation_set.deserialize` |
| 上下文、移动配置、足部设置 | `context.build`、`movement.definition`、`context.foot_settings` |
| raw 源闭包编译、逐资产读取/摘要/解码 | `raw_sources.compile`、`raw_sources.asset`、`raw_sources.read`、`raw_sources.payload_hash`、`raw_sources.decode` |
| active/spare 角色与 worker 初始化 | `character.create`、`worker.configure`、`worker.pose_profile`、`worker.animation_library`、`worker.graph_and_controller`、`worker.graph_build`、`worker.controller_create`、`worker.warmup`、`worker.complete_movement`、`worker.pose_modifier`、`worker.native_feet` |
| 动画库、逐动作场景及轨道绑定 | `animation.library_build`、`animation.clip`、`scene.load`、`scene.instantiate`、`animation.duplicate`、`animation.bind` |
| 角色原始 source player 构造，不推进帧 | `animation.character_sources` |
| Refactored 原始资源与蓝图 T3D 子图提取 | `refactored.catalog_read`、`refactored.read`、`refactored.payload_hash`、`refactored.decode`、`native_graph.index`、`native_graph.extract` |
| 通用资源实际加载与派生构建 | `resource.load`；Event 5 `ResourceCache` 记录缓存命中、失效、替换及释放 |

`demo.configured` 标记初始化完成；`character.first_visual_commit` 标记角色首个可见姿态提交（包含角色、generation 与 frame ID），不是 GPU 首次绘制证明。RuntimeInfo 记录游戏实际的 .NET 版本、进程架构和 GC 模式。当前没有逐帧 worker 各阶段的显式 scope；后续运行阶段的采样暂标为 `outside_startup_scopes`。

## 命令行

在项目主目录执行，使用 `global.json` 所要求的 .NET 8 SDK；游戏实际运行时由 Godot 宿主决定，两者应分别记录。不要为选择构建 SDK 而直接强制更换游戏的 `DOTNET_ROOT`。运行所需的 ignored 资产和 Godot 导入缓存必须存在，纯代码检出不是可运行基准。

```sh
dotnet tool restore
dotnet build GodotALS.csproj -p:Optimize=true
dotnet run --project tools/Als.PerformanceTrace -c Release -- capture --godot <实际的Godot.NET引擎可执行文件> --project <项目绝对路径>
```

仓库工具清单固定 `dotnet-trace` 为 `9.0.661903`，独立解析工具固定 TraceEvent 为 `3.2.8`，这些依赖不进入游戏/Core。`--dotnet <路径>` 可以指定采集器使用的 SDK host。默认组合 `dotnet-common,dotnet-sampled-thread-time`；只需要区间数据时可用 `--profile dotnet-common`，分配类型调查可另选 `gc-verbose`。

capture 从进程启动前配置 EventPipe，在首个可见姿态提交后停止采集，以便嵌入式 CoreCLR 正常写出完整 trace；游戏按 headless、60 Hz 固定模拟增量与最高帧率、180 帧有限退出。可调整 `--frames`、`--timeout-seconds`，额外 Godot 参数放在末尾 `--` 后。需要传游戏自定义参数时保留 Godot 自身的 `--`，例如 `... capture ... -- -- --single-thread`。

每次输出到新的 `artifacts/performance/<UTC时间>/`，`--output <目录>` 可指定独立证据目录；拒绝覆盖已有证据。超时只终止本次创建的采集器及其子进程。采集器/游戏非零退出、解析失败、事件丢失、缺失开始/结束、异常退出均不能视作成功；capture 还要求启动里程碑完整。独立 analyze 可用于只有 Import 区间的诊断 trace，不强制要求 Demo 里程碑。

```sh
dotnet run --project tools/Als.PerformanceTrace -c Release -- analyze --input <startup.nettrace> --output <新的分析目录>
dotnet run --project tools/Als.PerformanceTrace -c Release -- compare --baseline <基线summary.json> --current <当前summary.json> --output <新的comparison.json>
```

compare 输出按 phase/worker role 对齐的差值，允许新增或移除 phase；没有自动设定的通过阈值。比较前核对活动包、实际运行时、构建、采集 profile、场景参数及缓存/系统负载。同一份 summary 自比较应为零；新增子 scope 会把原父级的自身耗时归入子级，因此分析覆盖改变后应重新建立基线。

## AI 数据入口

| 文件 | 用途 |
| --- | --- |
| `startup.nettrace` | 原始 EventPipe 证据，可反复离线解析 |
| `run.json`、`result.json`、`validation.json` | Git 状态、活动指针、参数、工具版本、现有程序集摘要、进程退出/超时、验证结果；程序集摘要在采集前计算，不扫描生成资产 |
| `summary.json` | schemaVersion、实际运行时、完整区间与父链、里程碑、按阶段/角色汇总、GC/JIT 等运行事件计数、按阶段分类的托管调用栈热点 |
| `phases.csv`、`spans.csv` | 表格分析、逐资源/角色筛选 |
| `resource-cache.csv`、`summary.json.resourceCacheEvents` | 按资源 ID、manager、操作和资源版本统计命中、重复请求及生命周期 |
| `timeline.json` | Chrome Trace 格式的启动区间与里程碑，供 Perfetto UI/Trace Processor 使用 |
| `collector.stdout.log`、`collector.stderr.log` | 游戏及采集器输出，包括实际选中的资产包和完整性校验状态 |
| `events.etlx`、`parse-failure.json` | 本地解析中间文件；失败时保留结构化解析诊断 |

优先读取 `validation.json` 和 `summary.json.errors`，再按 `phases[].selfMs` 找热点；逐资源用 `spans` 的 `assetPath`/`assetId`，调用关系用 `parentId`，角色区分用 `characterId`/`generation`/`workerRole`。`sampleHotspots` 给出方法名、包含该方法的样本数和叶节点样本数；这些是线程调用栈样本数，不等于 CPU 时间。GC/JIT 表当前是事件计数，没有把 GC collection 时间误标为暂停时间。

`durationMs` 由 Stopwatch 计算；`selfMs` 扣除直接子 scope 的 Stopwatch 耗时，不再重复扣除孙级。`startMs`/`endMs` 是 EventPipe 事件相对时间，时间线使用该开始/结束区间保持严格嵌套，Stopwatch 数值保存在 args 中。托管分配是包含子级的累计新分配量，不是驻留内存；不同层级的分配、耗时或 byteCount 不应全表相加。itemCount 的单位随 phase 而定：动作库为动作数、读取为次数、raw 解码为采样键数。通用资源管理接入后，T3D 索引为输入行数，实际提取为切片输出行数，缓存命中用 ResourceCache 事件记录；此前基线的提取统计是每次全量扫描的输入行数，不能直接比较两者的 itemCount。见[资源管理契约](resource-management.md)。

可选 Perfetto SQL 按某个 phase 查询；这里只查询同一类区间，避免把父子耗时相加：

```sql
SELECT name, COUNT(*) AS calls, SUM(dur) / 1e6 AS total_ms, MAX(dur) / 1e6 AS max_ms
FROM slice
WHERE name = 'native_graph.extract'
GROUP BY name;
```

EventPipe 在 Windows 主要提供托管栈，当前没有原生 Godot/驱动栈符号解析；需要原生 CPU/IO 调度数据时仍需独立平台采集。当前验证是 Windows 普通 ALS headless 启动与命令行导出，不代表 GPU 首帧、人工动作验收、Linux/云端实际运行或长期帧性能通过。原始基线、失败和热点见[埋点验证](../verification/2026-10-11-startup-performance-instrumentation.md)，最新重复加载优化见[资源管理验证](../verification/2026-10-11-resource-management.md)。

工具协议参考 [EventSource](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventsource-instrumentation)、[dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)、[TraceEvent](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent/3.2.8) 和 [Perfetto Chrome Trace 支持](https://perfetto.dev/docs/getting-started/other-formats#chrome-json-format)。
