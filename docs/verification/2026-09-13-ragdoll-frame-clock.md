# Ragdoll 状态机、独立来源时钟与隐藏子图生产生命周期

第一百三十六批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 本批完成

承接第 135 批原始资源与 NamedSnapshot，新增 `AlsRagdollFrameCompiler`
与 `AlsRagdollFrameRuntime`，将状态更新、来源初始化、Flail 播放时钟、
姿势求值及取消/提交连成同一候选。新增 `AlsRagdollAnimationSource` 使用
真实 Godot 原始动画采样包装器，不由采样器维护第二套播放时钟。

正式编译核对 Ragdoll States 的编辑器连接、编辑器属性、烘焙拓扑和
编译节点身份：机器 14、Flail 播放器 16、两个状态及相反的 MovementState
条件；两个内部过渡均为零秒、HermiteCubic、StandardBlend。校验默认
状态、每帧最多 3 次转换、首次更新跳过混合、重新相关时初始化及无自定义
回调/通知/混合配置。不能把外层根的 0.4/0.5 秒套到这两个内部过渡上。

来源时长与 RateScale 取现有正式 `v4_movement_runtime_inputs.json` 的
syncAssets，并与导入动画验证；ALS_Flail 时长约 0.6666667 秒、RateScale=1，
没有 marker 或 Notify。Jump 与 Ragdoll 可共享同一个不可变 ALS_Flail
资源，但 Ragdoll 播放器有自己的 epoch、秒数及历史，不借用 Jump 的时钟。

运行时保留以下规则：

- 每个动画帧准备一次候选，即使父根当前不访问本分支。初始化访问初始
  状态；隐藏期间不 tick Flail、不推进状态机，也不采样该姿势。
- 全局 FlailRate 只在 MovementState=Ragdoll 时更新，读取 root 物理三维
  速度；其余状态保留历史属性。进入播放器时才转换为 float 播放倍率。
- 状态机按真正的遍历计数判断重新相关，支持计数回绕；角色 FrameId 或
  全局时间的间隔本身不作为重新初始化依据。骨骼缓存计数变化不重置时钟。
- 首次更新仍执行转换条件。离开 Ragdoll 时，内部立即选择命名快照；
  外层根可以继续淡出此快照。根标记 Inactive 不等于停止所有源更新。
- 非零/零速率、零 delta、暂停访问与重新进入均保留源初始化规则。
  DoNotSync 经现有 `AlsSyncRuntime.TryEvaluateAssetSyncBatch` 的独立来源
  路径推进，姿势采样只读取候选秒数。
- Prepare/Evaluate/ValidateCommit/Commit/Cancel 统一保留候选与已提交
  的状态机、FlailRate、epoch、秒数和来源历史；求值失败自动取消。

直接只读核对本机 UE 5.9 的 `AnimNode_StateMachine.cpp`、
`AnimNode_SequencePlayer.cpp` 与 `AnimTypes.h` 的更新、重新相关、首次
过渡、初始化及 WasSynchronizedCounter。没有修改/启动 UE 插件，也没有
新增 UE 最终图或状态机运行时 oracle。

## 实际生产接入边界

`--foot-ik-frame` 的真实帧所有者现在创建 Ragdoll 子所有者，使用正式
Mannequin mesh 名称与逻辑/物理骨映射。普通移动每帧准备隐藏子图，根的
冷初始化真正传到该状态机；其初始化和身份随最终姿势一起提交/取消。

`AlsFullMovementDiagnostics` 和控制器事务诊断加入明确字段的 Ragdoll
快照，避免 Godot 所用 .NET 对 InlineArray 内建 Equals 的限制。生产
回放逐帧断言：身份等于最终可视帧、机器已初始化且未更新、epoch=1、
时间和 FlailRate=0、未 tick。晚期姿势/事件失败检查包含这份提交历史。

此处仍只允许普通生产分支。真实 Ragdoll 输入没有伪造为零速度后接受；
生产混合分支仍保持未绑定状态的显式拒绝。动作/Ragdoll gameplay 没有
因此启用；默认 Demo 仍为 BaseLayer。

