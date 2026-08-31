# P5A 事件、同步与动作运行时设计

**状态：** 2026-08-30 已完成架构、事件语义、错误模型和验收边界确认；本文冻结实现前合同

**目标引擎：** Godot 4.7.2 .NET

**设计编写前代码基线：** `a69fda2325dc78b7c6045383055d0b38fcc88e80`

**参考实现：** `Sixze/ALS-Refactored` commit `b754d6f0f2bb03741d301f8fb88077ebfe561e17`

**上位合同：** `docs/superpowers/specs/2026-08-25-godot-als-port-design.md`

如果本文与上位合同冲突，以上位合同为准，除非后续提交的设计修订明确解释并解决冲突。

## 一、目标与完成边界

P5A 在已完成的真实 UE 资产管线以及 Gather -> Worker -> Commit 三阶段架构上，新增一套确定性的动画时间运行时：

- 通用 Curve 采样与多 clip 权重混合；
- Typed Notify 与 Notify State 时间线；
- Sync Marker、Leader/Follower 对齐和左右脚相位；
- 参考 ALS-Refactored 的 Dynamic Transition；
- 单 Action lane、稳定请求结果和中断原因；
- Worker 纯计算、Main Commit 验证后派发的事件通道；
- 真实 UE 资产、single/parallel、生命周期、golden 和短时性能门禁。

P5A 是 P5B Overlay gameplay 和 P5C Mantle/Roll/Root Motion 的共同基础。它必须能够独立验收，不能把后续 gameplay 行为偷偷塞进通用事件处理器。

## 二、当前基线与真实缺口

仓库已有以下可复用基础：

- `AlsAnimationSetDefinition` 已保存 curve、Notify 的 name/time/duration 和 Sync Marker 的 name/time；
- `AlsCurveSampler` 已能零分配采样 P4 curve，但绑定和接口是 P4 专用；
- `AlsEventBuffer` 已是 `AlsFrameResult` 内嵌的 16 槽值类型缓冲；
- P3/P4 已通过 generation 保护的双缓冲发布完整 `AlsFrameResult`；
- P4 Worker 已有 prepare/apply/commit/rollback 姿势事务；
- `AlsAnimationEventDigest` 已证明一个 P2B 小型事件顺序案例。

这些基础不能直接当作完整 P5A：

- `AlsAnimationEventDigest` 每次通过 LINQ 排序和分配，只是测试摘要，不是热路径 runtime；
- manifest v1 丢失 Notify class、typed payload、trigger policy、track 和 branching-point mode；
- manifest v1 没有导出 `AnimMontage` 继承的 Notify/Notify State 时间线；
- 当前 definition digest 没有覆盖 Notify、Marker 和完整 Montage 语义；
- 当前没有通用 Notify State 生命周期、Sync Group 或 ActionPlayer；
- 仅凭 `name + duration` 还原 UE Notify 类行为属于错误等价，明确禁止。

## 三、本阶段范围

### 3.1 一批实现

- Manifest v2 与 exporter v2 的 Sequence/Montage typed timeline；
- 对当前 ALS 267 项资产执行完整、两次可比对的 metadata 重导出；
- Curve、Event、Marker、Sync Group、Transition 和 Action 的严格导入与稳定整数绑定；
- 纯 C# Curve Runtime、Event Timeline、Sync Runtime、Dynamic Transition 和 ActionPlayer；
- 接入 P4 生产 Worker 事务和 Main Commit；
- 使用真实 ALS V4 Transition 与原地 Roll 的可操作 Demo；
- Schema、导出器、Import、Core、cross-engine golden、Godot headless、生命周期、single/parallel 和短时性能门禁。

### 3.2 明确不实现

- Overlay state、装备切换、道具生成和道具所有权：P5B；
- Footstep 音频、贴花、粒子和物理表面效果：P5B 的 P5A event consumer；
- Mantle/Roll gameplay 选择、motion warping、Root Motion 提取、碰撞消费和残差修正：P5C；
- Ragdoll、Get-up gameplay、Pose Recovery 和完整 Camera：P6；
- 30 秒热身与 10 分钟 Release 最终性能证书：P7；
- UE 任意并发 Montage instance 和任意 Slot 图。P5A 只提供一个固定 Action lane 和一个互不干扰的 Dynamic Transition lane。

P5A 可以预留后续需要的稳定 kind/tag，但不得执行对应 gameplay 副作用。

