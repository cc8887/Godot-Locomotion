# Refactored 起步／Pivot 源更新与真实时间反馈

## 已实现

新增 `AlsRefactoredMovementDetailsSourceRuntime`，按已验证的六状态内部图和共享状态机候选执行状态源遍历。它不推进私有时钟：所有十六个 SequencePlayer 仍使用已有 `AlsRefactoredSourcePlayerRuntime` 的角色共享 Sync batch。

更新输出包括：

- 每次状态初始化对 Movement 缓存的读取节点身份；不在此局部 owner 中合并初始化，统一缓存 owner 后续按完整初始化 counter 去重。
- 每个实际状态更新对共享 Movement 67 的独立读取上下文，包括原状态祖先链、权重、RootMotionWeight、Inactive、惯性化 requester/skipped-handler 和同步标记。
- 基于原归一化 VelocityBlend 的加速源请求，原播放器身份、起点、速率、组政策不变。局部通道权重相关时，即使外层状态权重为零也保留该更新。ApplyAdditive 的基础缓存读取先于加法源请求。
- 两个 Pivot CallFunction 的候选 ResetPivot 命令；使用已有回调 owner 保留 Enter→Source→Leave 的相关性，实际 Parent 字段修改尚未接入。

未参与本帧更新的直接播放器保留其节点时间和缓存权重；SetState 的清权重掩码会清除目标状态的所有观察权重，真正 Initialize 的节点另重置其初始时间，并保留 pending reset，直到该通道再次进入实际 tick。整个 candidate 取消时，这些历史不会发布。

`CaptureSourceTimes` 在角色共享 Sync batch 完成后读取真实时间，核对 catalog、player owner、节点身份、资产、Weight、PlayRate、非循环政策、惯性化标记，以及漏失或额外的本子图 tick。成功捕获才能 Commit；失败后保持不完整，允许修正本帧 batch 后重试。提交后的观察按原 baked player 顺序供下一帧状态机选择相关播放器，自动退出不再由测试手工填入动画结束时间。

本子图当前全部源都是原非循环 Sequence，观察不捏造循环 DeltaTimeRecord；后续若资源变成循环会拒绝，不静默套用缺失的环绕历史。

## 验证

`artifacts/refactored-movement-details-sources/initial.trx`：新增 5 项首轮通过。

`final.trx`：18 项通过（新源更新 5、Details 连续状态 6、共享 SourcePlayer 3、stance 回调 4）。

- 30/60/120 Hz，每场景六秒，总计 1260 提交帧。每帧先运行真实共享 Sync、采样参与源的 79 骨姿态、捕获时间，再取消并重复，源请求、上下文、缓存读者、回调、初始化、Sync 历史和下一帧观察相同。
- 实际十六个播放器全覆盖，使用非零全局 ID 偏移 7；每场景至少三次自动返回 Run，明确检查被选中的上一轮真实源时间等于实际资产长度，而非状态时间。
- 覆盖两次 Pivot、普通重叠过渡、零 delta、全零通道、Inactive 路径、外层权重零、惯性化同步输入、RootMotionWeight 与外层 state/requester/skipped-handler 的保留。
- 隐藏通道到下一次 tick 才初始化；已初始化通道不重复重置。显式实例重置先取消再重试，不污染已提交观察；未 Capture 或捕获缺失／错误权重／错误惯性化标记／不同玩家 owner 时拒绝提交。

扩充覆盖后的 `related.trx` 三个频率测试失败，原因是新增断言要求零权重 tick，而原输入只制造了零通道（完全不发 tick），未制造外层零权重。补入一次局部通道相关、外层权重零的实际遍历后，所有检查通过。未放宽断言或改变生产算法；失败文件保留。

Godot Optimize 构建成功，0 warnings / 0 errors。此批没有修改旧生产算法、UE 插件或资产，没有新增 UE 连续 oracle、Godot 场景、全量或十分钟性能运行。上述真实源采样仅验证调用链与布局，不等同于合成姿态的原生精度对照。

## 尚待接通

当前 Movement cache 读者和初始化请求只是完整 stance 调度的输入，尚未与方向子图及外层读者共同 Drain；ResetPivot 仍是候选命令。完整 Movement 缓存还须补原 Lean 加法、PoseMoving 曲线及其上下文。随后接加速 MultiWay 混合、ApplyAdditive、普通过渡与惯性化 pose/curve 求值，再做 UE 连续对照。

普通 Demo 未切入完整 Refactored；Standing/Crouching/Stop、真实 Parent/Notify、统一宿主及 Ragdoll/Get-up/Pose Recovery、Mantle、Camera、最终性能等既有缺口继续保留。用户文件不动；音频、道具物理、头颈诊断暂缓。
