# 普通角色 Ragdoll 入口帧交接

在主目录 `.`、`main` 接续 `8478770`。本批补齐入口数据边界及真实动画到物理初态的验证，尚未启用普通 gameplay Ragdoll。

## 实现

`AlsP3Character.CopyCommittedRagdollEntry` 一次读取已提交逻辑 FBX pose 和 `AlsRagdollEntryFrame`：完整帧身份、角色世界变换、骨架世界变换、角色实际速度。变换来自同帧 FootProbeSource，速度来自已提交 Diagnostics；不读取可能已推进到失败候选帧的 LatestMotorInput 或 live skeleton。

入口要求 Main、已配置且未销毁/停用、完整身份一致、Main 已提交且 presentation 完成，拒绝无效变换和速度。元数据在复制调用者 pose 之前检查，内部动画保存帧再次核对身份及长度。沿用现有 Main/Worker 阶段独占约定，不是任意线程可调用的快照容器。

这份角色速度不是每根物理骨骼的速度。已有 UE 源码检查表明 SetInstanceSimulatePhysics → UpdateInstanceSimulatePhysics 切换 kinematic 状态与 InitDynamicProperties 创建身体时设置初速属于不同路径。因此本批不把角色速度广播给所有身体冒充原生进入策略；还需核对普通入口时的身体历史及初始化路径。

## 验证

Optimize 构建零警告、零错误。实际普通 Demo 完整动画图的四次 headless 验证均退出 0，日志位于 `artifacts/ragdoll-entry-20260923/`：

- `single-final.log`：Single 两次失败后恢复。
- `parallel-final.log`：Parallel 两次失败并接受替换动作。
- `frozen-final.log`：Parallel 四次失败后保持冻结。
- `rolling-final.log`：Parallel 实际 Roll gameplay 位移、两次失败并替换动作。

检查第 12 帧 metadata/pose 同源，失败候选 13 不覆盖入口；恢复提交 13 后一起更新且旧 12 不可读。错误角色、错误 generation、错误缓冲长度和未提交身份被拒绝，调用者 pose 不变；读取不积分或移动角色。错误 generation 是拒绝测试，不代表完整槽位重用生命周期已在本批重跑。原有动作、Notify、重试不重复运动等断言保留。

使用真实已提交第 12/13 帧 pose 与 skeleton-to-world 调用 `AlsCorePhysicsPose.Seed`，由逻辑骨骼层级独立累积世界姿态，检查所有物理身体的位置与 basis 向量误差 < 0.0001。冻结场景仅验证第 12 帧。这里显式使用零测试速度，仅验姿态传输，不声称验证原生初速或真实物理激活。

日志中的 frame 13 `worker_evaluate` 是预期故障注入。本批未修改 Core/Import 算法或 UE，不重复它们的全量测试、原生导出和物理矩阵。

## 后续

下一步仍是原生进入初速/前八帧限速、胶囊停用、既有动画 owner 的 Flail 驱动、骨盆跟随、物理姿态显示与退出，再 Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能验收。最近静态矩阵 9/12、Flail 稳定性 0/3 的边界不变；此前 4200 帧同输入原生轨迹一致不等于这些稳定性目标已通过。用户 P4 规划修改保持不变且未纳入提交。