## 四、端到端架构

```text
UE 5.9 ALS Assets
  -> AlsGodotExporter v2
  -> als_manifest.json schema v2
  -> Als.Import 严格编译
  -> 只读整数 runtime bindings
  -> Main Gather 发布值类型请求
  -> Worker 求值 Curve/Event/Sync/Transition/Action
  -> Worker 恰好推进一次 AnimationTree
  -> Worker 事务提交 Pose + P5A State
  -> AlsFrameResult 携带值类型事件与 Action outcome
  -> Main Commit 校验 identity/generation/frame
  -> Main Commit 按固定顺序派发事件
```

Worker 不调用 gameplay，不发送 Godot signal，不访问外部 SceneTree，也不按字符串查找资产、curve、event、marker、section 或 AnimationTree 参数。

## 五、UE 导出与 Manifest v2

### 5.1 版本合同

canonical export 升级为：

```json
{
  "schemaVersion": 2,
  "exporterVersion": "2.0.0"
}
```

P5A importer 不接受 manifest v1。仓库 fixture 与 canonical generated batch 一次迁移。`reference/als-v4-export.lock.json` 保持自己的 lock schema，更新 manifest SHA-256，并继续记录唯一 UE 源工程和导出命令来源。

### 5.2 统一 Timeline Entry

`AnimationSequence` 和 `AnimMontage` 使用同一严格结构：

```text
stableEventId
kind
sourceClassPath
displayName
timeSeconds
durationSeconds
triggerWeightThreshold
tickMode
sourceIndex
trackIndex
payload
```

固定规则：

- `stableEventId` 为 UTF-8 文本 `<assetStableId>|timeline|<sourceIndex>|<sourceClassPath>` 的小写 40 位 SHA-1；
- `kind` 只能是 `Generic`、`Footstep`、`SetAction`、`SetGroundedEntry`、`EarlyBlendOut` 或 `RootMotionScale`；
- `sourceClassPath` 和 `displayName` 必须非空，仅用于审计，不进入逐帧 Core 数据；
- `timeSeconds`、`durationSeconds` 必须 finite，time 位于源时长内，duration 非负且结束点不得超过源时长；
- `triggerWeightThreshold` 必须 finite 且位于 `[0, 1]`；
- `tickMode` 只能是 `Queued` 或 `BranchingPoint`；
- `sourceIndex` 是 UE Notify 数组原始索引，在单一源资产内唯一；
- `trackIndex` 必须非负，用于审计与稳定排序；
- `payload` 由 `kind` 决定，拒绝未知字段、缺失字段和非 finite 数值。

Exporter 使用代码内显式 class registry 匹配已知 ALS C++/Blueprint class alias。未知 class 一律导出为 `Generic`，保留精确 `sourceClassPath`，绝不通过 display name 猜语义。

### 5.3 Typed Payload

每种 payload 只有以下合法形状：

| Kind | Payload |
| --- | --- |
| `Generic` | 空对象 `{}` |
| `Footstep` | `foot`：`Unspecified/Left/Right` |
| `SetAction` | `action`：`None/Rolling/Mantling/Ragdolling/GettingUp` |
| `SetGroundedEntry` | `mode`：`None/FromRoll` |
| `EarlyBlendOut` | `blendOutSeconds`、`checkInput`、`checkLocomotionMode`、`locomotionMode`、`checkRotationMode`、`rotationMode`、`checkStance`、`stance` |
| `RootMotionScale` | `translationScale`，finite 且 `>= 0` |

`EarlyBlendOut` 的 mode/stance 值只允许 P3/P4 已冻结的固定 enum。布尔开关为 false 时，对应比较值仍必须存在并通过 enum 校验，从而保证 JSON 形状稳定。

当前 ALS V4 Blueprint class 如果无法通过反射得到完整 typed payload，就保留为 `Generic`；实现不得为了让测试通过而从名称推断。P5A 的 synthetic typed fixture 与 ALS-Refactored golden 负责覆盖全部 typed kind，真实 ALS V4 asset path 负责覆盖实际 Generic/Footstep/Action 时间线。

### 5.4 Notify 与 Notify State

- `durationSeconds == 0` 是 instant Notify；
- `durationSeconds > 0` 是 state interval，但行为仍由 kind、payload 和 ownership 决定；
- Sequence 与 Montage 都必须导出 timeline；
- branching-point mode 保留用于审计与固定顺序，Godot Worker 不执行 UE callback。

