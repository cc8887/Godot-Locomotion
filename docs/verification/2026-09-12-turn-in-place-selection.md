# TurnInPlace 选择、Slot 查询与蹲姿数据修正

日期：2026-09-12，第九十二批。承接完整性路线第 2 项与 P4/P5A 动作前置。

## 本次完成

`AlsTurnInPlaceCompiler` 读取正式 `v4_idle_control_inputs.json`，核对
TurnInPlace 的原生执行连接和数据连接：先计算 TargetRotation 相对角色的
规范化 Yaw，再按绝对角度严格小于 130°、左右符号和站姿/蹲姿选八种资产，
最后进行 OverrideCurrent 或非重复播放检查。没有使用无连接的调试节点
推导行为。源图中局部变量、self 变量、结构字段、函数所属类分别验证。

八个 CDO 结构的真实动画 ObjectPath 映射至导出动画 ID，并核对骨架、非加法
属性及现有 pose profile 的方向、角度、倍率、Slot 和缩放开关。播放请求保留
原图的 0.2 秒淡入/淡出、一次循环、0 秒 BlendOutTrigger、调用者 StartTime
和 PlayRateScale。新模型只构造动作命令，不自行推进动画时间。

严格校验暴露了已有移植错误：四个站姿资产 ScaleTurnAngle=True，四个蹲姿
资产实际为 False。旧 p4_pose_profile.json 全部为 true，PoseProfileCompiler
和 P5 绑定校验也都要求 true。现修正这三处，并将 Core 参考设置的四个蹲姿
标记设为 0。相关测试按站/蹲分别验证，不再以旧错误常量作为参考。

## 原版语义及来源

直接证据为正式导出的 ALS V4 AnimBP :TurnInPlace 原生图，及本机 UE 源码：

- `D:/UnrealEngine/Engine/Source/Runtime/Engine/Private/Animation/AnimInstance.cpp:2712`
  的 IsPlayingSlotAnimation 按 MontageInstances 顺序查询；跳过非 active、
  非 playing、非 transient、没有目标 Slot 或该轨道不是单 segment 的项。
  遇到首个合格轨道便返回资产是否相等；即使不相等，也不继续找后面的匹配项。
- `D:/UnrealEngine/Engine/Source/Runtime/Engine/Classes/Animation/AnimMontage.h:528`
  定义 Playing/Stopped/Active。Stopped 由目标混合权重为零决定，当前仍有
  淡出权重不等于 Active。新观察结构要求所有者提供生命周期标记，不从权重猜测。
- 原图 PlaySlotAnimationAsDynamicMontage 的 then 直接接缩放分支，ReturnValue
  没有连接。尝试播放后即更新 RotationScale，函数返回 null 也不改变该顺序。
  站姿为 TurnAngle / AnimatedAngle × PlayRate × PlayRateScale，蹲姿为
  PlayRate × PlayRateScale。重复检查阻止调用时才保持旧值。

上一批“成功播放后更新”的文字已纠正。这一轮没有启动 UE，也没有运行新的
UE 原图逐帧探针；上述结论来自已导出的原生图与本地引擎实现。

## 验证

- Import：29 项新 TurnInPlace 测试，加原 PoseProfile/IdleControl 合计 111 项通过。
  覆盖八资产、130° 严格边界、方向、站/蹲缩放、起播时间、覆盖与重复抑制、
  有序查询提前返回、六种无效 Montage 状态，以及 11 种源图/数据变异拒绝。
- P5 绑定编译回归 47 项通过。图摘要从 44403C2869D8F615 变为
  FD59AB9C657B60DD；独立小端写入器一致。测试将四个蹲姿开关恢复为旧值后，
  能精确还原旧摘要。Layout/Binding 摘要保持。
- Core TurnRotate/ContractLayout 共 97 项通过；旧参考运行器增加站/蹲的
  缩放结果断言。最终 Godot 构建零警告、零错误。
- `artifacts/turn-selection-base-layer.log`：3360 帧、79 状态、18 次重入、
  68 个来源事件、12 次晚期故障及同帧重试通过。33 次 Turn 请求仍仅到静止
  检查层，Slot 仍为夹具；该场景不验证真实 Montage，也未消费新决策。
- `artifacts/turn-selection-production-single-final.log` 与
  `artifacts/turn-selection-production-parallel-final.log`：各 180 帧，结果
  21E164D829153157、完整姿势 CF9225D4DE9B2C8B，事件 10 个；正式共享定义
  能加载新编译器和修正后配置。该路径仍为旧 Standing 求值。

保留中间失败：第一次新选择测试揭露 pose profile 与 CDO 不一致；修正后
生产 smoke 又揭露 P5 的重复旧校验（无 final 后缀的两个日志）。修正绑定
后，冻结图摘要测试因四个数据位变化失败，已用独立写入器及旧值重建确认。
这些失败均已修正并重跑，并非忽略检查。

## 未完成与后续

新增决策已由 AlsMovementGraphDefinition 载入，但尚未进入
AlsBaseLayerFrameRuntime 的实际动作候选：缺少真实 Montage 列表、生命周期
和 Slot 播放所有者，不能用空列表代替这些状态。下一步需要一个动作候选
处理播放/替换/淡出、独立身份和时间，提供有序查询，再统一提交命令、
RotationScale、姿势和事件，晚期失败全部丢弃。随后接通最终曲线反馈与 Demo。

没有新增逐帧视觉截图或声称滑步、交错步、换髋、双臂问题已修复；没有完成
新的十分钟性能验收。动态分层、完整 Foot Lock/pelvis、全部 Overlay/道具、
Mantle/Roll/Root Motion、Ragdoll/Get-up/Pose Recovery、完整 Camera 与 P7
仍在原规划内，只有音频暂缓。未提交、合并或回滚用户工作区。
