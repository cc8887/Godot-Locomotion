# 已提交动画姿态的物理交接边界

主目录 main 的生产动画链路新增两份预分配逻辑骨骼缓冲：Apply 保存求值后的候选姿态；成功 Commit 后才复制到已提交姿态并发布完整帧身份。Discard/失败回滚不覆盖已提交姿态。此数据独立于 Godot live skeleton，调用者只能复制到自己的目标缓冲。

角色入口 `CopyCommittedAnimationPose` 检查主线程、角色、slot generation、Main 已提交帧和已销毁状态，内部再检查动画保存帧和完整骨骼数量。不允许把 Worker 已完成但 Main 尚未接受的候选帧作为交接姿态；不匹配时拒绝读取。数组复制在现有 Godot 主线程/Worker 分阶段独占协议内执行，不是可供任意外部线程调用的并发容器。

## 验证

`artifacts/animation-pose-handoff-20260922/` 保存构建和运行日志。Optimize 最终构建零错误/警告。完整普通动画图、真实动作输入下的以下 Godot headless 场景均退出 0：

- Single 两次 BeforePublish 故障后成功恢复。
- Parallel 两次故障后恢复并接受替换动作。
- Parallel 连续四次故障，达到重试上限后保持冻结。

新增断言验证第 12 帧姿态存在、修改调用者数组不改变保存数据、第 13 帧失败期间保存姿态逐值不变、成功恢复后可读新身份且拒绝旧身份。继续保留原场景的所有动作/Notify/物理积分不重复断言。最终日志为 `single-final.log`、`parallel-final.log`、`frozen-final.log`。注入的 worker_evaluate 诊断为预期测试行为。

本批没有 Core/Import 算法变更，不重复其全量测试；没有新增原生导出/UE 构建或物理矩阵。

## 下一步和未完成范围

保存的是 **最终逻辑 FBX 动画姿态**，适合后续进入 Ragdoll 的初态交接；目前仅测试调用，尚未传入实际身体 Seed。运行中根节点可能混合物理 snapshot，所以此缓冲不能冒充独立 Flail 动画电机目标。

后续仍需取独立 Flail 源姿态、建立逻辑骨骼到 native 局部姿态转换，连接 motor inputs→实际逐物理步，再完成胶囊/角色位置跟随及进入退出生命周期。普通 demo Ragdoll 尚未接通，最近物理矩阵 9/12，三项 30 Hz 旧失败保留；Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能目标全部保留。用户 P4 规划修改未变、未提交。