### 5.5 Sync Marker

每个 marker 导出：

```text
stableMarkerId / name / timeSeconds / sourceIndex / trackIndex
```

`stableMarkerId` 为 `<assetStableId>|marker|<sourceIndex>|<name>` 的小写 SHA-1。Marker name 不是 Sync Group name；Group、成员与 Leader/Follower 资格只来自 P5A profile。

### 5.6 Montage

保留既有 section、slot、segment 和 blend metadata，并加入统一 timeline。Import 必须验证：

- section time finite、严格有序且位于 montage 时长内；
- `nextSection` 为空或解析到同一 montage；
- segment 引用已导出的 AnimationSequence；
- segment range、play rate 和 loop count 合法；
- montage timeline 位于 montage 时长内。

一个 P5A Action definition 只允许绑定一个声明 slot。需要并行 slot 的源 Montage 在 profile compile 时失败，不允许静默压平。

Action timeline 由两类 authored event 合成：

1. Montage 自身 timeline；
2. 当前 segment 的 Sequence timeline，经 segment start、animation start/end、play rate 和 loop count 映射到 montage time。

二者按统一 occurrence 排序。两个不同 authored source 即使 kind/payload 相同也仍是两个合法事件；“重复”指同一 occurrence identity 被运行时发布两次，不是删除作者明确放置的两条事件。

### 5.7 完整确定性重导出

Exporter 在锁定 UE 5.9 工程上构建并运行。完整批次导出到两个独立目录，逐文件比较后才替换 canonical ignored generated assets。高层 inventory 预期仍为 267 项资产、141 个正式文件；如 UE 源 inventory 确实变化，必须记录差异证据，不能只修改预期数字。音频继续排除。

## 六、P5A Runtime Profile

新增 tracked 严格配置 `assets/config/p5a_animation_runtime.json`，schema version 固定为 1，包含：

- typed event kind 到 runtime semantic ID 的固定映射；
- Sync Group、成员、每个成员的 loop policy 和 Leader/Follower 资格；
- Standing/Crouching x Left/Right 四个 Dynamic Transition 槽；
- transition distance、blend、play rate 和 cooldown；
- Action definition 的 priority、interruptible、montage、slot、start section、play rate 和 loop policy；
- 真实 Transition 与 Roll demo case。

Exporter class registry 是 typed kind 的唯一来源，profile 不重复按 class name 分类。

所有非空 stable asset ID 在 import 时通过 `AlsAnimationSet.AssetIndex` 解析。如果当前 ALS V4 有意让多个 transition 槽使用同一 clip，profile 必须显式重复同一 stable ID；compiler 不猜 alias。

编译结果只保存整数 ID 和固定宽度值，不在 runtime 保留 JSON 或名字。

## 七、冻结的 Core 合同

Core 热路径新增或扩展以下值类型：

```text
AlsCurveKey
AlsCurveBinding
AlsTimelineEventDefinition
AlsSyncMarkerDefinition
AlsSyncGroupBinding
AlsTimelineCursor
AlsNotifyStateOwnership
AlsCommittedNotifyStateBuffer
AlsDynamicTransitionState
AlsActionRequest
AlsActionPlayerState
AlsActionOutcome
AlsActionOutcomeBuffer
AlsP5FailureCode
AlsActionResultCode
```

初始化 adapter 把只读 Import definition 转成这些 bindings。求值 API 接受 `ReadOnlySpan<T>` 与 caller-owned state/buffer，不在求值期分配。

`AlsAnimationEvent.EventId` 定义为全局编译后的 timeline event ID，不再是 clip 内数组下标。事件还携带固定宽度 occurrence handle、source animation/action ID、playback epoch/cycle、owner token、EventSequence、boundary ordinal、kind、phase 和 compact payload。整个结构保持 sequential unmanaged，并进入 layout tests。P5 inactive ID、section、segment、binding 和 graph-tail index 一律为 `-1`，唯一例外是作为 Start-ID permanent tombstone 的 `AlsActionPlayerState.LastProcessedRequestId=0` high-watermark；浮点默认值必须是 raw positive zero。因此含 required `-1` 的 CLR `default` 不是合法 P5 runtime default，生产入口必须使用并验证显式工厂。

