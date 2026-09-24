# PostLocomotion 接入组合分层与 Head

新增 `AlsRefactoredPostLocomotionSink`，将父图的 PostLocomotion Slot 插入 Locomotion linked input 之前。它与既有区域 Slot decorator 读取同一个冻结 Montage frame，复用 Slot 混合器和原生 source-weight 规则，只新增此 Slot 的 source-weight 历史，不创建播放时钟。

完整输入顺序现在可以组合为：上游 Locomotion → PostLocomotion Slot → Layering → Head。区域 Slot、Overlay 和固定基础姿态的回调继续转发。宿主必须在 Begin Montage 后绑定两层 Slot provider，在所有 owner 验证后共同提交；失败时取消这些候选，再重试。Notify 相关性为两层 provider mask 的并集。

## 数据和原生策略

`AlsMantlingHostPoseProfile.ForNativeSkeleton` 复用已有 Montage 自有曲线覆盖、身份检查与采样缓冲，保留原始 79 逻辑骨和厘米。旧构造器仍执行宿主骨架适配及 FBX 米制转换；没有把最终姿态重新生成虚拟骨。

原生模式强制提供完整 Montage 曲线资源。六 Montage 自有 62 条曲线继续覆盖 sequence 曲线，再进入 Slot 混合，避免只传两个 sequence 曲线而漏掉 View/Layering 控制。资源身份映射复用已存在的 host namespace，不把 native 局部 ID 插入旧 bank。

组合 compiler 现在也调用既有外层链编译检查，验证 PostLocomotion 位置及原文默认 source-update 策略。本地 UE `AnimNode_Slot.cpp` 构造器确认 bAlwaysUpdateSourcePose=false，Initialize 调用 WeightData.Reset；`AnimTypes.h` 确认 source weight 重置为 0。新 provider 在 linked input 初始化时重置候选历史，保留放弃候选时的旧已提交状态。全覆盖时跳过上游更新和采样；淡入时将源分支标为 inactive，重初始化不继承旧 weight 的下降状态。

## 验证结果

- 六个实际 Montage × 30/60/120 Hz × 三秒，共 3780 提交帧；共享 bank 同时播放一个既有宿主 action，两个组互不覆盖。上游使用真实固定 Stand，Overlay 同样是受控 Stand，**不是实际移动/Overlay 运行时接入**。
- 每帧丢弃整个图及 Slot 相关性候选再重试，pose、curve、Head、Notify 队列及随机种子一致；不再次推进 Montage。
- 每条轨迹第 4 帧在真实 Montage 采样后注入故障，共 18 次；图自动取消，PostLocomotion 禁止提交，宿主取消 provider 后重试成功，已提交 Head 和 Slot identity 未前移。
- 每条轨迹第 5 帧重新初始化，源分支 active 状态恢复；普通淡入第 3 帧为 inactive。所有轨迹都实际经过淡入/全覆盖/淡出/透传，全覆盖上游更新和求值计数均为零。
- 真实脚步通知按 Slot 相关性进入 queue，重试无差；只验证队列，不执行声音或特效。
- 原生采样入口另有六动作 × 31 时刻、186 姿态，与原生厘米采样器的全部 79 骨逐值相同；Montage 曲线值/存在性一致，未绑定曲线保持缺席。

新增 4 项测试。最终 Import 定向 19 项通过（含旧 FBX 宿主采样、上一批组合阶段和 Head 原生参考），Godot Optimize 构建 0 error / 0 warning。初轮、故障/重初始化补充与最终 binding 回归均通过；本批无失败。日志在 `artifacts/refactored-post-locomotion`。

无 UE 插件变更、新 native 整链 oracle、Godot 场景验证、全量 Core/Import 或打包。对照和测试证明上述 C# 资源/调度组合，不能扩张成普通 Mantle gameplay 已完成。

## 剩余接入

普通 Demo 尚未切换。继续实际 Locomotion/Overlay 的布局与曲线来源接入、整个宿主事务与视觉写回，再接障碍探测、移动目标、Mantle motion 的身份和中断生命周期。既有 Ragdoll/相机/最终性能缺项以及用户暂缓的头颈调查、道具物理、音频保持原范围。
