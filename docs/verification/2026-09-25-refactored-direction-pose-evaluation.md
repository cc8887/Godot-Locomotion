# Refactored 方向缓存姿态与逐骨过渡求值

## 实现

新增 `AlsRefactoredDirectionPose` 及独占 Sampler，使用原 79 骨厘米布局。Profile 校验六个方向源/冲刺源的骨骼名称、父子关系与 MoveDirectionChange 配置一致，建立完整曲线名称并集及源映射；不把源曲线缺失误作存在的零值。

求值从已准备好的 DirectionMachine、DirectionSource 与共享 SourcePlayerRuntime 候选读取数据，不推进时钟、不触发 Notify 或 Parent 更新：

- 一个 Sample 调用对应一个缓存求值作用域，缓存姿态和玩家结果各只计算一次，多个状态重复读取直接复用。再次 Sample 是新的作用域，但共享播放器的本帧采样缓存仍有效；不要把此接口误解为跨任意外层作用域永远复用姿态。
- Standing Forward 按原 Sprint/Acceleration TwoWay → Gait BlendList → SprintBlock TwoWay 顺序计算。复用 `AlsOverlayActionMix` 的原生单姿态/双姿态快速路径；TwoWay 保留浮点 `1-alpha` 再相减的实际计算顺序。
- 六方向状态按原 F/B/L/R 缓存连接进行 MultiWay 累加，零通道返回骨架参考姿态，再写 RotationYawOffset。Yaw 四输入现在在 Source.Prepare 冻结，采样阶段不重读或更改输入。
- 状态机依次求值活动过渡栈，骨骼使用 MoveDirectionChange 每骨 incoming/outgoing 权重，曲线使用全局 alpha；过渡栈全部累加后才归一化旋转，不用诊断贡献权重替代有序四元数运算。
- 验证 catalog、精确源路径/布局、机器 owner、候选帧以及缓存是否更新；Sampler 重入被拒绝。输出先在独占暂存区算完，再复制给调用者，失败不发布部分骨骼/曲线。全局提交/取消仍由外层组合 owner 协调。

## 验证

新增 11 项测试：

1. 两 stance 单通道极值，直接对照实际共享播放器结果，确认旋转/位置及非 Yaw 曲线透传，覆盖 Standing Base、Sprint、Sprint Acceleration 三路径。
2. Standing 前向三源混合，检查真实 79 骨位置对应 `.65 Base + .175 Sprint + .175 Acceleration`，并确认两个 cache、三个 player 各求值一次。
3. 双 stance × 30/60/120 Hz 共 840 提交帧、66360 骨：重叠过渡、冲刺切换、零 delta、显式重置、零权重、曲线变化、独立 Sampler、逐帧 Cancel/重试姿态与曲线完全相同；外来机器被拒绝且不改输出。该部分是移植侧组合/事务测试，不是新的 UE 移动姿态 oracle。
4. 既有原生方向 trace 的 2814 帧、222306 骨对照，验证全部本地 pose 分量与曲线存在性；沿用位置 1e-4 cm、四元数/scale/curve 2e-6 容差，四元数按同半球分量比较。资源哈希与骨骼顺序核对。**这批旧 trace 没有 VelocityBlend 输入，原生默认四通道全零，因此只覆盖参考姿态、方向过渡和 ModifyCurve 零值分支；所有帧 cache/player 求值次数均为零，不能用它宣称移动源姿态已与 UE 一致。**

`artifacts/refactored-direction-pose-evaluation/pose-initial.trx` 初始 10 项通过；补三源混合并将原生四元数检验改为更严格的分量差后，`related.trx` 最终 27 项通过（新 11、方向源更新 10、前向源 6）。Godot Optimize 构建 0 warning / 0 error。没有初始失败，没有放宽预算。

## 下一步与交付边界

下一批需导出**非零 VelocityBlend、动态 Stride/WalkRun/PlayRate、Gait、SprintAcceleration、SprintBlock、YawOffsets** 的 UE 连续方向子图轨迹，对比实际 cache 权重、Sequence/BlendSpace 玩家时间、79 骨和完整曲线。现有 exporter 只记录 SequencePlayer，Direction 分支尚未接 Gait 输入，需要补采集，不能用旧零权重参考替代。之后推进其余 stance 机器与真实 Parent/Notify/完整角色宿主。

本批无 UE 插件/资产修改或新导出，无 Godot 场景、普通角色输入、全量、性能验收。普通 Demo 未切入完整 Refactored 链；Ragdoll/Get-up/Pose Recovery、Mantle、Camera、最终十分钟性能等旧任务未关闭。保留用户未提交修改；音频、道具物理与头颈诊断仍暂缓。
