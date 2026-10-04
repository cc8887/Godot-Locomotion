# Lyra 真实 Source 通知桥

2026-10-02，直接在 Godot 主目录实施。沿用 ALS 人物/骨架和每角色一个 ItemAnimLayers 组的 typed 执行宿主；忽略 5.8/5.9 小版本差异。本批范围是共同 Sync 到通知提取的只读桥，整个 Lyra 移植目标继续开放。

## 实现边界

- `LyraNotifyCatalog` 加载原通知合同及目标动画的实际 UObject 身份，644 个原/目标路径、2,975 条定义、2,969 个对象身份、11 种 typed 类型。不同源/目标对象不合并；同一目标资源的别名复用同一政策范围。共享这些不可变定义，不共享角色时钟、随机种子或状态生命周期。
- `notify_source_modes_v1.json` 补齐固定 Main Lean 和三个动态 AimOffset 的实际通知策略，四者均为 `HighestWeightedAnimation`。两独立 UE 只读进程退出 0，0 资产保存，依赖与 676 个受保护包校验通过。资源 SHA256 为 `63d3e0f8eb0b03c8675d6faad62de4916672bdf0c72172d79b25e3bf753298d5`。
- `LyraMainLocomotionHost.PreparedNotifyContexts` 从实际源候选和嵌套状态更新捕获 Main/Linked 归属、原节点、epoch、active 上下文。Main 的 12/16/22 三个直接 Lean 不随装备换代；装备源采用新 Linked epoch。Idle 使用实际嵌套机更新，Recovery 使用原 `LandRecovery` 状态 2。当前受支持图没有显式通知过滤作用域，`ScopeFiltered=false`，不声称任意 UE 图兼容。
- `LyraSourceNotifyBinding` 按真实 Sync dispatch 顺序处理结果，按播放器身份关联图遍历登记；不能把组打包后的结果索引当成登记索引。使用实际 sample previous/delta、player context time、图权重、leader 和循环标志，BlendSpace 仅选择原最高权重样本；复用 Core 的原生窗口算法，没有建立额外播放器时钟。
- 捕获帧同时绑定 bridge 实例、Main 候选引用和角色帧身份，取消、重试、提交或换类后旧帧失效。提取只产生定义身份/ReachedEnd，不执行队列过滤、随机抽取、NotifyState begin/tick/end 或玩法副作用。

LeftHand 原 SequenceEvaluator 在三个当前 Provider 中资源为 null，既有合同已拒绝非空配置；它更新姿态入口字段，但没有应补造的独立通知源。

## 验证

验证入口为 `scripts/verify-lyra-source-notify.ps1`，结果汇总由 `tools/verify_lyra_source_notify.py` 生成。日志及报告位于 `artifacts/lyra-analysis/notify-source-*` 与 `lyra-source-notify-verification.json`。

三 Provider × 30/60/120 Hz 使用真实 Main 的自主选边/源登记/共同 Sync；输入来自既有玩法观察，不读取原生 source 时钟、权重或通知输出。每帧取消后重新 Prepare，两独立角色使用相同局部 player/sample ID、不同角色身份。每条轨迹 3/6/9 秒换类，共 27 次；完整 Layer 宿主每 17 帧求值一次，其余更新后提交，检查捕获/提取前后已提交历史及准备中 Sync 结果不变。

Debug/Optimize 均检查 7,560 帧、三个 Main Lean 节点、22 个 Linked 源节点、主从同步、非活动上下文、隐藏帧、最高样本策略、实际重排、441 次完整 Layer 姿态求值，以及逐帧重试一致。所测轨迹提取 377 个 ContextEffects/左右 FootPlant 事件；这个数量是本轨迹结果，不是原生通知连续等价证书。

该轨迹没有走到 Pivot 尾段的 `TransitionToLocomotion` NotifyState 区间。State 定义已载入、Core 状态算法有既有原生门禁，但本批不把它算成实际 Main 状态通知派发覆盖；后续真实队列/消费者必须补连续状态区间。

Core Release 定向 62 项（窗口、过滤/随机和状态生命周期原生夹具）通过；Import Release 29 项（原通知合同与 Standing 原生六组）通过。它们验证复用的原语和既有回归，不代表本批 Lyra 完整 UE 通知轨迹验收。

Debug/Optimize 引擎构建均 0 警告/0 错误。矩阵另回归既有 `lyra_main_als_native_smoke` 和普通 Lyra 60 Hz 入口的重绑/重试；它们检查此次 Main 查询 API 与缓存变化的影响，普通入口尚未消费新 bridge。优化运行临时替换六个程序集后按 SHA256 恢复 Debug。

两配置最终矩阵均退出 0、无 Godot ERROR/WARNING，源事件通道摘要同为 `9d22e277b2b45a526f9416c36d9e4a5422499dd70f2f3dbaecdd2a904358b890`。既有 LocomotionSM native 各 11,340 帧/9,762 姿态/246 条边通过；普通重绑入口各 480 个发布帧、6 次换类/6 次同类复用/480 重试通过，68 蒙皮骨局部姿态发布差 0。最终汇总脚本退出 0，六个 Debug 程序集另外复核恢复哈希相同。未新增本批渲染或性能验收。

复核既有通知资源合同，818 个旧 JSON、676 个原包及原合同哈希保持；本批新元数据追加为独立文件，不格式化或覆盖旧资产。

## 失败证据

首次 bridge 编译缺 Contracts using，首次 smoke 编译将 `FrameId` 写为 `Frame`；各失败日志保留。首运行只求值 LocomotionSM 却提交准备过的上身 Layer，改为现有完整 `LyraMainPoseHost`；下一轮发现共同 Sync 结果按组打包，已改身份重关联；再下一轮 Recovery 错取 State1，改原 State2。之后覆盖断言预设超过 1,000 个事件、又误要求此轨迹覆盖 Pivot 尾段状态通知，均由实际轨迹结果修正为明确的三类移动通知覆盖；原算法、资产时间、过滤阈值及原生精度门槛未调整。过程失败保存在 `notify-source-godot-debug-{first,fullhost,order,recovery}.log` 和 `notify-source-debug-matrix-first.log`。

## 下一步

后续源码及原生探针修正了队列归属：当前 Linked 继承 Main 根 Sync，源通知共享 Main 代理队列/随机流，原 Main/Linked 节点/player/epoch 仍保留。Source 队列/状态候选和角色事务进展见 [队列验证](2026-10-02-lyra-notify-queue.md)。五 Slot Montage 的 AnimInstance 随机流、实际 Slot 上下文及通知汇合仍待；随后接 typed GameplayEvent/ContextEffects/Transition/MotionWarping 消费者与完整连续参考。本桥的前述只读边界是该批次的历史验收范围。

`AN_PlayWeaponMontage` 操作武器 actor 的 mesh，不能直接塞进角色五 Slot bank；需要独立武器对象与动作宿主。音频暂缓要求保留。RootMotion 角色碰撞消费、完整 Main 联合原生验收、复杂地形/武器模型及视觉/性能仍开放。
