# Lyra 原资产 Notify 合同与 Linked Layer 边界

2026-10-02。继续按当前主目录路线推进，复用 ALS 人物和骨架，忽略 UE 5.8/5.9 差异。本批完成源/目标资产通知合同的只读导出和独立复核，尚未接入完整 Main 的运行时通知队列或消费者。

## 实际导出与保护

新增 `assets/generated/lyra_als/notify_contract_v1.json`，8,014,203 字节，SHA256 `952f5d0e6663f9093aa583736982ac8d3e3e445589dd95cd99da0942108c2b2d`。资源由原 logical234、Main Lean3、locomotion extras8、Montage55 与 AimOffset 策略合并；Aim 策略重复已有45绑定，保留目录/slot证据并按真实目标路径去重。

| 内容 | 数量 |
| --- | ---: |
| 原 AnimSequence / Montage | 344（299 Sequence、45 Montage） |
| 原事件 | 1515 |
| ALS 目标 AnimSequence | 300 |
| 目标事件 | 1460 |
| 目录与 slot 绑定 | 345 |
| 原/目标实际通知对象 | 2969 |
| 通知对象类 / Blueprint 类 | 10 / 7 |
| 原 NotifyState / 无对象命名事件 | 42 / 6 |

完整记录原对象路径、对象类及 CDO T3D、事件原 T3D、实际原生时间和 end-offset、过滤策略、tick mode、state behavior flags 与逐对象载荷。MotionWarping 保留嵌套 RootMotionModifier 的真实对象和 T3D；Blueprint 导出当前 member variables 与函数图。T3D/BlueprintLisp 用作原始结构证据，不当作已经可执行的 Godot 行为。

脚步 ContextEffects 保留 GameplayTag、socket、附着、位置/旋转、VFX scale、音频参数、扫掠通道/偏移及自体排除；本机实际值含 `AnimEffect.Footstep.Walk/Land`，不能把这类通知退化成一个“脚步计数”。音频播放仍按原要求暂缓，参数保留。

源与每个 ALS 目标动画的时间、类、过滤、flags、tick mode 及载荷逐项精确相同；旧189 clip / 1450事件与旧Montage/55段序列元数据逐值相同。只在新的 JSON 中补合同，未重写既有导出。

两独立 UE 5.8.1 commandlet 进程实际退出0，第二次重新读取全部资产并逐值比较第一份不可变合同。成功日志为 `notify-contract-ue-duration.log`、`notify-contract-ue-repeat.log`；外部插件复用 `artifacts/unreal/gasp58-lyra-rig-target/package/AlsV4AssetExporter.uplugin`，本批没有重编插件。原818 JSON、原669包加7个通知Blueprint共676包及项目描述 SHA256 保持，保存资产数0。UE原工具/tag等警告保留，不称零 warning。

验证入口 `tools/verify_lyra_notify_contract.py` 实际退出0，报告 `artifacts/lyra-analysis/lyra-notify-contract-verification.json`，日志 `notify-contract-verification-first.log`。本批只改 Python/PowerShell/文档，没有 C# 动画修改或新的 Godot运行、完整通知native轨迹、视觉/性能验收。

## 通知种类与消费者

| 原类/事件 | 原事件数 | 接入边界 |
| --- | ---: | --- |
| LyraContextEffects | 686 | typed效果请求、真实socket/trace及材质context；音频暂缓 |
| FootPlant Left / Right | 360 / 378 | 保留实际原函数语义；不得仅凭类名新增脚锁动作 |
| TransitionToLocomotion | 36 | 原instance的活跃NotifyState反馈 |
| PlaySound | 25 | 保留真实sound/attachment/volume/pitch；播放暂缓 |
| AN_PlayWeaponMontage | 9 | 原武器对象/武器Mesh行为，不能直接误播到角色五Slot bank |
| AN_Reload / AN_Melee | 6 / 3 | actor gameplay event：ReloadDone / MeleeHit |
| MotionWarping State | 4 | 原modifier和root运动消费，保持后续物理边界 |
| EmoteSound State | 2 | 原Begin/End生命周期及对象载荷，音频暂缓 |
| SaveAttack / ResetCombo | 3 / 3 | 原AnimInstance的命名事件派发 |

原类 flags 均0，本闭包没有 branching point。不能由此删掉通用 Core 已支持的相应语义。Blueprint图导出成功和无转换警告不等于完整对象执行/玩法验收；例如 AN_PlayWeaponMontage 仍须对照真实武器对象合同。

固定source inventory有一个实际 Main Lean BlendSpace，原 notify mode 为 HighestWeightedAnimation（1）；三个 Main source 节点共享该资源。动态 AimOffset 45个样本已全量读取，均无通知。本批没有为动态 AimOffset编造node/owner/过滤context，其节点映射及 mode 仍须在真正的 source bridge 接入时核对。

## 必须保持的原生语义

直接读取安装版 UE 的 `AnimInstance.cpp`、`AnimNotifyQueue.cpp`、`AnimTypes.cpp`、`AnimationBlueprintLibrary.cpp` 与 GASP58 `AnimNotify_LyraContextEffects.cpp`。

- 原14 Linked节点和 Main/Provider 的 Receive/Propagate flags 为false。`TriggerSingleAnimNotify` 对类通知调用对象的 Notify；命名事件才使用 Linked接收/传播逻辑。不能据false过滤Sequence的类通知。
- Queue提取使用实际共同Sync后的tick区间、选主、遍历Order、图weight及最高样本，不另起通知播放时钟。Montage段与直接事件保留真实Slot relevance和实例身份。
- 角色统一事务调度通知，但保留 Main/Linked来源归属和原instance的状态/RNG边界。仅按类名或event index去重会合并不同对象；Source资产与目标克隆对象路径也不能混同。
- State匹配用真实对象身份；NoMergeOnConcurrentPlay在原设置要求时还需实际source实例。原结束、开始、tick顺序以及隐藏、换层、销毁的End边界要在连续native对照中验收。
- 随角色候选准备提取/过滤/生命周期，整帧取消不派发，成功提交后消费副作用。先证明取消重试、跨角色、重绑和隐藏不会重复/丢通知，再移除旧示例独立通知时钟。

特别修正：`AnimationBlueprintLibrary.GetAnimNotifyEventDuration` 返回存储字段 `Duration`；运行时 `FAnimNotifyEvent::GetDuration` 返回 `EndLink.GetTime() - GetTime()`。本机 Unarmed crouch-right Pivot index0分别为 `0.53205406665802` 与 `0.5320539474487305`。合同分别保留 `storedDuration` 和原生 `duration`，触发/end时间使用原生 reader的全精度结果，不能从舍入后的T3D重算。

首轮 `notify-contract-ue-first.log` 错把Aim重复目录绑定当成重复目标错误；第二轮 `notify-contract-ue-bindings.log` 错要求存储duration与运行时duration逐位相同，两次实际退出-1，日志保留。分别按真实别名绑定和本机原生GetDuration语义修正后，两独立导出通过，未放宽时间门槛。

## 下一步

从现有 Main `PreparedTicks` 和完成的 Players/Samples 建立 typed source bridge，绑定真实来源和node上下文；合并当前五Slot Montage traversal，按原instance准备通知生命周期与typed请求，接 `LyraCharacterAnimation` 的统一提交。之后覆盖换类/多角色/取消重试/隐藏/终止、普通Demo及独立UE连续通知对照。完整Main联合native、root物理消费、武器/复杂地形、视觉/性能及整个Lyra移植目标继续开放。
