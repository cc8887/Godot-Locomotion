# 待执行目标的 Ragdoll 激活状态

主目录 main，接续 `7d0beae`。补齐入口状态读取与逐身体速度到 Godot/Core 身体的传输。普通角色尚未调用这些接口，未启用 Ragdoll gameplay。

`AlsKinematicBodyHistory.CopyPendingActivation` 要求精确 pending target 身份和完整身体布局。普通目标输出新 actor pose 与最后完成物理步的 V/W，不使用 pending 位移的差分速度；teleport 输出新姿态和零 V/W。返回单独的 completed velocity-source identity，避免把两个时刻标为同一份物理历史。读取不提交、不消费 target；Cancel、错误身份、长度或已完成的 target 拒绝读取。调用者必须只为已经接受并提交给物理 owner 的目标开放激活读取，不能把失败的动画候选当作已接受目标。

这沿用上批源码/原生时序观察：PositionTarget 提交后速度尚未刷新，物理步完成后才产生；TeleportPhysics 在游戏线程立即清速度。未新增原生导出；新 API 的非零旧速度与不同目标差分组合由 Core 测试覆盖，不声称本批新增完整原生入口轨迹对照。

`AlsCorePhysicsPose.SeedWithBodyVelocities` 接受 asset body 顺序的 native V/W，先验证全部速度，再验证并构建 pose，成功后将动态身体速度逐值继承。不重新变换坐标或叠加质心杠杆臂；asset Kinematic 身体仍使用当前岛合同要求的零速度，环境数组后缀不变。失败不得改变目标身体状态或 SeedIdentity。

验证：Core 历史五项 Release 测试在 .NET8/9.0.17 通过，含 pending 新姿态+旧150cm/s而非新850cm/s、瞬移立即清零、旧完成帧仍保持、取消/错代际/提交后拒绝。Godot Optimize 零错误/警告。实际两模型各四姿态 smoke 通过：160 旧姿态交接、192 回写、40 拒绝检查、新增160逐身体速度逐值交接；最大位置误差4.4284284e-6m、basis6.415968e-7，旧速度场误差1.1920929e-7m/s。新 native 速度继承使用逐值相等断言，无容差。

证据 `artifacts/ragdoll-activation-state-20260923/build.log`、`pose.json/log`、`net9.log`；.NET8五项结果见本次终端测试输出。无 UE 源码/构建变化，未重复全量或矩阵。下一步普通角色历史 owner、目标与物理阶段排序、激活/胶囊/Flail/骨盆跟随/显示退出及全部剩余目标。静态9/12、Flail0/3稳定性边界未变。用户 P4 规划修改保留。