`AlsFrameResult` 保留 16 槽 inline `AlsEventBuffer`，新增 2 槽 inline `AlsActionOutcomeBuffer` 和 P5A failure code。2 槽上限来自下述三种有序路径而非截断：replacement interruption 后 acceptance；runtime-failure interruption 后一个 normal-request result；rejected normal command 后 existing-owner `Completed`/EBO terminal。`AlsResultDigest` 覆盖每一个 P5A 字段。

Occurrence layout version 固定为 `1`。每项为 `(SourceKind, SourceBindingIndex, GraphSlotIndex, OccurrenceHandleId, AuthorityGroupId)`；handle 必须等于 span ordinal，source key/handle 全局唯一，authority 为从 0 开始的 dense set，Transition/Action graph slot 固定为 0。唯一 public Core gate 是 `AlsP5OccurrenceLayoutContract.Validate(version,digest,entries)`，它不分配、不排序、不修改 caller span，并按 `Version int32 LE -> Count int32 LE -> 每项 SourceKind byte + 四个 int32 LE` 私下重算 FNV-1a 64。Import/Core 精确 field-copy bridge 由 Task 13 实现，Core snapshot/Godot adapter 比较由 Task 14 实现；本阶段不声明不存在的 cross-layer round-trip，也不允许 Core 引用 Import。

## 八、Curve Runtime

### 8.1 采样

- Constant 返回前一个 key 的值；
- Linear 在相邻 key 间线性插值；
- Cubic 使用 exported arrive/leave tangent 的 Hermite 插值，tangent 单位为 value/second；
- 非循环 source 在首尾 key 钳制；
- 循环 source 先分解成 `cycle:int64 + inCycleTime:float`；
- 空 curve 只有在 binding 明确标为 optional 时返回 0，required 空 curve 是导入错误。

Manifest 中的 pre/post infinity mode 保留在 definition digest 和审计数据中。P5A 播放求值先按 clip loop policy 归一化或钳制时间，因此不会再次用 infinity mode 改写 clip 外时间。

### 8.2 混合

所有 active clip weight 必须 finite 且非负。Curve 按归一化权重加权求和；总权重为 0 时返回 0，optional missing curve 贡献 0。

P4 canonical `RotationYawSpeedRadiansPerSecond` 的含义保持不变。root-track provenance 与 profile sign conversion 不重复导出、不重新推导、不重复应用。现有 P4 sampler 先建立 characterization tests，再委托给通用 Core 实现。

## 九、Event Timeline

### 9.1 时间与 occurrence identity

每个 playback instance 保存完整 compiled occurrence ownership：

```text
occurrenceHandleId / sourceAnimationId / sourceActionId
animationId / playbackEpoch / cycle / ownerToken
previousTime / currentTime / weight
eventAuthority / activeStateOwnership
```

推进区间固定为 `(previous, current]`。同一 epoch 内时间不得倒退；restart、replacement 或 section jump 创建新 epoch，并在新 timeline 求值前关闭旧 epoch 拥有的 state。

Instant、Begin 和 End 的唯一 dedup/ownership identity 为：

```text
occurrenceHandleId / sourceAnimationId / sourceActionId / eventId /
playbackEpoch / playbackCycle / ownerToken / boundaryOrdinal / phase
```

Tick 的 identity 在同一 tuple 后额外包含当前 `AlsFrameResult.Identity.FrameId`。这样同一帧 retry 不会重复 Tick，而下一帧仍会正常产生一个 Tick。同一 Sequence/event 在 Base 与 Action 或两个 Montage segment 中即使 local epoch/cycle 相同，compiled `OccurrenceHandleId` 仍使其合法地区分。`AnimationTime` 固定为从当前 frame simulation start 起算的 finite nonnegative occurrence offset，不是 clip-local authored time；authored local time只由 `EventId` 指向的 immutable definition 保存。唯一兼容例外是 synthetic P3 regression event：它保留历史 `state.AnimationPhase` 字节，同时所有 P5 identity 均为 invalid/default、不会激活 P5 digest extension。

### 9.2 Blend authority

每个 blend/sync group 每次求值只选一个事件权威：

1. effective weight 最大；
2. weight 相同则 animation ID 小；
3. 前两项相同则 playback epoch 小。

只有权威 clip 可以在该 group 发 instant event 或开始新 state，从而避免 crossfade 的 outgoing/incoming clip 重复触发。Action 和 Transition lane 拥有独立 authority，可以合法产生彼此独立的 authored event。

