# Lyra 实例 Montage 委托与发布后回调变更

2026-10-03。在现有主目录继续推进 ALS 人物和 Lyra Interface/Layer 路线。本批把真实物理实例委托接到 bank，并接入原 Emote。新原生三频对照、Core 与最终 Debug/实际 Optimize 的二十四个 Godot 进程全部通过，独立审计通过。完整目标保持 active。

## 实例与回调执行

新增 `AlsMontageCallbackBindings.cs`，委托按物理 InstanceId 和事件种类绑定，与可序列化的 Montage 状态分开。委托表随 bank 候选复制、统一提交或取消；发出事件时，四类队列中的 envelope 捕获当时的委托。后续重绑不能替换已经排队的委托。实例终止时释放自身表项，已排队的 Ended 仍保留捕获值。

`BeginWithActionRequests` 增加 `beforeWeight` 候选配置入口：在 prerequisite 播放/停止请求之后、第一次 weight/Advance 之前，为新实例安装原 task 委托。此入口配置候选，不发布外部效果。`LyraEmoteAbility` 在真实实例绑定 BlendingOut/Ended，角色和 Emote 验证场景删除传入通用 observer 的调用，消费实际捕获的实例委托。物理成功后才发布立即效果，动画重试继续核对并确认同一事件凭据。

排队委托在角色最终发布以后派发。bank 在这一阶段让现有 PlayAction/PlaySequence/StopInstance/StopSlots 操作访问真实已提交物理状态，不复制另一个 bank，也不重新 tick；嵌套回调共用同一个现场。回调创建的新实例位置和权重仍为零，下一 tick 才推进。已冻结的代理姿态与当前帧 RootMotion 区间不因发布后新请求重新计算；新的惯性请求留给下一次更新。

global multicast 在实例委托返回后读取当前绑定。候选修改的 global 绑定也随取消回滚。回调异常时恢复 bank 的访问状态，已发布回调中的物理变更保留；这不是任意 UE 脚本异常或对象销毁语义的验收。

## Stop 的原执行顺序

本机 `AnimMontage.cpp:1561` 先设置混合、DesiredWeight、active lookup 和中断状态，再调用 `QueueMontageBlendingOutEvent`；原 `bPlaying=false` 位于回调返回后的尾部。针对这一顺序修复：

1. 委托能够查询到已经停止的混合和移除后的 active lookup。
2. 委托内再次停止同一实例并缩短混合，返回后保留新值；外层不能写回旧快照。
3. 即使停止时长为零，实例委托和随后的 global 广播仍看到此前的 Playing；回调结束后才清除。
4. 首次停止的惯性请求在委托之后登记，保留原 last-per-group 次序。
5. outgoing 委托可能在 Play 的组停止阶段创建新实例，外层创建实例前再次检查容量。

本批原生 `out-play` 正好由零 BlendIn 的原 Pistol Fire 替换触发第三项，初次对照确实失败，修复后通过。不是只用人工期望支持这个结论。

## 原生参考和验证范围

新增可选 `LyraMontageBankOracle`，复制上一探针，原 WholeMain/Linked/MontageDelegate 源码保留。新函数只读执行原 Manny、原 Montage 与真实 UAnimInstance；资源 Notify 副作用排除。没有保存资产、重导人物、修改原资产图或引擎源码。

两个独立 UE 进程的完整 request/native JSON 字节相同。四种案例各有 30/60/120Hz、七秒：Ended 委托再播放；BlendingOut 委托停止另一实例；BlendingOut 委托播放同组替换；发出 Out 后重绑验证旧委托仍被捕获。共 12 条、5880 帧；27 Out、27 In、27 End，各有 global 广播，再加 9 个回调返回观察，共 171 次。

新 Godot 场景执行自己 authored 的请求和回调，原参考只用于断言。每帧先取消并重建候选，检查物理状态、事件载荷和四容器；派发前、每个 queued 回调内部及派发后的完整公开物理库存按八字段核对，并检查 frozen proxy 未被回调改变。每构建为 5880 retry、171 callback、33021 检查。

立即事件仍是物理成功边界后的外部效果，因此本夹具对立即事件检查载荷、次序和阶段，没有把更新之后的库存假装成原 weight 当刻库存。任意立即委托修改本帧 weight/Advance bank 仍未实现；当前入口明确拒绝在延后效果内修改 bank/绑定，以及 Commit、Discard、StopForRagdoll 和立即事件凭据确认。这些门禁保护已经推进的物理候选；不能把拒绝执行当成原生兼容。

本机 `AnimInstance.cpp:2262` 的 weight 循环每次读取实际 `MontageInstances.Num()`；回调新增实例可以进入同一次 weight 遍历。`Montage_Advance` 同样读取动态库存，并在回调返回后校验当前索引仍指向同一实例。下一批必须让同步候选回调在这个真实阶段修改同一 bank，保留嵌套停止和实例身份，再将外部玩法效果纳入提交或物理成功凭据。单纯提前调用当前 Emote 的外部回调或保持固定 `advanceCount` 都不能关闭这项要求。

