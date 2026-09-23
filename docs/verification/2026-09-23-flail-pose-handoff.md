# 独立 Flail 姿态提交

`AlsRagdollFrameRuntime` 现在预分配并保存成功提交的 Flail 源姿态，身份独立于最终 Root Blend 输出。只有被访问、已求值、处于 Flail 状态的成功提交可发布；隐藏分支或恢复 Snapshot 状态的提交清除可读身份。失败/Cancel 保留上一成功帧，精确身份读取拒绝未来、旧角色/代次和已失效帧；返回 false 不写调用者缓冲。

生产 layered→movement→controller→worker→character 已转发只读复制接口。角色入口要求 Main，绑定当前 Main 提交身份及存活代次；输出为逻辑 FBX 局部姿态，不是 native 电机输入。未创建第二套 Flail 时钟，没有从 live skeleton 采样。

## 验证

日志在 `artifacts/flail-pose-handoff-20260923/`：

- Core RagdollFrame 17 项在 .NET 8/9 通过。新增用例覆盖提交前不可读、调用者修改隔离、源采样失败保留旧帧、异角色/代次拒绝、Snapshot/隐藏分支提交失效和重新进入恢复发布。
- Godot Optimize 构建通过，零警告/错误。
- 真实资源 RagdollFrameSmoke 私有/共享源两模式均通过 30/60/120 Hz，分别每 owner 120/240/480 帧，四 owner 单线程与四 owner 并行逐帧重试结果相同。
- 新断言逐帧检查已提交 Flail 与根混合前的真实源姿态逐值相同；只有有效源状态可读。两模式 pose/curve/clock digest 相同：30 `BD11AAE5F41C01D476FB4690C78FB14D61600ECAD4AEF7A20351A99F2583A243`；60 `E456BB269B9D64373A7D86298B49B3082F0A01B6E820DE5CF187B9ADDFA1A90D`；120 `488FE9023A9ECC542A213347B1C05CBA4ACA757CC8301761C03EECC9C16030DC`。

本批只完成独立源姿态发布及生产访问接口，测试为实际消费者；尚未连接 native 骨骼转换、电机组装和实际身体步进。下一步按资产骨骼名字/父链严格绑定、转换局部坐标并验证目标，再完成普通角色 Ragdoll 进入/运行/退出。最终动画交接缓冲仍仅用于初态，不能替代本源。

无新 UE 导出/构建、全量测试或物理矩阵。最近物理矩阵仍 9/12（三项 30 Hz 旧失败）；普通 demo Ragdoll、Get-up/Pose Recovery、Mantle、完整 Camera 和十分钟性能目标未完成。所有变更在主目录 main，用户 P4 修改保留。