Authority 切换时，旧 authority 的 active state 先 End；如果新 authority 当前已经位于 state 内，则同帧发 Begin + Tick。低于 trigger threshold 的 instant occurrence 被消费，不会在后续权重上升时补发。

### 9.3 Notify State 生命周期

- 本帧进入并停留在 state 内：`Begin` 后 `Tick`；
- 后续仍在 state 内：每个求值帧一个 `Tick`；
- 到达 state end：一个 `End`，end boundary 不再 Tick；
- 单帧完整跨过 state：`Begin` 后 `End`；
- Worker 成功处理的 explicit cancel、replacement 或 section jump：每个 active owner 恰好一个 synthetic `End`；
- End 只能撤销同 playback epoch、event ID 和 owner token 写入的状态，不能清理新 Action/State。

Begin 要求当前 weight 达到 exported threshold。Begin 成功后，即使 weight 下降，End 仍必须保证。

运行时求值失败不发布 synthetic End：该帧完整回滚并保留上一个已提交 ownership。下一次成功帧通过 `CancelForRuntimeFailure` 关闭旧 state，并发布 `InterruptedByRuntimeFailure`。

Main Commit 为每个角色维护一个固定 16 槽 `AlsCommittedNotifyStateBuffer`。它只根据已验证并实际派发的 Begin/End 更新，因此是最后已提交 ownership 的 main-thread mirror。角色 deactivate 或 generation retirement 没有恢复 Worker 帧时，Main lifecycle teardown 从该 mirror 按固定顺序派发 synthetic End，并分别产生 `InterruptedByLifecycle` 或 `InterruptedByGeneration`；它不得读取 Worker 正在修改的 state。

### 9.4 固定顺序

Worker occurrence 排序 tuple 固定为：

1. absolute occurrence time；
2. old cycle/old owner 的 End 先于 new cycle/new owner；
3. UE `sourceIndex`；
4. phase 顺序 `End -> Trigger -> Begin -> Tick`；
5. animation ID；
6. playback epoch；
7. 完全相等时以 `OccurrenceHandleId` 为最终 complete-tie key。

排序后从 0 开始为当前 character frame 分配单调递增 `EventSequence`。Main Commit 继续遵守：

```text
FrameId -> CharacterId -> AnimationTime -> EventSequence
```

### 9.5 容量与事务

Timeline 写入前先计算当前帧需要发布的 event 数。超过 16 时返回 `EventBufferOverflow`，不写部分事件，也不推进 cursor/action state。Debug gate 立即失败；Release 保留最后有效视觉，只写一条有界 failure record。

## 十、Sync Runtime

Sync Group 配置与 Marker 数据分离。每次求值一个 group 恰好选一个 Leader，可以有 0 个或多个 Follower：

- Leader 按 play rate 推进；
- Leader 求 previous marker、next marker 和区间归一化 phase；
- Follower 在自己的同名 marker pair 中映射 Leader phase；
- missing pair、duplicate marker time、非法顺序或 undeclared group 返回 `InvalidSyncGroup`；
- loop wrap 保持 Left/Right 次序；
- Marker 不进入 `AlsEventBuffer`，不调用 gameplay。

输出包含 marker pair integer IDs、cycle、Leader ID、区间 phase 和固定宽度左右脚 phase。Core/golden 对 phase 使用 `1e-5` tolerance；ID、cycle 与 Leader 精确相等。

## 十一、Dynamic Transition

Dynamic Transition 复现锁定 ALS-Refactored 的判断顺序，并只读取现有 P4 foot-placement 值：

1. 每帧最多求值一次；
2. 先消费 cooldown；
3. `AllowTransitions >= 1 - 1e-5`；
4. foot lock relevant 且 target-to-lock distance squared 大于配置 threshold squared；
5. 两脚都满足时选距离更大的脚，完全相等选 Left；
6. 由 stance + foot 选择 profile 中显式 animation ID；
7. 以固定 blend/play rate 排入 Transition lane；
8. 只有有效选择后才把 cooldown 设为 2 帧。

target 与 lock position 使用 P4 已发布的同一 character/world-space 证据，Worker 不做世界查询。

Transition lane 与 Action lane 独立，不能中断、替换或改变 Action priority。它仍是 Worker-owned 动画状态，不是 gameplay callback。

