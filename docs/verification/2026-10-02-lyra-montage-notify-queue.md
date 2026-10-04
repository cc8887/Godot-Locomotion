# Lyra Montage 与 Source 通知汇合

后续已将原Melee/Reload通知实际投递到角色GameplayEvent接收器，独立原BP对象/真实ASC及Debug/Optimize生产验证见 [GameplayEvent消费者](2026-10-02-lyra-gameplay-notify.md)。本页保留本批队列汇合范围；完整NotifyState/ContextEffects/武器/MotionWarping、root碰撞与完整目标继续开放。

2026-10-02，直接在主目录继续 ALS 模型/骨架和 typed Linked Layers 的完整移植。真实 Montage 窗口桥、两条过滤随机流与合并队列已接入角色事务，最终 Debug/Optimize 原生精确对照与生产回归均通过。只关闭本批窗口/汇合/角色事务范围，完整迁移目标保持进行中。

## 真实窗口与队列顺序

`LyraMontageNotifyBinding` 从同角色物理 bank 的 `NotifyTraversal` 捕获真实 HandleEvents 完成区间、实例 ID、旧/新最大 NotifyWeight 和中断状态。原 Montage 通知直接提取，全部轨道的 Sequence 通知按实际 segment 起点/终点映射后提取。姿态仍采样 ALS 目标动画；轨道通知引用保留原 Sequence UObject/路径身份。

没有增加播放时钟。当前原 45 Montage 均为单 section、每轨单 segment、正 clip rate、Queued 通知；Native `GetValidPlayRate` 还包含 Sequence RateScale，本批逐轨确认其实际值与导出合同一致。此检查不能删掉后推广到未知资源。反向区间直接使用两个 float 端点，避免先相减成 delta 再还原终点造成舍入漏事件。

安装版 UE5.8 的 `FAnimMontageInstance::HandleEvents` 先把 Montage 本身的事件过滤到 AnimInstance 队列，再把轨道事件过滤到按 Slot 的桶；未访问的 Slot 仍参与过滤/概率抽样。`FAnimInstanceProxy::PostUpdate` 将已经过滤的源队列 Append 到上述直接队列，然后 ApplyMontageNotifies。最终顺序为直接 Montage → Source → 相关 Slot 桶；桶保持原首次插入顺序，Append 不再抽随机数。

`LyraNotifyQueueRuntime` 因此保留 Main 代理源 seed 和 AnimInstance Montage seed 两条独立随机流。当前 Linked 共同根 Sync 的源通知依然共用 Main seed，原 owner/node/player/sample/epoch 只是 provenance。Source 和 Montage 汇合后仅执行一次状态生命周期/命名事件描述生成。

Slot 通知相关性取当前或上一 tick 的实际 MontageLocalWeight 相关性；隐藏/未访问帧将当前位清零。原 Slot 的缓存 source weight 历史不等于通知相关性历史，不能据它让离图 Slot 永久相关。Main 的真实五节点遍历直接提供 mask，受控完整资产轨迹另使用实际 Slot owner 的显式访问。

## 共享 EndData 与取消边界

首轮原生精确比较在 Unarmed/30Hz/frame101 的 FingerGuns Emote MW 失败：短 MotionWarping state 已到结束点，仍持续的 EmoteSound state 也读到 `ReachedEnd=true`。原 `HandleEvents` 的 TickRecord 预先分配 MontageInstanceContext；`GatherTickRecordData` 共享该 context array，而 `AddContextData(EndData)` 直接写入同一数组。因此一次 HandleEvents 中任一提取事件达到结束点，会影响该次所有直接和轨道引用，包括先已入队引用及被过滤事件。

Godot 按当前受支持的每实例单个连续 HandleEvents 作用域保留这项共享行为。原提取仍按事件判断区间；只读的最终引用在所有轨道完成后统一呈现共享 EndData。原错误 Core 输出保留为 `notify-montage-core-output.json`，修复后精确输出另存 `notify-montage-core-output-v2.json`；请求和原生 JSON 没有为修复改写。

物理 bank 增加只读 Prepare serial，用来区分取消后同一 identity 的重试。它不是 Montage 播放 ID，不影响原 serial allocator、时钟或候选值。桥同时校验 serial、bank buffer/identity、完整 NotifyTraversal 及当前/上一 Slot mask，因此取消后的旧窗口不会因重试重建相同数值而恢复资格。

`LyraCharacterAnimation` 在真实 Main Prepare 后捕获 Source 和 Montage；模型、碰撞、Main、bank 和通知统一预校验。通知在 Main/bank 提交使 guard 失效前提交，迟到失败取消整个候选。Main、bank、两个 seed 及状态历史均跨换装备保留，每角色各自持有可变队列。已提交内容仍是 callback 描述，具体玩法副作用尚未执行。

## 验证入口与已通过范围

