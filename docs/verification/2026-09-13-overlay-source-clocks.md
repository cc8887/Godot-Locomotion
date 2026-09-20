# Overlay 独立来源时钟与同步角色

第 118 批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`。

## 本批完成的完整性缺口

第 117 批已经验证完整姿势图，但将 UE 轨迹的来源时间直接提供给采样器。
本批新增每个角色独立拥有的来源时钟，由真实 Overlay 图的初始化和更新
回调驱动，再通过现有混合资产同步内核推进。新的回放模式不读取 UE
播放器时间作为输入；原轨迹只作为期望值。

- 148 个节点各自保留初始化代数和时间，资源共享不合并播放身份。
- 122 个 teleport evaluator 在更新时钳制显式时间，不推进同步播放或通知窗口。
- 26 个 SequencePlayer 保留原始零/非零倍率、权重、inactive 上下文和惯性化同步请求。
- 三组 `IdleAdditive`、`Locomotion`、`SecondaryMotion` 在 Overlay 子图内统一处理。
- 隐藏帧仍执行空同步批次，退休旧的组历史；候选时钟和初始化可以取消、同帧重试。
- 同步内核新增 `AlwaysFollower`，默认 `CanBeLeader` 的旧调用行为不变。

## UE 行为与数据依据

本机引擎 `../UnrealEngine` 的 `FAnimGroupInstance::TestTickRecordForLeadership`
使用 `weight - 2` 作为 AlwaysFollower 分数，不是零分，也不是禁止该节点
在任何情况下成为 leader。跟随者排在所有可作为 leader 的节点后面；
全组只有跟随者时仍按权重选举。排序、惯性化重入的比较和历史 LeaderScore
都使用该分数。当前接口只接受实际需要的两种角色，其余角色显式拒绝。

`FAnimNode_SequencePlayerBase` 的初始化/更新和
`FAnimNode_SequenceEvaluatorBase` 的显式时间钳制作为来源时间依据。
特别注意，UE evaluator 的 `GetCurrentAssetTime()` 返回显式输入引脚，
而非内部 accumulator，未初始化节点也可能返回非零默认引脚。因此原生
`sourceTimes` 用于比较 26 个真正播放器；evaluator 通过最终姿势及单独
钳制/不进入同步队列测试验证，不能把其引脚值错误地当成已推进的时钟。

新增 `tools/unreal/export_overlay_sync_inputs.py` 只读取真实资产。
正式数据 `assets/config/v4_overlay_sync_inputs.json` 包含 29 个资产的
RateScale、长度和 6 个 marker 的原顺序、名称、时间，并绑定原 Overlay
输入 SHA。编译时与已有动画清单核对长度和 marker，沿用全动画集的数字
marker 符号表，不为 Overlay 另造一套名称身份。

新增数据的冷启动与普通 Editor 重复导出 SHA256 均为：
`E80830C39B631E2879718FC25EDB9779F8DA78C5C954B11DC281E84E2828E04C`。
没有保存或修改 UE 资产，也没有修改原生姿势夹具或扩大比较容差。

## 验证

`overlay_pose_smoke.tscn -- --overlay-own-clocks` 使用自行推进的时间：

- 冷启动既有夹具：886 个完整姿势、52 个隐藏帧、148/148 来源覆盖通过。
- 普通 Editor 既有独立夹具：同样的 886 个完整姿势通过。
- 26 个播放器在每个可见帧的时间比较误差为 0。
- 886 次取消后重新初始化/更新/同步/求值的输出完全一致。
- 最终位置最大误差 `1.0071215e-6 m`，四元数距离 `7.90745e-7`，曲线 `2.3841858e-7`。
- 4 个状态转换通知和 366 个惯性化请求的诊断计数保持；不是通用通知分发已完成的证明。

8 个同步角色专项通过，覆盖重权重零倍率跟随者、marker/长度同步、全跟随者
选举、负分历史、惯性化重入、非法角色的批次原子拒绝和热调用零分配。
这些混合角色案例依据本机 UE 源码构造；本批没有新导出混合角色原生 tick 轨迹。
9 个新 Import 专项通过，覆盖数据绑定、marker、取消/重试、跨角色身份拒绝、
teleport、零权重 inactive 更新，以及四所有者一致与 10,000 次热事务零分配。
同步/移动 Core 相关回归 570 项通过；Godot 优化 Debug 构建零警告、零错误。

全量 Release Import 最终为 1,931 通过、1 个既有跳过。首次全量运行的
热调用检查测到 7,944 字节：该测试只在 Parallel.For 中预热，没有保证
计量线程本身预热。补入与现有姿势测试相同的计量线程显式 500 帧预热，
仍测完整 10,000 次事务且要求严格 0 字节，最终全量通过；原失败日志保留。

正式 Worker 单/并行各 600 帧通过，摘要保持：业务 `EAAF62E4D0A80A76`，
完整姿势 `EE519FBE375F4A2B`，根 `A4F6C26CBAB8A0E7`；各 28 个事件，
lag/stale 为 0。生产路径仍是 complete_base_layer，这项回归不能代替
新最终上身的接入验收。

UE 全项目 Editor 构建和插件审计通过，BuildId
`a4192a27-77ab-4bb5-9996-c71b2d55f53d`，状态 fingerprint
`28DA3AAF4178852D44C6BF2F6EC0D9D739D7080CF0DA62B7C2E7C4B3A5EE30CD`。
冷启动最终导出退出 0；普通 Editor 受控重启/导出退出 0。
没有修改原生插件、加载配置或描述符，本批未重新执行插件打包/项目资产验证。

首次导出尝试发现 marker 的 track_index 未暴露给 Python；失败日志保留。
当前新增导出只记录本次运行所需的 index/name/time，不伪造 track 值。
首次回放也保留 evaluator 引脚误当 accumulator 的检查失败；修正检查
边界后，未改期望时间或姿势。两个旧姿势夹具沿用第 117 批文件。

日志：`artifacts/overlay-clock-*`，其中 `native-pose-second.log`、
`editor-pose.log` 为自行推进时间的完整输出，`core-regression.log` 和
`tests-second.log` 为上述 Core/Import 验证。`editor-verified.log` 为
实际等待退出码的普通 Editor 导出，`native-console-final.log` 为冷导出。
`import-regression-final.log` 为最终全量 Import，`worker-single.log`、
`worker-parallel.log` 为正式移动回归。

## 明确未完成与下一项

当前组 ID 属于 Overlay 局部命名空间。该独立 owner 尚未加入正式移动图
的来源绑定、采样批次和通知事务。必须按组名映射为同一组，保留节点的
独立身份，并在两图收集后只 tick 一次。尤其需要用真实移动 leader 驱动
三个零倍率步枪手臂 follower；本次 Overlay 隔离轨迹没有外部移动 leader，
不能以该回放通过宣称跨图同步已经完成。

正式定义加载已编译时钟数据，但默认 Demo 没有使用新的最终 Overlay 层。
下一项仍是跨图来源/通知的整帧提交，然后将 Aim、Overlay、BasePoses、
LayerBlending、脊柱/手部修正接到最终输出，再闭合最终曲线、脚锁/pelvis/
平台约束，按同输入/同脚相位进行横移、换髋、起步滑步的多帧与人工验收。

既有全 Core 23 项失败、十角色性能 p95 2.559 ms 超过 2.5 ms 门槛，
以及 P5A–P7 其余功能仍保留；本批相关通过不代表整仓或最终性能通过。
全部 Overlay/道具玩法、Mantle/Roll/RootMotion、Ragdoll/Get-up/PoseRecovery、
完整 Camera 和十分钟性能验收均未取消，音频按用户要求暂缓。
