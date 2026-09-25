# Standing 五状态姿态合成与外层惯性化

在主目录 `.` 的 `main` 继续实施。普通 Demo 尚未切换，完整移植目标仍未完成。

## 实现

`AlsRefactoredStandingPose` 将原 Standing65 的 Idle、Move、Stop、Rotate Left、Rotate Right 接到同一候选帧。资源校验包含 catalog、79骨名称/父级布局，曲线按名称预计算映射并保留存在性。

Begin 从真实 Standing 候选状态机确定本帧姿态求值所需的状态集合。Idle 必须提供已经过 Slot 的输入；Move 直接读取本帧119惯性化输出；Stop 调用既有完整腿层/曲线采样；Rotate 绑定实际播放器并要求完成本帧 Evaluate。重复或隐藏状态提交、缺少必需状态、外来 owner/帧/角色代际、取消后遗留的就绪状态均被拒绝。宿主仍负责所有源的 Update 顺序及统一提交。

合成沿原标准过渡栈顺序逐边 BlendRaw，最终统一归一化旋转；曲线执行 Scale + Accumulate。重复出现在栈中的状态只采集一次。左右 Rotate 的惯性切换已在状态更新阶段完成，不做第二次普通渐变。本批还没有把 Idle 的 Parent 回调/动作执行接进源遍历。

`AlsRefactoredStandingInertialization` 实现原118：

- 校验原编译和编辑接线118→65、默认 profile、相关性重置、跳过缓存转发、无骨过滤和唯一 RotationYawSpeed 过滤。
- 真实 Standing 切换请求与119转发到118的请求合并去重；其它目标继续暴露给宿主；66 skipped 上下文使用118的请求队列进行转发，同节点请求为去重无操作。
- 候选/已提交/求值后三份状态隔离。Update-only 保留请求和累计时间；初始化与相关性中断遵循既有原生语义；Evaluate 失败禁止提交，Cancel 后可重试；重复 Evaluate 从同一 Update 候选重算。
- 原生 `Engine/Private/Animation/AnimNode_Inertialization.cpp` 在差分中移除过滤曲线，并在输出中直接取目的值和存在性。实现将固定的 RotationYawSpeed 通道排除在独立曲线历史之外，求值后复制目的通道，避免重新生成已经缺失的过滤曲线。其它骨骼/曲线使用已有全精度 Core 求值。

共享 StandingMovement 遍历新增精确上下文校验；完整 Standing pose 新增身份校验，供118核对候选来源。

## 验证

记录：`artifacts/tests/standing-pose`。

- `standing-pose-initial.trx`：扩展后的三频率完整 Standing pose 集成通过，首次3/3。
- 新增生命周期测试首次编译误对只读 AlsFrameIdentity 属性使用 `with`；改为显式构造身份，随后 `standing-pose-lifecycle.trx` 1/1通过。这是测试代码编译错误，未修改生产策略或断言标准。
- `standing-pose-outer.trx`：新增生命周期、既有缓存生命周期及外118三频率整链，5/5通过。
- 最终 `standing-pose-related.trx`：50项通过、0失败、0跳过。包含 Standing/Rest/共享遍历、Rotate、StopEvaluation、StanceCallback。
- Import Release 编译、Godot Optimize 构建均0警告/0错误。

三频率整链扩为每场景八秒，30/60/120 Hz 共1680候选帧、132720骨输出，逐帧 Cancel/重试并重复 Evaluate。使用真实 Parent→Direction→Lean→Details→119→Stop，以及真实待机和左右旋转动画。覆盖五状态、重叠过渡栈、原始单状态输出逐值一致、旋转惯性切换不双重混合、实际平滑后的骨姿态与输入不同、Yaw曲线逐值直通。

旋转切换后连续三帧只 Update 外118，断言请求一直保留；恢复 Evaluate 后平滑继续。注入非法 component 导致求值失败，禁止提交，重试输出逐值恢复。更新 counter 中断后历史重置为单帧。

限制：Idle 使用实际待机姿态作为空 Slot 输出，未播放真实转身 Montage；Parent 的 DynamicTransitions/Turn/Rotate 更新仍未接入。测试不是 UE 连续整图 oracle，没有新 UE 导出、Godot 渲染/截图、全量或十分钟性能验收。119→118及其它祖先转发有生产接线，但本批没有新增跨节点转发专用 native 对照，不能据此宣称全部转发场景验证完成。

## 下一步

继续原 Idle/Rotate 源遍历、Parent 动态过渡与转身刷新、TurnInPlaceStanding Slot 和 StopQuick/停止回调实际动作消费；随后做 UE 连续 Standing 整图对照，接统一角色宿主与普通 Demo。Lean update-only、其他 stance、Ragdoll/Get-up/Pose Recovery、相机及最终性能目标仍保留。音频、道具物理、头颈专项继续暂缓；用户现有修改未纳入提交。
