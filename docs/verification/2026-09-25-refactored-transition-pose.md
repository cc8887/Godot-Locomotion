# Transition 原始姿态与曲线采样

## 实现

在主目录 `D:/GodotALS` 的 `main` 增加 `AlsRefactoredTransitionPose`，将上一批共享物理 Montage 的动画 ID 路由到两条原始 Standing Transition mesh-space additive 资源。

- 资源按 catalog 哈希绑定；检查宿主为原生 79 骨名称、顺序和父级，保持 UE 厘米与骨骼基底，不接受旧 V4/Godot 已转换布局。
- 宿主显式提供曲线布局，可重排并按名字忽略大小写匹配，但不能缺失原始曲线或含重复名字。宿主额外曲线在该 additive sample 中保持 absent；Slot 混合保留已有源曲线。
- 资源被冻结共享，每个 worker 创建独立 sampler。采样读 Montage Evaluation 的 float Position，不推进任何播放时钟；复用已验证的固定 base pose 差分和 mesh additive 原始键采样。
- 实现 `IAlsMontagePoseSource`，可直接用于共享 `AlsMontageSlotPose`。只接受本资源表中的 Transition Slot、动态 sequence、mesh additive 类型和合法时间/输出布局。输出在完成采样和映射后发布，失败不部分覆盖上一次结果；同一 sampler 拒绝重入。
- `AlsRefactoredTransitionMontages` 暴露 catalog digest 和动画 ID 对应的原始路径，不改变此前播放/组/停止算法。

## 验证

- 新增 5 项通过，相关 Import **30 通过、0 失败、0 跳过**，Optimize **0 警告、0 错误**。
- 两条资源各 61 个 float 时刻，共 122 次采样逐骨逐曲线与直接原始 additive sampler 完全一致；两个独立 sampler 并行采样一致。
- 两条资源 t=0 共 158 骨与既有 UE 原生参考核对。旧参考的其他时刻是 rational double，而 Montage Position 是 float；本批未将不同输入时刻的姿态硬作原生误差比较。相关回归仍包含原始 42 additive 资源的完整既有原生测试。
- 30/60/120 Hz 合计 840 帧真实 Montage 时钟→原始动画→Transition Slot 混合，重复左右播放、结束回归基础姿态、重复 Evaluate 不推进时钟、晚取消重试姿态/曲线一致；HostExtra 曲线保持不变。
- 错误 Slot、ID、additive、时间、action 身份不部分发布；缺曲线、重复曲线、错误骨序拒绝。
- 产物位于 `artifacts/refactored-transition-pose/`；本批无编译或测试失败。

本批没有新的 UE 连续 Transition Slot 姿态 oracle、通知函数消费 oracle、UE/Godot 启动或全量/性能/打包验收。连续混合测试验证的是已采样资源与共享 Slot 的接线和事务行为，不能替代真实原生连续姿态对照。

## 后续

下一步补实际 UE 通知函数播放/停止与 Transition Slot 连续姿态对照，然后实现四武器 state 源更新和整图姿态，再接真实 Locomotion 图及统一角色宿主。stopQueued/worker 覆盖队列仍未完成。

Overlay 完整姿态仍 9/13；普通 Demo 未切换；Ragdoll/Flail/Get-up 与其他旧缺口未整体验收。用户未提交修改保留，道具物理、音频和头颈诊断继续暂缓。