两条 lane 的 logical gameplay owner 与 visual tail 严格分离，每条 lane 只有两个 Core-owned visual bank：outgoing `O` 与 incoming `I`。唯一离散 step 按此精确 float 运算顺序执行：`Step(value,target,seconds,b) = b <= 1e-5 ? target : clamp(value + sign(target-value) * seconds / b, 0, 1)`。Core 只计算一次 `LaneWeight` 与 `IncomingMix`，再依声明顺序计算 `OutgoingEffectiveWeight = O.Active ? LaneWeight * (1 - IncomingMix) : 0`、`IncomingEffectiveWeight = I.Active ? LaneWeight * IncomingMix : 0`。只有 I 时 mix=1，只有 O tail 时 mix=0，O+I 时 mix 位于 `[0,1]`，两者皆无时两个 scalar 均为 raw positive zero。

首次 accept/promotion 无 tail 时安装 I、mix=1并以完整 frame delta fade lane；有 tail 时保留唯一 O、安装 I、mix 从0开始。rapid replacement 丢弃更旧 tail，把被替换 logical source 的旧 instruction effective contribution `P` 冻结为唯一 O，并在安装新 I 前 rebase `LaneWeight=P, IncomingMix=0`，禁止从旧 `LaneWeight*IncomingMix` 跳回 full lane weight。terminal 在 frame offset `tau` 先推进到 tau、捕获 P、rebase 为 O，再仅用 `frameDelta-tau` fade；EBO 是例外，本帧 graph/timeline 继续使用 pre-rebase incoming instruction，next state 才 rebase 且 residual fade 为0。Visual tail 不进入 Timeline、authority、Notify ownership 或 outcome，也不成为 logical owner。

每个 Timeline playback 持有自己的 local contributing window；Base/Turn/Rotate 复制 descriptor offsets，Transition replacement old close 用 `[0,0]`、current 用 `[0,FrameEndOffsetSeconds]`，Action Montage/Sequence 复制各 traversal slice offsets。每个 event `Weight` 必须 bit-copy 同一 frozen graph instruction 中对应 source 的 effective weight：通常 outgoing/incoming 各取自己的 effective scalar，Action Montage/Sequence 共享 incoming scalar；terminal rebase 后 close 使用 outgoing scalar，EBO 本帧 close 使用 pre-rebase incoming scalar。Timeline、threshold/authority 与实际 graph pose 不得二次计算权重。

## 十二、ActionPlayer

### 12.1 固定 lane 与 section

P5A 只有一个 Action lane：

```text
Idle -> Accepted -> Playing -> Completed -> Idle
                    |       |
                    +-> Interrupted -> Idle
```

Action definition 绑定一个 montage、一个支持的 slot、start section、priority、interruptible、play rate、blend 和 loop policy。section 按验证后的 `nextSection` 表推进；非循环 Action 的 section graph 不允许 cycle，循环 Action 的 cycle 必须回到已声明 start section。

### 12.2 请求规则

`AlsActionRequest` 是 Gather 输入中的值类型，包含 request ID、command、action definition ID、start section integer ID、priority 和 slot generation。command 只能是 `None`、`Start`、`Cancel` 或内部使用的 `CancelForRuntimeFailure`；lifecycle/generation teardown 由 Main Commit mirror 处理，不伪造 Worker request。

- Idle 接受合法 Start；
- 当前 Action 为 `interruptible=false` 时，新 Start 返回 `RejectedBusy`；
- 可中断 Action 遇到较低 priority Start 时返回 `RejectedLowerPriority`；
- equal/higher priority Start 先以 `InterruptedByReplacement` 关闭旧 ownership，再递增 epoch 并接受新 Action；
- owning Cancel 只影响匹配的 action/request owner。对 non-owning Cancel，`RequestId < LastProcessedRequestId` 是 delayed replay/no-op；相等或更新的首次 stale Cancel 返回一次 `RejectedInvalidRequest` 并记录 command pair，完全相同 replay 随后 no-op；
- 相同 request ID 重放必须幂等；
- Commit callback 只能写 Gather N+1 请求，不能重入当前 Worker frame。

每帧先应用 external recovery latch 提供的 `CancelForRuntimeFailure`，再处理最多一个 normal request。2 槽 outcome 的三种顺序固定为：旧 Action `InterruptedByReplacement` -> 新 Action `Accepted`；`InterruptedByRuntimeFailure` -> 一个 normal-request result；normal command reject -> existing-owner `Completed`/EBO terminal。每条路径均必须在写 buffer 前预计算容量，绝不截断。

