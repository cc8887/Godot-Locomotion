# Movement Parent 的 Pivot 与换髋回调

在 `.` 的 `main` 实现，承接 `10a4966`。保留用户已有修改。

## 本批实现

原本 Details/Direction 遍历只收集回调命令。本批增加 `AlsRefactoredMovementParentRuntime`，在原 OnBecomeRelevant 调用点、子源更新之前执行 ResetPivot / SetHipsDirection。Details 与延迟 Movement→Direction 遍历可共享同一个候选 Parent；原命令列表继续供审计。旧调用者不传 Parent 时保持原收集行为。

- ResetPivot 清除 PivotActive；SetHipsDirection 直接赋值，不额外添加插值或延迟。
- Hips 枚举保持 UE 的 Forward、Backward、LeftForward、LeftBackward、RightForward、RightBackward 顺序，不使用方向状态机的状态编号。
- ActivatePivot 使用未按 mesh scale 修正的 cm/s 速度，严格比较 `speed < threshold`。等于或高于阈值会清除激活状态；阈值由调用者提供，尚未声称设置资产已完整接通。
- Parent 使用 Prepare / ValidateCommit / Commit / Cancel。角色、generation、帧及 catalog 校验；回调必须完整匹配冻结原表，拒绝伪造节点/源/阶段/方向以及本批尚不支持的刷新函数。
- 所有更改只进入候选。宿主仍须在任一参与者失败时取消整帧，并在提交前验证全部参与者；Parent 不自动提交其他对象。

依据为本地 ALS `AlsAnimationInstance.h` 的 SetHipsDirection / ResetPivot、`AlsAnimationInstance.cpp` 的 ActivatePivot，以及 `AlsGroundedState.h` 枚举。没有修改或构建 UE 插件，也没有新导出。

## 验证

- 目标测试 9 项通过，含新增 4 项。产物：`artifacts/verification/2026-09-25-movement-parent/parent-target.trx`。
- 新连续场景 30/60/120 Hz，共 840 个提交帧，每帧取消后重试：真实 Details 状态机、共享源时钟、Direction 延迟遍历和 Parent。一次激活仅进入一次 First Pivot，不进入 Second Pivot；保持多个 Pivot 帧后由实际源时间自动回到 Run。
- 同帧 ResetPivot 在延迟 Movement 更新前已清除激活；换髋结果等于实际更新顺序中最后一个有效回调。逐帧重试的 Parent 状态及全部 source 输入严格一致。
- 六种方向与原枚举序号、严格速度边界、重复激活赋值、初始化取消、外角色/catalog、伪造回调和非有限速度拒绝均覆盖。失败后的 Parent 禁止提交。
- Godot Optimize 构建成功，0 警告、0 错误；diff whitespace 检查通过。
- 相关回归 15 项通过，包含回调 relevance、Details 源生命周期，以及 Standing/Crouching 三频率的 6 项已有 UE 方向移动对照；产物 `parent-related.trx`。没有修改原生对照容差。本批测试无失败。

## 边界与后续

本批只接通 Parent 的两个状态字段。Pivot 激活在测试中显式调用，尚未接真实 Notify；Grounded/Standing/Crouching movement refresh、SprintTime、VelocityBlend、Lean、步幅/播放速率及 yaw 曲线尚需完整 Parent 集成。已有曲线 nativeText 仅六位小数，下一步先补全精度设置/曲线导出，再实现这些刷新函数和新的 Refactored Movement 整链 UE 连续对照。

外层 cache66 / Standing 主状态机、统一角色宿主、普通 Demo 切换、Ragdoll/Get-up/Pose Recovery 和十分钟性能验收仍未完成。未运行 Godot 普通场景、人工录像或全量测试，本批不能作为可见动作已经修好的证明。音频、道具物理、头颈排查继续暂缓。
