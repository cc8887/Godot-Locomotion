# Lyra Main 自主 LocomotionSM 与十根宿主

2026-10-01，在 Godot-Locomotion 主目录实现并验证。使用原 ALS skin68/raw69/logical81 和既有不可变 Lyra 资源。安装版 UE 5.8.1 源码作为规则与生命周期依据；本批没有启动、修改或重新导出 UE。

## 关闭范围

新增 `LyraMainLocomotionHost` 和 `LyraLocomotionResources.CreateMainHost`。同一角色的一次完整 Main 宏先生成观察候选，Main LocomotionSM 消费 Godot 自身上一提交的相关源和 Sync 历史，生成当前/上一权重、初始化、清权重与根访问。十根 Source Scope 接收同一 Main 候选，按机器产生的顺序调用真实子图，登记全部源；宿主执行一次共同 Sync 并保留真实 tick order/leader，供后续通知消费。

求值输出是 **LocomotionSM 边界** 的81骨、曲线、整数属性和 typed RootMotion。骨骼采用既有标准过渡栈，曲线保留存在性，属性按 uniform Multiply/Accumulate，RootMotion 按原 uniform operator 混合。Main 的惯性请求排在子图请求之前，但本批没有把原位于上身/additive 之后的最终惯性节点挪进 LocomotionSM。

Main RootYaw mode 从上一图提交携入下一宏更新。Main 与 Idle Provider 的反馈各自保留，最终曲线需由调用者经 `StageFinalFeedback` 显式提交；本批测试的封闭图以 LocomotionSM 为最终边界。生产接入应在后续上身图完成后提交其实际最终输出。

## 原语义与修复

- `AnimNode_StateMachine.cpp:688` 的相关源选择只扫描当前状态的直接播放器及对应 Linked Layer 的 `GetGraphAssetPlayerInformation`，严格大于才替换候选。同权重保留第一个，IgnoreRelevancy 的 Main Lean/HipFire 被排除。当前原编译图 Idle 和 Pivot 无可选相关源，不能把其嵌套机器叶子补进 Main 查询。
- 相关源读取实际 owner 已提交的 Asset、长度、PublicTime、Previous/Delta；Start/Stop/FallLand 保留原 authored ExplicitTime，包括负值，不用采样钳制后的时钟代替规则输入。独立的 cached-weight 登记只保存相关性权重，不推进第二份动画时钟；原入状态清权重在候选内处理。
- `AnimSync.cpp:490` 判断组有效性时先查询读缓冲名称是否存在，再查询 MarkerEnd；没有 marker 的已登记空组有效。两个 buffer 各自保留登记键，提交才翻转。Core 返回空 output slot 不会自动创建原组。
- 联合运行暴露了源 Update 与最终 Evaluate 的差异：旧标准过渡已更新源，但随后完成的惯性过渡可移除它们。增加机器实际姿态栈产生的 `EvaluationRoots`；未被最终姿态需要的源仍提交时钟，但 Warp 与 Provider 曲线反馈不因多余求值而推进。反馈提交与提交门禁绑定同一求值根集合，原受控 Scope 默认仍要求全部访问根求值。
- Main 宏、机器、相关性、双缓冲键、Sync、子图字段和反馈统一预验证后提交。prepare/resolve/evaluate/验证阶段及晚期取消均不发布；失败候选、重复提交、失效姿态读取、缺失最终反馈和将姿态反馈当仅更新帧提交被拒绝。

## 验证

新测试只读取已编写的 gameplay observation 请求，**不读取 native state/rules/weights/source clocks/Sync 输出**。三 Provider 每条轨迹固定实例；原请求中的换类段没有在此宿主实施，因此不与旧 Manny Main oracle 的状态轨迹宣称相等。

| 验证 | 最终结果 |
| --- | --- |
| 自主 Main，Unarmed/Pistol/Rifle，30/60/120Hz | 7560帧、6330混合姿态、512730骨；全部10真实状态/10根、216转换、30带自动时间调整的转换、2380混合帧 |
| 自身源与 Sync 历史 | 5476有效相关源帧、84负ExplicitTime、2385已登记空Locomotion组仍有效帧；165隐藏、1230仅更新帧 |
| 同资源的角色隔离与逐帧取消重试 | 独立角色保持初始历史；候选、最终姿态/曲线/属性/RootMotion与 clean run 一致，49065次门禁拒绝 |
| Update但最终不Evaluate | 4个根访问；未求值Start/Cycle Warp仅按原初始化重置或保留 |
| 实际转身反馈，三Provider三Hz | 3780帧，1576次反馈变化、1880 Test组登记帧、1716非零Main转身比值帧、95个Idle与最终混合反馈不同帧；348仅更新、294晚期取消重试 |
| 原 Main机器 native观察驱动回归 | 7560帧/10状态/9924更新，原严格门槛通过；仍是此前的组件 oracle |
| 原共同四地面根 native回归 | 3780帧/8400姿态/680400骨，源/Lean时钟逐位通过 |
| 原五Air图 native回归 | 3780帧/7650姿态/619650骨/37800时钟通过 |
| 原Main Idle根回归 | 2520实际UE回调帧逐位通过 |
| 原标准混合/最终惯性组件回归 | 840帧、10状态、depth3、18请求，姿态/曲线原门槛通过 |
| 受控十根Scope回归 | 3780帧/11064姿态、20顺序/三组、3021反馈事务通过 |

Debug 与 ExportRelease Optimize 最终0错误0警告。Godot 场景最终进程均正常退出0，成功标志唯一，无 ERROR/WARNING。508个既有原UE包与643个既有JSON按上一捕获哈希保持。本批代码和证据核验入口为 `tools/verify_lyra_main_own_host.py`，最终报告 `artifacts/lyra-analysis/main-own-host-final-verification.json`。

首次 smoke 使用不存在的 Quaternion API/错误参数类型，编译失败日志保留；第二次运行夹具误要求隐藏帧必须拒绝普通提交，修正夹具。之后全访问求值门禁暴露真实 Update/Evaluate 根差异，上述门禁按实际姿态栈修复，不增加虚假求值、不放宽原生精度阈值。日志均保留在 `artifacts/lyra-analysis/main-own-host-*`。

## 仍开放

这是自主 Main 与十根源/混合的组件接线，不是新完整主图 native 验收。下一步在 ALS81 上采集原 Main 机器与实际所有子图共同执行、最终混合曲线/RootMotion 的连续 UE oracle，逐帧比较当前自主宿主；覆盖换类、真实层初始化和清权重的边界。

三个 Provider 目前固定实例；新宿主未实现换类、self-layer/Unlink、完整14 typed图入口、source Notify统一队列、Montage、原上身/additive后最终惯性、完整足部后处理或普通Demo/Gather/Worker/Commit。Pivot规则的通知输入只留显式事件边界，未作为已完成通知系统。原生闭环、三种装备的普通玩法/握持视觉、多角色和性能仍需验收。没有全量、渲染、十分钟或人工矩阵。本批不关闭完整 Lyra 目标，也不改变 ALS R2–R7 或用户暂缓项。