最终 Release 定向 Core 540 项通过、0 失败、0 跳过，包含新八项捕获/重绑/嵌套停止/再播放/frozen proxy/取消/receipt/零时长/异常恢复边界及相关既有原数值和零分配门禁。不是全量 managed 测试。

原生证据前缀为 `artifacts/lyra-analysis/montage-bank-callbacks-v1`，package 为 `package-montage-bank-callbacks-v1-final-v2`；最终 guarded 源码构建、Core、运行矩阵与审计使用 `montage-bank-callbacks-v1-final`。前两次 C++ 编译失败（私有 interrupted 字段和不存在的 getter）保留，新快照仅读取公开 getter，真实 Interrupted 由事件载荷校验。首 Core 命令在原 SDK 固定目录执行失败，随后从父目录成功执行。首两个 Godot 比较失败分别为 float JSON 精度表达和夹具误用 Linear，改为完整 double 表达原 float 与本机 HermiteCubic 默认，门槛未改。后续 Playing 失败修复了运行时代码，全部失败日志保留。

## 最终构建、生产回归与独立审计

最终冻结 89 份源码后构建 Debug 和实际 ExportRelease Optimize，两构建均零错误、零警告。所有运行均使用最终增加事务门禁的程序集；门禁增加前的 Debug 矩阵保留为中间证据，不用于最终验收。

| 每个构建的检查 | 实际范围 |
| --- | --- |
| 新 bank 回调原生对照 | 12 轨迹、5880 帧及同数 retry、171 回调、33021 检查 |
| 原四容器及单 Section 事件 | 21 轨迹、8823 参考行、8820 retry |
| 原 Emote | 54 轨迹、27720 帧及同数 retry、1774080 检查 |
| 三频 Emote 真实移动 | 六角色、30/60/120Hz，共10080移动与各10080次移动前/动画 retry |
| 原 Warp 与 root 物理 | 各60Hz六角色、2880移动及两类 retry；Warp 522非零/87阻挡 |
| 普通 Main 十角色与 E 输入 | 每角色十四独立实例，共5280角色发布帧，含换类、同类复用、武器、事件阶段与 retry |
| 原 ALS 普通入口 | 60Hz、1700帧、4次实际 Rest 动态补步 |
| 完整 Main 最终 Rig 对照 | 三 Provider、十四实例布局、1080帧及同数 retry，同时检查当前34/47 Linked字段 |

每构建十一项场景和一项完整 Main 进程，共二十四个最终 Godot 进程均退出0，具备成功标记且无 Godot ERROR/WARNING。三频移动及两份普通角色完整报告跨构建相同；普通报告还与上一批对应基线逐项相同。完整 Main 对照使用受控物理输入，不代表 UE/Chaos 与 Godot/Jolt 世界物理等价。

`tools/verify_lyra_montage_bank_callbacks.py` 独立检查上述终态日志、报告、Core TRX、源码/实际程序集、原生双捕获、原探针与 package 源码、两轮六文件 Debug 恢复，以及869份旧资源JSON、710个原UE资产包和9项项目/Config文件的SHA256。最终 `montage-bank-callbacks-v1-final-integrity.json` 为 `auditPassed=true`，`committedCallbackBankMutation=true`，`immediateCallbackBankMutation=false`，`fullMontageEventDispatch=false`，`goalComplete=false`。

本批未执行GPU/近景、新完整世界物理轨迹、全量managed、十分钟性能、独立游戏导出或人工全矩阵验收；既有失败记录和暂缓项目保持。

## 当前整体路线与剩余要求

继续使用 ALS 68 skin /69 raw /81 logical，生成 typed Interface 合同，按类和 Group 路由真实 Layer 实例，共同 Source Sync 与单一骨架发布。当前三个 Provider 的十四入口和 1/3/4/14 布局保持；明确私有字段对照范围仍为 34/47。

本批关闭上述 captured physical delegates、指定发布后回调变更及 Emote 接入。立即 weight/请求回调的同步 bank 变更与事务化效果、重新 Advance、弱对象和销毁、真实 Section 跳转与循环、Ended 的 active NotifyState 结束顺序仍开放。不能把本批 kernel/公开状态对照称为完整通用 Montage 回调验收。

非空左手源、余下 Linked 配置/引擎字段、Main Montage 共享模式变化、default/self/Unlink/部分覆盖和同函数多调用点的通用整图、其它 Provider，仍按原路线推进。完整 Chaos/Jolt 同输入运动仍有既有 314/1680 帧差异，本批不关闭；近景握持/脊柱变形、复杂地形/平台、独立导出、跨平台和性能仍待。音频、道具物理与头颈专项按原要求暂缓。
