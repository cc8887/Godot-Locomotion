# 原资产通知表与真实播放 tick 接口

本批在主目录实现通知迁移的资源和提取边界。**尚未替换普通 Demo 的角色级通知队列，不关闭 R3/R6，也未移除旧移动源时钟兼容更新。**

## 实现

- 复用既有 UE 只读通知导出函数，遍历原动画 catalog 的全部 127 个 Sequence、9 个 Montage，输出独立 `refactored_notify_inputs.json`。完整 177 条通知：Footstep 154、GroundedEntry 1、EarlyBlendOut 6、SetLocomotionAction 9、RootMotionScale 1、CameraShake 6。没有保存 UE 资产，也没有导出音频。
- 不可变通知 bank 校验 catalog/source 哈希、完整闭包、原对象/类/索引/名字、有效触发偏移、过滤策略和类型化载荷。原生文本仅用于检查六位小数的 authored 字段；运行时保留独立导出的 float 全精度。Sequence ID 与原 Sync bank 对齐，Montage ID 排在完整 Sync 资源之后，不混用旧 V4 ID。
- 保留脚步左右脚、空中抑制、效果设置引用及音频参数元数据；GroundedEntry/Action 标签、EarlyBlendOut 条件、根运动缩放和相机震动参数均类型化。音频、粒子、贴花和震动效果没有在导入或提取阶段派发。
- SetLocomotionAction/EarlyBlendOut 两个原生类自带 branching 语义，即使事件的 MontageTickType 为 Queued 也不能当普通 Montage 通知。bank 显式保留该区别；本批未改既有 Montage 分支播放器。
- `AlsRefactoredSourceNotifyBinding` 消费已准备好的真实 source player batch，按 Sync tick Order 提取，保留实际 Leader、sample 的 DeltaPrevious/Delta、原 player context time 和图节点 weight。BlendSpace 使用最高样本权重并保留首个平局项，不能把样本权重乘进通知过滤权重。Inactive/ScopeFiltered 由图上下文显式传入。
- 播放身份包含 owner scope、player、sample 和 epoch；同资源多个播放节点、不同子图同样的局部 ID 不得合并为一个播放。scope 须由后续角色 owner 唯一分配，本批接口不自行创建全局注册表。
- 不新增播放时钟、随机数种子或状态生命周期 owner；提取接口只返回候选事件。错误容量不发布半成品，取消和重试使用同一已提交历史。事件进入角色级唯一队列后才能统一过滤、状态更新及成功提交后的副作用。

## 验证与失败保留

证据目录：`artifacts/tests/refactored-source-notifies/`。

- UE 全 Editor 目标构建 0 actions；ALS、AlsGodotExporter、AutoTestTools、BlueprintLisp 四插件审计通过。BuildId `2192dbcd-0924-430b-9a3d-1daff6c15a77`，fingerprint `4CE25A651115C8ABA1D5AA81FCFC0A97C638C958D714D73DABA212A75B25F83E`。
- 冷导出 PID 21644、普通 Editor 重启导出 PID 15332 均实际退出 0，136/177 语义标记成功，两份 JSON SHA256 均为 `B16D21411993112CEDAB15670B1173BF5DC2F941CB0A03F3322C72F714B00FBE`。普通 Editor 仍有两条旧 Condition failed 和五条旧警告，本批没有解决它们；不称 UE 日志无错误。
- 首次 C# 编译因 Sync enum 的 using 缺失失败，已修。首轮 13 测试因把嵌套 EndLink 当开始 LinkValue 全部失败；修正原生文本层级后 13 项通过，随后载荷/过滤策略检查加强后的 13 项也通过。
- 关联回归首轮 42 项中 39 通过、3 个 Roll 正常末帧测试失败。原因是测试错误地期待到动画末帧就结束状态：原 Roll 长 1.5 秒，状态有效结束为 1.500100016593933 秒；UE GetAnimNotifiesFromDeltaPositions 明确保留零移动下重叠的状态。修正场景为末帧仍保留、离开图后 End，而非修改算法或截断原偏移。中途隐藏则立即结束；三频率均验证同一状态实例持续 Tick 及一次 Begin/End。
- 真实 source 测试覆盖 30/60/120 Hz 各 4 秒、共 840 提交帧，每帧取消重试、独立身份/epoch、反向/循环、主从、最高权重样本、上下文、容量失败和错误 scope。Roll 三频率正常/中止合计 840 提交帧，使用真实序列→提取→Core 队列→状态生命周期，检查同一候选重复处理一致。
- 最终 Import 关联 42 项全部通过（`notify-final.trx`），含本批 23 项、真实 source/sync 及既有同步对照等相关回归。Core 通知窗口/队列/生命周期及旧 P5 source 相关 100 项通过。Godot Optimize 构建通过，0 警告/0 错误；Python 语法检查通过。

## 后续接线

1. 将 Standing/Movement、Rest、Crouching、Grounded、Locomotion 的 source owner 和对应图上下文汇入角色事务；统一分配播放 scope/occurrence handle，保留跨子图更新顺序和隐藏帧。
2. 与真实共享 Montage bank 的 direct/slot-segment 通知在同一角色队列合并，统一随机数、状态分配器和回调顺序。避免重复过滤、重复副作用或与旧 V4 事件 ID 冲突。
3. 将类型化通知接入原 GroundedEntry、RootMotionScale、动作状态和相机/效果消费者；取消、停用、销毁必须结束正确实例。按用户要求继续暂缓音频。
4. 替换 Demo 旧源通知并移除兼容时钟更新，再跑普通三频率、动作物理恢复、多角色 single/parallel、多帧观感及新的连续 UE 整链对照。

本批未修改 UE C++/插件配置，没有新的 UE 通知连续 oracle、Godot 运行场景、全量回归、十分钟性能或人工验收。现有原生 Core 通知证据仍为组件证据。其余 R2–R7、道具物理和头颈专项暂缓项保持原计划。
