# Standing Movement 的 Lean / PoseMoving 外层

在 `D:/GodotALS` 的 `main` 实现；上一批真实 Details 源时间提交为 `bfc6736`。本批不修改普通 Demo 入口，不覆盖用户现有修改。

## 实现与原始依据

- 读取冻结的 AB_Als_Standing 原始图，交叉校验 authored 连线与 compiled property identity：Movement67 → ModifyCurve122 → ApplyAdditive120；基础方向机器201、Lean evaluator202。
- Lean 使用 BS_Als_Lean 的实际 local-additive 资产、三角形、过滤参数；X=LeanState.RightAmount，Y=LeanState.ForwardAmount；normalizedTime=0、teleport=true、DoNotSync，不创建新的移动播放器时钟。
- Alpha 原绑定 PoseState.UnweightedGaitRunningAmount，先限制 [.5,1]，增加20/减少1插值。逐帧历史可撤销、重置与重试。
- 接收冻结骨骼/曲线布局的基础姿态，执行 LocalApply 与 additive 曲线累加，最后用原 ModifyCurve/Blend 算法写 PoseMoving=1；其他基础曲线保留。
- 本地引擎 `AnimNode_ApplyAdditive.cpp` 的 Initialize/Update/Evaluate 用于核对初始化、Alpha、local-additive 顺序。没有改动或构建 UE 插件。

## 验证

- 新增4项：30/60/120 Hz 各3秒，共630帧、79骨；升降权重、零delta、初始化、每帧Cancel/retry、独立owner一致、错误Evaluate后禁止提交、修正输入后重试。
- 10类资源变异拒绝：base接线、PoseMoving数值、插值速度、采样时间、sync、teleport、资产、Alpha绑定、Lean轴、回调。
- 首次4项失败：authored方向输入经过K2Node_Knot重路由，直接Follow只到中间节点。改为已有FollowReroutes（保留双向连线验证），没有改资产/算法/门槛。`initial.trx` 保留，`reroutes.trx` 4通过。
- 相关回归22通过：本批4、Details source5、Blend filter2、Direction pose11；`artifacts/refactored-movement-cache/related.trx`。
- `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0警告、0错误。

## 范围与后续

本批测试基础输入是79骨identity姿态，Lean来自真实资产；证明外层连续历史、布局、曲线及失败恢复，不等于真实移动整图原生pose对照。无新UE参考导出、Godot场景运行、全量或十分钟性能验证。

当前owner要求Evaluate后才能Commit；尚未支持下游跳过求值时的update-only提交。它尚未承担外层共享cache调度/上下文权重传递/初始化去重，也不消费Details的ResetPivot或惯性化请求。下一步需统一Movement缓存与Details状态姿态合成、共享调度和惯性化，再进行原生连续对照；不能把两个独立缓存闭包依次Drain冒充整图顺序。

Standing/Crouching其余机器、真实Parent、统一角色宿主及Ragdoll/Get-up整体验收仍待完成；普通Demo未切换。道具物理、音频、头颈排查继续暂缓。
