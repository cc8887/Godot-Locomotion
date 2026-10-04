# Lyra Montage Ended 前的活动 NotifyState 收尾

2026-10-03，接续 ALS 人物和 typed Animation Interface/Layer 路线。新增原 Ended 派发前的活动状态收尾，已接普通 `LyraCharacterAnimation` 的 Main bank/角色队列。原生双捕获、Core557、最终 Debug/实际 Optimize 三十二进程及独立审计均通过。完整移植目标保持 active。

## 原执行边界

本机 UE5.8 `AnimInstance.cpp:2512` 的 `TriggerMontageEndedEvent` 先处理活动 NotifyState，再调用被 envelope 捕获的实例委托和当刻 global。其原顺序为：

1. 从活动数组最后一项开始倒序遍历。
2. State 对象 Outer 必须是 Montage，引用中的物理 MontageInstanceID 必须与 Ended 对应。
3. 仍在数组中时调用 NotifyEnd；ShouldTrigger/editor 过滤不通过也要删除匹配状态。
4. 回调返回后检查当前索引有效，再按 RemoveAtSwap 删除状态及其引用。
5. 索引若已因回调清空/缩短库存而失效，直接返回，不发送该次实例或 global Ended。

序列对象直接拥有的 State 即使通过 Montage 播放，也不在这个分支结束。同一 Montage 的其它物理实例必须保留。回调中的追加、嵌套和重绑按原库存及原广播顺序处理；不能在循环前复制一个待结束列表再全部清除。

## Godot 实现

新增 Core `AlsMontageNotifyStateEndList<T>`，承载现有活动库存，不增加播放器或时钟。Replace 接收角色提交的不可变状态，End 按实际可变库存执行倒序/交换删除；Clear 支持回调导致的库存失效，外部只读快照随删除更新。

四容器 `AlsMontageEventQueue` 在 Ended 的实例/global 前调用收尾入口。运行时通过现有 `AlsMontageCallbackContext` 执行，使用已提交身份和 PostTick 阶段；被收尾返回拒绝的事件不执行实例、global 或 observer。绑定随候选队列复制，取消不提前执行状态回调。

`LyraNotifyQueueRuntime` 以原 Montage catalog 的资产身份分类直接 Montage State，结合已有 SourceKind/物理实例上下文筛选。常规 Prepare 仍使用原生命周期/allocator/RNG；Commit 将其状态交给活动库存。普通角色构造时连接同一 Main bank 与队列，提供 typed `MontageNotifyStateEnd` 回调。回调在骨架/角色发布后执行，收到原状态/引用和实例 ID；其它实例及序列状态保留供下一次常规派发使用。

角色生命检查失败会清空本库存并拒绝该次 Ended。这是当前 Godot 所有者边界，不据此声称 UE 的弱对象/Actor Destroy/UninitializeAnimation 全部已迁移。原 EmoteSound 音频按暂缓要求未播放；本批提供状态结束和 typed 投递，未实现任意 Blueprint 状态对象。

## 原生证据与限制

新可选 `LyraMontageNotifyEndOracle` 从上一同步探针复制。旧 WholeMain、Bank、Immediate、Delegate 和 Binding CPP 均保持字节；仅增加函数声明和两个新文件。使用合法的 SkeletalMeshComponent-owned `UAnimInstance`、独立 GamePreview World、原 Manny、原 Montage/Sequence 对象，不保存资产。

探针直接调用原 QueueMontageEndedEvent 和四容器 dispatcher。为隔离无关资源 Tick，使用显式模板实例化访问真实 private TriggerQueuedMontageEvents；没有编辑引擎或复制一套派发算法。受控原生 State 类记录 NotifyEnd 时的活动库存；另外三个用例实际调用原 EmoteSound/MotionWarping State 对象，其业务音频效果不纳入验收。

十一类场景：倒序交换删除、ShouldTrigger过滤、缺少上下文、清空、嵌套结束、追加、global重绑、队列延迟，以及原对象单实例/并发同资源/MW多状态。两独立 UE 进程退出0，requests/native JSON各自字节相同，31回调观察。原生中嵌套结束移走外层索引时，外层立即返回；追加尾部不会在本次倒序循环中再次访问。Godot 在原顺序和每次回调的完整活动库存上精确匹配。

Godot 另以原资源、真实物理 Montage bank 和角色队列适配器验证30/60/120Hz：输入显式保留的完成窗口，取消后重试，不提前 End；提交派发只结束实例1的一个直接状态，实例2及序列状态保留，End早于实例委托。三次retry/三次End通过。这是受控窗口适配检查，不是自然玩法或完整 UE Actor 运动轨迹。

Core新增八项覆盖交换删除/实例和来源隔离、过滤、清空、无组件保护、嵌套、追加、取消及实例/global/observer门禁；关联集合最终557通过，0失败0跳过，不是managed全量。两最终构建零警告零错误。

## 完整窗口参考的刷新

扩展回归发现旧 `montage_notify_queue_v2_requests.json` 与当前运行时生成的请求不同。当前程序集及上一轮已验收的 Debug 程序集都在同一 immutable 门禁失败；旧文件未被覆盖。该次上一程序集替换前后六文件保存/恢复和哈希记录为 `notify-termination-v1-baseline-restore.json`。

