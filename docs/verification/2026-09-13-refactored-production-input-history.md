# Refactored 预测与最终曲线历史接入生产帧

日期：2026-09-13，第一百七十六批。原 P4 输入/脚部链补完。

## 实际接线

`--refactored-pose-curves` 为生产可选入口，要求同时启用 `--foot-ik-frame`，
以取得完整角色组件/姿势输入。该开关自动包含两个 PoseMoving 缓存生产点，
BaseLayer 构造时绑定全部六个状态曲线点。默认 Demo 尚未启用此选项。

Motor 在主物理阶段用当前胶囊中心、实际速度、组件缩放、胶囊世界尺寸、
可行走角度和已提交屏蔽值准备/采集查询。水平半径缩放遵循 UE
UCapsuleComponent::GetScaledCapsuleRadius 的较小水平分量，半高取垂直缩放。
当前测试场景均为单位角色缩放；不声称非均匀 Godot 碰撞体已完成效果验收。
桥接指定 Visibility 阻挡的 static/dynamic/destructible 层组为 1/2/4，
排除自身 RID；当前 Demo 的既有物体使用层 1，新道具碰撞/层分类仍待玩法接入。

新增无引擎对象的 AlsRefactoredGroundPredictionSample，包含观测和上一
已提交最终曲线的反馈，随 AlsFrameInput 进入现有双缓冲交换。BaseLayer
全局阶段检查当前帧、前帧、世代、序号与 allowance；仅空中更新独立预测，
其余状态保留原值。Main Movement 显式接收该值，不再仅靠受控回放数据。

完整生产根求值后读取 PoseGrounded/PoseInAir/PoseMoving，以及
GroundPredictionBlock，形成下一份反馈。Controller 将它写入可视候选，
Worker 完成提交后由主 Commit 阶段发布给下一次 Motor 采集。
BaseLayer 同时保存本次使用的旧 PoseState 与预测值，后续新 Rig 可消费
正确时点的骨盆输入。不会用本帧新曲线覆盖本帧应读取的缓存 PoseState。

替换世代时，重新查询新角色当前胶囊，并使用冷动画历史；不重标记旧
查询身份。通用 ReleaseYawAndFootProbes 不清除已提交动画曲线反馈，
避免失败/恢复后动画历史与物理输入脱节。

## 验证

完整生产 single/parallel 各 960 帧通过，含角色世代替换。两种模式：

- 各 52 帧空中真实预测为正，逐帧验证查询、缓存 PoseState 和最终反馈
  的身份关系；预测及三个 PoseState 值摘要均为 C330BE565B1894C0。
- 新 PoseMoving 正值 478 帧，摘要 AAB0325E448AAC0C。
- result DB9FEFC95ADA4B15，pose EFEF274D127B1A99，
  full_pose 765E1669B4501131，root 3C8B520C47ECA5C7。
  姿势摘要保持，因为新 Rig 尚未替换旧脚部消费者；不是视觉改善证据。

日志：`artifacts/refactored-production-176-single-recapture.log` 和
`artifacts/refactored-production-176-parallel.log`。两次测量之后只把
胶囊半径水平缩放由 max 改为原生 min；单位缩放案例两者相同，最终构建通过。

并行晚期真实通知失败在第 25 帧注入，验证预测、旧 PoseState、动画侧最终
反馈和主线程反馈全部保持；无候选通知泄漏，已有姿势/根/来源/随机状态
回滚也通过。日志 `artifacts/refactored-production-176-late-source-history.log`，
标记 REFACTORED_POSE_ROLLBACK_OK 与 P5_SOURCE_EVENTS_ROLLBACK_OK。

25 项 Core 合同/交换测试通过，确认新快照无引用、字段顺序和旧槽覆盖；
15 项预测/姿态曲线输入测试通过。结果分别为
`artifacts/tests/refactored-production-176-contracts-verified.trx` 和
`artifacts/tests/refactored-production-176-inputs.trx`。Godot 最终优化 Debug
构建零警告、零错误，diff check 通过。

首轮生产在第 121 帧拒绝旧世代查询，保留 `-single-first.log`；修复为
冷角色重采集后通过。首次故障命令误加互斥的 full-movement-coverage，
初始化拒绝，去掉该测试选项后完成故障验证。新增交换测试最初错误地
认为另一双缓冲槽写入会覆盖旧槽，改为同槽覆盖断言；两个遗漏命名空间
造成的编译首错也已修正。

## 仍需完成的源曲线适配

正式 V4 manifest 的 126 个动画中，GroundPredictionBlock、FootLeftIk、
FootLeftLock 均为零条；Mask_LandPrediction 存在于 4 个起跳动画，
FootLock_L 存在于 15 个动画。图内八个写入点并不能补全动画自身的所有
同义曲线。目前生产按原 Refactored 名字读取缺失 Block=0，不暗中复用
V4 预测结果；因此不能称为屏蔽曲线语义已经完整移植。

下一项明确并验证这些源曲线的版本映射及写入/混合位置，再把完整新
脚部 Rig 接入脊柱→脚→手顺序。随后做支撑接触、换向、起步、平台和
多帧视觉验收。第 616 帧旧失败、默认完整入口、P3/P4 整角色和 P5A–P7
仍开放，音频继续暂缓。
