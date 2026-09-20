# 第十六批：混合资产标记同步

日期：2026-09-10。继续完整性恢复计划 A/B，不改变完整 ALS 示例项目的目标。

## 相比上一批的进展

上一批只实现样本最终时间收尾，有标记测试仍借用 UE 解算后的样本时间。
本批增加真正的组与样本标记推进，回放只输入玩家参数、已解算的样本权重/顺序和
源资产元数据。后续每帧的播放器时间、样本时间、标记索引/距离和组位置完全来自
Core 上一帧输出，不用原生输出修正漂移。

## 实现

新增文件均属于既有 `AlsSyncRuntime`：

- `AlsAssetSyncContracts.cs`：纯数值的播放器、样本、序列、标记及候选历史合同。
- `AlsAssetMarkerRuntime.cs`：循环标记定位、位置编码、Leader 推进、Follower 映射。
- `AlsAssetSyncRuntime.cs`：混合 Sequence/BlendSpace 组求值，复用原排序及最终时间计算。

入口为 `TryEvaluateAssetSyncGroup`。玩家时间单位明确区分：Sequence 是秒，
BlendSpace 是归一化时间。内部样本保留独立秒时间与 DeltaTimeRecord。
调用方持有并提交组/播放器/样本历史；本入口不创建独立时钟或事件队列。

原生规则包括：

1. 原权重排序及同权重的不稳定顺序，不能改为按 AnimationId 稳定排序。
2. 组标记准备、资产/epoch/相关性变更，旧组位置与惯性化重同步上下文。
3. Leader 无有效结束标记时继续尝试后续候选；零速不能一律提前返回。
4. FAnimGroupInstance::Finalize 比较排序后的 Leader 索引，而非播放身份。
5. BlendSpace 按动画资产关联旧样本缓存，内部最高标记样本驱动其他样本。
6. Leader 与 Follower 的零速/零帧步长分支不同，且 PreviousTime 不等于上次输出 Time。
7. 序列与 BlendSpace 不同的重同步/DeltaTimeRecord 语义，最后统一逐样本收尾。

支持上限：128 个组播放器、512 个组样本、单 BlendSpace 128 个样本、
一次 Tick 256 次越过标记。超限返回失败，不丢标记、不写出半帧候选。
所有输出先暂存，数值/容量或晚期求值失败时保留调用方输出和旧组状态。

当前有标记范围限定为真实 ALS 本批资产使用的循环、非镜像、CanBeLeader、
非 evaluator、match-sync-phases=false，组内非空标记集合完全相同。
重复标记名可用，符号是编译后的数值身份。不同集合求交、非循环标记、镜像、
其他组角色和新版 phase-match 等尚未验证/实现，不能误称通用 UE Sync 全覆盖。
无标记 Sequence 同时支持循环和非循环，复用同一组入口。

## Detail 组件接线

`DetailMachineSmoke.SourceState` 改用混合入口。16 个真实 Detail 播放实例和三组
历史仍从源绑定建立。状态初始化改变 epoch；组/播放器/样本候选全部参与复制与
重试一致性检查。采样使用统一输出时间，资产倍率只在 Sequence 层应用一次。

这仍是组件验证所需的状态持有者，不是最终 P5 facade 或可玩 Demo 的事务所有者。
不会以新的组件调度代替尚未完成的正式接线。

## 原生对照

本批未修改 UE 插件或重写 fixture，使用已审计的原生记录：

- `v4_blendspace_tick_native.json`，SHA256
  `245677E2F3D9CD833780032222F2B6597C391BD84BFC0F09D6712DFABCD82E7C`。
  去掉未分组 Lean 的 192 帧，余下 1,152 帧覆盖标记混合组连续自主历史回放与重试。
- `v4_length_sync_native.json`，SHA256
  `C66141B04B51AC950BCE0F0BC8F47100B783F9516D8C7F6DC4CB7D7372F91FE3`。
  1,350 帧非循环长度组通过同一个入口，包含同权重、反向和惯性化重新加入。

有标记对照逐帧比较组 Leader、组前后比率、组标记名/位置、播放器时间/Tick delta、
每个样本的时间/PreviousTime/Tick delta，以及标记索引和到标记的距离。
权重和缓存顺序作为上游输入，不把这个测试称为完整 BlendSpace 滤波或完整 AnimBP 验收。

## 验证结果

- 新增两个测试类，共 13 项专项测试；Debug/Release 均 13/13。
- 最终 Core 回归 1,575/1,575，明确排除 `AlsP5aGoldenTests` 与
  `AlsP5aTraceSchemaTests`，不将它们记为本批通过。
- 两组原生数据合计 2,502 帧自主时间/历史回放通过。混合组还逐帧从相同旧状态重试，
  比较候选组、播放器与样本输出完全相同。
- 覆盖损坏的历史标记索引/数值、过量越过标记、晚期 follower 有效长度溢出、
  输出容量不足、非法帧步长，以及明确不支持的配置拒绝；失败不写调用方缓冲区。
- HotPathOwnHistoryIsAllocationFree：1,000 次持续推进 0B。最初测试辅助函数的
  断言闭包导致 120B/次分配，拆出失败断言后重新测量通过，不以该测试开销修改运行时。
- `dotnet build GodotALS.csproj --no-restore`：0 warning、0 error。
- Godot Detail：`DETAIL_MACHINE_OK rates=30,60,120 states=6 bone_checks=71400 retries=1050 requests=38 resets=6 correction=0.064214 alloc=0B sync=asset_runtime identities=source_graph demo=not_connected`。
- Standing Cycle 原有回归通过：`hip_transitions=9 wait_frames=231 rollback=9 interrupted_rollback=9 max_active=11 movement_direction=36 pose_bridge=63 alloc=0B active_alloc=0B curves=603`。
  这是旧 Cycle 路径回归，不是新 Sync 已接入 Demo 的证据。
- 镜头与输入文件哈希保持不变；本批没有改动 UE 资产、启动 GUI 或提交 Git。

## 后续工作

源图正式编译仍需纳入原生 length/phase 标志、标记表与新的数值绑定摘要。
再完成 P5 facade、全局源布局与原子提交，贯通 Cycle/Detail/ShouldMove/Stop/Pivot、
Notify 以及动态上身分层。镜头和输入未改变，没有新的 Demo 滑步改善结论。
