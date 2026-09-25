# Rest 与动作共用 Transition 请求队列

新增 Rest Parent 的共享队列绑定模式：构造时校验同一 settings、Montage 资源，逐帧绑定具体 bank/queue/角色/generation/frame；Dynamic 回调在实际执行位置直接写入 `AlsTransitionQueueRuntime`。此模式下 Parent 不再保存第二份 QueuedTransition。

Stop 状态回调、Dynamic 回调、武器过渡现在可以向同一个线程候选队列写入。最后一次有效调用覆盖之前请求；StandingIdleOnly 拒绝的武器请求不覆盖已有请求。Dynamic 自身两帧延迟仍由 Parent 维护，不因请求被覆盖而撤销。

新增 `AlsRefactoredRestMontages.PostUpdate`，按原 NativePostUpdate 顺序消费共享 Transition → Turn → Stop。停止标记存在时保留 Transition 与 Turn 请求，清除 Stop 后允许后续帧消费。Turn 的曲线播放速率仍只在真实播放被接受后更新。

共享模式拒绝调用旧的独立 `PlayQueued` 路径，避免宿主误把 Dynamic 延迟到帧末再覆盖别人的请求。独立模式仍供已有单模块测试使用。完整 Standing 180帧测试已经切到共享模式，而不是仅测试新增接口。

## 验证范围

- 四组真实 Dynamic/Stop 回调先后顺序 × StopQueued 开关测试，覆盖按顺序覆盖、Transition先于Turn的实例顺序、停止阻塞后次帧恢复、候选取消重试和旧入口拒绝。
- 实际武器资源的主线程通知：停止消费后，拒绝的 idle-only 请求保留 Dynamic；允许的通知覆盖并立即播放；下一帧不回放旧 Dynamic。
- Standing 真实转身→Idle Slot→65→118→68 测试使用同一队列生命周期。
- 本批无 UE 插件/配置修改、Editor运行、新导出或 DataValidation。只读核对本地原始 NativePostUpdate/RefreshDynamicTransitions/PlayQueued/StopQueued C++。
- 首四组顺序测试4通过；Rest/Standing/Transition相关112通过；补充 Apply 入口队列身份检查后最终新5通过，0失败。队列被提前丢弃时，Dynamic回调拒绝且不吞掉本帧刷新机会；恢复同帧队列后可正常请求。
- TRX 位于 `artifacts/tests/standing-actions/`：`rest-shared-queue-initial.trx`、`rest-shared-queue-related.trx`、`rest-shared-queue-final.trx`。本批无测试失败，无容差或跳过调整。
- 最终 `dotnet build GodotALS.csproj -p:Optimize=true --no-restore -v minimal` 0警告0错误；`git diff --check`通过。

## 后续

共享队列模式已提供并在上述链路验证，完整角色宿主仍需采用它。StopQuick 的主线程通知和原设置、UE 连续 Parent/Slot 动作对照、Crouching、普通 Demo 切换、视觉和性能仍未完成。所有 Ragdoll/Get-up 等原目标继续保留；用户未提交改动与暂缓项未修改。
