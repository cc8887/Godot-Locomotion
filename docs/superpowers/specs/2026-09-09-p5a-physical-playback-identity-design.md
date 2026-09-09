# P5A 物理播放器身份修订

用户于 2026-09-09 确认实施。本修订替代 P5A 原计划 Task 5/6 的单份 Turn/Rotate 身份布局，解除 Task 15/16 的合同冲突；不改变 P5B/P5C/P6/P7 边界。

## 源码依据

- UE `AnimNode_AssetPlayerBase.cpp`：节点保存自己的时间、Marker、DeltaTime 状态；播放器身份来自动画实例和节点 ID。
- UE `AnimInstance.cpp` / `AnimMontage.cpp`：新 Montage 播放创建独立实例，初始化时分配 InstanceID。
- UE `AnimNotifyQueue.cpp`：事件经过 Leader/Follower 与权重筛选，播放身份独立不等于重复发布事件。
- 锁定 ALS `AlsAnimationInstance.cpp:2012`：Turn In Place 使用动态 Montage 和惯性化淡出。此次只借鉴身份/贡献分离，不宣称已实现其惯性化算法。
- 当前 Godot `BuildP4ActionChannel` 的两个物理 bank 各自创建所有 Turn/Rotate clip 节点，原 Import 表却只登记一份。

## 布局 v2

布局项 ABI 不变：`SourceKind, SourceBindingIndex, GraphSlotIndex, OccurrenceHandleId, AuthorityGroupId`。

- 唯一查找键为 `(SourceKind, SourceBindingIndex, GraphSlotIndex)`。
- Base 保持 22 项，`GraphSlotIndex = SourceBindingIndex`。
- Turn/Rotate 按 bank 0、bank 1 的顺序登记；每个 bank 内保持 profile 顺序。
- Turn/Rotate 的 `GraphSlotIndex = bank * bindingCount + SourceBindingIndex`，因此同一 binding 的两个播放器拥有不同 handle。
- handle 等于完整布局数组的 ordinal。消费者只能复制/查询编译结果，不能自行分配 handle。
- Transition、ActionMontage、ActionSequence 的逻辑事件身份保持单份，GraphSlotIndex 仍为 0。它们的冻结视觉 outgoing tail 不增加第二套事件时钟。

当前单 Roll 配置：

| Source | Handle | 数量 | Authority |
| --- | --- | --- | --- |
| Base | 0..21 | 22 | 0 |
| Turn bank 0 | 22..29 | 8 | 0 |
| Turn bank 1 | 30..37 | 8 | 0 |
| Rotate bank 0 | 38..41 | 4 | 0 |
| Rotate bank 1 | 42..45 | 4 | 0 |
| Transition | 46 | 1 | 1 |
| Action Montage | 47 | 1 | 2 |
| Action Sequence | 48 | 1 | 3 |

布局版本 2，摘要 `f2336240d749284b`；Core binding 版本仍为 2，摘要 `40f33e59692dfd38`。GraphBuildView 的资产/遮罩描述未变，版本 1、摘要 `44403c2869d8f615`；完整四段 stamp 中的 layout/binding 摘要识别物理布局变化。旧布局版本必须拒绝。

## 生命周期和权限

`current/outgoing` 是运行时角色，不属于身份键。角色改变不得改变仍存活播放器的 handle/epoch。槽位重启、倒退 seek、关闭后重用时增加 epoch；同动画在两个槽位播放时，epoch 可以相同。单个槽位不能用两个 epoch 承载重叠时间窗。

沿用 Core 每 handle 一个 cursor、完整 Notify ownership 身份和共享 authority 裁决。Task 16 的候选 epoch/time/closure 与图参数一起准备、回滚、提交；失败重试不得额外增加 epoch。此次修订不提前宣称 Task 16 控制器已完成。

Sync 仍只有 17 个声明的 Base 成员及 17 项精确映射，不为 current/outgoing 标签复制 Base 节点。同一 Base 节点重新进入属于重启，不是两个独立播放器。将来若真实增加 Base 物理节点，必须通过新布局明确登记，不能借用临时角色 ID。

## 迁移与验证

1. 先证明旧编译器/Core 校验拒绝新双 bank 测试。
2. 修改 Import 布局、Core 校验、snapshot bridge、每 handle 的 authored Timeline 展开和 Godot 物理描述符。
3. 验证真实 Turn/Rotate 同动画双 bank：相同 epoch、不同时间/权重、独立 cursor、同一权限域只发布获胜者事件；同 handle 的不同 epoch 重叠必须原子拒绝。
4. 更新 Oracle frozen stamp、计划 hash、port cursor 数量及合成测试的逻辑 handle。脚部曲线资源仍为 34 份，不因播放器复制资源。
5. 通过真实 UE generator 重新采样并生成 native golden，不手工改写观测数据或复制 Core 输出充当 UE 证据。保持 8 cases / 374 frames。
6. 运行 Import/Core、Godot binding smoke、P4 pose、golden generation/verification，并明确记录未执行的昂贵完整矩阵。

后续 Task 15 从这份 snapshot 构图；Task 16 实现上述生命周期；Task 17 接入单一生产 Worker 链。此修订不等于这些后续任务已经完成。
