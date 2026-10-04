# Lyra Main 延迟启动与首次骨缓存

继续使用 ALS skin68 / raw69 / logical81 和原十四个 Animation Layer Interface 入口。完整 Lyra 移植目标保持 active。

本批把 Main 的固定启动计数替换为实际首次根访问，并补齐 Main 初始化后对全部 Linked 子图的初始化与骨缓存调用。Debug 和实际 ExportRelease Optimize 的78个 Godot进程及独立审计全部通过；关闭范围仅为下述 Main 启动与固定骨布局的失效门控。

## 原组件启动证据

新增独立 `LyraStartupOracle`，使用原 Main 和三种装备 Provider Blueprint、原 Manny164 网格以及真实登记的 SkeletalMeshComponent。组件设置为参考姿态初始化，使原根初始化自然进入 deferred 分支；调用原 `UpdateAnimation_WithRoot` 与 `CacheBones`，原节点继续实际执行。

三装备、四种1/3/4/14实例布局与 before-root / after-root / self，共36组。两次独立 UE 进程退出0，requests/native/closure三份文件分别字节相同。没有直接赋值 Proxy counter 或 GFrameCounter，没有保存原资产。分组元数据仅在采集内临时覆盖，并由作用域恢复。

原 Main 初始 Initialization / CachedBones / Update / Evaluation 均为未更新值。先 Link 时，Linked 函数根可以继承未更新的 Initialization 与 CachedBones；不能为这些调用预设有效计数。

原首次 Main 根更新顺序为：

1. Main 根 Initialize，Initialization 从未更新推进到0。
2. 按 Main 原 Linked 属性顺序，对全部十四个函数执行 InitializeSubGraph 和 CacheBonesSubGraph；这时 Main CachedBones 仍未更新。
3. Main 在实际失效门控允许时执行 CacheBones，CachedBones 首次推进到0。
4. 继续原 Update。实际 Linked Update 根还有自己的 `CacheBones_WithRoot` 失效门控；此项不属于本批实现关闭范围。

first-update 的阶段前缀共528个真实根入口。per-call布局中三装备共27个未访问实例保持未更新的 CachedBones，但其 Initialization 已为0。Main 完成根启动后再 Link，则新实例全部继承有效的 Initialization0 / CachedBones0。

原 `CacheBones()` 在缓存没有再次失效时不进入根、也不增加 counter。采集使用原 `RecalcRequiredCurves` 再次失效，Main 进入一次 CacheBones，随后重复调用仍不遍历。同类 Link 保留实际实例。

原 UE 两次捕获各保留744条加载等警告，无 Error/Fatal/Ensure；不得与 Godot 的零警告构建和运行合并表述。证据为 `artifacts/lyra-analysis/startup-phase-v{1,2}-{requests,native,closure}.json` 和对应完整日志。

## 生产接入

`LyraMainSelfGraphPhases` 的两个阶段历史从未更新开始，Main 根在实际 `LyraMainPoseHost.Prepare` 的首次访问进入初始化。构造期只建立图、固定骨布局和真实 Linked 绑定；不会提前进入 Main 根。首次 Prepare 使用当前外部执行帧，保留现有 Update/Evaluation 的角色候选历史。

Main 根先发布实际 Proxy 阶段，再进入节点回调。全部 Linked 子图在 Main 根 Initialize 之后按原属性顺序调用，目标 Proxy 在各实际入口继承当前 Main 计数。`LyraProviderGraphPhases` 和实际缓存生命周期接受原合法的未更新阶段上下文；真实绑定、退休、空闲和 pending 门禁继续检查。未更新 counter 不会被当作缓存命中。

Main 增加失效门控与实际计数推进。有效缓存重复调用不遍历；显式失效后下一次根缓存推进一次。旧显式 counter 的受控 `CacheBones(counter)` 保留为已有节点原生对照入口，生产首次启动不使用该受控计数。

首次 graph 初始化属于持续的图生命周期；取消首次动画候选会丢弃 Update/Evaluation、Sync 和动画帧，不重复初始化已经进入的图。新测试同时验证这一边界，现有每帧取消/重试测试仍保持原断言。旧组件测试在记录每帧快照前显式经过真实根阶段，以隔离 graph 初始化与动画候选的不同生命周期；没有改变原姿态误差门槛或原生夹具。

## 新对照与已完成预检

新 `lyra_startup_smoke.tscn` 实际从未初始化的 Main 创建角色图，并通过生产 Prepare 自动进入首次启动。对照36组原组件记录的 Main四阶段历史、Provider初始化/骨缓存、真实调用点实例归属、阶段入口顺序和对应 counter/frame；同时检查回调入口看见的真实 Main/Provider Proxy 历史。

最终版本单构建的新对照包含9024项比较、684次阶段入口、72次重复骨缓存门控和36次首次动画候选取消/重试。684包含528次首次阶段前缀与156次失效后缓存入口。原 Manny164证据仅用于拓扑、阶段和实例历史；Godot继续使用ALS81，不能据此声称Manny与ALS骨姿态逐值相同。

原三装备图阶段30步/15机器预检通过。Sequence、BlendSpace、Skeletal初始化和旧缓存节点相关预检通过。Debug与实际 ExportRelease Optimize最终构建均0错误0警告。Core源未变，本批不重跑前批已通过的170项Core组。