原请求的首批差异出现在Main Source混合权重及窗口数量。当前结果为6583引用/6667 callback描述，旧记录为6573/6657；Montage引用均6299，Source从274变为284。此前 Main/混合/历史等修订不能继续用旧请求字节作为当前执行结果。

保留旧资产请求/native与旧输出，新证据位于 `artifacts/lyra-analysis/notify-termination-windows-v1-*`。`LyraMontageNotifySmoke` 默认生成/比较该新证据集，旧包和源文件的 `LyraMontageNotifyOracle` 继续执行原 HandleEvents/Proxy.PostUpdate。新 capture 脚本只把输出写到 artifacts，原JSON字节级依赖不变。

十五轨迹/33235帧、九Main轨迹7560帧/27换类、全部45 Montage正反向/60轨道、32695取消重试/65390旧窗口拒绝均保留。新的Godot窗口输出和原生队列精确匹配，CurrentTime按原float字节检查，其它字段精确比较，没有放宽数值或概率门槛。该探针验证完成窗口的HandleEvents/队列原语；物理Advance和完整Main分别依赖已有原生门禁，不提升为完整Notify BP dispatch。

## 最终生产矩阵与独立审计

冻结源为 `notify-termination-v1-frozen-sources.json`，共140份。最终 Debug/实际 Optimize 各十五个场景进程和一个完整 Main/Rig 进程，合计三十二进程均退出0、成功标记齐全、无 Godot ERROR/WARNING。

| 每构建检查 | 最终范围 |
| --- | --- |
| 新状态收尾 | 十一原生场景/31回调；三频实际bank适配器/3retry/3状态End |
| 刷新完整窗口 | 15轨迹/33235帧，45资产/60轨道，32695retry/65390旧窗口拒绝；精确native=True |
| 旧Source派发 | 原状态历史/命名通知参考及取消重试 |
| 同步/发布后回调 | 27轨迹/5670帧/405回调；12轨迹/5880帧/171回调，均逐帧retry |
| 原四容器与Emote | 21轨迹/8823行/8820retry；54轨迹/27720帧及同数retry |
| 三频六角色移动 | 共10080移动及各10080次移动前/动画retry |
| 原Warp和root物理 | 各60Hz/六角色/2880移动及两类retry |
| 普通十角色和E | 每角色十四实例，共5280角色发布帧，换类/复用/武器/事件及retry |
| 原ALS入口 | 60Hz/1700帧 |
| 完整Main最终Rig | 三Provider/十四实例布局/1080帧及同数retry，当前34/47 Linked字段 |

三频移动和两份普通场景完整报告跨构建相同，也与上一同步回调阶段对应报告相同。两次新窗口原生输出字节相同；其原float字节及其它字段精确比较通过。完整Main使用受控物理输入，不能提升为Chaos/Jolt世界运动等价。

`tools/verify_lyra_montage_notify_termination.py` 核查终态日志、窗口/状态双捕获、旧包与源文件、557项TRX、140源哈希、实际程序集、两轮六文件Optimize恢复、前述上一程序集诊断的六文件恢复证据，以及869份JSON/710个原UE包/9项项目和Config文件。最终 `notify-termination-v1-integrity.json` 为 `auditPassed=true`、`montageEndedStatePrelude=true`；`fullNotifyDispatch`、`worldTeardownAccepted`、`goalComplete` 仍为false。

复跑入口：

```powershell
& scripts/capture-lyra-montage-notify-end.ps1 -RunTag <new-tag> -PackageName package-notify-termination-v4
& scripts/verify-lyra-montage-notify-end.ps1 -Configuration Debug
& scripts/verify-lyra-montage-notify-end.ps1 -Configuration Optimize
```

已使用的矩阵标签不得覆盖；如需复跑，先复制验证脚本并使用新的证据标签。Optimize不能与Debug并行，共用发布目录的六份程序集在finally恢复。窗口重采样用 `scripts/capture-lyra-notify-termination-windows.ps1` 的新LogName/Capture标签，保留旧输出。

## 失败记录和整体范围

首 Core 编译失败为params数组单元素的目标类型构造，两处改显式State后八项过。原生首build失败为private调用、context宏之后访问权限与SharedRef序列化；改真实private入口访问/明确public/正确SharedRef后构建成功。第一capture因CreateWorld后重复初始化触发WorldSettings断言，第二因非法Package-owned AnimInstance触发Outer断言；失败包/日志/请求保留。修复使用已有CreateWorld初始化方式与合法组件Outer。无组件检查仅有Core和源码依据，不列入原生验收。

本批关闭指定Ended前收尾的活动库存顺序、typed接入和当前窗口参考。任意Blueprint/GAS状态对象、正常Begin/Tick回调中的库存/世界修改、Actor销毁与弱对象安全、完整autonomous/dispatch政策、重新Advance与Section/loop保持开放。Linked仍核验34/47；非空左手源、Main共享Montage模式变化、通用self/default/Unlink/部分与同函数多调用点、其它Provider继续推进。

ALS68 skin/69 raw/81 logical、角色共同Source Sync和唯一骨架发布保持。完整Chaos/Jolt同输入既有314/1680帧差异、近景握持/脊柱变形、地形/平台、独立导出、跨平台和性能仍待；音频、道具物理、头颈暂缓。本批无GPU/近景、十分钟性能或人工全矩阵验收，整个目标尚未完成。
