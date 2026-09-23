# Ragdoll 限速的物理步事务

主目录 main，接续 `52b59b7`。将上批限速候选接到物理宿主接口，普通角色激活流程尚未调用。

## 实现

`AlsJointIsland.Step` 新增可选 `velocityOverrides`：有序、唯一的 dynamic body COM 速度替换。预分配 step-start states，从已提交身体复制后施加替换；积分、接触检测 PreV、睡眠观察使用同一份步初状态。它不是额外冲量，不会用 Reset 清掉持续接触/睡眠历史。非法输入、求解或 StageCommit 失败都不会发布身体。每步重新复制，失败候选不会泄漏到下一步。变化的速度可唤醒岛，唤醒失败仍保留原休眠状态。

`AlsCoreJointHost.StepRagdollScene` 只对资产身体前缀准备限速，环境不受影响，转换为 dynamic body 速度替换，然后执行场景 Capture 和 Island.Step。只有求解成功才更新限速计数，再沿用代理 Publish/场景 CommitCapture。入口即时限速仍由调用者在初态候选上调用 Begin；该接口每成功刷新一次消耗一个后续计数。调用方应先准备 Flail 参数再执行限速，以保持 ALS 先读取 pelvis 速度、更新驱动、再限速的顺序。

事务保证覆盖岛求解失败。既有 Godot 代理 Publish/场景 CommitCapture 的提交后异常不是可回滚求解，不声称本批提供任意外部错误下的整场景原子性。宿主接口尚未有普通 gameplay 调用，也未在本批单独运行其限速场景路径；真实速度替换、失败恢复和计数候选组合已在 Core 执行。

## 验证

- 新增四项岛测试：限速替换在重力前、检测收到相同 PreV；后段失败不发布，重试与无故障路径相同；非法后缀不污染下一步；失败唤醒保留睡眠历史。与上批四项计数测试共八项，在 .NET 8/9.0.17 Release 均通过。
- Core Release 固定 JIT 串行回归 2892 通过，使用既定过滤排除 AlsP5aGoldenTests 与 AlsP5aTraceSchemaTests，不称无过滤全量。
- Godot Optimize 构建零警告/错误。
- 实际 Godot scene contact 60 Hz 回归通过：3 场景、13 环境身体/形状、13 几何和 9 生命周期检查。验证默认步的实际碰撞/运动环境路径，没有开启新增宿主限速接口。

证据位于 `artifacts/ragdoll-speed-step-20260923/`：targeted.log、net9.log、core-release.log、build.log、scene.json/log。

本批无 UE 修改/原生新对照、无 Import 全量或角色落地矩阵重跑；不宣称原生速度设置的唤醒/平滑细节全部等价。静态 9/12、Flail 0/3 边界仍保留。下一步继续普通入口的 native 初速历史与物理激活、胶囊切换、既有 Flail、骨盆跟随、显示/退出，以及 Get-up/Pose Recovery、Mantle、完整 Camera、十分钟性能目标。用户 P4 规划修改保持原样。