`EarlyBlendOut` state 在 Worker 内根据已导出的开关和 Gather 值类型状态判断；条件满足时 Action 返回 `InterruptedByEarlyBlendOut`，不直接调用 gameplay。

`SetAction`、`SetGroundedEntry`、`Footstep` 和 `RootMotionScale` 在 P5A 只形成 typed event/owned state。P5B/P5C 才消费道具、效果和 Root Motion 行为。

### 12.3 真实 P5A Action

Demo 与 integration golden 使用已导出的 ALS V4 Roll montage 及其真实 Sequence。P5A 原地推进 section、blend 和合成 timeline，不移动 `CharacterBody3D`，也不消费 Root Motion。位移与碰撞合同只在 P5C 引入。

## 十三、稳定 Reason Code

`AlsP5FailureCode : ushort`：

| Value | Name |
| ---: | --- |
| 0 | `None` |
| 1 | `InvalidDeltaTime` |
| 2 | `NonFiniteInput` |
| 3 | `InvalidBinding` |
| 4 | `InvalidTimeline` |
| 5 | `InvalidSyncGroup` |
| 6 | `EventBufferOverflow` |
| 7 | `StalePreparedFrame` |
| 8 | `NonFiniteOutput` |

`AlsActionResultCode : ushort`：

| Value | Name |
| ---: | --- |
| 0 | `None` |
| 1 | `Accepted` |
| 2 | `Completed` |
| 3 | `RejectedInvalidRequest` |
| 4 | `RejectedMissingDefinition` |
| 5 | `RejectedBusy` |
| 6 | `RejectedLowerPriority` |
| 7 | `InterruptedByReplacement` |
| 8 | `InterruptedByExplicitCancel` |
| 9 | `InterruptedByEarlyBlendOut` |
| 10 | `InterruptedByLifecycle` |
| 11 | `InterruptedByGeneration` |
| 12 | `InterruptedByRuntimeFailure` |

Import/Schema 错误继续使用带稳定 code 和 JSON path 的 `AlsCompilationException`。Runtime 不把 exception message 当作行为输出。

## 十四、Worker 事务与 Main Commit

### 14.1 Gather

Main Gather 发布 input、action request 和 immutable world evidence，不采样动画时间、不派发事件。

### 14.2 Worker

Worker 使用一个 whole-frame transaction。pre-foot `TryPrepare` 接收已经求值的 P3 locomotion/view/Turn/Rotate candidate（保留 prior committed P4 foot/P5 fields），先应用 runtime-recovery cancel、再 normal request，推进 logical owners/two-bank instructions，并一次性预检完整 Timeline。post-foot `TryFinalize` 接收同一 candidate 经本帧 P4 foot/pose transaction 后的 state，只覆盖 P5-owned fields，因此每个 P4 foot-lock/pelvis/pose 字节都必须保留。任一失败不发布 candidate state/event/outcome。

Worker 固定顺序：

1. 把 cursor、sync、transition 和 action state 复制到 candidate；
2. 解析 action/transition integer binding；
3. 采样 Curve 并求 Sync time；
4. 只更新变化的 AnimationTree 参数；
5. 恰好推进一次 AnimationTree；
6. 执行现有 P4 pose/foot transaction；
7. 把 Montage 与 Sequence timeline 映射并求值到 candidate event buffer；
8. 所有步骤成功后一次提交 pose + P5A state；
9. 发布一个完整 `AlsFrameResult`。

失败恢复完整 local pose、visual root 和全部 P5A state。同一 identity retry 产生相同 digest，不能重复 event。Runtime recovery latch 位于 committed Core state 之外；失败不能清除它，严格更新 identity 的成功 finalize/controller/state commit 后才消费。

### 14.3 Commit

成功发布的 `AlsFrameResult.P5FailureCode` 必须为 `None`。失败 attempt 只产生独立 `AlsP5FailureRecord`，不得构造 failure-only result。Task 17 在 controller finalization 和 exchange mutation 前执行首个 production publication preflight；Task 18 在 visual commit/event-outcome dispatch 前防御性复验。Main Commit 在 exchange candidate、FrameId、CharacterId、SlotGeneration 和 stage order 全部通过前不派发任何 event。stale、future、duplicate 或 generation mismatch result 派发 0 个事件。

