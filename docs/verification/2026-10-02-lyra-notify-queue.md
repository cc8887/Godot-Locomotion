# Lyra Source 通知队列与角色事务

2026-10-02，在主目录继续 ALS 模型/骨架、typed ItemAnimLayers 的移植路线。本批接共同 Sync 的实际源通知队列、状态候选及普通角色事务；五 Slot Montage 汇合和玩法副作用不属于本批关闭范围。

后续同日五Slot Montage通知已汇入此队列和角色事务，保留两条随机流及真实Slot/共享context语义；Debug/Optimize精确原生及生产门禁通过，见 [最新汇合验证](2026-10-02-lyra-montage-notify-queue.md)。下文未汇合描述是本Source批次的历史边界；具体玩法消费者和完整原类连续dispatch仍开放。

## 原生归属修正

此前 Source 桥文档写“Main/Linked 各自队列、随机流”不适用于当前共享根 Sync 的执行链。安装版 UE 5.8 的 `AnimInstanceProxy.cpp:1388` 保留已有根 Sync 消息，`AnimSyncScope.cpp` 将子节点登记转发给根代理，`AnimSync.cpp` 以该代理的 `NotifyQueue` tick 资产。因此 Main 与 Linked 的原节点/player/sample/epoch 身份都保留，但这里的源通知过滤使用同一个 Main 代理队列及随机流。

独立 `LyraNotifyOracle` Editor 插件创建真实共享更新上下文、两个原生代理及两个实际 Sequence tick record。两个源均推进到 1.5 秒，Main 队列 20 条、Linked 队列 0 条；Linked seed 保持初值 90,345,571。这证明实际原语的根作用域归属，不是运行原 ABP 完整角色图的联合验收。

`AnimInstanceProxy.cpp:670` 再把已过滤的代理队列 Append 到 AnimInstance 队列，然后 ApplyMontageNotifies。Append 不能重新过滤或抽随机数；Montage 在 AnimInstance 队列中使用另一条随机流。换装备不应重置 Main 代理 seed 或 Main 已激活的合并型状态。

## 实现

`LyraNotifyQueueRuntime` 仅消费 `LyraSourceNotifyBinding` 捕获的真实完成窗口，没有额外动画时钟。按 dispatch 顺序提取，再使用原事件权重、主从、专服、LOD、概率和请求过滤条件；State AddUnique 保留第一个接受引用的上下文。源 seed 跨帧持续，队列每帧清空。

候选包含原资产/对象身份、Main/Linked 原节点及 player/sample/epoch、活动上下文/ReachedEnd、过滤后队列、状态和 callback 描述。Prepare 不执行回调；候选同时绑定源帧 guard 和角色帧。取消不发布 seed、分配器或状态；源先取消或提交后，guard 拒绝旧候选；重试结果一致。

类通知复用 Core 原生生命周期顺序：instant，然后旧 End、新 Begin、全部 Tick。命名事件插回原队列的 instant 顺序，使用 `-1` 描述而不消耗类通知 ID。命名接收函数和具体 Notify UObject/BP 的玩法行为尚未执行，不以这些描述替代真正消费者。

`LyraCharacterAnimation` 在真实 Main Prepare 后采集通知，与完整姿态、物理快照、Montage bank 和蒙皮发布共同预校验；在 Main 提交使源 guard 失效前提交通知描述。迟到的物理/模型失败由角色取消整个候选。重绑沿用这个 Main 队列；角色退役通过 EndAll 清除状态并撤销旧候选资格。每角色持有独立的可变队列，共享不可变通知资源。

UE 原生 NotifyInstanceID 是进程级静态分配器。本实现当前仍使用角色内的 opaque callback ID；已验证的是生命周期关联及角色隔离，不声称原生数字 ID 或跨角色全局分配次序等价。未来有依赖原生 ID 的消费者时，需要补统一主线程分配/映射。

## 验证入口与覆盖

- `scripts/export-lyra-notify-queue.ps1`：独立 UE 只读进程，使用单独打包的探针，不修改 Engine 或 GASP 资产。
- `scripts/verify-lyra-notify-queue.ps1`：Debug/Optimize 队列 native、六角色三 Hz、普通十角色和既有 Main native 回归。
- `tools/verify_lyra_notify_queue.py`：逐帧数据、日志、两配置、受保护资源和程序集恢复的证据汇总。

