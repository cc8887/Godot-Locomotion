# Standing 统一运行时

在主目录 `D:/GodotALS` 的 main 中新增 `AlsRefactoredStandingHostProfile` 和 `AlsRefactoredStandingHost`。此前完整链路的组装主要存在于测试夹具；现在生产入口统一拥有 Standing 子图、Parent 候选、播放器、动作 bank 和提交生命周期。

## 实现范围

- 冻结 profile 校验既有导出资源并组合 Standing、Movement Details/Direction、Lean、Stop、Idle/Rotate、惯性化及 node68 输出。各角色运行时独立持有可变历史；原始 79 骨骼布局和厘米单位保持。
- Prepare 先冻结物理 Montage 快照，再执行 Parent、root203、Standing65 和原序状态更新。Idle 进入 57/59/58 回调及 Slot60；Stop 的本地绑定取值发生在延迟 cache66 回调之前。
- 同一延迟遍历推进 cache66/67 和方向缓存；统一准备 Movement/Rotate 播放器、捕获自动退出时间，再准备 119/118 惯性化。清理和回存 Rotate 的观察权重由宿主负责。
- Evaluate 从真实播放器取 Direction、Lean、Details、Stop 与 Rotate 姿态；Idle 读取真实 Turn Slot；五状态合成后经过 118，最后 node68 写入 PoseStanding。重复求值重新收集候选姿态，不推进时钟。
- PostUpdateActions 按 Transition play → Turn play → Stop 消费 Parent 队列，随后派发原 Standing StopQuick 通知。新实例只进入候选 bank，当前帧已冻结姿态快照不变。重复后处理被拒绝。
- 支持整帧 update-only：仍推进状态、播放器、Parent、动作和滤波，提交后不提供陈旧姿态。
- 统一 ValidateCommit 在发布之前核验全部实际参与者；按依赖顺序 Commit。Prepare、Evaluate、后处理异常自动 Cancel 所有候选。错误身份、缺失后处理、重复帧和重复阶段调用被拒绝。

调用顺序为 `Prepare(context, input, initializationCounter)` → 可选 `Evaluate(component)` → 等待工作线程完成 → `PostUpdateActions()` → 读取候选输出并 `ValidateCommit(identity)` → `Commit(identity)`。每个运行时必须独占调用，不允许阶段间并发。当前没有新增线程调度器或 Godot 场景入口。

## 验证结果

1. 首轮新增 4 项全部通过：`artifacts/tests/standing-host/standing-host-initial.trx`。
2. 补充后处理故障及初始化/counter 间断覆盖后，相关 57 项全部通过、0 失败、0 跳过：`artifacts/tests/standing-host/standing-host-related.trx`。
3. Godot Optimize 构建通过，0 警告、0 错误。

三频率 30/60/120 Hz 各 11 秒，共 2310 个提交帧身份：覆盖全部五状态、真实 Turn/Transition bank、QuickStop、动态调整、移动缓存、原地旋转、周期性连续三帧跳过求值、初始化及 counter 间断。每帧在后处理后取消重试，姿态/曲线/缓存/Parent/动作候选严格重现；与另一同输入运行时比较，并验证跳过求值与全求值运行时的状态及动作一致。所有求值骨骼验证有限有效、PoseStanding=1；尚未用完整原生图轨迹校验这些姿态。

额外覆盖：在 Rest 校验前已有其他 owner 准备的故障、姿态求值时无效 component、后处理已修改候选 bank 后 QuickStop 参数非法。失败后已提交状态和动作保持不变，同帧有效重试与干净运行时一致。

相关回归包括旧 StandingMovementTraversal、RestMontage、QuickStop、MovementCache。未重跑全量、UE 导出、Godot 运行或性能测试；未放宽既有容差，本批没有测试失败。

## 尚未完成的边界

这是 Standing 子图宿主，输出到 node68；不能称完整角色宿主或普通 Demo 完成。内部 18 个 Montage 资源使用本宿主的局部 ID 和 Grounded group，仍需在完整角色中与其他动作共享绑定。当前只在 Idle 混合 TurnInPlaceStanding Slot；Stop/Dynamic/QuickStop 的播放时钟和队列已统一，但它们的 Transition Slot 位于角色外层，尚未叠加进这个 Standing 输出。不能把候选 bank 有实例当成最终画面有对应动作。

输入中的移动/Rest/既有曲线和脚部位置由上层提供；仍需接完整角色 Parent/曲线反馈、通用源 Notify、其他 Overlay 与动作。`ActivatePivot` 目前是明确输入，尚不是自动消费完整源通知。

下一步对统一宿主做完整原生连续对照，并接角色外层 Transition Slot、Crouching、共享 Montage/Overlay/层级与 Godot 适配，再切普通 Demo 进行键鼠和多帧画面验收。Ragdoll/Get-up/Pose Recovery、完整相机与最终十分钟预算等总目标继续保留；道具物理、音频和头颈调查仍暂缓。用户已有修改及未跟踪诊断产物不属于本次提交。
