# 第十三批：同步后的通知提取窗口

日期：2026-09-10。工作区：`../GodotALS-p5a-events-actions`。
归属：原 P5A Sync / Notify 事务接线的时间语义补项。目标仍是完整移植，不是另建事件系统。

## 接线检查发现

`AlsP5Runtime.TryPrepare` 先同步，再用映射时间计算曲线和 Timeline。
但旧 Timeline 合同要求本帧 PreviousTime 与已提交 cursor 完全连续、时间只能向前，
且 PreviousTime == CurrentTime 时不能占用非零帧窗口。

UE `UAnimSequenceBase::TickAssetPlayer` 的行为不同：

- Leader 重同步、Follower 时间比映射都能改变本帧起点，不代表新播放身份或新的 epoch。
- 通知调用 `GetAnimNotifies(PreviousTime, MoveDelta, Context)`；MoveDelta 不是总能用
  CurrentTime-PreviousTime 代替，尤其是端点 Leader 和带负速率修正的 Follower。
- 零推进依然可以提取当前重叠的 Notify State，不能把整帧当成无效时间窗口。
- 提取后还有 Queue 过滤/状态去重，再由 `UAnimInstance::TriggerAnimNotifies` 处理
  跨帧 Begin/Tick/End。旧连续边界 Timeline 不能只放宽一个校验就宣称与此等价。

另一个数据合同差异：当前 `AlsNotifyClassRegistry.cpp` 使用 `GetTime()` / `GetDuration()`，
没有保存有效触发偏移。原生提取使用 `GetTriggerTime()` / `GetEndTriggerTime()`。
本批读取的真实 Walk F 两个通知没有偏移，因此此数据缺口不能直接解释当前 Walk F
视觉问题。偏移边界通过明确标记的合成通知验证，不能冒充 ALS 实际资源的偏移。

## 本批实现

在既有 `AlsTimelineRuntime` 中新增 `TryExtractNonLoopingAssetNotifies`，不更改旧入口行为。
输入是单个资产的有效通知边界、本次 Tick 起点和带符号推进量；输出是按原始资产数组
顺序排列的通知定义索引、事件 ID 和 ReachedEnd 上下文，不自行排序或分发回调。

正向区间用 start <= current && end > previous；反向用 start < previous && end >= current。
推进先钳制到资产端点；钳制后不移动时使用正向/零推进的重叠规则。支持有效触发边界
略超出资产首尾，不能拿 authoredTime 范围检查错误拒绝这些偏移。

实现不持有时钟、cursor、事件队列或所有权；不补发同步跳过区间内的通知。
它是将原生提取语义纳入现有事件模块的第一步，而不是生产 Notify 迁移已经完成。
有限数值、合法区间、唯一事件 ID、输出容量全部通过后才写输出；失败保持输出不变。
预热后 1,000 次提取为 0 B 托管分配。

## 原生证据

新增 `AlsNotifyWindowCommandlet`，真实调用 `UAnimSequenceBase::GetAnimNotifies`，
通过公开 `UAnimNotifyLibrary::NotifyStateReachedEnd` 读取结束上下文。

- 六个真实资源：四个 Detail Accel、Run BasePose、Walk F，共两个原生通知。
- 五个临时测试动画：长度与同步资产分别一致，继承 UE 原生提取，每个含九个刻意
  不排序的通知，覆盖交叠状态、瞬时通知、首尾及正负触发偏移。不执行通知回调。
- 1,539 个直接窗口 + 9,726 个上一批原生同步 Tick 窗口，共 11,265 个窗口。
- 分别用原生窗口输入和 Core Sync 连续自主时间回放输出进行提取，比较完整结果顺序、
  索引和 ReachedEnd，均精确一致，没有为布尔事件结果放宽容差。
- 测试明确反证：从旧 cursor 扫到新位置会补发被跳过的通知；使用姿势采样时间差会
  漏掉负速率 Follower 的实际 Tick 窗口。

原生 fixture：`tests/Als.Core.Tests/Fixtures/P3/v4_notify_window_native.json`，2,550,367 bytes。
SHA256：`8BE658ADCC871FB6E7EC7CDC35446D5FEA7DB528C38D8BD6D981ACB4AD5AAA4E`。
它依赖上一批 `v4_length_sync_native.json`，SHA256：
`C66141B04B51AC950BCE0F0BC8F47100B783F9516D8C7F6DC4CB7D7372F91FE3`。

按照 `ue-diagnosing-plugin-build-load` 技能完成完整 Editor target 构建和跨插件审计后
运行 commandlet。技能引用的 superpowers 技能未安装，使用实际构建、审计和对照测试
作为证据，不声称执行了不可用技能。

