# 2026-10-11 启动埋点与真实采集

在主目录 `F:/Github/Godot-Locomotion` 实现 EventSource/Stopwatch 启动区间，补充 CLI EventPipe 采集、TraceEvent 离线解析、JSON/CSV、Chrome Trace 与阶段基线差值。保留已有 dirty 修改，未创建 worktree，未改生成资产、活动指针、哈希开关、门禁或动画事务；未提交、推送。

长期协议与命令见[启动性能埋点](../architecture/performance-instrumentation.md)，工具为 `tools/Als.PerformanceTrace`。代码覆盖动画设置/上下文、逐资产 raw 源、动画库与场景加载/绑定、active/spare 图与控制器/预热、Refactored catalog 和 T3D 提取，以及首个可见提交。

## 环境与命令

- Godot `4.7.2.stable.mono.official.ed1daf0bf`，使用实际引擎 exe，未使用 console launcher。
- 项目要求 .NET 8 SDK；系统当前只有 SDK 10，故构建用 `C:/Users/yuchencui/.dotnet/dotnet.exe`（SDK `8.0.425`）。优化构建为 `build GodotALS.csproj -p:Optimize=true`，最终零警告、零错误。
- 工具固定 dotnet-trace `9.0.661903`、TraceEvent `3.2.8`。成功 trace 的游戏 RuntimeInfo 实际为 `.NET 10.0.8`、X64、workstation GC；SDK 目标 `net8.0` 不代表 Godot 必然使用 .NET 8。
- 默认活动包仍为 `als_nonprop_crouch_slip_20261011_v2`，161 个替换身份。成功运行日志确认 `ALS_RESOURCE_PACK_HASH_VALIDATION enabled=False verified=0 skipped=1768`。

成功采集命令（输出每轮独立命名）：

```powershell
& C:/Users/yuchencui/.dotnet/dotnet.exe tools/Als.PerformanceTrace/bin/Release/net8.0/Als.PerformanceTrace.dll capture --godot F:/Github/Godot-Locomotion/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --output artifacts/performance/20261011-startup-instrumented-04 --dotnet C:/Users/yuchencui/.dotnet/dotnet.exe --timeout-seconds 180
```

采集使用普通 `als_demo.tscn`、headless、固定模拟 60 Hz/最高 60 FPS、180 帧。provider 在进程启动前启用，首个可见提交触发停止采集，让嵌入式 CoreCLR 在游戏退出前完成 trace 写出。没有 IDE、GUI 自动化、引擎 fork 或测试资源替换。

## 采集结果与热点

初轮粗粒度成功采集保存于 `artifacts/performance/20261011-startup-instrumented-02/`：1,664 spans、15,351 线程样本、0 丢失、0 错误，active 图/控制器阶段 22.706 s。调用栈指向 `AlsNativeNestedGraph.Extract`、Regex、字符串复制/Split 和 Refactored 资源解析，因此继续增加 T3D/catalog 与 graph/controller 的细分计时。

最终覆盖重复运行保存于 `artifacts/performance/20261011-startup-instrumented-03/` 和 `...-04/`。两轮均 6,444 spans，0 丢失、0 验证错误，游戏/采集器退出 0；分别 14,336/12,835 个线程样本。区间与工作次数一致，但耗时存在正常机器/缓存/负载波动，不是优化前后基准。

| 阶段 | 03 | 04 |
| --- | --- | --- |
| `demo.bootstrap` | 28.762 s | 25.691 s |
| `context.build` | 5.450 s | 5.227 s |
| active `worker.graph_build` | 20.986 s | 18.257 s |
| active `native_graph.extract`，777 次 | 12.754 s | 11.015 s |
| active `worker.controller_create` | 5.438 ms | 5.569 ms |
| active `worker.animation_library` | 约 0.3 s | 0.341 s |
| spare `worker.graph_build` | 0.375 s | 0.357 s |

04 的首个可见姿态提交出现在 EventPipe 相对时间 27.013 s，角色 `0`、generation `1`、frame `2`；这不是 GPU 首次绘制时间。启动根区间托管累计新分配约 38.28 GB，active 图构建约 36.08 GB；其中 T3D 提取累计 28.47 GB，扫描 18,500,094 行。均是分配总量，不是驻留内存，不能把根和子级再次相加。

Refactored catalog 全启动读取 999 次，涉及 161 个不同相对文件；active 图阶段为 840 次、约 1.036 GB 读取字节，读取/哈希/解码在 04 中分别约 0.795/0.398/1.577 s。T3D 提取 777 次涉及 218 个不同子图路径，数据中同一 `AB_Als_Standing` 子图有重复提取。生产 raw 闭包为 123 次读取、约 69 MB，04 的 raw SHA-256 总计约 29.9 ms，解码约 0.618 s。**读取次数不等于不同资产数量；T3D 图定义/配置不是动作 FBX。**

