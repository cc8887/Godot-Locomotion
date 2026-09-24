# Ragdoll 最终物理姿态显示

在 . 接通普通角色内部 Ragdoll 生命周期的物理显示，承接骨盆/胶囊跟随。

## 实现

Main Lifecycle 顺序为共享 Flail 驱动物理步、胶囊跟随、物理姿态回写。回写前确认动画 worker 空闲、已提交动画身份等于物理消费身份、物理步未重复显示。使用跟随完成后的实际 Skeleton3D 世界变换求逆，把身体世界姿态转换为骨骼局部姿态，避免胶囊位移再次叠加到网格。

AlsCorePhysicsPose 新增显式当前动画基底的 Capture 重载：物理骨骼来自已完成的物理岛，非物理骨骼来自当前已提交动画，保留其 TRS。该读取不重新 Seed、不改变身体速度或进入基准；旧重载仍使用进入基准，服务既有独立探针。完整输入验证和私有候选输出避免非法输入部分写入调用方。

生产动画运行时使用独立物理显示缓冲，保留 committed animation、precise Flail 以及它们的时间/身份。骨骼写入失败恢复显示前姿态。动画失败保持旧源时物理显示继续；暂停时不推进，恢复后继续原 owner。下一动画帧照常覆盖工作姿态，Main 再写本物理步最终姿态。

## 验证

优化构建零警告、零错误。实际 Godot/Jolt 日志均在 artifacts/：

- ragdoll-display-single60.log：Single60，120动画/120物理步，最大身体世界位置误差9.727806e-7 m。
- ragdoll-display-parallel30.log：Parallel30，60/60步，最大误差5.2047517e-7 m。
- ragdoll-display-parallel120.log：Parallel120，240/240步，最大误差9.762251e-7 m。
- ragdoll-display-rendered60-final.log：真实 OpenGL 渲染、Parallel60、120动画/121物理步，一次故障保持及暂停三回调，最大误差9.61096e-7 m；退出0，无 ERROR。
- ragdoll-display-world-pose.log/json：两模型八姿态、160身体交接、192回写、56拒绝、160原生速度交接通过；新增当前非物理基底修改、物理世界保持、非法输入不发布、进入基准不变检查。
- ragdoll-display-ordinary.log：Parallel普通翻滚、角色替换、故障恢复及既有身体历史/激活检查通过。

逐帧验证所有资产物理身体的位置及方向、非物理骨骼当前 TRS、胶囊跟随、Flail playback epoch=1。故障保持帧额外检查 committed animation 不因物理显示变化。日志中的 worker_evaluate diagnostic 和截图 HUD Errors=1 是主动注入的故障。

连续截图保存在 artifacts/ragdoll-display-captures-final/：ragdoll-0015、0045、0075、0105.png。检查了下落与倒伏过程，无明显胶囊重复位移。截图测试使用固定偏移的观察相机，不代表完整 ALS Camera 验收。首轮 ragdoll-display-rendered60.log 退出时重复断开截图回调报错，已改为幂等清理；最终日志验证关闭该问题。首轮截图保留，不覆盖证据。

本批未修改 Core/Import 生产代码，未重跑其全量测试、UE 构建、旧十秒物理矩阵或十分钟性能验收；两秒截图不能证明长期稳定性。既有静态9/12、Flail0/3等问题保持未关闭。

## 尚未完成

普通键位/自动触发仍未绑定内部 BeginRagdoll；active action 的原生中断规则尚未接。持有道具当前仍消费动画阶段的 attachment，Ragdoll 物理显示后的道具重定位需要补齐；本次使用 Default overlay 验证。随后接动作中断、普通触发、退出/Get-up/Pose Recovery。重复激活/退出生命周期尚未实现，显示步号目前属于一次 activation。

完整 Camera、Mantle、长期物理稳定性、最终性能预算继续保留。用户 P4 规划文件未改变且不纳入提交。
