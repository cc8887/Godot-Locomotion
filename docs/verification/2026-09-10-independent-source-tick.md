# 第二十七批：独立来源 Tick 与共享批次

## 结果与边界

已补 DoNotSync 的独立来源 Tick，并纳入上一批的候选批次。命名组先求值，独立来源
随后按注册顺序各自求值；两部分都成功才写出候选历史，不新增动画时钟、虚构同步组
或事件队列。真实 Lean 原生轨迹已通过独立入口及批次入口回放。

实际 Demo 仍使用七个 Cycle 来源，Lean 的加法姿势和完整 Standing/Detail/Stop 外层
尚未接入。本批是正式全源接线所需的时间运行时补项，不是上身或滑移的视觉修复。
没有修改相机/输入，没有新的多帧捕获，也没有提交或撤销 Git 更改。

## UE 依据

本机引擎源码：

- `Engine/Source/Runtime/Engine/Private/Animation/AnimSync.cpp:369`：每个无组来源
  单独构造 single-animation TickContext，无组来源之间没有 Leader 排序或共享比例。
- `Engine/Source/Runtime/Engine/Classes/Animation/AnimationAsset.h:430`：新 TickRecord
  的 bCanUseMarkerSync 默认 false；无组路径不执行组 Prepare 的启用过程。
- `AnimationAsset.h:820`：每个新上下文的 Leader=true，初始比例为零，不请求组重同步。
- `Private/Animation/AnimSequenceBase.cpp:500`：独立 Sequence 因标记同步未启用而
  按自身秒时间推进，仍保留实际 Tick delta；非循环末尾钳制不把 delta 缩成姿势时间差。
- `Private/Animation/BlendSpace.cpp:573`：single-animation 条件可启用 BlendSpace
  内部样本标记同步，并按 Animation 复制旧样本时间/标记历史。它与跨播放器同步不同。

本批只读源码和已有原生夹具，没有修改/构建 UE 插件、启动 UE 或重新导出资源。

## 实现

`AlsIndependentAssetRuntime.cs` 新增 `TryEvaluateIndependentAssetPlayers`，复用现有
TickAssetSyncPlayer、标记和长度算法。每个来源获得新的局部上下文，不使用命名组的
上帧比例、Leader 或惯性化入组重同步。初始化标记/样本历史的公共部分由原组路径提取
为共享助手；仅相同 PlayerId、AssetId、Epoch 可借用历史，组路径的额外失效规则保留。

独立 Sequence 不启用标记推进；独立 BlendSpace 可保留内部标记历史。不同独立来源
可以拥有不同标记集合。当前带标记 BlendSpace 仍限循环和完整一致的内部标记集合，
MatchSyncPhases、镜像、Root Motion 提取不在本合同内。Stop TeleportEvaluator 不是
普通播放器，仍不得借用此 API 自动推进时间或生成通知。

`AlsAssetSyncBatchRuntime.cs` 接受 playerGroupId=-1，声明的 groupIds 仍只能是非负
命名组。输出先为命名组范围，再为独立来源尾部范围；独立尾部没有 GroupHistory。
批次核对完整历史范围和全局源/采样身份，重映射后调用独立 Tick，统一暂存后发布。
命名组 128/512 上限不变；独立来源使用批次 512/2048 总上限，无人工拆组。该容量
不是最终 P5 物理布局，也不代表最终性能预算通过。

## 验证

原生夹具仍为 `v4_blendspace_tick_native.json`。scenario 6 的真实 Lean 在 30/60/120 Hz
共 192 帧，现已不再跳过：

- Core 分别经独立入口、统一批次回放，逐帧比较自身时间、Tick 窗口、每个样本的时间、
  前值和标记记录；从上次自有历史推进，再从同一旧状态重试，均一致。
- 原有 1152 帧命名组轨迹继续经直接/批次两个入口回放，保留原容差。
- Import 用正式源绑定回放全部 1344 帧，Lean 的 SyncGroupId=-1、样本身份及资产
  元数据由编译绑定提供。原生探针刻意从 .2 起播，而 Lean 节点默认是 0，测试明确区分，
  不将该探针说成节点默认初始化验证。