请求先由 Godot 从自主 Main 的共同 Sync 捕获，随后 UE 根据请求的完成区间调用原资产 `GetAnimNotifies` 和真实 `FAnimNotifyQueue.AddAnimNotifies`。未用 UE 输出反向驱动 Godot 源时钟、权重或选边。原生探针输出每窗口的提取顺序、每帧队列引用及过滤前后 seed，Godot 逐项比较；时间按 float 精确比较，身份、顺序、布尔值和 seed 精确比较。

九条实际轨迹为三 Provider × 30/60/120 Hz，共 7,560 帧、27 次换类；源桥旧摘要不变。另 482 个受控原资产窗口覆盖 42 条原 State + 36 条目标 State，以及 6 条命名事件：重复源保留首引用、连续状态、换 epoch 不重建合并状态、端点、反向、离图 End、EndAll 和逐帧取消重试。此附加覆盖包含 Montage 资产上的元数据窗口，不能算成真实 Montage Advance、Slot 相关性或动作中断覆盖。

两独立 UE 最终进程退出 0、输出相同，0 资产保存；源根归属检查通过。Debug/Optimize 均与原生 8,042 帧精确对照，过滤后 1,129 引用、1,449 callback 描述、347 个 seed 变化帧、160 Begin/160 End/316 Tick。Core Release 定向窗口/过滤/生命周期 62 项通过。源请求 SHA256：`88c5a6a0a4b4da0c1b3641f2cb435a91f13695c496d84264be4c573df5785864`；原生资源 SHA256：`bece5650d4eed4b2b50e0225043000d7aa96fecd127c20c3d6851dbc460e11e2`。

两配置各六角色三 Hz 共 10,080 角色帧，逐对完整 pose/curve/attribute/root/history、通知窗口/队列/callback/seed 对照一致；每 Hz 24 次换类、2 次角色销毁重建，迟到物理父链失败、跨角色候选和逐帧取消重试均通过。原角色局部 ID 相同仍保持可变队列隔离；源队列已纳入 committed history 比较。

普通 Lyra 十角色两配置各 4,800 发布帧、461 个通知描述，主角色 480 次取消重试、6 次换类/6 次同类复用和 5 次迟到失败，蒙皮局部差 0；源队列帧与角色帧逐一对应。既有 LocomotionSM native 回归各 11,340 帧/9,762 姿态/246 条边通过，它是原范围回归，不提升为完整 Main 验收。

最终矩阵各退出 0、无 Godot ERROR/WARNING，Debug/Optimize 的队列门禁、三个多角色完整报告和普通十角色模型报告逐值相同。两引擎构建 0 警告/0 错误；优化运行替换的六个 DLL/PDB 已按 SHA256 恢复 Debug。汇总报告为 `artifacts/lyra-analysis/lyra-notify-queue-verification.json`，验证脚本退出 0。本批没有新增渲染、性能、完整原类 NotifyState 连续 dispatch 或完整 Main 联合验收。

原通知合同、818 个旧 JSON、676 个 GASP 原/目标包由两次导出逐个哈希验证保持。原有 GASP GameplayTag 和启动警告保留，未修改资产或配置来消除它们。

## 失败与边界

初次 C++ 编译失败来自 const message 访问和私有代理队列访问，修为真实可变共享消息及只读探针的成员指针访问。下一轮链接暴露 `FAnimNotifyQueue.Reset` 未导出，改走导出的原生 `UAnimInstance.ClearQueuedAnimEvents`，没有复制 Reset 算法。首次 UE 运行缺 SharedContext 导致断言，补真实 `FAnimationUpdateSharedContext` 后重建，两次新进程正常退出。上述失败日志及每轮包全部保留。

首次 C# smoke 编译使用错误的 `CurrentAnimationTime` 名称，改为实际 `CurrentTime`，失败日志保留。没有改资产时间、概率/LOD/权重门槛或原生误差阈值。

尚未完成五 Slot Montage 通知过滤/合并、原类/BP 连续生命周期原生参考、typed Gameplay/ContextEffects/Transition/MotionWarping 消费者和武器 mesh 上的 AN_PlayWeaponMontage。原生产根运动碰撞消费、完整 Main 连续原生、复杂地形/握持、渲染与性能验收继续开放；音频暂缓要求保留。当前提取暂存上限为每窗口 512 条，超限整候选失败，不截断或默默丢事件。