## 验证

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 最终优化 Debug 构建 | 0 错误、0 警告 | `dotnet build GodotALS.csproj -c Debug -p:Optimize=true --no-restore` |
| Ragdoll、通用状态机、根专项 | 49/49 | `artifacts/test-results/ragdoll-frame-core-verified.trx` |
| 正式 Ragdoll 资源/输入/帧编译专项 | 13/13 | `artifacts/test-results/ragdoll-frame-import-verified.trx` |
| 真实资源根/Ragdoll 组合 | 30/60/120 Hz，4 所有者，single/parallel 各 3,360 帧 | `artifacts/ragdoll-frame-root-verified.log` |
| 最终普通生产回归 | parallel 960 帧通过，新增隐藏子图逐帧断言通过 | `artifacts/ragdoll-frame-production-verified.log` |
| 晚期姿势失败 | 根、Ragdoll 历史、控制器与姿势回滚通过 | `artifacts/ragdoll-frame-late-transaction.log` |
| 晚期来源事件失败 | 回滚通过，事件回调泄漏 0 | `artifacts/ragdoll-frame-late-source-event.log` |

Core 专项包含计数回绕、重新相关、隐藏冷启动、零时间切换、物理速度
属性保留、Inactive 与父级状态上下文、失效代际、求值失败及取消重试。
热身后连续 2,000 次状态机/时钟/姿势更新零托管分配。

组合场景 `scenes/tests/ragdoll_frame_smoke.tscn` 使用真实 ALS 动画资源，
外层根真实 0.4/0.5 秒混合，反向中断、隐藏后重新进入以及命名快照。
每个并行帧均在完整求值后取消，再以相同输入重试；最终姿势/曲线和
播放器秒数、倍率、epoch 的摘要与单线程相同，重试另比较完整诊断。

| Hz | 每所有者帧数 | Flail 更新帧 | 快照访问帧 | 双分支混合帧 | 最终 epoch |
| --- | --- | --- | --- | --- | --- |
| 30 | 120 | 69 | 27 | 60 | 3 |
| 60 | 240 | 138 | 55 | 122 | 3 |
| 120 | 480 | 276 | 112 | 248 | 3 |

组合中的普通姿势和保存快照为受控输入：普通源是真实片段，快照取上一帧
输出的物理骨局部姿势；没有模拟真实布娃娃刚体，也没有将完整 BaseLayer
作为该场景的普通分支。因此这些结果不等同于实际角色进入/退出 Ragdoll
或 P6 物理恢复验收。第 135 批 UE 原始姿势对照仍是来源采样层证据。

普通生产结果保持 result=`D898A6B5BD5DE295`、fullPose=`7B82A91E8A09C723`、
root=`DB5B813964D3479C`、sampledPose=`6804D603D2523040`；223 个播放器、
257 个样本，316 帧脚锁、910 帧偏移、37 个事件，lag/stale=0。新增
Ragdoll 播放器尚未加入这个共享活动来源批次，普通回归不宣称覆盖其播放。

过程中修正了正式默认值读取对 CRLF 的处理、组合测试遗漏普通动画自带
曲线的布局，以及 InlineArray 诊断比较。早期失败日志保留；采用上表
verified 结果，未把它们计作未修复的产品故障。最后仅加强编辑器/烘焙
一致性校验后完成 13 项 Import 与 Debug 构建，未重复无行为变化的回放。

## 下一步与保持未完成的范围

继续真实根混合分支调度和普通分支重新相关/初始化/CacheBones 的传播，
明确全局输入每帧更新与普通子图不再相关时暂停来源的边界，并接实际
共享来源批次。真实物理快照采集、物理/动画所有权及恢复按 P6 推进。

随后回到平台、支撑脚轨迹、UE 同输入多帧与人工验收，再切换默认 Demo。
本批没有新的视觉验收，不关闭起步滑步、换髋交错步或上身效果问题。
完整 P6 仍不作为修复这些 P3/P4 问题的前置条件。

P5A 其余通用事件/同步/动作/Slot、P5B Overlay/道具玩法、P5C Mantle/
Roll/Root Motion、P6 完整物理恢复/Camera、P7 十分钟性能验收继续保留。
既有 Core 23 项失败、Import 分配不稳定、旧 p95=2.559ms 超过 2.5ms 未
关闭。音频暂缓；没有 commit、revert 或 merge，保留既有工作区修改。
