# Ragdoll 接入统一播放来源与普通移动回归

第一百三十七批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 本批完成

承接第 136 批，新增 `AlsRootSharedSourceCompiler`，在既有 Main/Overlay
的 223 个播放器、257 个样本后追加根 Ragdoll 播放器 223、样本 257。
最终根入口使用 224/258 的正式绑定；原来源编号、同步组和 Overlay 映射
保留。Jump 与 Ragdoll 复用不可变 ALS_Flail 资源，各自持有独立的物理
播放身份、epoch、秒数和权重历史，不按资产路径合并播放器。

`FlailRate` 是正式的来源倍率输入，只允许用于 Ragdoll 的无同步组循环
Sequence。缺失或非有限倍率会在 tick 输出写入之前拒绝；其他来源沿用
原有策略。Flail 的时长、RateScale、无 marker/Notify 属性均核对正式数据。

新增 `AlsRagdollSharedSourceCollector` 收集候选初始化、epoch、时间和
更新上下文，交给既有共享来源批次推进。Ragdoll 所有者在此模式下不再
独自 tick；必须收到同一帧、绑定、播放器、样本、epoch 的批次完成结果，
才可求值/提交。隐藏子图不贡献活动 tick，但初始化历史仍在候选中，且
必须经过批次完成。取消不会发布候选历史。

图定义同时保留原 223/257 Overlay 视图与新 224/258 根视图，切换视图
时重建对应的 Turn/Montage 通知绑定。不开原始资源采样的定义加载路径
也会编译根绑定。`--foot-ik-frame` 已选择新视图；`--layered-frame`
仍选择原分层视图。没有修改默认 Demo 入口。

## 验证

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 最终优化 Debug 构建 | 0 错误、0 警告 | `dotnet build GodotALS.csproj -c Debug -p:Optimize=true --no-restore` |
| Core Ragdoll/来源专项 | 40/40 | `artifacts/test-results/ragdoll-shared-core.trx` |
| Import 根/Overlay 正式绑定专项 | 8/8 | `artifacts/test-results/ragdoll-shared-import.trx` |
| 共享批次真实资源组合 | 30/60/120 Hz，4 所有者，单/并行各 3,360 帧 | `artifacts/ragdoll-shared-root-verified.log` |
| 普通生产单线程 | 960 帧通过 | `artifacts/ragdoll-shared-production-single-verified.log` |
| 普通生产并行 | 960 帧通过 | `artifacts/ragdoll-shared-production-parallel-verified.log` |
| 原分层视图兼容 | 并行 600 帧，223/257 | `artifacts/ragdoll-shared-layered-compatibility.log` |
| 晚期姿势失败 | 候选历史/控制器/姿势回滚通过 | `artifacts/ragdoll-shared-late-transaction.log` |
| 晚期来源事件失败 | 回滚通过，回调泄漏 0 | `artifacts/ragdoll-shared-late-events.log` |

Godot 无界面编辑器扫描正常退出，无 ERROR/FAIL，已由引擎生成新增
`AlsRagdollSharedSourceCollector.cs.uid`；未手写 UID。日志为
`artifacts/ragdoll-shared-editor-import.log`。本批文件空白检查通过。

组合回放使用真实 ALS 动画资源。Jump 和 Ragdoll 同时采样 ALS_Flail，
逐帧检查两者时钟独立；共享完成前不允许私自推进 Ragdoll。每个并行帧
在求值后取消再重试。三种频率下姿势、曲线与 Ragdoll 播放历史摘要均与
第 136 批独立时钟组合相同。普通分支和保存快照仍为受控输入，并非实际
布娃娃物理或完整 BaseLayer 双分支 gameplay 验收。

普通生产两模式结果均为 `B289A6FB5130DBB7`，完整姿势摘要
`7B82A91E8A09C723`、根 `DB5B813964D3479C`、采样姿势
`6804D603D2523040`；316 帧脚锁、910 帧偏移、37 个事件，lag/stale=0。
逐帧确认隐藏 Ragdoll 的共享 epoch/time 等于已提交子图历史，且不在
活动播放器批次中。这里的脚锁/偏移帧数只证明覆盖，不证明支撑脚无滑移。

新播放器令静态布局之后的动态通知句柄偏移，因此直接结果摘要与第 136
批不同。回归在诊断副本中按来源种类/绑定/图槽及 Montage authored key
映射回原布局，归一化摘要两模式均为 `D898A6B5BD5DE295`，与原回放
一致。正式发布帧不做此映射。最初诊断只覆盖静态来源，遗漏动态 Turn
通知句柄而失败；已补齐语义映射并保留唯一性检查，采用上述 verified
日志。未放宽结果一致性断言，也没有用统一减一猜测身份。

## 实际完成边界与下一步

本批补齐共享来源依赖，原生脚部生产入口仍仅运行普通根分支。生产端
完整根混合调度、普通分支失去相关性后的重新初始化与 CacheBones 传播
仍未闭合，全局属性更新也尚未与普通子图暂停访问完全拆开。真实物理
快照采集、动画/物理所有权和 Get-up 保持 P6 范围。

下一项先处理上述动画生命周期依赖，然后回到 P3/P4 的平台、支撑脚、
同输入 UE 多帧和人工验收，验收后切换默认 Demo。不能把完整 P6 物理
玩法作为当前上身、换髋和起步滑步修复的前置条件。没有新的最终图视觉
对照，本批不宣称这些问题已解决。

原 P5A 通用事件/同步/动作/Slot 收口、P5B Overlay/道具玩法、P5C
Mantle/Roll/Root Motion、P6 完整恢复/Camera、P7 十分钟性能验收保持。
既有 Core 23 项失败、Import 分配不稳定及旧 p95=2.559ms 超过 2.5ms
尚未关闭。本批是专项验证，不是全套通过。音频暂缓；没有 commit、
revert 或 merge，保留既有工作区修改。
