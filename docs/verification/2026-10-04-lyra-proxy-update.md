# Lyra Main / Linked 的实际 Update 计数

继续沿主目录移植 Lyra，使用现有 ALS skin68/raw69/logical81 与十四个 Interface 入口。本批关闭完整 Main 的 Update 候选推进和实际 Provider 根继承，完整移植目标仍 active。

## 实现

`LyraMainLocomotionHost` 和每个实际 `LyraItemLayerGraphInstance` 各拥有一份 `LyraProxyTraversalHistory`。完整 Main 在子图遍历之前建立角色候选，实际根访问推进 Update；访问到的 Linked 根在 worker 入口前复制调用方 Update。共享实例的多个函数继承同一个计数，隐藏实例保留历史。

SkeletalControls 的完整 Main 输入改为该实际 Update counter，首次访问为0，不再由观察帧号加1推算。未访问的首次候选可以携带未更新值-1；真正访问仍拒绝-1。组件级 Locomotion/SkeletalControls 调用保留原受控入口边界，只有完整 Main 调用进入新的 Proxy 根事务。

上一批 Main Evaluation counter 移入同一 Main owner，借用姿态视图序号仍单调增长。Main/Provider 的候选先统一预校验，再随角色提交；取消恢复全部已提交值。initial self、Unlink/reLink 和换类保留 Main 历史，新 Provider 保留自己的独立历史。default 空根在真实访问时推进 Main Update，未访问路径不推进。

骨缓存阶段的 pending 检查前移到阶段状态与 counter 更新之前，防止拒绝后留下 running 标记。夹具重复拒绝 pending CacheBones，并在取消后实际遍历同一 Provider 的 CacheBones，验证可以继续使用。

候选严格绑定原 Main macro 对象；RootYaw 回调返回的更新副本不替换事务身份。现有曲线、Source 时钟、共同 Sync、延迟缓存调度和唯一 skin writer 继续运行。

## 原 UE 完整图证据

新增独立临时 `LyraProxyUpdateOracle`，复用已验证完整图探针的原 Blueprint、原 worker、原状态机与 linked roots。在原根 tap 中增加只读阶段快照，继续转发原 Initialize/CacheBones/Update/Evaluate，没有代替原节点计算或写 Main Update counter。

使用本机 UE5.8 原入口，实际三种装备、四种实例布局：1/3/4/14 个 Provider。第一次短轨迹1440帧用于检查入口；最终联合轨迹 4320 帧、12条轨迹、120 次同类重新绑定，包含 23144 次原函数根更新与 9334 个隐藏 owner 更新。独立审计比较 193168 个原计数/帧断言。

完整图首轮确认 Main Update 从未更新值-1进入0；逐函数布局未访问的 Provider 保持-1，访问的 Provider 继承 Main 当次计数。原请求显式记录每条轨迹从1开始的受控外部执行帧，Godot 用该请求时钟对照；没有从原生动画输出读取计数来驱动 Godot。

适配仍使用临时 ALS81 骨容器、目标 mask、瞬时序列与完整最终 ControlRig。原探针的 RequiredBones 适配显式推进 CachedBones counter，因此不能宣称整个探针零 counter 写入。没有保存、重导或覆盖原 UE 资产。

## 验证

Core 相关170项通过，包含上一批原 Proxy/Linked 入口、同帧重复与有符号回绕证据。最终 Debug 与实际 ExportRelease Optimize 构建均0错误0警告。

最终两构建共 70 个 Godot 进程全部通过，无 Godot ERROR/WARNING。覆盖 MainPose/真实反馈各11340帧、Slot47610、Rig7560，原阶段、三Hz initial self/解绑重连、默认入口、武器、通知、普通十角色和 Emote。新的 pending 骨缓存门禁在两种 MainPose 模式中通过。

新增联合轨迹每构建四布局4320最终 Main 帧，并逐帧取消重试；Main Update/Evaluation 与每个 Provider Update 在启动、更新、求值和提交边界严格对照，共 907560 个计数/外部帧标量比较。Initialization/CachedBones 与 Provider Evaluation 的绝对值不在这项通过范围内。原最终姿态与已比较 worker、PreUpdate、movement 和 graph 字段继续沿用原门槛。

普通十角色/Emote 完整报告在 Debug、Optimize 和前批之间相同。四轮 Optimize 六程序集备份均逐文件校验恢复，当前为最终已验证 Debug 程序集。

审计通过 21 份冻结实施源码、其余 4554 份基线、870 份资产JSON、710 个原UE包、9 份宿主配置及 7 份本机引擎源码。探针构建保留2条原 SetBase 过时API警告；两次原 UE 捕获各 3135 条加载/瞬时压缩等警告，无 Error/Fatal/Ensure。Godot 构建和最终运行的零警告结论不包含这些 UE 警告。

## 过程记录

v4 主姿态预检11340帧及前五项矩阵通过。检查拒绝路径时发现 pending CacheBones 会在拒绝前改变阶段状态，主动结束本任务自己的反馈测试进程，保留部分矩阵与失败退出记录；它不是完整 v4 验收。修正检查顺序并增加恢复断言后重新构建，最终完整证据为 runtime v5/native joint v2-full/package-v1。

## 继续开放

生产默认外部帧仍来自现有物理观察。显式受控执行帧用于联合对照，尚未接自然 Godot/UE 全局调度，也未解决多个物理 tick、渲染帧、URO 和同帧 worker 门控的整链等价。

新原生启动快照显示 Main Initialization=0、CachedBones=1，Provider 的初始阶段因实际访问而异，逐函数隐藏实例可仍为未初始化；现有阶段控制器使用受控 seed，不能据本批结果宣称其绝对启动计数已对齐。Provider Evaluation 的完整阶段历史仍需在真实求值入口统一接入。下一步按本批联合快照处理自然初始化/绑定与全局执行帧，再接后续整图重初始化、RequiredBones/LOD 和 Rig Construction。

任意重复调用、self scalar、部分绑定、其它 Provider、非零 Aiming 原生参数传播、全部 Source/Foot/Leg 私有字段、字段34/47及多组32/47、新ALS默认完整native、UE/Jolt314/1680物理差异、复杂地形/近景/GPU/全量/十分钟/性能仍开放。音频、道具物理与头颈继续暂缓。没有修改原项目/引擎源码配置、提交或推送。
