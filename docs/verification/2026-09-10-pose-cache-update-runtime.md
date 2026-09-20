# 第二十三批：缓存更新上下文与 Standing 子图组合

## 完成边界

本批继续完整性恢复计划 A/B，不新增独立动画时钟或 gameplay 事件队列。
新增的是缓存更新调度，以及 Standing/Stop/Detail 的组合更新入口；不是完整
SaveCachedPose 生命周期，也没有替换可玩 Demo 的临时 MovingWeight。

新增文件：

- `AlsPoseCacheTraversal.cs`：源缓存顺序、调用上下文、延后更新、跳过路径回调。
- `AlsStandingCachedGraph.cs`：将 Standing、Stop、Detail 的更新结果提交到同一缓存遍历。
- `AlsPoseCacheCompiler.cs`：原生缓存顺序/引用编译，以及上述三个状态机的缓存绑定校验。
- `GroundedCacheSmoke.cs`：真实 Detail/Plant 资源、同步时间、曲线和惯性化联合组件验证。

`AlsStopMachineGraph.PrepareSources` 接受已经求出的 Stop 候选，不再为了准备 Plant
选择器重复求值一次状态机。旧独立 Prepare 入口调用它，因此既有组件测试保持覆盖。
Detail 组件测试的源同步辅助类增加父上下文惯性化同步标志，不创建第二套源时间实现。

## 原生数据与行为

只读命令 `AlsStopGraph -IncludeCaches` 输出新的 `v4_pose_cache_graph.json`。
旧 IncludeInputs/IncludeCycle/IncludeInventory 模式的输出格式没有改变。
缓存数据含 9 个原生图入口、32 个缓存生产节点和 105 个引用；这些不是新增播放器。

原生 BaseLayer 更新顺序（compiled node index）：`100, 99, 33, 32, 30, 31`。
Standing 缓存 99 在 Detail 缓存 33 前，后者在 Cycle 缓存 32 前。
这些值读取 `GetOrderedSavedPoseNodeIndicesMap`，不是按名称、编辑器数组或拓扑猜测排序。
AimOffsetBehaviors、BasePoses、Foot IK、OverlayLayer 的合法空缓存顺序也保留。

新导出及独立重复导出的 SHA-256 均为：
`7935D7B91999D58B9E1F3293519A1619563B842C766006B969E3C2D02F9F344A`。

同时运行真实 `FAnimNode_SaveCachedPose` 和 `FAnimNode_Inertialization` 的 Update：
30/60/120 Hz 各 8 例，共 24 例，覆盖不同权重、同权重、全零、低于通常姿势阈值的权重、
零 delta、无共享上下文、关闭请求转发，以及重复 PostGraphUpdate 不重复更新源。
C# 对照原生记录的源更新次数、权重、delta、RootMotion 权重修饰值、惯性化同步标志和
三个惯性化接收节点的请求次数。此处仅传递 RootMotion 上下文数值，不是 Root Motion 功能完成。

对照的引擎代码：

- `AnimNode_SaveCachedPose.cpp:PostGraphUpdate`：最高权重胜出，严格大于才替换，同权重先到优先；
  即使调用权重全为零，仍更新一次选中的源。先更新源，再通知被跳过的共享上下文。
- `AnimNodeMessages.cpp:CopyForCachedUpdate`：每种消息类型保留顶部消息。
- `ActiveStateMachineScope.cpp`：顶部活动状态消息本身包含祖先状态链，不能只保留最内层状态。
- `AnimNode_Inertialization.cpp:Update_AnyThread`：缓存跳过处理器将其节点的待处理请求转发至
  被跳过路径的顶部惯性化接收者；处理器和接收者是不同消息，内层关闭处理器不消除外层处理器。
- `AnimBlueprintExtension_CachedPose.cpp`：缓存编译更新顺序；本次直接导出其最终结果。

## 运行时接线

`AlsStandingCachedGraph` 由上游 Main/Slot 调用方提供带身份的入口上下文，不固定假定
Main Grounded 或 Standing 的上下文权重为 1。图按原生顺序运行：

1. Standing 更新后，Moving 提交 Detail 缓存调用；Stop 先更新自身状态机，再由 Pre-Stop、
   Lock/Plant 内容状态提交同一个 Detail 缓存的调用。
2. 所有上述贡献收集完毕后，Detail 只取最高权重调用更新一次；其上一帧机器权重独立记录。
3. Detail 的各内容状态提交同一个 Cycle 缓存，最终只调用一次 Cycle 源更新。
4. 初始化/清权重、状态事件候选、源注册、惯性化请求交给调用方 sink；不直接分发 gameplay。

选中的上下文保留父状态链、delta、RootMotion 修饰值、惯性化接收者/处理器和同步标志。
未选中的状态链不合并进选中源的 Notify 归属。父惯性化同步标志与子状态标志按继承关系保留。
缓存层不持有资产播放实例，多个 UseCachedPose 不会产生额外源身份。