最终构建日志前缀：`20260910T064550670Z-559251257b3e42ab9fe78c77154e37fc`。
最终 BuildId：`0de43418-1cd2-47c8-9f70-40628649acf3`。
Build fingerprint：`6DD926FD7964D33B10D3A9B3EA2B67BFEE4C15667FD3D1C427762FE3EEE7E06B`。
最终日志：`artifacts/notify-window-native-transient-20260910.log`，退出 0，0 errors / 0 warnings。

```text
ALS_NOTIFY_WINDOW_OK assets=11 native_notifies=2 windows=11265 sync_windows=9726 assets_saved=0
```

初次构建暴露内部 EndDataContext 类型符号没有跨模块导出，已改用公开引擎接口，
没有改引擎或复制 DLL。第一次可执行探针复制完整动画产生五条 FBX 导入数据依赖加载
警告，后改为仅定义长度/通知的临时测试动画；两份结果 SHA256 完全一致。
第一次结果保留为 `artifacts/notify-window-duplicate-source-20260910.json`。
最终构建正常重建了 UBT 判定需要更新的 NetCore；未手工修改任何引擎源文件或 BuildId。
未运行 GUI 重启、项目数据验证或打包，不将本批当作插件发布验收。

## 验证结果

- 新提取测试 15 项，覆盖原生窗口、Core Sync 连续回放、正反向边界、零推进、
  偏移、跳过区间、Tick delta 与姿势时间差、无效数据、容量失败原子性、重试、
  无分配和无托管引用的顺序布局合同。
- 相关 Core Locomotion / Sync / 提取测试 Debug、Release 分别 407/407。使用独立输出目录
  `artifacts/notify-window-tests-bin/` 和 `artifacts/notify-window-tests-release/`，避免覆盖
  仍被大回归占用的测试程序集。
- Import 全量 600/600；Godot 编译 0 warnings / 0 errors。
- Core 扩展回归 1547/1547，失败 0、跳过 0。筛选为
  `FullyQualifiedName!~AlsP5aGoldenTests&FullyQualifiedName!~AlsP5aTraceSchemaTests`，
  保留 Timeline、P5 事务及本批提取测试，未覆盖上述两个长时集合。
- DetailMachine 联合回归仍通过，日志为
  `artifacts/detail-machine-notify-window-regression-20260910.log`。该场景仍标记
  `sync=length_runtime demo=not_connected`，本批没有把提取入口冒充已接入该场景。
- Standing Cycle 单/多线程与回滚回归通过，日志为
  `artifacts/standing-cycle-notify-window-regression-20260910.log`。
- Camera/Input 文件 SHA256 与上批一致。未跑完整 Release/P7、UE GUI 或新的 Demo 多帧截图。

一次同目录补跑在复制测试 DLL 时被正在运行的大回归锁住，MSBuild 重试后失败；
当时保留该运行，通过上述独立输出目录完成最终相关测试。随后确认大回归筛选仅
排除了 GoldenTests，误包含 TraceSchemaTests 的全量变异矩阵；运行超过 30 分钟后，
核验进程身份并取消了本次启动的测试进程树。该运行按取消记录，不算通过；随后用
同时排除这两个集合的筛选重新运行，得到上述 1547/1547 结果。两个完整长时集合
仍属于正式集成后的验收门禁，不能用这次筛选回归替代。

## 未完成和继续顺序

本批没有改动正式 P5 bindings、存储容量或 Prepare/Finalize 提交合同；也未把新入口
接入可玩 Demo，因此没有新的滑步、上身或交错步改善结论。

接线需要同时补齐以下合同，不能把源时间跳转伪装成 epoch 重启：

1. 用真实源图替换 Base22/总49布局，记录 Cycle、Stop、Detail 的每个采样身份与组历史。
2. 保留 authored time，同时导出/编译有效触发边界及 Queue 所需的 Follower、LOD、
   权重/概率、状态实例行为等配置；旧数据不能缺字段时默认成“原生等价”。
3. 复用本批提取，然后接 Queue 过滤/状态去重和跨帧生命周期。源码确认当前 UE 支持
   `NoMergeOnConcurrentPlay`，所以独立播放器时钟与 Notify State 是否合并是两个
   不同问题，不能只按 AnimationId 合并时钟，也不能忽略配置固定一种状态合并策略。
4. 共同推进 Prepare/Finalize：源时间、组历史、队列候选和事件所有权只在成功后提交，
   覆盖晚期失败、重试、零推进、重同步和并发源；再接外层起停与 Demo。

原计划 A/B/C/D 的其余范围不变，音频继续暂缓。
