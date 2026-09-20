# Ragdoll 原始动画、输入与命名姿势快照

第一百三十五批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`。

## 对完整性修复的作用

承接最终根选择器的双分支依赖，补齐 Ragdoll 分支所需的真实动画数据、
FlailRate 输入公式和 NamedSnapshot 求值组件。本批没有把完整布娃娃玩法
提前标记完成，也没有用此项替代 P3/P4 起步、换髋、上身和脚部验收。

当前不能以“同一批动画和部分公式”推导出 Demo 已经 1:1：来源播放器的
时钟与相关性、状态机过渡、缓存、曲线混合与历史反馈、骨骼控制顺序和
最终生产入口也必须一致。每项仍分别记录数据、组件、Demo、UE 对照和
人工验收，不能以组件存在代替最终接入。

## 本批实现

`export_ragdoll_inputs.py` 只读导出本机 ALS V4 的 9 个动画图、4 个角色图、
Ragdoll 烘焙状态机与 9 组 UE 原生 FlailRate 数学样本。正式数据保存至
`assets/config/v4_ragdoll_inputs.json`。初次探索数据保留在
`artifacts/ragdoll-inputs-exploration.json`，没有删除源资产。

`export_ragdoll_sources.py` 复用原始序列导出器，取得 `ALS_Flail` 的原始
骨骼轨道与 84 组 UE GetBonePose 对照姿势。正式来源索引为
`assets/config/v4_ragdoll_source_inputs.json`，对照数据为
`tests/Als.Import.Tests/Fixtures/Ragdoll/native_source_poses.json`。
此片段为非附加动画，没有浮点曲线；所有 UE 导出日志均报告 assets_saved=0。

`AlsRagdollPoseCompiler` 校验正式源图的播放器、FlailRate 连线、无同步
播放器策略、NamedSnapshot 查询名称和 RagdollEnd 保存位置。特别保留：

- FlailRate 读取 owning mesh 的 `root` 骨骼物理线速度，使用三维速度长度，
  以 UE 厘米/秒的 0..1000 映射到 0..1。不能替换成角色水平移动速度。
- `RagdollPose` 保存于 MainAnimInstance，并在 RagdollEnd 的第一个执行
  分支发生；后续才处理移动模式、Get-up 和停止物理。
- 播放器的独立身份、来源绑定摘要和原始资源闭包被校验，不能把另一个
  来源的采样缓存当成 Flail 的播放时钟。

`AlsRagdollAnimationInput` 提供纯输入计算；`AlsNamedPoseSnapshot` 在发布
时复制骨骼名和局部姿势，防止主线程后续物理更新改变 Worker 正在读取的
快照。`AlsNamedPoseSnapshotRuntime` 实现原 NamedSnapshot 求值：

- 相同 mesh 按 mesh 骨骼索引复制，不重新按名字排序。
- 不同 mesh 按骨骼名映射；局部缺骨和虚拟骨保持参考姿势。
- 缺少对应命名快照时输出参考姿势；快照不包含动画曲线，输出曲线清空。
- 按本机 UE 的实际查找/ApplyPose 路径保留 IsValid 元数据，不额外增加
  原节点没有的 IsValid 求值门控。
- 不同角色、已退休代际和未来帧快照在写出姿势前拒绝。

`AlsMovementGraphDefinition` 和原始动画加载器现在正式加载 Flail 来源库，
并复用兼容的不可变骨架资源。`RawAnimationSourceSmoke` 增加
`--ragdoll-source`，通过真实 Godot 动画来源包装器验证该资源。

## 验证与证据

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 最终优化 Debug 构建 | 0 错误、0 警告 | `dotnet build GodotALS.csproj -c Debug -p:Optimize=true --no-restore` |
| 快照组件专项 | 8/8 | `artifacts/test-results/ragdoll-snapshot-core.trx` |
| 最终正式图/输入/资源专项 | 5/5 | `artifacts/test-results/ragdoll-source-import-final.trx` |
| UE 原始动画姿势对照 | 84 组、6,636 骨骼样本 | `artifacts/ragdoll-raw-native.log` |
| 独立来源并行采样 | 4 个所有者，单/并行各 480 次逐位一致 | 同上 |
| 热采样分配 | 64 次来源采样零托管分配；快照组件另测 2,000 次零分配 | 同上及 Core 专项 |
| 实际普通移动生产回归 | 并行 960 帧通过 | `artifacts/ragdoll-sources-worker-parallel.log` |

原始动画最大位置误差 3.674277E-07 米，四元数分量误差 1.3978529E-07，
缩放和曲线误差为零。该结果验证 raw-before-additive 来源采样，不是 UE
最终动画图或布娃娃物理姿势对照。

普通移动回归仍为 result=`D898A6B5BD5DE295`、
fullPose=`7B82A91E8A09C723`、root=`DB5B813964D3479C`、
sampledPose=`6804D603D2523040`。223 个播放器、257 个样本绑定保持；
316 帧脚锁、910 帧脚部偏移、37 个事件，lag/stale=0，代际替换通过，
旧脚部写入为零。Flail 尚未加入该实际帧调度，不把此回归计作 Ragdoll 验收。

UE 导出前按 `ue-diagnosing-plugin-build-load` 技能执行完整 Editor target
构建及插件审计：AlsGodotExporter、AutoTestTools、BlueprintLisp 均通过。
构建日志前缀为 `20260912T225444831Z-e36a3cea775a4adf8953f29fa78bc3c3`，
位于 UE 项目的 `Saved/Logs/PluginBuild/`。本批未修改 UE 原生插件源码。
导出进程均退出 0，正式日志为 `artifacts/ragdoll-input-final-export.log`
和 `artifacts/ragdoll-source-export.log`。

直接核对本机 UE 5.9 的 `AnimNode_PoseSnapshot.cpp`、`AnimInstance.cpp`
和 `AnimInstanceProxy.cpp`，以本地资产与所用引擎语义为准。

## 未完成项与后续顺序

本批完成的是正式数据、组件和定义加载。仍须实现 Ragdoll States 的状态
更新、Flail 播放时钟、仅在原调度分支更新的 FlailRate 历史、真实物理
快照采集及根分支绑定。已导出的状态机为 In Ragdoll / Blend Out Pose，
以 MovementState 判断、双向零秒过渡；不得用根混合时长替代状态机时序。

先完成根的双分支与普通分支相关性、初始化和 CacheBones 生命周期；
随后执行平台/支撑脚、UE 同输入多帧与人工验收，再切换默认生产入口。
不要求先完成 P6 的全部物理玩法才处理当前移动问题。

默认 Demo 仍为 BaseLayer，完整分层/脚部在显式 `--layered-frame` /
`--foot-ik-frame` 入口。当前起步滑步、交错步、上身视觉尚未验收关闭。
本批没有新渲染或人工验证，不能声称这些可见问题已修复。

P5A 剩余通用事件/同步/动作/Slot，P5B 全部 Overlay 与道具玩法，P5C
Mantle/Roll/Root Motion，P6 Ragdoll/Get-up/Pose Recovery/完整 Camera，
P7 十分钟性能验收继续保留。既有 Core 23 项失败、Import 分配不稳定和
p95=2.559ms 超过 2.5ms 均未在本批关闭。音频暂缓，未 commit/revert/merge。
