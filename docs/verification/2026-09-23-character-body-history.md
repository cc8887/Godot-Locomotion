# 普通角色接入逐身体运动学历史

主目录 main，接续 `0cac317`。普通完整动画角色现在创建 `AlsCharacterBodyHistory`，真实消费已提交动画姿态；尚未启用动态 Ragdoll、身体碰撞或胶囊切换。

## 阶段与生命周期

History 节点只在 Main 的 Lifecycle 阶段运行，位于动画 Commit 之后；仅处理配置完成、未销毁、Active、VisualReady 的角色。按同帧入口数据复制逻辑 FBX pose 与 skeleton world，映射到真实 PhysicsAsset 身体，再用 Core kinematic history 完成当前运动学步。物理历史步号独立于动画源帧：动画失败重试时仍保持第12帧身体位置，后续没有新 target 的历史步清零 V/W，不能因为动画13失败就重复继承移动速度。

`SourceAnimationIdentity` 与 `PhysicsIdentity` 分别发布。停用清除历史；恢复首次可见成功姿态以零速度重新建立，避免跨暂停差分。物理编号在同一角色代际中继续递增，不重用旧身份。销毁清历史并停止处理。预分配 pose/body/actor 缓冲，资源绑定在配置时完成；最终性能预算未测。

提供显式 MarkTeleport，但普通传送调用方尚未全部接线，也没有自动根据任意 scene transform 写入推断 teleport。pending 目标激活选择、实际 Ragdoll 进入及动态物理 owner 仍是下一步。此节点当前执行的是 Core 运动学历史计算，不是让 Jolt 与 Core 同时积分。

## 实际逐帧接入发现并修复的问题

抽样12/13帧通过并不代表连续动画可交接。普通 Roll 恢复后第14帧 `ik_foot_root` 的 Scale=(1.005158,1.005158,1)，属于没有物理身体的 IK 分支；旧 Seed 错误要求所有逻辑骨骼都是刚体，导致 history 故障。现在只约束物理身体及其祖先链的刚体性质，其他分支保留完整原始 local TRS；Capture 不再丢弃非物理骨骼的缩放。

停用恢复13帧还暴露 head 的浮点 Basis 累积漂移：scale约(0.999999,0.99999905,0.99999917)，det=0.99999726，触发桥接检查。在所有物理祖先局部变换与 component world 已经通过刚体验证的前提下，对派生身体 world Basis 正交化后转换；不放宽真实身体缩放检查、不清掉 IK 动画缩放。已添加非物理 IK 分支缩放保留与实际身体祖先缩放拒绝的回归。

第一轮 recovery 日志虽有 OK 标记，但之后的14帧存在 GD error，所以判为失败；现每帧检查 history.Failure，并检查最终日志。初始失败和定位日志完整保留，不计为成功。

## 验证

`artifacts/character-body-history-20260923/` 保存证据。最终优化构建零错误/警告（build-final2.log）。完整普通 Demo 四组恢复场景：single-final、parallel-final、frozen-final、rolling-final；检查已提交动画来源一致、失败期间身体位置不变/VW清零、物理编号继续前进，保留动作/Notify/运动不重复等原有断言。

停用恢复三边界：lifecycle-committed-final、lifecycle-pending-final、lifecycle-callback-reopen-final；检查停用立即清历史、恢复零初速且物理身份不重用。最终结果及退出码以这些日志为准，早期不带 final 或 verified 中间结果不能替代。

真实两模型各四姿态桥接回归（pose-final.json/log）：160姿态交接、160逐身体速度交接、192回写、48拒绝，另覆盖八次非物理 IK 缩放保持。最大位置4.952927e-6m、basis7.4981125e-7、旧速度场误差7.8680387e-7m/s；native逐身体速度仍逐值相等。

本批只修改 Godot 层，无新 Core/Import 算法/UE 导出，不重复其全量或落地矩阵。静态9/12、Flail0/3及旧UE退出异常保持未关闭。下一步实际进入/胶囊/既有Flail/骨盆跟随/显示退出，再Get-up/Pose Recovery、Mantle、完整Camera和十分钟性能目标。用户P4规划修改保持不变且未提交。