原生输出只提供已经解析的样本权重/顺序和缓存倍率作为 Tick 输入；期望源时间、
样本时间或标记不反灌。这是原生 Tick 对照，不验证 Lean 二维输入滤波/网格权重、
完整 AnimBP 或最终骨骼。Lean 本身没有标记，带标记独立 BlendSpace 另由源码依据和
定向单元测试覆盖，不宣称新增了原生带标记独立来源探针。

新增 14 项独立来源测试覆盖权重不耦合、入组标志不触发独立重同步、非循环末尾与
未缩短 delta、不相交标记集合、epoch 重置、错误历史、晚组/独立尾部失败原子性、
别名提交重试、退出及零分配。批次新增 512 播放器/2048 样本全独立来源容量用例。

联合源测试扩展为仅命名组及包含 Lean 两种配置，分别在三种频率运行共 840 帧。
包含 Lean 时为九个 Cycle 加十六个 Detail，共 25 播放器/47 样本；使用真实绑定但
贡献权重是夹具，不是 Demo 接线，也不冻结全图布局。

| 检查 | 结果 |
| --- | --- |
| Core 常规 Debug，排除 P5aGolden/P5aTraceSchema | 1708/1708 |
| Core Sync/独立来源相关 Release | 48/48 |
| Import 源相关 Debug | 38/38 |
| Import 全套 Debug 首轮 | 697 通过，1 失败 |
| Import 失败项单独复跑 | 1/1 |
| Import 全套 Debug 原命令复跑 | 698/698 |
| Godot 优化 Debug 构建 | 0 错误、0 警告 |

Import 首轮旧 `RepeatedCreateViewsAllocateZeroBytesAndKeepStableHeadersAndSpans`
零分配断言测得 4136 B；单独及全套复跑均通过。未改其代码、预热次数或断言，不据此
断言分配来源。失败和重跑 TRX 均保留在 `artifacts/test-results/independent-source/`。

实际资源组件：

```text
STANDING_CYCLE_OK rates=30,60,120 phases=3 hip_transitions=9 wait_frames=371 rollback=9 interrupted_rollback=9 max_active=11 movement_direction=36 pose_bridge=63 alloc=0B active_alloc=0B curves=603 source_timing=15216 timing_authority=1260 movement_frames=5040 sync=asset_runtime
DETAIL_MACHINE_OK rates=30,60,120 states=6 bone_checks=71400 retries=1050 requests=38 resets=6 correction=0.064214 alloc=0B sync=asset_runtime identities=source_graph demo=not_connected
GROUNDED_CACHE_OK rates=30,60,120 frames=1260 bone_checks=85680 detail_frames=1000 stop_frames=128 requests=35 pose_evaluations=3251 pose_cache=scoped sync=asset_runtime cycle_update=once upstream_pose=fixture demo=not_connected
```

实际 Cycle 的 single/parallel 各 180 帧通过，result=`C658034A39C5917B`、完整姿势
`076E345A0151A012` 均未变。两种模式晚期失败注入确认 source_sync、movement_input、
controller、pose、p4_banks 恢复。日志为 `artifacts/independent-*.log`。

完整 `verify-p4-pose.ps1` 通过：图、曲线、转身、两种模式脚部、晚期回滚及姿势门禁。
拓扑/活动姿势各 10000 次为零分配，活动耗时 4599.974 ms。该结果仍非 P7 十分钟预算。

## 下一步

不再将独立 Tick 或 Lean 原生时间轨迹列为未实现；接下来推进正式 source-aware
快照/身份和现有 Controller/Worker/P5 事务。当前旧 Base22 编译器仍明确拒绝新源图，
不能移除这道保护后将不同来源合并为同一个动画身份。

完整生产接线必须包含实际 Cycle、Lean/Sprint Impulse、Standing/Detail/Stop、缓存、
惯性化与 Main/Slot 上游，曲线及通知窗口使用同一候选源历史。既有组件不能替代实际
图贡献。随后继续动态上身分层、Overlay/道具和原定 P5C/P6/P7，保留同输入原生整图、
关键骨骼、多帧移动与人工验收。
