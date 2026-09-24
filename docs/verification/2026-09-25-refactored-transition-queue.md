# Transition 工作线程队列与停止生命周期

## 实现及原生语义

在主目录 `D:/GodotALS` 的 `main` 增加 `AlsTransitionQueueRuntime`，绑定唯一物理 Montage owner、角色与 generation。队列只保存值类型命令和候选状态；worker 不操作物理 bank，宿主须在 worker 完成后消费，所有参与者预检后一起提交，失败一起 Discard。

- worker 多次播放请求采用最后一次被门控接受的请求；移动、非精确 Standing tag 的 standing-idle-only 请求不覆盖旧请求。显式 null sequence 会覆盖旧请求。
- 主线程即时播放先更新队列再立即消费，每次可创建独立 Montage，不能把同帧重复通知合并为一条。
- 待停止标志阻止 `PlayQueued`，但不删除待播放请求。`StopQueued` 依次停止 Transition、Standing Turn、Crouching Turn，清停止标志并将时长还原为 -1，保留待播放请求；它可能在后续帧消费。
- `NativePostUpdateAnimation` 的原顺序是播放 Transition、播放 Turn、最后停止。公开分开的消费入口保留中间 Turn 阶段；没有把三个阶段误合并成一个可交换操作。
- 停止请求也最后写入覆盖；负时长沿用各 Montage 原淡出时间；已停止的淡出实例和其他 Slot 不改动。
- 显式 host command 从 `AlsRefactoredTransitionMontages.Command` 解析实际绑定；资源丢失拒绝消费并保留候选供失败处理。队列与 bank 的提交/取消属于同一宿主事务，队列不隐式提交 bank。

原生依据为本机 `UAlsAnimationInstance::PlayTransitionAnimation`、`PlayQueuedTransitionAnimation`、`StopQueuedTransitionAndTurnInPlaceAnimations` 和 `NativePostUpdateAnimation`。null sequence 时原生仍保留无效播放参数，本队列将其投影为无待播放命令；这些参数不会在没有新 sequence 的情况下被消费。

## 真实工作线程对照

扩展现有导出器，在真实线程池 worker 上调用原 ALS 播放/停止函数，主线程等待完成，再调用真实角色的 `NativePostUpdateAnimation`。没有模拟 `IsInGameThread`，没有在 worker 创建 Montage。普通主线程蓝图通知仍走原生成函数。

新轨迹覆盖四武器 × 30/60/120 Hz，共 12 组、3,360 帧、265,440 骨，96 个播放实例、292 个多实例求值帧。包含：worker 重复覆盖、主线程重复即时通知、延迟 PostUpdate 多帧、受门控拒绝、null 清除、停止与播放同帧、被停止阻止的请求次帧播放、worker 和主线程请求交错、零时间步、自然结束。

逐帧比较队列来源/参数/停止标志、Montage 实例身份/时钟/权重/速度/播放状态、Slot 权重以及 79 骨 TRS 和全部曲线。结果：

- 队列全部匹配；播放时间和权重差 0。
- 位置最大差 `7.993605777301127e-14 cm`，四元数分量符号对齐后最大差 `6.661338147750939e-16`，缩放差 0，曲线存在性相同且最大差 `1.1920928955078125e-7`。
- 沿用上一批预设误差预算，没有放宽。
- 新 worker 原生 12 项通过；既有主线程原生 12 项已改为经过新队列路径，全部通过。
- 新 Core 6 项、相关 Core 共 167 项、相关 Import 共 98 项通过；Godot Optimize 构建 0 警告/0 错误。
- Core 首轮 5 过/1 失败：夹具将“物理 owner 未 Prepare”的异常错误地写成 ArgumentException；实际契约为 InvalidOperationException，修正期望后全过。日志保留。无原生对照失败。

新参考 `assets/config/refactored_transition_worker_trace.json`，38,336,996 字节，SHA256：

`628ABE74EEBC963449626E6BE0EA86E6D9672F20920F64DBA40400334534C73E`

产物：`artifacts/refactored-transition-queue/`。

## UE 构建和重启

按 `ue-diagnosing-plugin-build-load` 技能完成全 Editor 目标构建（4 actions）和全部项目插件审计。BuildId 仍为 `c5f9ab63-c24e-4fd4-9d58-bd9ad58d20b5`，fingerprint：

`C9B6E6574E1E21E612BCBD890B38F34CDC2CCDFE17230708A8F373477E509369`

冷启动退出 0；普通 Editor PID 33852 退出 0，两个 worker 输出字节一致。旧主线程轨迹重新冷导出退出 0，SHA256 仍为 `FC5E68CDF8C7C4631886A9C3D228711FC54F00FC6B3DF741390A98ADC70CD7E0`，没有改动旧参考。普通 Editor 两条既有 Condition failed 和五类既有警告仍保留，不声明已修复。DataValidation 退出 0，0 errors / 3 既有 warnings。未保存 UE 资产，无新打包验证。

## 仍未完成

本批是共享队列及资源接线和原生受控轨迹，不是 Godot 普通角色已经使用 Refactored 完整图。未运行 Godot 场景/人工视觉/最终性能/全量测试。完整 Turn 队列、DynamicTransitions 足部判定和帧延迟需随实际移动图接入；本批只保留其消费顺序，不声称这些调用端已移植。

下一步四武器 state 源更新与完整姿态、真实 Locomotion 图及统一角色宿主。Overlay 完整姿态仍 9/13，普通 Demo 未切换；Ragdoll/Flail/Get-up/Pose Recovery、相机复杂边界和十分钟性能验收等全部旧缺口保留。音频、道具物理和头颈诊断仍暂缓，用户未提交修改未纳入本批。
