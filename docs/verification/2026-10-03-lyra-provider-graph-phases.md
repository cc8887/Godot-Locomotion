# ALS 人物上的 Lyra Provider 启动与持久阶段历史

2026-10-03，直接在主目录推进。沿用 ALS skin68/raw69/logical81、原十四入口合同和实际 Linked 实例归属，忽略 UE5.8/5.9 小版本差异。完整移植目标保持 active。

## 本批行为

Main 的阶段控制器现在由 `LyraMainPoseHost` 持有，跨 Link/Unlink 保留自身 SaveCachedPose 与状态缓存计数。Provider 的阶段历史放在真实 `LyraItemLayerGraphInstance` 内，同类 Link 保留对象，换类与重新 Link 创建新对象，退役对象不能继续调用。没有额外 Provider、播放时钟或 Sync。

初始 Linked 路径和新 Provider 替换先对每个实际绑定函数执行 InitializeSubGraph 与 CacheBonesSubGraph，再沿原 Main 编译拓扑执行启动遍历。换装只初始化新 Provider 的绑定函数，保留 Main 的状态、物理 Montage、装备和最终 Rig。原普通初始 self 路径继续零 Linked 实例，完整 Root 初始化仍访问被空默认根在 Update/Evaluate 中裁掉的输入。

阶段根据导出的原 Root、链接、机器状态名和缓存节点遍历。LinkedInputPose 在 Initialize/CacheBones 不递归调用者输入；外层 Linked 调用节点在目标 Root 后遍历全部 Pose 输入。Main 的两个 SaveCachedPose 和 Aiming 的 BasePose 分别维护自己的生命周期。状态机 CacheBones 按真实提交状态权重选择状态，并分别保存 counter/global frame；同计数同帧跳过，同计数不同帧、回退和回绕重新访问。

实际 Idle/IdleStance/Additives/Pivot 机器在阶段访问时进入初始状态。此前未访问 Pivot 的状态为 -1，现于实际绑定函数初始化后为0、时间0、权重[1,0]。LeftHand/Additives 宿主按实际绑定函数提前建立。首次 Update 的子源初始化指令保留为待消费状态，并随成功提交清除；取消仍可重试。初始化不会提前执行 SetUpIdle 等 InitialUpdate、BecomeRelevant、源 Update 或播放回调。

固定 ALS81 骨映射继续由原算子构造绑定提供。新增阶段控制器负责启动与持久遍历计数，尚未统一全部既有 Update/Evaluate 缓存的生命周期，也没有动态骨布局、骨映射重建或完整后续重初始化。Rig VM Construction 保持原首次求值的 pending 路径。不能把阶段访问顺序对照描述为所有源播放器、骨控制和 Rig 内部 Initialize/CacheBones 字段逐位完成。

## UE 源码和独立参考

本机 `AnimInstance.cpp` 确认普通 Link 按真实类/组建立实例，再对受影响函数调用 InitializeSubGraph/CacheBonesSubGraph；同类操作保留原实例。LinkedAnimGraph 的子图初始化同步调用者 InitializationCounter，函数 Root 不递增目标 Proxy 完整初始化计数。CacheBones 同步调用者 CachedBonesCounter，然后直接进入 Linked Root。状态缓存需比较计数和 global frame，不能只要求计数单调增加。

新增可选 `LyraGraphPhasesOracle` 在临时 GamePreview 世界使用原 Main、原三类 Provider 和 Manny164 网格。实际 Link 后在反射 PoseLink 与真实 LinkedRoot 上加转发 Tap，调用原 Main Root Initialize，随后用原节点执行十个受控 CacheBones 上下文。组件注册已发生；这里显式重初始化原 Root，是受控阶段参考，未采集 UE 世界自然启动或普通组件 tick 的完整时序。

两次独立 UE 进程实际退出0，无 Error/Fatal/Ensure；request/native/closure 逐字相同。各744条原加载/GameplayTag Warning 保留，阶段采集不执行通知。每个 Provider 的 Initialize 观察56项；十个 CacheBones 的观察数量依次为56、34、56、34、56、34、56、56、56、34。上下文覆盖重复、回退、相同计数不同 global frame、32767到-32768。每个配置读取五个真实机器快照，状态/时间0，初始权重1；阶段结束读取26个 SequencePlayer/Evaluator 的时钟，全为0。

StateMachine 初始化会重建非反射 StatePoseLinks。原生 Tap 不能单独观察 Main StateResult8，以及本轨迹 Provider StateResult14/16/2。managed 比较只排除这四个明确节点，仍比较它们的可观察子节点、真实状态与权重。没有任意删除访问项或修改误差门槛。

