# 跨 linked graph 的共享 Source Sync 范围

本批是通知接入前发现的同步完整性前置项。已实现共享收集器和完整移动角色资源绑定；**尚未把生产宿主的实际节点访问迁入收集器，普通 Demo 仍使用现有局部同步/通知兼容链路。**

## 原生依据与缺口

本机 UE 源码显示，`FAnimNode_LinkedAnimGraph::Update_AnyThread` 通过 `WithOtherProxy` 继续传递更新上下文；`FAnimInstanceProxy::UpdateAnimation_WithRoot` 在已有同步消息的 linked instance 内不另建根同步范围。`FAnimSyncGroupScope::AddTickRecord` 将记录转发到外层 Proxy。因此 Standing 与 Crouching 中同名 `Movement` 必须参与同一次同步，不能在两个局部 bank 分别选主后，仅合并通知。

当前 `AlsRefactoredStandingHost`、`AlsRefactoredCrouchingHost`、`AlsRefactoredGroundedHost` 和 `AlsRefactoredLocomotionPoseRuntime` 在各自 Prepare 内立即推进 source，并读取下一帧观察时钟。此结构需要改为“节点访问时收集 → 外层一次 Sync → 各 owner 捕获已完成时钟”，再求值姿态、提取通知和提交。

另外，`FAnimSync` 的两份 TMap 分别维护组名首次插入顺序。`Reset()` 与 `ResetAll()` 都只清组的内容，不移除 map 的键；`ResetAll()` 将 write index 归零。不能使用一个全局永久排序表，或在初始化时删除组名顺序。实际 Tick 先按当前写 map 的顺序处理组，再按访问顺序处理独立播放器，随后翻转缓冲。

## 实现

- `AlsRefactoredSourceSyncScope` 将多个 owner 的局部 player/group ID 显式绑定为单一物理 source bank 的全局 ID。组名使用 FName 式不区分大小写身份；同资源、同局部编号的不同 owner 保持独立播放器/epoch。
- `Begin/Enqueue/Complete/Commit/Discard` 统一事务；Enqueue 必须发生在真实节点访问处，保留图权重、Active/Inactive 和 ScopeFiltered。未 Complete 不允许读取时钟、姿态或通知；重复播放器、错误角色/代际/帧/更新 counter/weight 被拒绝。
- Complete 只调用一次原 source runtime。维护两份已提交组插入顺序、候选 write index；取消不发布新组顺序或索引。未曾出现在当前 map 的组只占内部空尾部空间，不产生 tick，也不暴露在该 map 的 Groups 中。
- `ReinitializeOwner` 保留局部节点重置与整实例重置的区别，隐藏节点直到下一次实际访问才更新 epoch；取消的初始化请求不泄漏。
- source runtime 支持候选组顺序，上一帧 group history 按 group ID 找回而非按新数组序号取值。批次总播放器容量采用已有 Core batch 上限 512；单组 128/样本 512 的限制仍由原 Core 校验，未改变同步公式。
- `AlsRefactoredLocomotionHostProfile.CreateSourceScope` 绑定原 Standing Movement、Standing Rotate、Crouching Movement、Crouching Rotate、Grounded 两源和 Locomotion 空中源六类 owner，保留全部八个组名。固定帧 teleport evaluator 不在资产 tick 列表中。
- 通知读取本批共享同步结果，使用全局 player/sample 身份及实际 Leader，不重新选主或推进时钟。此处不另建随机数或状态 lifecycle，也没有派发副作用。

## 验证

日志目录 `artifacts/tests/refactored-source-scope/`。

- 三频率跨 owner 测试各 3 秒，共 630 提交帧，每帧取消重试；覆盖同名组共同选主、双方领导权切换、隐藏再相关、不同组与独立源交错登记、Active/ScopeFiltered、真实脚步提取及 follower 过滤。每个 player/sample/tick context 与独立构造的单一平面 bank 精确一致；姿态重复求值一致。这是共享收集组件对照，不是新的完整 UE 角色轨迹。
- 180 帧单 owner BlendSpace 隐藏/恢复、反向播放和样本缓存保留，与直接原 runtime 精确一致。
- 候选组插入取消、两份 map 不同顺序、ResetAll 保留 map 键并复位 write index、局部隐藏初始化、错误 context/重复访问拒绝均有专项检查。
- 首轮 6 项中 3 项失败：测试错误要求同一 marker 组的 leader/follower 时间逐位相等，实际原算法存在一个 float ULP 的 marker 换算差异。改为逐身份对照单一完整 bank，未修改公式或放宽任何 native 门槛；同时修正隐藏一个三节点 owner 后剩余节点数量的测试预期。随后关联 39 项及双缓冲专项 6 项通过。
- 加入完整角色资源测试后关联 40 项中 39 通过、1 失败：测试误把静态组编号的编排顺序当作资源要求。修正为校验完整组名集合；实际执行顺序仍独立按访问/双缓冲断言。
- 最终 `scope-inventory.trx` 新增 7 项全部通过，包括六类原 owner 的全部 player 同帧登记、共享 Movement 跨 stance 选主、通知接口及逐播放器 79 骨求值；随后全隐藏帧提交。此完整库存测试同时激活所有源，仅验证绑定/生命周期，不能当真实状态机遍历或视觉验收。另加双缓冲两组同时活跃的 12 帧专项，检查执行顺序分别保持、每组历史不互换和实际时钟逐 float 相等。
- Core AssetSync 43 项通过，Godot Optimize 构建 0 警告/0 错误。既有 Standing 独立/共享的六个连续 UE 对照包含在关联回归中，原阈值保持不变。

本批没有 UE C++/配置修改，没有启动 UE 或重新导出原生轨迹，也未运行 Godot 场景、全量测试、十分钟性能或人工验收。用户未提交修改保持原样；音频/道具物理/头颈专项继续按原约定暂缓。

## 下一步

1. 把六类生产 source owner 改为共享 player ID/view，在实际访问位置 Enqueue，包括 deferred cache 之前的直接源；不能在图遍历完成后按 owner 拼接。
2. 将 Details/Rotate/Grounded/Air 的候选时钟捕获延后到一次 Complete 之后，原状态机仍读取上一提交帧观察；支持空图全局帧和统一取消/提交。
3. 接 Demo 并验证站蹲重叠/跳跑/转向/多角色的一致性，补新的跨 linked graph 原生连续对照。
4. 使用共同 tick 次序汇总图通知，与 Montage direct/slot-segment 队列按 UE 顺序合并，接类型化消费者，最后移除旧兼容更新。其余 R2–R7 仍保留。
