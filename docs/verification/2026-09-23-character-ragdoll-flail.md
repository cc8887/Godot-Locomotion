# 普通角色共享 Flail 与物理反馈

主目录 `D:/GodotALS`、main，接续 e8e96db。普通角色新增内部 BeginRagdoll 入口，成功创建物理对象后关闭胶囊碰撞，后续 Gather 改为物理驱动输入；既有完整动画图进入 Ragdoll，并将其真实 committed precise Flail 交给同一个物理对象。主 Demo 的自动高落差/翻滚离地触发尚未接线。

## 输入与时序

新增纯值 AlsRagdollPhysicsSample：动画目标身份、激活身份、相对激活的已完成物理步数、原生 cm/s 骨盆速度。保留两个时钟，未把步数当作动画帧号。无托管引用，追加到 AlsFrameInput 尾部并更新布局测试。

Core 在 Active Ragdoll 时要求 PhysicsDriven、CompleteMovementGraph 和有效匹配的物理样本，优先于 Floor 决定 locomotion state；清除跳跃/落地恢复状态。原 Grounded/InAir 路径保持原行为。旧 AnimationState 字段仍描述 normal locomotion 分支，不新增虚假的旧播放器；真正选择由 ResolvedLocomotionState 的最终 root 控制。

StepPhysicsDriven 不调用 MoveAndSlide、不积分重力、不消费 Root Motion，记录本帧生命周期 checkpoint，保留控制视角/动作输入，给动画提供 Ragdolling action 和零移动输入。实际骨盆速度由物理对象读取，Flail 使用原生精度样本，不从胶囊速度反推。Motor IntegrationCount 在此路径不增长。

复核本机 ALS RefreshLocomotion 的 GetVelocity、UE Pawn.cpp 的 APawn::GetVelocity 和 CharacterMovementComponent.cpp 的 MOVE_None 分支：关闭移动模式会清零 CMC 速度，而 RagdollingState.Velocity 单独读取骨盆。因此普通 ActualVelocity 应为零、首次加速度为从上一移动速度到零的差分；不能用骨盆速度替代普通 locomotion globals。初版实现混用了这两者，最终已分离，并新增逐帧零移动速度断言。骨盆样本和 FlailRate 保持物理来源。

生产动画 adapter 接通 Ragdoll state/observation，并用已有 Flail 的时间与速率发布 source timing；没有另建播放器。Main Lifecycle 阶段不再更新运动学历史，而是用同帧 committed Flail 推进动态物理。动画失败时保持已提交 Flail，物理仍可推进；重试使用已捕获的同一 Gather 输入，不重新取后续物理速度。

阶段暂停同时停止动画和物理，恢复保持胶囊禁碰；销毁由角色统一 Dispose simulation。尚未验收所有 gameplay SetActive/代际替换/退出恢复边界，不能将本批 scheduling pause 测试替代它们。

## 集成中修复

首次切入第21帧缺少 Refactored prediction/history，被现有校验拒绝。Ragdoll 虽然不求落地预测，全局动画仍需要同帧查询身份和上一动画反馈。现保留该记录并显式 suppressQuery，不执行射线，不放宽身份校验。首轮失败 single60.log、完整堆栈 diagnostic.log 保留；prediction-fixed.log 首次连续运行通过。

## 验证

`artifacts/character-ragdoll-flail-20260923/` 保存证据，优化 build-verified.log 零错误/警告。

- single60-verified.log：120动画样本/120物理步，Flail epoch=1，胶囊额外积分0。
- parallel60-retry-pause-verified.log：120样本/121物理步，注入一次动画失败，保持1帧；中途暂停3次回调，动画/物理不推进；恢复后 epoch=1，胶囊仍不积分。
- parallel30-verified.log：60样本/60物理步；parallel120-verified.log：240样本/240物理步。均逐帧检查物理样本身份、原生速度对应的 FlailRate、共享 source 已提交且被 simulation 同帧消费。未带 verified 的同类日志是分离移动/骨盆速度前的中间结果。
- ordinary-rolling-regression.log：普通 Parallel Roll 替换/故障恢复通过，原动作所有权、激活候选与身体历史检查继续通过。
- Core Release 固定 JIT/串行既定过滤2901通过（core.log）；新增 Ragdoll routing/无效样本不发布和布局定向24项通过，LatestMajor 复跑24通过（core-dotnet9.log，本机最高已安装 runtime 为9.0.17）。Import 首次进程结束但 import.log 无结论，不能计通过；独立重跑2485通过/1既有Editor条件跳过，保存 import-final.log 和 import-final.trx，耗时5分3秒。

最终运行均要求退出0且无 ERROR；故障注入仅允许已知 diagnostic。该两秒验证不证明长期睡眠稳定、完整视觉表现或原生完整轨迹等价。

## 未完成及下一步

BeginRagdoll 当前只接受无 active action 的完整已提交角色；原生 Montage_Stop/动作中断尚未接通，因此不能启用翻滚离地自动 Ragdoll。普通手动输入也未绑定此入口。

当前显示仍是共享动画图的 Flail，并非物理骨架；胶囊位置保持进入位置，尚未做骨盆跟随、地面修正和相机跟随。下一步把物理 Capture 接到最终显示/快照，并使用当前动画作为非物理骨骼基底，保持 Flail 电机源与物理显示独立。然后接原生进入动作中断、普通触发和退出/Get-up/Pose Recovery。

Mantle、完整 Camera、最终十分钟性能，以及静态9/12、Flail0/3和旧UE退出异常继续保留。无新 UE 源码/构建或旧落地矩阵；用户P4规划文件原哈希不变且不提交。
