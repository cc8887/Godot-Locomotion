# Lyra 普通 NotifyState 实时派发

2026-10-03。沿用 ALS 68 蒙皮骨、69 原始通道、81 逻辑通道和现有 typed Interface/Linked Layer 路线。完整 Lyra 移植目标仍开放。

## 实现与原引擎依据

本机 UE 5.8 `Engine/Source/Runtime/Engine/Private/Animation/AnimInstance.cpp` 的 `TriggerAnimNotifies`（1820）、`EndNotifyStates`（2130）和此前 `TriggerMontageEndedEvent` 是时序依据。本批可选探针直接调用原函数，没有修改引擎或保存原资产。

此前普通角色在 Commit 时直接替换最终活动状态数组，再逐项回放预计算回调。这个顺序无法表达 Begin 当刻可见的旧数组，也无法让 Tick 回调的追加、清空影响后续遍历。本批改为：

1. Prepare 保留无副作用的窗口采集、过滤和生命周期预测，供消费者预校验；Cancel 不执行回调、不更改活动库存或分配器。
2. 角色提交 Main、Montage、物理凭据和骨架后，派发器遍历已提交队列。instant/named 通知在队列遍历当刻执行，named 不分配 Notify 实例编号。
3. 状态匹配复用旧编号并立即交换删除；未匹配旧状态按实时数组正序 End。普通 End 不删除该旧条目，Begin 仍可看到未匹配旧数组。
4. Begin 全部处理后切换新数组；Tick 按实时数组长度正序执行，追加和清空影响本轮剩余遍历。
5. 普通派发和 Montage Ended 前收尾共用一个活动库存。回调生成的实际实例编号进入下一帧预测，不增加通知时钟、过滤随机流或另一套状态登记。

`AlsAssetNotifyLiveDispatcher<T>` 提供上述内核。`LyraNotifyQueueRuntime.Commit(..., deferDispatch:true)` 把实际生命周期延迟到角色发布边界，`LyraCharacterAnimation` 在真实遍历处调用 typed 消费者与状态回调。消费者按来源 occurrence 校验原计划，接受本次实际编号，避免回调改变匹配结果后回放旧编号。Core 分配器的当前值也在回调内可读。

普通 End 回调清空当前索引时，Core 返回未完成，不继续 Begin/Tick；此项为源码与 Core 门禁，未作本批 UE 世界销毁验收。Begin 清空库存而角色仍存活时，随后新数组替换仍然发生，符合原函数顺序。

## 原生对照

新探针在真实角色的 SkeletalMeshComponent 下创建合法 UAnimInstance，通过 transient Notify/NotifyState 记录每次回调的实例、来源和当刻活动库存。两独立 UE 命令行进程捕获的请求与结果 JSON 字节相同，13 类场景、47 次回调：

- 普通匹配、交换删除、End/Begin/Tick 数组可见性。
- Begin 清空、Tick 清空、End 追加、Tick 追加、instant 清空。
- Begin/Tick 嵌套调用原 TriggerAnimNotifies。
- 状态回调过滤、Default 跳过来源、强制 Montage 模式。
- NoMerge 并发来源匹配、EndNotifyStates 忽略普通状态过滤。

Godot 测试逐字段对照全部回调库存、顺序、最终数组和分配器；本批编号均在精确整数范围内，原 float 字段按 float 精度读取，未放宽原动画精度门槛。原生全局编号通过前后 Notify witness 观察；生产编号仍属角色内生命周期，不由本批推导跨角色全局编号等价。

普通物理角色使用原 Emote 资产，30/60/120 Hz 各两角色、每角色三秒。一个角色在 Begin 清空库存，另一个在首次 Tick 清空库存；前者按原数组切换继续 Tick，后者下一帧产生新编号并重新 Begin。每帧取消、重试均不派发状态回调，且读取到的皮肤发布已经完成。

## 回归中修复的换装边界

