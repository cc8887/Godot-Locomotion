# Refactored Standing 外层状态与移动入口

本批直接在 `D:/GodotALS` 的 main 实现。普通 Demo 仍使用原入口；本批没有启动 Godot 场景，也不代表角色观感已验收。

## 实现范围

- 从现有原始 authored/compiled 资源编译 Standing65 的 Idle、Move、Stop、Rotate Left、Rotate Right 五状态及十二条有序转换，交叉校验规则连接、状态/播放器身份、回调、通知、混合时间与策略。
- 规则保留上一帧记录的 Move/Stop 权重，区分未完全进入移动时的快速停止与完整 Stop。旋转自动退出读取实际播放器时间、长度、循环信息，不另建计时器。
- 复用共享状态机，引入独立 Standing 输入域；记录同帧全部转换边，生成按 exit→entry 排列的回调命令。StopQuick 保留局部通知索引 1，旋转换向保留惯性化请求。命令和通知尚未连接实际动作播放器。
- cache66 的源入口封装原回调链：121 RefreshGroundedMovement → 142 InitializeStandingMovement（首次相关）→ 143 RefreshStandingMovement。Begin 在子源更新前执行候选 Parent；Complete 在子源更新后反向 Leave，未 Complete 不可提交。宿主仍负责所有参与者的统一取消/提交。
- 已在 90 帧真实 Parent→Direction→Lean 姿态集成中替换手动刷新，并验证 79 骨输出。

## 验证

运行于主目录，测试使用 Release。记录位于 `artifacts/refactored-standing`。

| 检查 | 结果 |
| --- | --- |
| Standing 资源、八种资源变异、连续运行和入口生命周期 | 14 项通过（包含在下列 36 项中） |
| Standing / Movement traversal / Direction native / Movement Details runtime | 36 项通过，standing-final.trx |
| 共享 Grounded 状态机 / Transition stack / Refactored 相关 Core | 39 项通过，core-related.trx |
| 新入口接真实移动与姿态链 | 1 项通过，entry-integration.trx，90 帧 |
| Godot Optimize 构建 | 0 警告、0 错误 |

Standing 连续测试在 30/60/120 Hz 共运行 1680 帧，每帧取消重试；使用实际旋转源播放器时钟，覆盖五状态、惯性化和自动退出。此测试没有覆盖播放器跨循环边界的 PreviousValid 路径，也不是新导出的 UE 外层整图 oracle。原有 Direction native 回归仍通过，未放宽容差。

最初资源测试曾失败，分别定位为 authored 模板 OnStateEntry 与编译后实际函数不同、GreaterEqual 带未连接 ErrorTolerance 元数据引脚、推测的缓存入口回调顺序错误。已按原资源/接线修正，失败 TRX 保留（standing-resources、standing-resources-second、standing-resources-third）。随后 standing-runtime 的 13 项、最终含入口的相关测试及集成通过。

没有改动 UE 插件、重新导出资源或运行新 UE oracle；没有全量测试、性能验收或普通 Demo 切换。

## 下一阶段

1. 编译 Stop53 内部状态与姿态源，接 Idle/Rotate 状态源及真实相关性观察。
2. 把 cache66、Movement67 与方向缓存纳入一次有序调度；接外层118惯性化和跨节点请求。
3. 将状态回调与 StopQuick 通知接实际动作播放，再进行真实 Parent 驱动的 UE 连续整图姿态对照。
4. 统一角色宿主与普通 Demo 接入后，再验收 Ragdoll/Get-up、动作衔接和最终性能。原物理与其他未完成项继续保留；道具物理、音频及头颈专项仍暂缓。

用户已有的 project.godot、规划文档、AlsLayerBlendingRuntime 和未跟踪诊断/uid 文件未纳入本批。