Manny164 参考证明原拓扑的受控阶段语义，不证明 ALS81 骨缓存内部几何、自然世界启动时序或新默认最终姿态。原生不执行 Update、Evaluate、物理移动或保存原资产。26个时钟是阶段结束的读取，不是全部源逐阶段内部初始化字段捕获。

启动计数使用宿主自己的初始序号；本批比较访问序列、明确传入的十个CacheBones上下文以及调用者/目标的相对同步，没有采集并对照组件注册产生的全部绝对Proxy计数。后续应统一真实组件阶段驱动，不能用当前构造期计数代替自然世界初始化或LOD计数。

可选插件首次构建因直接调用 protected GetMachineDescription 失败，日志保留；改为合法派生成员指针访问后 package-graph-phases-v2 构建成功，8 actions。原工程源码、配置和引擎源码没有修改，临时插件已移回 artifacts。十二份本机 UE 原源码副本与 SHA ledger 保留。

## 验证证据

新真实 Godot 阶段场景已对照三Provider、30个缓存上下文和15个机器快照，并检查源/worker/Montage历史不变、同类保留、Unlink退役、新实例重新 Link 及函数 Root 计数同步。快照比较包含Main状态/时间/权重、Provider状态/权重；没有逐项比较所有Provider elapsed或全部源内部初始化字段。构建 Debug、实际 ExportRelease Optimize 均0错误0警告；既有 Core LinkedLayer/合同131项通过。这131项是关联门禁，新增宿主阶段由 Godot 场景验收。

最终 Debug/实际 Optimize 每构建24个、共48个 Godot进程全部退出0、无ERROR/WARNING。包括新阶段场景、六个初始self/首次Link用例、六个旧Unlink用例、默认根/路由/命名通知/武器/实时通知、普通十角色/Emote和四布局完整Main最终Rig。三Provider三频及取消重试都在普通角色中覆盖；空中默认帧仍是明确抬高后的实际下落，不称为自然Jump等价。

两构建全部初始/解绑计数与上一批精确相同。每构建初始用例14,040角色提交、7,020默认Rig帧/同数retry、180切换；旧解绑用例14,040角色提交、4,680默认Rig帧和7,020retry。四布局外部Main每构建4,320帧/同数retry，继续对照既有原生最终Rig参考；单组/逐调用点保持34/47字段范围，另外两布局保持32/47范围。普通十角色/Emote完整JSON在两构建与上一批之间一致。

`tools/verify_lyra_graph_phases.py` 最终独立审计通过，结果为 `artifacts/lyra-analysis/graph-phases-v1-integrity.json`。36份冻结源及其余受控源码、870份JSON、710个原包、9项工程/配置、十二份原UE源码保持；三轮Optimize各六个Debug程序集/符号逐字恢复，实测Optimize DLL与Debug不同。源阶段结束快照、原生日志、构建、Core TRX、48个进程报告和保护清单均被审计。

证据统一前缀 `artifacts/lyra-analysis/graph-phases-v1-`，包括两独立原生requests/native/closure、两配置verification、whole-main/other-groups报告、core.trx、build-optimize.log与audit.log。第一轮阶段场景已通过，随后加上Main空闲帧门禁并重建；最终完整矩阵使用冻结后的源码。没有运行期失败或误差门槛变化。

## 继续开放

下一步统一阶段与实际 Update/Evaluate 的缓存所有权、完整源及骨控制初始化，再接后续重初始化、真实 RequiredBones/LOD 和 Rig Construction；需取得这些阶段及 ALS 默认最终姿态的独立连续参考。self scalar、部分绑定、同函数多调用点和其他 Provider 仍待通用执行。原字段34/47、UE/Jolt314/1680帧物理差异、近景握持、地形、材质和性能保持开放；音频、道具物理、头颈暂缓保留。

当前Main更新遍历将Aiming的Provider78映射到181，求值Scope也用83/78/181三个固定身份；新阶段历史则按实际Provider对象区分78。统一时须保留实际owner/node身份，连接状态进入与相关性重初始化对状态骨缓存计数的影响，并保留每次Evaluate的新Scope。不能把这三份存储简单合并，也不能将Pose缓存Scope当作播放器时钟。

本批没有新GPU、全量managed、十分钟或性能验收，没有原资产重导/保存，没有提交或推送。用户未提交改动、ignored资源及JSON字节级依赖保留。