可据此确认当前主要启动成本在首次图配置构建，尤其全量 T3D 重复扫描及其分配；下一步适合检查已编译配置复用、子图提取缓存或导入期预编译，再分别测量 context 自身剩余约 4.3 s。该优化本轮尚未实施，也未证明所有 Refactored 加载均为可删除资源。

## 机器读取与回归

- 04 的 `summary.json` 包含实际 runtime、6,444 个完整区间、父链/资源/角色身份、61 个 phase/role 汇总和调用栈热点；`run.json` 记录三个现有程序集的长度、修改时间和 SHA-256，计算发生在采集前，不扫描生成资源。
- JSON/CSV 对齐：`spans.csv` 6,444 行，`phases.csv` 61 行，`timeline.json` 6,446 个事件（含两个里程碑）。严格父子/线程/完成状态检查通过。
- 03 自比较的所有阶段差值为零，结果 `03/self-comparison.json`；03→04 差值文件为 `04/comparison-to03.json`。这是观测差值，没有自动通过阈值。
- 原始 04 trace 在 `04/reanalysis/` 再解析通过，区间、线程样本、耗时及验证结果一致。
- 新增 Core 测试 Release **4/4**：禁用后零分配、事件格式/实际 runtime/无损角色身份、嵌套关系与分配计量、异常退出与重复 Dispose、跨线程身份隔离。
- 新增解析工具测试 Release **5/5**：只扣直接子级，检查缺失/跨线程父级、同步区间重叠和越界。
- 相关 Import Release 回归 **1,160/1,161 通过，1 失败，0 跳过**，约 5 分钟。筛选 RawAnimationSourceCompiler、Refactored、AnimationSetCompiler、Manifest 测试。

失败为 `AlsRawAnimationSourceCompilerTests.ActualProductionSourceBankCoversRequestedPlayersAndNativeDependencies` 第 345 行：原版 manifest 编译出的 `DefinitionDigest` 为 `10c6abed...`，`assets/config/v4_movement_source_inputs.json` 声明为 `152e7913...`。断言在调用本次改动的 raw compiler 前执行；这些输入与该断言未被本轮更改。独立 .NET 10 testhost 重跑仍复现同一失败，结果保留于 `artifacts/performance/20261011-regressions/raw-source-runtime10.trx`。未跳过断言、重写原版资产或替换旧闭包来制造通过；当前不能宣称整个 Import 矩阵通过。

最终普通禁用采集启动也通过：使用当前程序集、默认活动指针和同样的 180 帧参数，进程退出 0，stderr 为空，无 `ERROR:`。独立日志为 `04/disabled-godot.log`、`disabled.stdout.log` 和 `disabled.stderr.log`；总进程墙钟约 28.008 s，包含有限帧运行和退出，不是启动根 scope 的计时，也不用于估计严格采集开销。

本次涉及的已跟踪文件 `git diff --check` 通过，新文件尾部空格扫描为空。全工作区检查指出用户已有 `assets/config/als_resource_pack.json` 的行尾问题；该文件未被本轮重写。

## 保留的环境失败

01 错误地把构建 SDK 目录同时设置为游戏的 `DOTNET_ROOT=C:/Users/yuchencui/.dotnet`，强制宿主使用该目录的 `.NET 8.0.31`。普通 Demo 在 context 中报 `Mantle full-pose root channels differ from motion source`，没有进入 worker 构建；关闭采集、保持同样 runtime root 时也退出 1，见 `01/disabled-dotnet8-startup.log`。没有修改该一致性门禁。

该次 `.nettrace` 随嵌入宿主提前退出而不完整，离线解析报 `Read past end of stream`；原文件、collector 两路日志、`result.json`、失败解析中间文件与 `01/analysis-error-retained/parse-failure.json` 均保留。工具明确返回非零，未把不完整记录当作成功。成功轮次不再覆盖游戏 runtime root，并使用首个可见提交作为正常停止事件。上述运行时差异只记录实际现象，本轮未进一步修复 .NET 8 下的 Mantle 精度/通道问题。

## 覆盖边界

这是主目录、当前默认 ALS 包的 Windows headless 启动诊断与埋点交付。没有修改启动算法来缩短耗时，没有重跑全部 Core、完整 Godot 运行矩阵、GPU/人工视觉验收、冷缓存受控基准或长期帧性能；Linux/云端 CI 和 Perfetto SQL 执行尚未实际验证。采样提供托管线程栈，不能将样本数当作 CPU 时间，也没有声称解析了原生 Godot/驱动调用栈。
