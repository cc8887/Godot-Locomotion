# Lyra Linked Layer 的实际求值入口

本批继续使用 ALS skin68/raw69/logical81 和原十四个 Interface 函数，接入实际 Provider 的 Evaluation 历史及缓存入口检查。完整移植目标保持 active。

**最终 Debug/实际 Optimize 共70个进程及独立审计通过；本批关闭实际 Provider 求值入口及缓存读检查，完整移植仍未验收。**

## 入口与缓存归属

原 UE5.8 `AnimNode_LinkedAnimGraph.cpp:180` 在进入目标函数根之前同步 Provider 的 Evaluation counter，随后执行原函数根；输入姿态在该根的实际遍历中求值。Godot 完整 Main 现在遵循这个顺序。

SkeletalControls 在计算任何输入姿态之前进入实例求值；Aiming 在读取其 Provider78 输入缓存之前进入实例求值。LeftHand 在 Main locomotion 输入之前进入；Additives 和十个状态来源根在实际回调处进入。读取已生成的输出通道不会增加根访问，Slot 裁剪和缓存命中继续保留原遍历规则。

`LyraMainLocomotionHost.EnterLinkedEvaluation` 校验原角色候选、Main 已实际求值以及调用方 counter，然后同步目标实例。共享实例的不同函数继承同一个计数；未进入求值的实例保留已提交历史。求值访问集合来自实际回调，不能由 Update 访问集合推定。

Main 的实际78/83与 Aiming Provider 的实际78继续使用各自的 `LyraPoseCacheLifecycle`；逻辑181映射到后者。缓存 `Read` 在改变缓存历史、执行来源或返回命中数据之前校验所属实例的求值入口与当前计数。角色候选另存本次实际入口标记，已提交的旧计数不能表示当前候选已进入求值。

普通 Main、物理 Montage 路径及 Rebind 创建的新 Slot composition 均接入检查。阶段计数仍随角色统一预校验、提交和取消；姿态借用视图的单调序号继续独立。initial self、Unlink/reLink、既有姿态运算与唯一 skin writer 继续运行。

## 原 UE 对照

复用前批最终 `proxy-update-joint-v2-full` 原完整 Blueprint 捕获，无本批 UE 启动、资源保存或重导。三装备、四种1/3/4/14实例布局共12条轨迹、4320帧、120次同类重新绑定。

新增独立审计核对23096次实际函数根求值、9346个隐藏实例求值帧；连同23144次根更新、9334个隐藏实例更新，共334400项原始计数断言。内部 Aiming/Main 观察 tap 不计作 Interface 函数访问，全部实例最终快照仍逐项校验。

Godot 联合对照增加所有 Provider Evaluation，在启动、更新前、更新后、求值后、提交以及逐帧取消重试边界检查。每构建四布局4320帧，两构建合计1573104项计数/外部帧标量比较通过。Initialization/CachedBones 完整历史不属于这项通过范围。

探针使用原函数和原节点计算，Main Update counter 没有赋值覆盖。显式外部执行帧来自原请求；原 ALS81 RequiredBones 适配仍推进 CachedBones counter，不能据此宣称自然组件调度或整个探针零 counter 写入。

## 验证

Debug 与实际 ExportRelease Optimize 构建均0错误0警告。最终每构建35个 Godot 进程，共70个通过，无 ERROR/WARNING。

两种 MainPose 模式各11340帧、23442次 Provider 历史检查、621次提前缓存读取拒绝；每种174次同帧重复求值、207次取消重试、1578个仅更新帧。提前读取在来源回调和缓存历史修改之前拒绝。角色取消快照增加所有 Provider 已提交计数。

两构建均覆盖 Slot、最终 Rig、三Hz initial self/解绑与四布局、默认路由、武器、实时通知、普通十角色和 Emote，以及原完整最终图和实例字段回归。普通十角色/Emote 完整报告在两构建和前批之间相同。上述运行均为 headless，本批没有新增 GPU 观感验收。

Core 源码未变，关联170项沿用前批已通过结果，本批没有重新执行该 Core 测试组。原捕获保留3135条加载/瞬时压缩等警告，无 Error/Fatal/Ensure；这些警告不属于本批 Godot 构建和运行的零警告范围。

最终审计通过11份冻结实施/验证源码、其余4569份基线、870份导出JSON、710个原UE包、9份宿主配置和7份本机引擎源码。首次冻结后仅修正审计脚本的内部观察点分类，十份运行/测试源码和脚本保持原冻结字节；最终审计源码另存冻结清单。

四轮 Optimize 切换均逐文件验证六个程序集恢复；当前为已验证 Debug 程序集。证据见 `artifacts/lyra-analysis/proxy-evaluation-v3-integrity.json`、`proxy-evaluation-v3-native-integrity.json` 及各组 `*-verification.json`。本批最终实现与运行为 v3，原生夹具继续使用前批 joint v2-full。

## 过程与后续范围

v1 构建和四布局求值联合预检通过；v2 补充真实入口拒绝及候选历史断言，实际反馈11340帧预检通过。首次 v3 审计误将 `Main_InertiaInput` 等内部观察 tap 当作 Interface 函数，因未知 hook 拒绝；修正为明确的十四函数及内部观察点集合后审计通过。失败日志与原冻结清单保留，运行实施源码在修正期间保持不变。

本批关闭范围为完整 Main 中实际 Provider Evaluation 继承、缓存读入口检查及角色事务。Initialization/CachedBones 的绝对启动与后续完整阶段仍开放；现有启动控制器使用受控 seed，原 Main 首次初始化前的 Linked 子图可能继承未更新值，需要按真实绑定时序接入。

自然 Godot/UE 全局执行帧、多个物理 tick/渲染帧/URO与同帧 worker 门控、整图重初始化、RequiredBones/LOD和 Rig Construction 继续开放。任意重复函数调用、self scalar、部分绑定、其它 Provider、非零 Aiming 原生参数传播、全部 Source/Foot/Leg 私有字段、字段34/47及多组32/47、新ALS默认完整native、UE/Jolt314/1680物理差异、复杂地形/近景/全量/十分钟/性能等仍保持原验收边界。音频、道具物理及头颈继续暂缓。

没有修改原 UE 项目或引擎源码配置，没有提交或推送。
