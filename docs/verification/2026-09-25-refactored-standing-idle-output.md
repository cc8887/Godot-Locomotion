# Standing Idle Slot 更新规则与最终输出

## 改动

新增 `AlsRefactoredStandingIdleSlot`，将原 Slot60 的更新与求值规则接入真实 Rest Montage bank：

- 复用 `AlsSlotSourceUpdate`，使用上一提交帧的 SourceWeight 判断源是否退出，并传递原始状态祖先、惯性请求者、RootMotionWeight 和按比例计算的源权重。
- 非 additive 转身占满 Slot 后，不更新/求值 source63/61；有源权重时才采样固定待机姿态和原三条曲线。
- 支持显式初始化时清零权重历史、update-only 提交、重复求值、取消重试和失效快照/角色校验。
- 姿态仍由共享 Slot mixer 在物理 Montage Position 上计算；不新增播放时钟。

新增 `AlsRefactoredStandingOutput`，严格绑定原节点68的 compiled/runtime/authored/native 连线和常量。在118惯性化之后执行原 Blend 模式 `PoseStanding=1`，不修改惯性历史、其他曲线或骨骼。输出要求匹配具体 profile、惯性 owner、frame、角色和 generation。

真实 Standing 转身退出测试已使用新 Idle Slot，并沿 Slot60 → machine65 → inertia118 → curve68 求值。Parent Idle 回调和惯性请求仍在 Slot 源被裁剪之前执行；原地转身结束请求不受 SourceUpdate=false 的影响。

## 证据

- 只读核对本地 `../UnrealEngine/Engine/Source/Runtime/AnimGraphRuntime/Private/AnimNodes/AnimNode_Slot.cpp`，并读取现有哈希绑定的 Standing 原始图资源。无新 UE 导出、Editor 启动、插件修改或 DataValidation。
- `standing-idle-slot.trx`：10 通过，0 失败。
- `standing-output.trx`：真实180帧输出测试1通过。
- `standing-final-related.trx`：Rest/Standing 相关66通过，0失败。包括七种原节点政策变异拒绝。
- 最终补充骨架父级校验后 `standing-idle-slot-final.trx`：3通过。30/60/120 Hz 共630帧，覆盖源裁剪、退出标记、update-only、初始化、取消重试和原 Slot mixer 对照。
- 以上 TRX 位于 `artifacts/tests/rest-montage/`。末次三项测试构建曾等待仍在运行的相关测试释放 DLL，产生 MSB3026 复制重试警告，随后自行成功，未重启或中断测试进程。
- Godot Optimize 构建通过，0警告、0错误；diff whitespace 检查通过。

## 后续

尚无 UE 连续 Rest Parent/Slot/惯性退出整链 oracle。本批以原源码/原图校验和本地真实资源测试为证据，不能声称完全1:1原生整图对齐。

仍需停止回调/通知的实际动作消费、完整 Crouching、统一角色宿主和普通 Demo 切换，随后进行 Godot 多帧视觉、完整 ALS Camera/Ragdoll/Get-up 等既定目标验收与十分钟性能测试。原计划的其他缺口继续保留；音频、道具物理、头颈诊断暂缓，用户未提交文件未纳入本批。
