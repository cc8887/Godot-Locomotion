# Ragdoll 进入后的限速状态

主目录 main，接续 `4288c41`。实现 `AlsRagdollSpeedLimit`，用于普通 Ragdoll 生命周期后续接入；目前尚无 gameplay 调用，不代表普通 Ragdoll 已启用。

## 源码依据及实现

本机 `D:/AdvancedLocomotionSystemV/Plugins/ALS/Source/ALS/Private/AlsCharacter_Actions.cpp`：

- StartRagdollingImplementation 811–823：可选开启；上限为角色三维速度长度转 float 后与 200 cm/s 取大；计数设为 8，并立即调用 ConstraintRagdollSpeed。
- RefreshRagdolling 955–960：剩余计数大于零时减一并限速。因此总计进入一次，加后续八次，而非总共八次，也不是按秒计时。
- ConstraintRagdollSpeed 997 起：遍历角色身体，仅当线速度平方超过 float 上限平方才归一化乘上限。保留角速度；低速身体不会被赋予角色速度。

Core 接口使用 native cm/s、double 长度/归一化和 float 身体速度存储。输入只能是角色身体前缀，不包含环境身体。先校验整个前缀的线速度，再修改候选状态；只返回不可变的新计数，调用者必须在所属物理帧成功时发布该计数和身体状态，失败则丢弃候选。Begin 的即时限速也必须在候选初态上执行。

本批不推断初始逐骨速度，不从动画位置差分制造速度，也不使用 Reset 岛的方式每帧限速，以免破坏持续接触/睡眠历史。实际 owner 的事务及岛内速度更新接入仍待实现。

## 验证

四项 Release 定向测试在 .NET 8 与 .NET 9.0.17 均通过：进入+八刷新、保持角速度和姿态、完整三维角色速度上限、低速保持、后部非法输入不部分写入、候选重试结果一致且旧计数不变、关闭选项及非法入口保持输入。优化 Godot 构建零错误/警告。

日志：`artifacts/ragdoll-speed-limit-20260923/core.log`、`core-net9.log`、`build.log`。初次编译因 Core.Math 遮蔽 System.Math 失败，已用明确限定修正。未新增 UE 运行时 oracle，不宣称本批逐值原生数值等价；未重跑 Core 全量、物理矩阵或普通 Demo。

下一步核对普通 UE 身体进入时的速度历史，接物理 owner 限速事务、胶囊停用、既有 Flail、骨盆跟随/显示/退出，再全部 Get-up/Pose Recovery、Mantle、Camera、十分钟性能目标。静态 9/12、Flail 0/3 旧稳定性边界保留。用户 P4 规划修改未改变、未纳入提交。
