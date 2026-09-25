# Standing 待机与原地旋转姿态分支

本批直接在 `D:/GodotALS` 的 `main` 实现。普通 Demo 尚未切到 Refactored 完整图，不能把本批单元与集成测试解释为视觉验收。

## 原图与实现

`AlsRefactoredStandingRestGraph` 校验 Standing 三个状态内的十四个节点、原生与编辑图源链接、闭包、回调与动态绑定：

- Idle：64 → 57 RefreshDynamicTransitions → 59 InitializeTurnInPlace（首次相关）→ 58 RefreshTurnInPlace → 62 Scale → 60 TurnInPlaceStanding Slot → 63 ModifyCurve → 61 Stand Pose 固定帧零。
- Rotate Left：14 → 13 Scale → 12 原左旋转播放器。
- Rotate Right：11 → 10 Scale → 9 原右旋转播放器。

Idle 的三条锁定/许可曲线 FootLeftLock、FootRightLock、AllowTransitions 在 Slot 之前设为 1；原模板值为 0，以编译值与可见引脚交叉验证。Slot 不强制更新被完全遮盖的源。Slot 后 RotationYawSpeed 绑定 TurnInPlaceState.PlayRate；左右旋转绑定 RotateInPlaceState.PlayRate。既有 RotatePlayers 继续校验独立播放器身份、动态循环及 DoNotSync。

`AlsRefactoredStandingRestPose` 绑定原 Skeleton 曲线 metadata 门禁和三条实际动画的共同 79 骨布局，冻结待机固定帧姿态。接口明确分开：

- SampleIdleSource 输出 node63，供真实 Slot 混合。
- FinishIdleSlot 接受调用者已经求值的 Slot 姿态/同布局曲线，仅缩放转向曲线，不重新锁脚或恢复许可值。
- BindRotate 固定到实际 SourcePlayerRuntime owner 与两个播放器身份；Sample 校验候选帧且要求已 Evaluate，使用实际播放器输出。曲线映射预计算，保留额外 Slot 曲线及不存在状态。

本机 `AnimNode_ModifyCurve.cpp` 在 Scale 后仍执行 `Lerp(Current, Current*Scale, 1)`。本批保留该 float 运算次序，并在输入缺失时写入存在的零曲线。非法布局、非有限输入、未求值与错误帧拒绝输出；Idle 支持原地缓冲区操作，采样输出不能改动冻结资源。

本批仅编译 Idle 三个回调的身份与顺序，**没有实现这些 Parent 函数的算法或执行动作**。Slot 混合仍是显式宿主边界，不用假装空 Slot 的快捷路径代替实际转身动作。

## 验证

日志：`artifacts/tests/standing-rest`。

- `standing-rest-initial.trx`：新增 12 项全部通过，无首次失败。八项资源变异拒绝；待机固定帧与曲线顺序/存在性/失败不污染；三频率旋转连续采样。
- 30/60/120 Hz 共 840 候选帧、每帧两个独立旋转身份，共 132720 骨姿态检查；覆盖循环开关、停止循环、负速率、重新初始化、非零播放器偏移、未 Evaluate/错误帧拒绝。直接按实际时钟重新采样原动画逐值比较，并逐帧 Cancel 后重试。
- `standing-rest-related.trx`：49 项通过、0 失败、0 跳过，包括新12、旧 Rotate、Standing、共享 StandingMovement 遍历、StopEvaluation、StanceCallback。
- Import Release 编译和 `dotnet build GodotALS.csproj -p:Optimize=true --no-restore` 均为 0 警告、0 错误。

Slot 曲线顺序测试使用受控已求值输出，未运行真实转身 Montage；三频率测试不是 UE 连续整图 oracle。本批没有新 UE 导出、Godot 渲染、多帧截图、全量测试或十分钟性能验收。

## 后续

仍需 Idle/Rotate 状态源遍历与 Parent 原生刷新、真实 TurnInPlaceStanding Slot 动作消费、完整 Standing65 姿态混合和外118惯性化，再进行 UE 连续整图对照与统一角色宿主/普通 Demo 接入。既有 StopQuick/停止回调、Lean update-only、其他 stance、Ragdoll/Get-up、相机与性能目标均保留。音频、道具物理、头颈专项仍暂缓；未纳入用户现有修改与诊断文件。
