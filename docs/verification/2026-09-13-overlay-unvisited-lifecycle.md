# Overlay 初始化、隐藏与共享来源恢复

第一百四十三批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 范围与实现

本批承接 P3/P4 完整最终图所需的 P5A 生命周期依赖，不是 Overlay 道具
玩法完成，也没有切换默认 Demo。当前不能称为完整 1:1 移植。

AlsOverlayStateMachine 接受实际 UpdateCounter，相关性采用本机 UE
StateMachine 的 WasSynchronizedCounter 语义。FrameId/Serial 仍用于
事务顺序校验，但不再因编号跳变重置连续访问的机器；真正跳过动画 Update
后重入会初始化。显式模式拒绝改回旧的隐式计数所有权。

AlsOverlayPoseRuntime 的候选/已提交银行保存完整遍历。冷启动和显式
Initialization 计数变化访问正式图的初始化闭包，隐藏时也生效；相同
计数只改变 GlobalFrame 不重复初始化。updateSource=false 要求显式
遍历，不更新节点、混合状态、来源或通知，也禁止求值与读取候选姿势。
隐藏候选可校验并提交身份与初始化。取消和来源求值异常保留旧银行。

Overlay 的正式惯性化配置关闭 bResetOnBecomingRelevant。因此普通
隐藏/恢复保留历史，显式 Initialize 才清空，不把状态机的重入重置规则
错误地应用到外层惯性化。骨架与 BlendProfile 索引在构造时绑定到固定
逻辑骨架；Bones 遍历变化不重置来源，不代表动态骨架或 LOD 已支持。

映射生产入口 AlsLayeredAnimationFrameRuntime 向 Overlay 传同一
实际遍历。旧受控组合入口保留旧 Overlay 模式作为兼容路径。最终根仍
未开启混合；隐藏生命周期由真实 Base/Overlay 组合专项调用验证。

共享来源收集器原本已支持无来源 Update 的初始化批次，本批验证其在
隐藏图中的实际连接：先准备 Base 全局状态、再到来源收集边界准备
Overlay；两个图都隐藏时共享 Player/Sample 数为零，但初始化 epoch
仍参与候选。Sequence 初始化回到 authored start；Evaluator 初始化
保留累计时间，不冒充一次 Evaluate 或同步 tick。

## 来源核对

直接对照现有 ALS V4 正式资产和本机 UE 5.9：

- `Engine/Source/Runtime/Engine/Private/Animation/AnimNode_StateMachine.cpp`
  的 Update 相关性判断。
- 同目录 `AnimNode_Inertialization.cpp` 的 Initialize 清空历史与
  bResetOnBecomingRelevant 门控。
- AlsOverlayPoseCompiler 对正式 Overlay 惯性化、状态机及固定骨架
  语义的现有编译约束。

没有混用 ALS-Refactored 版本规则，没有启动 UE、修改插件或重新导出。
本批没有新增 UE 最终图逐帧 oracle。

## 验证

优化 Debug 构建通过，0 警告、0 错误。Import Overlay 专项 89 项全部
通过，结果 `artifacts/test-results/overlay-unvisited-import.trx`。
新增覆盖计数回绕、FrameId 跳变、Update 间断、隐藏初始化/骨骼刷新、
同初始化计数不同 GlobalFrame、惯性化历史保持以及失败后同帧重试。

Godot `overlay_shared_frame_smoke.tscn -- --unvisited-overlay` 在
30/60/120 Hz 共 630 帧通过：207 隐藏、6 初始化、3 隐藏骨骼刷新、
6 恢复、7 来源求值故障。每帧取消并重试，检查共享时钟/epoch、来源
事件、生成通知、姿势/曲线与惯性化银行不泄漏。使用真实 Base 与 Overlay
资源、223 players/257 samples；根选择和物理输入受控，不是物理 Ragdoll
或最终根生产调度。日志 `artifacts/overlay-unvisited-shared.log`。

`--owned-frame` 的完整上身/手部组合 1,260 帧通过，日志
`artifacts/overlay-unvisited-owned.log`。直接求值参考的 Aim 和
LayerBlending 现在使用与所有者相同遍历，仅对参考关闭 Post 缓存；
保持完整历史比较，不以忽略遍历字段规避差异。这仍是组合参考，不能
当独立 UE 姿势基准。

真实映射输入 1,260 帧、每帧重试、10 次失败通过，日志
`artifacts/overlay-unvisited-layered-input.log`。

原生脚部生产单线程/并行各 960 帧通过，日志
`artifacts/overlay-unvisited-production-single.log`、
`artifacts/overlay-unvisited-production-parallel.log`。共同摘要保持：
result=B289A6FB5130DBB7，fullPose=7B82A91E8A09C723，
sampledPose=6804D603D2523040，root=DB5B813964D3479C；224/258，
37 事件，lag/stale=0。没有据此声明十分钟性能或视觉验收通过。

晚期姿势/来源事件故障回滚通过，来源事件回调泄漏为零，日志
`artifacts/overlay-unvisited-late-transaction.log`、
`artifacts/overlay-unvisited-late-events.log`。本批修改文件空白检查通过。

## 后续顺序与未完成边界

下一项分离 AlsFootIkFrameRuntime 的全局属性更新和普通姿势控制访问。
即便最终根暂不访问普通图，全局 Foot IK 属性仍须按 MovementState
更新；最终曲线反馈应来自真正的根输出，不能只在普通 Foot Evaluate
后保存。随后接手部隐藏提交和最终根调度，保持候选状态统一提交/回滚。

之后用相同输入对照 UE 的起步速度/步幅/同步相位、A/D 换向条件与过渡、
上身附加层和空间语义、接触窗口支撑脚与平台变换，完成 P3/P4 人工验收
后切换完整默认入口。当前默认仍是 BaseLayer，本批没有宣布原始视觉
问题已解决。完整 Ragdoll/Get-up 玩法仍归 P6，不作为当前视觉修复前置。

原 P5A 通用事件/动作剩余项、P5B 全 Overlay/道具玩法、P5C Mantle/
Roll/Root Motion、P6 物理恢复/完整 Camera、P7 十分钟性能保持范围。
既有 Core 23 项失败、Import 分配不稳定、旧 p95=2.559ms 超过 2.5ms
没有在本批关闭；未运行全套测试。音频暂缓。
未 commit、revert 或 merge，保留已有工作区改动。
