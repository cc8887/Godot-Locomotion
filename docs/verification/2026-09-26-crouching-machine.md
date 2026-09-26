# Refactored Crouching 外层状态机

在主目录 main，基线 `1d7ff88` 上实施。没有新建项目副本，没有修改导出 JSON。本批补齐真实 Grounded 上游所缺的 Crouching 外层状态执行基础；没有宣称 Grounded 或完整蹲伏姿态已经接通。

## 原图检查与实现

原 Grounded 包含 Standing、Crouching、两条专用站蹲过渡序列及 Roll 基底，还有缓存、回调、惯性和曲线写入，不能简化为一个站蹲权重。现有 catalog 有它的编译节点和编辑图，但 `refactored_stance_machines` 只导出了 Standing/Crouching 的 baked machines。本批先使用已有完整资料实现 Crouching，Grounded 的 baked machine 与新连续原生对照仍待导出。

新增 `AlsRefactoredCrouchingResources`，同时核对原编译/编辑图、baked states/transitions、资源目录哈希、播放器身份、规则表达式与成员函数绑定。原节点为 machine33、外层 inertia34，五状态依次是 Idle、Move、Rotate Left、Rotate Right、Stop；不能沿用 Standing 的状态下标。共享表达式解析器验证两套原图使用的相同布尔表达式，运行时采用独立 Crouching 输入和规则域，Stop 权重读取 state4。

| 原行为 | 本批保留的语义 |
|---|---|
| Idle 出口 | Move 优先，然后右转、左转；保留原 baked 次序及编辑优先级 |
| 完整 Move 停止 | 0.1 秒混入 Stop；进入 Stop 生成 PlayStopTransitionAnimation 回调候选 |
| 尚未完整进入 Move 就停止 | 0.2 秒回 Idle，生成原 notify0 StopQuick |
| Stop 退出 | 上一更新记录的 Stop 权重达到 1 后，0.5 秒 QuickFeet 回 Idle |
| 转身时移动 | 0.4 秒回 Idle，然后同次更新进入 Move；保留回调顺序 |
| 转身换向 | 0.2 秒惯性请求；每帧最多三次状态切换 |
| 转身自动退出 | 使用独立左右原动画播放器、前次权重和物理时钟，动态 loop 策略沿用原绑定 |

`AlsRefactoredCrouchingRuntime` 复用已有通用状态转换栈算法，独立管理 candidate/commit/cancel。状态通知、状态进入/退出函数、惯性请求只作为候选输出；本批没有调用主线程副作用或新建调度器。隐藏后重入通过 traversal counter 间断重新初始化，显式重置、失败输入和错误提交不得覆盖已提交状态。

QuickFeet 保留原 79 骨父序、18 项权重条目、33 组既有原生权重样本，并检查 native 容差 2e-6；Stop→Idle 的边标记带入状态定义。该资源尚未应用到新 Crouching 最终骨骼输出，不能把资源校验称为蹲伏姿态混合验收。外层 node34 的过滤策略（RotationYawSpeed）已校验，实际接收惯性请求仍属后续 pose host 工作。

## 验证结果

测试均在主目录执行，Release，`DOTNET_TieredCompilation=0`。记录保留在 ignored `artifacts/tests/crouching-machine/`。

- `crouching-initial.trx`：6 通过、5 失败。Stop 的 authored Node 模板是 OnStateEntry，而实际成员及 compiled runtime 是 PlayStopTransitionAnimation；修正为分别校验两者，并验证编辑成员引用。
- `crouching-callback-binding.trx`：10 通过、1 失败。测试把左右同时请求的预期写成单条边；原图实际上受 maxTransitions=3 限制，依次为 1→10→7。修正测试，未改变生产切换规则。
- `crouching-related.trx`：56 通过、0 失败、0 跳过，其中 Crouching15、Standing14、StandingHost7、StandingHostNative6、CharacterAction14。Standing 原生独立/共享三频率共 4620 帧仍通过既定严格门槛。
- `crouching-core.trx`：共享 Grounded/TransitionStack 等相关 81 通过、0 失败、0 跳过。
- 增强 stateIndex/name 和编辑 PriorityOrder 门禁后，`crouching-final.trx`：Crouching16 通过、0 失败、0 跳过。
- 最终 `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`：0 警告、0 错误；`git diff --check` 通过。

Crouching 连续测试在 30/60/120 Hz 各运行 8 秒，共 1680 帧，并逐帧取消重试，比较状态、回调候选和真实播放器历史。覆盖全部五状态、自动退出、惯性请求、停止前帧权重延迟、部分 Move 的 QuickStop、隐藏重入、显式重置、错误播放器/NaN/越界时间和跨规则域拒绝。另有十类图/资源变异拒绝。此测试是受控组件验证，**没有新的 Crouching UE 连续 pose oracle**；既有 QuickFeet 样本和 Standing 原生回归不能替代它。

没有运行全量 solution 测试，没有修改/启动 UE，没有运行 Godot 或进行多帧画面、人工和性能验收。

## 后续接入

下一步补 Crouching Idle/Stop/Movement 的实际姿态源、缓存遍历、共享 Parent 回调和 node34 惯性接收，组合成可参与统一事务的 pose host。角色动作协调器当前仍要求 Standing 更新，必须按真实相关性支持隐藏 Standing/Crouching；不能靠每帧伪更新两个子图维持提交。

随后补 Grounded 的 baked 状态资料和专用站蹲序列，接 Grounded→Transition→Locomotion，再捕获完整原生连续轨迹。普通 Demo 本批仍使用旧链路；全部 Ragdoll/Get-up/Pose Recovery 等目标保留，音频、道具物理和头颈专项继续暂缓。
