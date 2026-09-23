# Ragdoll 逐身体运动学历史

主目录 main，接续 `a1a4525`。新增 Core 已完成物理步历史缓冲，并扩展原生探针验证速度产生的时间边界。普通角色尚未调用这个缓冲，不代表 Ragdoll 已接通。

## 原生观察

仍使用两套真实 mesh/PhysicsAsset、两种初始碰撞模式；受控 ACharacter，没有动画求值或 PIE。同步 Chaos 场景实际推进，每步 float 1/60 秒；记录外部游戏线程与内部物理线程速度。

| 阶段 | 游戏线程速度 | 物理线程速度 |
| --- | --- | --- |
| 初始、刚排入 PositionTarget | 0 | 0 |
| PositionTarget 物理步完成 | 逐身体位移/旋转产生的 V/W | 相同 V/W |
| 随后切换模拟 | 保留 V/W | 保留 V/W |
| 下一步没有新 target | 清零 | 清零 |
| 重新建立非零 V/W 后调用 TeleportPhysics | 立即清零 | 暂时保留之前 V/W |
| 瞬移的物理步完成 | 0 | 0 |

因此入口不能把新动画目标的差分速度提前当成已完成物理速度；也不能在待执行瞬移时盲目继承旧物理速度。上一批只看同步切换保留的结论仍成立，但不足以确定所有入口时间边界。

源码依据：PBDRigidsEvolutionGBF::ApplyKinematicTargets 的 Position→Reset→None，使用 actor X/R 而非重新构造质心轨道速度；ChaosEngineInterface::SetGlobalPose_AssumesLocked 对 kinematic 外部 V/W 立即清零，而 SetKinematicTarget_AssumesLocked 只排入目标。不得把这些速度再加一次 COM 杠杆臂速度。

## Core 实现

`AlsKinematicBodyHistory` 固定角色/代际身份、预分配已提交与候选身体缓冲。构造零速度初态，PrepareTargets 使用现有 AlsKinematicMotion.PositionTarget，成功 Commit 才发布姿态、速度和物理帧身份。Cancel、后部非法目标、错误身份或跳过物理步都不发布。PrepareNoTarget 表示确实执行了一步但无新目标，清速度；不能把“没有运行物理步”当作同一操作。teleport 候选在提交后位置改变、V/W 为零。

该类明确表示 **已完成的同步物理步**，不模拟 UE 游戏线程立即生效的瞬移状态。后续普通入口仍需记录待执行 teleport、目标提交与物理完成的边界，按入口时间选择状态；不能直接将 CopyCommitted 作为任何时刻的完整 UE 激活状态。substep、异步插值、velocity target 模式和现存动态身体不在此类范围内，也不宣称已覆盖它们。

## 验证

- Core 四项新测试在 .NET 8/9.0.17 Release 通过：未提交目标隔离、失败/Cancel/重试、无 target 清零、非零线/角速度后瞬移清零、后续正常移动、错误角色/代际/跳帧拒绝。
- `VerifyKinematicHistory.ps1` 直接调用编译的 Core PositionTarget，与 80 个原生身体样本对照。恢复 native float 存储后 V/W 最大逐分量差均为 0；同时检查各阶段 GT/PT 差异和模拟切换保留。不是完整动画到物理轨迹等价。
- 旧 `VerifyRagdollEntry.ps1` 的 240 个同步切换身体检查继续通过。
- Godot Optimize 构建零错误/警告。无既有 C# 算法修改，未重复 Core/Import 全量或落地矩阵。
- 最终全 Editor 构建与插件审计通过，fingerprint `1CFC5657A618E30F849577D7EA003062B54DB04B41EF728389B91D449D0DF5E3`；BuildId 保持 `186ff094-6861-4ab2-95dd-e0889004ba00`。
- 最终两次冷导出退出 0 且字节相同，SHA256 `38ED5F9AF9C4C5F002A1C834BCC5462F258884F18CA201AFA17751224A80AD9C`；DataValidation 0 error / 3 既有 warning。
- 最终二进制普通 Editor PID 33448 加载检查成功、退出 0xC0000005；确认退出后独立复跑 PID 4976 加载成功并退出 0。两旧 Condition failed/间歇退出异常未修复，不能称所有门禁稳定通过。中间二进制 PID 28132 退出 0 不替代最终复跑证据。

证据 `artifacts/ragdoll-native-history-20260923/`：最终参考 final-native/repeat.json，compare 以 comparison-verified.log 为准，Core core-final/net9-final.log，Godot godot-build.log，UE build-teleport.log。初版瞬移前速度已为零，补为非零后发现 GT/PT 暂时不同；初版 verifier 过强的全阶段相等断言失败日志保留，最终改为对瞬移前/后的明确状态分别断言，不放宽速度差门槛。原生首编译句柄类型错误已修正为 kinematic handle。未重写任何旧冻结资产。

下一步是普通角色逐身体历史 owner、待执行瞬移/动画提交与物理步边界、实际进入和速度继承，再胶囊/Flail/骨盆跟随/显示退出，以及 Get-up/Pose Recovery、Mantle、完整 Camera 与十分钟性能目标。最近静态 9/12、Flail 0/3 稳定性边界保持；用户 P4 文件未修改、未提交。