首次探针编译因局部变量与UE的PI宏重名失败，改名后构建成功。首次Godot测试枚举类型编译错误也已修正并保留日志。v2阶段对照通过后，补充真实Proxy入口发布和首次取消重试；v4部分Debug矩阵主动结束，其完成日志保留。最终实施源码为v5，17份源码/验证文件已冻结；运行期间不修改。

## 开放范围

本批推进真实 Main 首次初始化、全部 Linked deferred 子图遍历和 Main 首次/显式失效骨缓存。Provider 在 Update 根中的 `CacheBones_WithRoot` 门控及完整阶段仍需继续；固定ALS81失效API不等于完整 RequiredBones / LOD / 重初始化集成。

已从本批原生记录独立列出39次额外 Linked Update 根骨缓存入口，分布在十二组 before-root 轨迹：single三次、three-groups九次、mixed十二次、per-call十五次。它们出现在 Main Update 已进入之后，不属于新测试所比较的528次阶段前缀。清单为 `artifacts/lyra-analysis/startup-runtime-v5-update-bones-inventory.json`，明确记录 `implementedThisBatch=false`。

本机原 `AnimNode_LinkedAnimGraph.cpp:61` 的直接 CacheBonesSubGraph 继承调用方计数，但不清除目标 Proxy 的失效标记；`Update_AnyThread` 则先继承 Update，再进入目标 Proxy。原 `AnimInstanceProxy.cpp:1483` 的 `CacheBones_WithRoot` 使用目标自己的 CachedBones，仅在传入根等于该 Proxy 的 AnimGraph 根时递增。下一步须按实际实例补齐这个门控、访问顺序和取消/重试边界，不能把 Main 的一次骨缓存当作目标失效标记已经清除。

原生捕获没有推进自然场景调度或GFrameCounter；Godot仍沿用受控外部执行帧。因此自然全局frame、多个物理tick/渲染帧、同帧worker门控和URO仍开放。通用重复调用、部分绑定、其它Provider和参数类型、非零Aiming参数原生传播、全部Source/Foot/Leg私有字段、新ALS默认完整native及全部原物理314/1680差异、复杂地形、近景握持、GPU/十分钟/性能验收保持原范围。音频、道具物理和头颈仍暂缓。

没有修改原UE项目/引擎源码或配置，没有保存或重导原资产，没有提交或推送。

## 最终验收记录

最终实施与运行版本为 `startup-runtime-v5`，两次原生启动捕获为 `startup-phase-v1/v2`。Debug 和实际 ExportRelease Optimize 构建均0错误0警告；两构建各39个 Godot进程，共78个，全部实际退出0，无 Godot ERROR/WARNING。

| 每构建验证组 | 进程数 | 最终结果 |
| --- | ---: | --- |
| 新启动、源初始化、缓存、MainPose/反馈、Rig、图阶段、初始self/解绑、路由、武器、通知、普通十角色和Emote | 31 | Debug/Optimize全部通过 |
| 原完整最终图 single/per-call，含Montage事件字段 | 2 | 两构建全部通过 |
| 原完整最终图 three-groups/mixed，含既有实例字段 | 2 | 两构建全部通过 |
| 四布局 Update/Evaluation 联合原生对照 | 4 | 两构建全部通过 |

新启动对照每构建36组、9024项比较、684次阶段入口、72次重复缓存门控和36次首次候选取消重试。MainPose两种模式每构建各11340帧、23442次实例历史检查、621次提前读取拒绝、174次同帧重复求值、207次取消重试和1578个仅更新帧；既有误差门槛与原生夹具保持不变。四布局联合对照每构建4320帧、786552项标量比较，两构建合计1573104项通过，仍明确只比较其原受控 Update/Evaluation 范围。

普通十角色每构建发布4800角色帧；十角色与Emote两类完整报告，在本批两构建及前批 `proxy-evaluation-v3` 两构建之间分别逐字相同。独立证明为 `startup-runtime-v5-ordinary-final-comparison.json`，不据此宣称新的完整native物理等价。所有本批 Godot运行均为 headless，无新GPU/观感/十分钟/性能验收。

独立完整性审计验证17份冻结实施/验证文件、其余4568份基线、870份导出JSON、710个原UE包、9份宿主配置、5份探针源及4份本机UE5.8引擎源。运行期间冻结源码没有改动，原资产及导出字节保持。四轮Optimize切换的六个程序集均逐文件恢复，24个备份哈希校验通过；当前为已验证Debug程序集。

证据为 `artifacts/lyra-analysis/startup-runtime-v5-integrity.json`、`startup-runtime-v5-assembly-restoration.json`、`startup-runtime-v5-build-comparison.json`、`startup-runtime-v5-ordinary-final-comparison.json` 和八份 `*-verification.json`。原UE日志的744条警告分别保留，不能称UE零警告；Core170沿用前批已验证结果，本批未重跑。

运行入口：`scripts/verify-lyra-startup.ps1 -Configuration Debug|Optimize -EvidenceTag startup-runtime-v5`；独立审计：`tools/verify_lyra_startup.py --tag startup-runtime-v5`。证据脚本拒绝覆盖已有记录。整个移植目标仍为active，后续首先补齐前述39次实际Provider Update根缓存门控，再推进完整阶段、RequiredBones/LOD和其它开放验收项。
