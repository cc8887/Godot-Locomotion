# 普通角色 Ragdoll 激活初态

主目录 `D:/GodotALS`、main，接续 e75b5ed。本批完成普通角色的激活候选准备接口，尚未开启普通 Demo 的动态 Ragdoll。

## 实现

`AlsCharacterBodyHistory.PrepareActivation` 在 Main 读取当前已提交姿态、同帧世界变换和角色速度，继承最近已完成物理历史的逐身体 V/W。动画身份和速度来源的物理身份分别返回；新动画目标尚未经历物理步时，不用新目标差分速度替换旧速度。固定身体按 PhysicsAsset 保持零速度。

待处理 teleport 立即使候选 V/W 归零，但准备操作不消耗 teleport，也不修改已完成历史。可选进入限速调用既有 Core 实现：max(200 cm/s, 角色速度)，立即限制线速度，返回后续八次刷新预算；角速度不变。重复准备结果相同，不提前扣除刷新预算。

所有身体在预分配私有缓冲中完成验证和限速，成功后才复制给调用方。无历史、停用、错误帧/代际、错误身体数量均拒绝；不能据此接口认为实际动态 owner、胶囊或动作状态已经切换。MarkTeleport 的普通传送调用方仍待接线。

## 验证

优化构建零警告/错误，见 `artifacts/ragdoll-character-activation-20260923/build.log`。首轮测试编译使用了只读身份的 with 赋值，改为构造身份后通过；未改身份类型。

普通完整动画图恢复 smoke 增加：真实身体姿态与骨骼世界变换核对、动态 V/W 继承、固定身体零速、初始限速/八刷新预算、重复准备、错误身份与短缓冲不改输出、pending teleport 清速且不消耗、已完成历史不变。

额外在第13帧动作提交回调中调用接口，此时动画13已提交而身体历史的动画来源仍是12。检查新姿态与旧已完成速度组合，准备后两条时钟均不推进。这是真实普通角色的提交边界，不是仅用构造数据模拟。

最终日志为 `single-verified.log`、`parallel-verified.log`、`frozen-verified.log`、`rolling-verified.log`：分别覆盖 Single 恢复、Parallel 替换、四故障冻结、实际 Roll 位移与恢复。冻结场景没有13帧成功回调，只覆盖已完成/冻结激活候选；另外三组必须命中 pending 激活边界。四组均要求退出0且无 ERROR。

首次运行 single.log 因测试错误假设所有身体均动态而失败；按资产 PhysicsType 区分后通过。中间 `*-final.log` 是增强世界姿态核对前的通过日志，不替代最终 verified 结果。

## 剩余工作

该接口目前由回归场景消费，普通 gameplay 尚未创建动态岛、切换胶囊/移动状态或显示物理姿态。下一步用候选创建动态 owner，接入已有 Flail 动画源、接触与后续限速，再骨盆/胶囊/相机跟随和退出恢复。不能另建 Flail 播放时钟。

本批无 Core/Import 算法或 UE 源码变化，没有重跑其全量测试或落地矩阵；静态9/12、Flail0/3及旧UE退出异常仍未关闭。Get-up/Pose Recovery、Mantle、完整 Camera 和最终十分钟性能目标继续保留。用户 P4 规划未修改、未纳入提交。