Main-only event sink 接收 immutable value event。它只能在对应后续阶段产生音频、道具或 gameplay 副作用，不能同步修改刚提交的 Worker state。

Commit 同时更新 main-owned `AlsCommittedNotifyStateBuffer`。Deactivate/generation teardown 只消费该 mirror；synthetic End 继续使用最后已提交的 event ID、owner token、source ID 和稳定顺序，并在清空 mirror 后再回收槽位。

## 十五、测试与证据

### 15.1 Export 与 Import

- Exporter source contract tests 覆盖 Sequence/Montage timeline、class registry、payload、stable ID 和 sort key；
- Schema tests 覆盖每个 required/unknown property、enum、finite、range、duplicate ID 和 unresolved reference；
- Import tests 验证确定排序，并证明每个 Curve、Event、Marker、Montage section 和 Action 相关字段都会改变 definition digest；
- 完整 export 两次逐文件相等后才更新 canonical lock。

### 15.2 Core

- Constant/Linear/Cubic、边界钳制、loop 和 weighted blend；
- non-loop、loop wrap、多 cycle 和 large-delta timeline；
- blend authority 选择与 handoff；
- Notify State Begin/Tick/End、整段跨越和全部 interruption；
- Marker pair phase、Follower 映射、foot phase 与 loop order；
- Action accept/reject/replace/cancel/section completion/idempotent request；
- replacement 的双 outcome 顺序与 lifecycle/generation 的 Main mirror closure；
- 16 槽 overflow 的全有或全无事务；
- rollback/retry digest 相等；
- warm-up 后当前线程求值 `0 B` allocation。

### 15.3 Cross-engine Golden

新增 versioned P5A trace，记录真实 UE asset ID、sample window、curve value、Notify/State occurrence、marker pair/phase、transition selection 和 action state。Curve/phase 使用明确 tolerance；ID、cycle、event order、action outcome 精确比较。Godot reason code 由同一 case 的固定 expected mapping 比较，不伪称 UE 原生存在这些 Godot code。

### 15.4 Godot Headless

生产路径覆盖 1/10 角色的 single 与 parallel：

- state/event/sync/action digest 完全一致；
- parallel Worker 确实 off-main；
- event sink 始终 main-only；
- 每帧恰好一次 animation advance 和 result publication；
- duplicate/missing/reordered/stale event 均为 0；
- worker failure、deactivate、replacement 和 generation reuse 均覆盖；
- measured P5A path steady-state managed allocation 为 `0 B`。

### 15.5 Demo

P5A Demo 扩展已接受的 P4 键鼠/相机场景，保留全部移动与 camera 语义。脚误差满足规则时显示真实 Dynamic Transition，并提供明确输入预览真实原地 Roll Action。Demo 不伪造 Root Motion 位移。

### 15.6 统一验证入口

`scripts/verify-p5a.ps1` 固定执行 focused exporter/import/Core tests、cross-engine golden、Godot smokes、1/10 single/parallel 四格矩阵、demo smoke、repository Pester、P0-P4 regression 和 Debug/Release closure。只有全部通过后输出唯一一次：

```text
P5A_VERIFICATION_OK
```

P5A 短时性能必须保留既有阶段上限和 warm-up 后 `0 B`，不得降低前 10 个角色质量。10 分钟 Release 认证仍只属于 P7。

## 十六、完成定义

P5A 只有同时满足以下条件才算完成：

- Manifest v2 从真实 UE 工程导出严格 Sequence/Montage typed timeline，且完整批次可重复；
- 通用 Curve 采样/混合零分配并保持 P4 canonical curve 语义；
- Notify/Notify State 在 blend、loop、split window、cancel 和 lifecycle replacement 下顺序确定；
- Sync Group 显式配置，Marker 永不变成 gameplay callback；
- Dynamic Transition 判断顺序与锁定 ALS 参考一致；
- ActionPlayer 返回冻结的 accept/reject/complete/interruption code；
- Worker 不调用 gameplay，不修改外部 SceneTree；
- Main Commit 只派发通过当前 generation 验证的 result；
- 真实 Transition 与 Roll 进入 Godot 生产路径；
- 1/10 single/parallel digest 一致，event integrity error 为 0，warm allocation 为 0；
- `verify-p5a.ps1` 唯一输出一次 `P5A_VERIFICATION_OK`；
- P5B、P5C、P6 和 P7 的职责仍明确未实现。