帧/角色/槽位身份、入口引用和编译顺序均校验。调用已处理的缓存会失败，不补一次额外 Tick。
失败后必须开始新的候选遍历；已提交状态由调用方保留，可用原状态重试。
Core 状态/缓存组合的初始化后热路径与重试无分配。状态链当前容量 16，调用缓冲也有明确容量；
超限失败，不丢弃通知归属。完整图接入时仍需按实际布局审计这些容量。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| Godot Debug 构建 | 0 错误、0 警告 |
| Core 常规 Debug | 1642/1642，排除 AlsP5aGoldenTests、AlsP5aTraceSchemaTests |
| Import 完整 Debug | 692/692 |
| 新 Core Release | 16/16 |
| 新 Import Release | 19/19，含 24 个原生缓存案例回放 |
| Standing 缓存组合测试 | 30/60/120 Hz 起停、Pre-Stop/Plant 归属、部分权重、间断重置、源失败重试与零分配 |
| Godot 新联合组件 | 1260 帧，85680 骨骼检查，1000 Detail 帧、128 Stop 帧、35 次惯性化请求，候选重试一致 |
| 旧 Stop 组件回归 | machine_bones=71400，fixed_sources=12，alloc=0B |
| 旧 Detail 组件回归 | bone_checks=71400，retries=1050，requests=38，resets=6，alloc=0B |

新 Godot 组件通过同一组合更新入口驱动真实 Detail 加法源同步、Stop/Plant 选择器，
再组合 Standing 姿势/FootLock 曲线并经过惯性化；Idle/Cycle 基姿及 Main/Slot 上游上下文
仍是明确测试夹具。该测试不是完整 UE AnimBP/角色输入对照，也未声称联合姿势测试自身零分配。

## UE 构建与失败迭代

按 `ue-diagnosing-plugin-build-load` 技能执行完整项目 Editor 构建、插件审计、冷启动导出、
独立 BuildPlugin、项目 DataValidation 和非 NullRHI Editor 重启，不部署打包产物的 DLL。
该技能引用的额外调试/完成验证技能在当前会话不可用，使用首个失败日志和逐项结果检查替代。

- 默认 dotnet 缺 .NET 10，改为本次进程使用 UE 自带 DotNet/10.0/win-x64，不安装或修改全局环境。
- 初版探针直接使用缓存跳过处理器时链接失败，因为类型符号未导出。改用真实惯性化节点的
  请求入口观测转发，不修改引擎或仿造该内部处理器。私有 UPROPERTY 设置使用原生反射。
- 该失败构建同时重建了已有 NetCore 引擎模块，产生新 BuildId。经核实，项目内旧 receipt/
  三个插件 manifest、DLL/PDB 已移入 `D:\AdvancedLocomotionSystemV\Saved\BuildReceiptBackup\20260910T114718340Z`；
  没有删除源码或资产，备份可恢复。随后完整 Editor 重建与三插件审计通过。
- 初次探针在无共享上下文时调用 GetMessage 触发 ensure；补正确的空检查后，正式与重复导出
  均 exit 0、0 errors、0 warnings。失败日志 `pose-cache-export-20260910.log` 保留，
  正式日志为 `pose-cache-export2-20260910.log`，重复日志为 `pose-cache-repeat-20260910.log`。
- 新 Import 测试初版误指定 skeleton 0，改用现有 locomotion profile 的 SkeletonId；未放宽资产校验。

最后完整构建日志前缀：`20260910T115340266Z-176eac003ccc42a4ad0862a2493ac492`。
BuildId：`ecdb3ab0-4478-4b59-aa61-8240267e9b06`。
独立 Win64 BuildPlugin exit 0，目录 `artifacts/unreal/AlsPoseCachePluginValidation-20260910`。
DataValidation exit 0、0 errors、3 个既有 AI/PawnActionsComponent/旧 Navmesh 警告。
普通 Editor 初始化完成并 TestExit 0；仍有两个既有 `LogAutomationTest: Condition failed`，
以及引擎材质/渲染与旧 AI/Navmesh 警告，不能称为完全干净的交互式启动认证。

## 仍需继续

1. SaveCachedPose 的初始化/骨骼缓存/求值计数与求值作用域、单次姿势求值和相关性历史。
   本批实现的是延后更新语义，不是整个缓存节点生命周期，也不是通用消息栈所有类型。
2. 将组合更新的源回调接到正式 P5 源布局及唯一 Gather/Worker/Commit 事务，补 Main Movement/
   Main Grounded/Slot 的实际上下文，以及 Idle/Rotate/Cycle 全部源的姿势、曲线和通知。
3. 将跳过路径回调接到生产惯性化请求候选和历史，完成 Notify 队列去重/生命周期与统一回滚。
4. 用同输入 UE/Godot 完整状态、源时间、曲线、骨骼与多帧截图验收，再处理动态上身分层。

本批没有修改已人工确认的相机/键鼠，没有新的可玩 Demo 滑移改善结论，没有执行新图
单/多线程完整事务矩阵、两个长套件或十分钟最终性能预算；这些均未关闭。
未提交 Git，也未回退用户修改。后续 Overlay/道具、Mantle/Roll/Root Motion、Ragdoll/Get-up/
Pose Recovery、完整 Camera 和最终性能预算保持原范围，音频继续暂缓。
