# Ragdoll 骨盆与胶囊跟随

按用户优先级，先实现位置跟随，工作目录为 .。

## 实现依据与边界

对照本机 Plugins/ALS/Source/ALS/Private/AlsCharacter_Actions.cpp 的 RefreshRagdolling、RagdollTraceGround，以及 Engine/Source/Runtime/Engine/Private/Components/CharacterMovementComponent.cpp 的 MIN_FLOOR_DIST=1.9 cm。

读取 Core 物理岛中 pelvis 的 actor 世界位置，不读取动画 socket，也不使用质心位置。世界坐标转换为 Godot 米制；X/Z 跟随骨盆，空中 Y 跟随骨盆。球形扫描从骨盆上方两倍胶囊半径到下方 halfHeight-radius，命中后使用球心（不是接触点）+halfHeight-radius+0.019 m 修正胶囊中心。零目标沿用当前 actor 位置。半径、高度读取实际胶囊与缩放。

使用配置中的场景碰撞 mask，排除自身；不能读取已经置零的当前胶囊 mask。初始重叠先做零运动查询，再进行运动扫掠，避免 Godot 漏掉初始穿透。胶囊保持 layer/mask=0，进入时立即清除 CharacterBody velocity，不调用 MoveAndSlide，不写入物理身体位置或速度。

跟随在 Main Lifecycle 的一次成功物理步之后执行，使用刚完成的骨盆位置；下一 Gather 捕获该 transform。动画失败保持旧 Flail 时物理及跟随继续，角色停用时两者暂停。这是本项目的已完成物理步时序适配，不宣称与 UE pre-physics tick 的采样时刻完全相同。单机本地目标已接，远端复制目标及 PullForce 尚未实现。

## 验证

优化构建零警告、零错误。实际 Godot/Jolt 场景日志位于 artifacts/：

- pelvis-follow-fixed-single60.log：Single 60 Hz，120 动画/120 物理步。
- pelvis-follow-fixed60.log：Parallel 60 Hz，120 动画/121 物理步，注入一次动画失败、暂停三回调再恢复。
- pelvis-follow-fixed30.log：Parallel 30 Hz，60/60 步。
- pelvis-follow-fixed120.log：Parallel 120 Hz，240/240 步。

各项逐帧验证胶囊水平位置等于实际物理骨盆、空中高度一致、额外移动积分为零、共享 Flail 身份保持。另检查高空、零目标、平地 1.9 cm 间隙、初始重叠四种查询；人为移动胶囊不改变骨盆位置、速度和物理步数。结束要求角色已发生明显位移且落地；暂停检查胶囊位置不变。

首轮 pelvis-follow-single60.log 为初始实现通过；pelvis-follow-verified60.log 记录初始重叠失败，已通过显式起点查询修复，不能将其算作通过。最终以 fixed 日志为准。本批没有重跑 Core/Import 全量、UE 构建、旧物理稳定性矩阵或十分钟预算；没有完成坡面、移动平台和缩放胶囊的专项验收。

## 后续

最终显示仍是动画图 Flail，尚未把物理骨架回写到渲染姿态，因此本批并非完整 Ragdoll 视觉交付。下一步接当前动画的非物理骨骼基底与物理最终显示，避免视觉随胶囊重复位移；再完成动作中断、普通触发、退出/Get-up/Pose Recovery。完整 Camera、Mantle、十分钟性能预算和旧静态9/12、Flail0/3等未完成项继续保留。

保留用户 P4 规划文件，不纳入提交。
