# Lyra Layer Initialize / CacheBones 调用阶段

2026-10-03。上一批普通角色 Unlink/reLink 已完成并验证，本批继续处理其初始化边界。直接在主目录实施；ALS68/raw69/logical81、本地资源和用户其它改动保留。整个移植目标仍 active。

## 源码与实现

本机 UE5.8 的 `FAnimNode_LinkedAnimGraph::Initialize_AnyThread` 先初始化有效目标 Root，再按原顺序初始化所有 InputPoses。`CacheBones_AnyThread` 同样先 Root、后所有输入。Root 缺失也不能漏掉第二个或后续输入；self 的空 Result Root 在 Update/Evaluate 不遍历输入，并不意味着 Initialize/CacheBones 也跳过输入。

`FAnimNode_LinkedInputPose` 在这两个阶段只同步输入 link 的调试计数，不递归到输入；输入遍历由外层 LinkedAnimGraph 统一负责。实际重连还使用独立的 `InitializeSubGraph_AnyThread`；它不能被错误替换成一次完整输入初始化。

Core `AlsLinkedLayerExecution` 新增 Initialize、CacheBones 与两个 SubGraph 入口。Godot `LyraLinkedLayerCallRoutes` 接入四个阶段，使用既有真实调用点、目标、实例、组和 epoch 校验；完整阶段检查输入数量、保持 Root 在前与全部输入在后的次序。self 执行已导入的真实空 Root 语义，不创建 Provider；unbound 只遍历输入；external 回调交给该调用点的实际实例。

本批增加的是调用阶段 API。**普通 Main 仍未按初始化/缓存阶段遍历其完整内部图并调用该 API；状态机、Pose cache、惯性、RequiredBones 与跨 Proxy counter 的完整调度尚未接入。**现有准备帧中的 Initialize 标记和构造时资源绑定不能替代该阶段，不能称完整 Initialize/CacheBones 已完成。

## 验证

新增可选 UE 原生探针 `LyraLayerPhasesOracle`，在真实 Main 实例上执行初始 self、实际 Link、实际 Unlink、实际重新 Link。每阶段十四个原调用点；另十二个 unbound/零至双 Pose 输入、普通/加法与冷/已填充输出组合，共68用例。只临时替换活实例的 Root tap 和受控输入，逐次恢复，不改 CDO、编译元数据或保存原资产。

每个实际绑定阶段的十四个 Root 均初始化/缓存一次，三个 Pose 输入均在 Root 之后各访问一次；unbound 十二例无 Root、合计十二次输入访问。Aiming self 的两条记录均为 `root → input:11`，同时其后 Update/Evaluate 仍不访问输入。external 用例只采集真实 Initialize/CacheBones，不作外部姿态或完整 Main 验收。

最终原生包为 `package-layer-phases-v5`。两独立 UE 进程 `layer-phases-v5` 与 `layer-phases-v5-repeat` 退出0，无 Error/Fatal/Ensure；request/native/closure 文件均逐字相同。原有 GameplayTag 与加载依赖 Warning 保留。

Core Release 新68项原生顺序对照和4项目标/Root guard 与 SubGraph 边界，加原59项，最终131通过、0失败、0跳过。没有改变原生姿态误差门槛。

Debug/实际 ExportRelease Optimize 构建均0错误0警告。Godot 两种构建各三个、合计六个最终进程通过且无 ERROR/WARNING：

- 路由阶段：三种 Provider、四种1/3/4/14实例布局，每构建3360次阶段调用、672次真实 external owner 回调、360次输入访问、48次非法调用拒绝；既有 Update/Evaluate 的336 self/336 unbound/168 external/60拒绝亦通过。
- 默认 Main：每构建1260帧、同数 retry，沿用既有2520帧受控默认 Main 原生数据，验证 worker 与未遍历历史。
- 普通角色 Unlink30Hz：每构建1080角色提交、360默认帧、540 retry、24次切换，默认帧Rig完成360/姿态改变354、实际扰动后下落54帧；计数与上一批相同。此项仍不作自然 Jump 物理等价证明。

独立审计通过：13份本批源码冻结、其余既有受控源码保持、870份资源 JSON、710个原包及9项项目/配置哈希保持；七份原 UE 源码已逐字复制并校验，六个 Debug 程序集/符号恢复匹配。最终证据：`artifacts/lyra-analysis/layer-phases-v5-integrity.json`。Debug 构建日志标签为 v2、Optimize/Godot标签为 v4；冻结的运行时源码一致，v5修订只涉及原生探针测量与原生测试数据路径。

## 过程证据与边界

v1将旧 fallback 夹具扩展到 external 后，额外求值遇到缺少 CachedPose scope 的断言，日志退出3保留。v2直接构造缓存 scope 遇到未导出符号链接失败；失败包又启动一次采集，缺少有效模块，未纳入验收。v3尝试调用 Proxy 的受保护求值入口编译失败，保留构建日志。最终探针明确采集 Initialize/CacheBones；外部完整姿态继续由既有整链入口验证。

v4的真实阶段顺序已采集，但计数在后续 Update 之后读取，混入其自动 CacheBones，断言失败。v5在阶段结束时快照顺序与计数，原始数据先保存再校验；没有改引擎算法或降低期望。最终两进程与131项测试一致通过。

下一步应接完整 Main 初始化/缓存阶段及其实际输入子图，处理初始self、状态机/缓存/惯性初始化、RequiredBones与Proxy counter传递，再取得ALS81默认最终姿态的新连续原生对照。scalar 参数、部分绑定、不同Provider/通用非空默认图、字段34/47和完整物理314/1680差异继续开放。近景/地形/性能等其余目标保留，音频、道具物理和头颈专项仍暂缓。

本批只新构建可选探针模块，临时工程插件目录已移到本仓库证据目录；没有修改UE引擎或原项目源码/配置、保存或重导原资产。没有新ALS默认最终姿态原生数据、GPU观感、全量managed、十分钟或性能验收，没有提交或推送。