扩大消费者验证后，旧武器夹具暴露了已有冲突：ReloadDone 玩法通知要求在已提交回调内 Rebind，角色的 `_dispatching` 保护却拒绝了它。该保护在本批前的源码中已经存在，失败日志保留为 `notify-live-v1-debug-weapon-consumer.log`。

本批允许已提交回调在没有 Main/物理候选时替换装备；Prepare 仍拒绝重入。命名消费者保留当前 Main 队列命令，后续派发重新查找当前 Linked 接收者。专项复跑确认 signal 内销毁、QueueFree、换装和三次重入 Prepare 拒绝通过，替换武器在同帧最终人物挂点后 Tick。没有改动原武器测试、其阈值或原资源。

## 验证记录

相关 Release Core 测试 607 通过、0 失败、0 跳过，包含新的十项实时派发测试及原 169 组生命周期参考。Debug 和实际 Optimize 构建均为零错误、零警告。

最终 Debug/实际 Optimize 各 23 个 Godot 运行，共 46 个进程均退出 0、具备成功标记且无 Godot ERROR/WARNING。包括新状态测试三频各两角色共 1,260 提交帧与同数重试、四类消费者、原状态收尾、15 轨迹/33,235 帧原通知窗口、Montage 三批原生对照、原 Emote、三频六角色物理、Warp/root、普通十角色/E、原 ALS Demo，以及逐调用点十四实例的完整 Main 最终 Rig 对照（每构建 1,080 帧、已有 34/47 私有字段范围）。

两构建的完整物理/普通场景报告相同，并与前批已接受报告相同。独立审计确认两次 UE 捕获字节相同、178 份当前冻结源码与封装探针一致、869 份旧 JSON/710 原包/9 项目配置保持，以及两轮六个 Debug DLL/PDB 恢复和当前 ExportRelease 程序集哈希。

最终审计为 `artifacts/lyra-analysis/notify-live-v2-integrity.json`，`auditPassed=true`、`goalComplete=false`。可复核的运行时代码差异为 `notify-live-v2-runtime-changes.diff`；完整来源清单为 `notify-live-v2-frozen-sources-final.json`。首轮编译/武器失败与 UTF-16 日志解析失败均保留；审计器按 BOM 解码，没有重写日志或改变验证阈值。最终运行证据使用 `notify-live-v2`，两次原生捕获使用 `notify-live-v1`。

复核命令（Windows PowerShell 7）：

```powershell
./scripts/build-lyra-notify-live-oracle.ps1 -EngineRoot '../UE_5.8' -UnrealProject ../GASP58/GASP58.uproject -PackageName <new-package>
./scripts/capture-lyra-notify-live.ps1 -PackageName <new-package> -RunTag <new-capture>
./scripts/verify-lyra-notify-live.ps1 -Configuration Debug -EvidenceTag <new-evidence>
./scripts/verify-lyra-notify-live.ps1 -Configuration Optimize -EvidenceTag <new-evidence>
```

现有成功记录不可覆盖；新运行应使用新名称。当前烟测读取固定已审计原生参考；新捕获如需成为参考，应单独审查和版本化。最终审计器复核固定本批证据；本批审计已写入，重复执行会主动拒绝覆盖。

## 剩余完整目标

本批接通实时生命周期和上述 controlled callback 语义，没有执行任意原 Blueprint NotifyState 的完整行为。原 Emote 音频继续暂缓；任意世界/对象销毁、Uninitialize/弱生命周期、同步队列变更与完整 autonomous 策略仍需原生专项。当前角色入口不支持递归 Prepare；Core 嵌套派发对照不能扩大为任意角色递归更新验收。

通用 default/self/Unlink/部分覆盖和同函数多调用点整图、其余 Linked 字段与 Provider、非空左手源、完整物理既有 314/1680 帧差异，以及 ALS 近景握持/脊柱、复杂地形、平台、独立导出和性能仍未关闭。音频、道具物理和头颈专项保持既有暂缓安排。本批没有 GPU/视觉、全量 managed、十分钟或性能验收。