- `scenes/tests/lyra_montage_notify_smoke.tscn` 自主推进 bank/Main，先生成请求及 Core 结果，再读取原生输出；不会用 UE 输出驱动播放时钟、主图状态或 Slot 访问。
- `tools/unreal/LyraMontageNotifyOracle` 是独立只读 Editor 插件，真实调用原对象的 HandleEvents、Source GetAnimNotifies、原生过滤及 Proxy.PostUpdate，没有复制队列算法。
- `scripts/export-lyra-montage-notify-queue.ps1` 运行独立 UE 进程，验证受保护包/旧 JSON 和项目描述，0 资产保存。
- `scripts/verify-lyra-montage-notify-queue.ps1` 验证两构建的新队列、旧源队列、六角色三 Hz、普通十角色、旧 Slot native 和 LocomotionSM native；Optimize 结束恢复六个 Debug DLL/PDB。
- `tools/verify_lyra_montage_notify_queue.py` 汇总原生逐项对照、日志、两配置完整报告、资源及恢复哈希。

原生范围为 15 轨迹、33,235 帧：九条实际三 Provider×三 Hz Main 共7,560帧/27次换类，另六条全部45资产正向/反向完整物理播放。60轨道、12,430反向遍历帧、7,747隐藏Slot帧；32,695取消重试，补强后再逐一拒绝同identity恢复的旧窗口。过滤后6,573引用，其中6,299 Montage、274 Source，6,657 callback描述，335 Montage seed变化帧。

两独立 UE 最终进程退出0，结构内容相同。原生对照精确比较资产/事件/实例、时间float、Active/ReachedEnd、直接/槽/源/最终队列顺序、两条seed与mask。原生探针验证的是完成窗口的 HandleEvents/队列原语，不是用原 AnimInstance 连续 Advance/完整主图/原 Notify BP dispatch 驱动相同角色。物理 bank 既有 Core native 回归61项，本批窗口/过滤/生命周期Release63项通过。

最终两配置各通过上述33,235帧精确对照、32,695取消重试和65,390失效拒绝；新原生请求15,226,279字节/SHA256 `c72068ac761c15598a9261612c70cf15e6e54f0f273e825813e9e9fa0e682532`，原生结果9,803,304字节/SHA256 `0803ef21ab6eaab943b98e0c9d80f87d9a7a0a0d59ade88d3d92e5865a87dd78`。旧Source队列8,042帧/1,129引用/1,449描述及请求/原生字节保持，原窗口算法/概率/权重/LOD/误差门槛没有放宽。

两配置各六角色三Hz10,080角色帧：每Hz24换类/2重建，逐对完整姿态/曲线/属性/root和包含两个seed的已提交历史一致；真实双轨动作每Hz4个Montage callback，共12个。跨角色候选、迟到物理/模型失败、逐帧取消重试与退休资格通过，两配置三个完整报告逐值相同。首轮选用的Generic_Unequip双轨没有通知，覆盖门禁拒绝；改为按真实通知合同选择含通知的双轨动作（当前Pistol_Melee），未添加事件或放宽非零要求。失败矩阵另存 `notify-montage-debug-matrix-first.log`。

普通十角色两配置各4,800发布帧、461 Source callback描述；主角480重试、6换类/6同类复用和5迟到失败，全部68骨局部发布差0，整个十角色报告逐值相同。此普通夹具没有Montage动作输入，Montage描述为0；真正动作通知的生产验证来自上述六角色双轨场景，不能把普通报告的接入标志当动作覆盖。

旧Slot native各13,440帧/67,200槽及旧LocomotionSM native各11,340帧/9,762姿态/246边回归通过；这些是原范围回归，不提升为完整Main原生验收。Release窗口/过滤/生命周期63项与bank/native61项共124项通过、无失败/跳过。两引擎构建0警告0错误，最终两矩阵均无Godot ERROR/WARNING，Optimize替换的六个DLL/PDB已逐文件SHA256恢复Debug。最终验证脚本退出0，汇总为 `artifacts/lyra-analysis/lyra-montage-notify-verification.json`；818旧JSON/676 GASP包、原Source探针源文件及两个旧队列JSON保持，0资产保存。本批无新渲染、性能、全量或十分钟验收。

所有失败和各轮原生包保留。UE探针首轮PreUpdate断言来自临时World未初始化PersistentLevel，改为真实CreateWorld初始化后重建成功。一个仅打包源文件的UAT轮次没有生成DLL，未用于运行；最终使用 `package-fourth` 的实际编译产物。新夹具初次编译使用不存在的BlendOutTime字段、首轮请求对隐藏Main求值，分别改为实际BlendOutSeconds和真实Skeletal访问门禁后通过，失败日志保留。

## 尚未关闭

原类/BP NotifyState 连续 dispatch、typed ContextEffects/GameplayEvent/Transition/MotionWarping 消费者、武器mesh的 AN_PlayWeaponMontage、进程级原生 NotifyInstanceID 数字映射继续开放。音频暂缓要求保留；保留音频事件元数据和生命周期描述不代表音频消费者实现。

RootMotion生产碰撞消费、完整Main连续UE联合参考、武器挂点/握持、复杂地形/平台、渲染与性能仍属于完整目标。当前每窗口512提取容量超限整候选失败，不截断。当前 Montage context 边界只支持上述原45资产形状；未来 branching、多section、多segment/loop必须增加真实HandleEvents子区间身份与原生验证，不能按本批单区间假设扩展。
