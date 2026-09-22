# 真实流形恢复与原生容差

本批直接在 D:/GodotALS 的 main 完成。修正 native polygon 恢复容差经过 Godot 米制 float 代理包围盒后再换回厘米的舍入偏差；使用已有原生厘米 leaf bounds。右手、左手实际保留流形的 UE 恢复对照通过，但完整物理矩阵仍为 **8/12**，四项休眠失败没有关闭，普通角色尚未接入 Core 刚体后端。

## 修正与实际证据

Godot query 原先从 Shape3D 的 Aabb.Size 计算恢复容差。Chaos 从实际 implicit leaf bounding box 的最长完整尺寸计算：先存为 float，再取两形状较小值并乘 0.1f。原生几何绑定已有厘米 LocalBounds，现在直接使用它，保留非原生绑定的原有计算路径。碰撞 margin 不缩小此包围盒。

普通 120 Hz 两模型各 23 帧（零起点 76、77、78、140..159）共 182 次非零恢复容差检查：

| 接触形状 | 原生 float 容差（cm） | 修改前 Core（cm） | 不同次数 |
| --- | ---: | ---: | ---: |
| AnimMan hand_l | 1.560869574546814 | 1.5608696937561035 | 22 |
| Mannequin hand_l | 1.5608688592910767 | 1.560868740081787 | 23 |
| Mannequin calf_l | 2.700000047683716 | 2.700000286102295 | 23 |
| Mannequin calf_r | 2.700000047683716 | 2.700000286102295 | 23 |

修改后 182 次全相同。Godot 回归遍历全部 7 个实际资产 box；修复前先在 neck_01 失败（1.5035836 对 1.5035834），修复后全部通过。原生左手恢复导出也在修复前以退出码 53 拒绝容差不一致，未产生目标文件；修复后同类实际输入成功导出。

## 同输入恢复对照

- Core 提供只读 value snapshot：已提交 key/epoch/tolerance、上次新建流形时的相对位置/旋转基准、当前保留点、初始点、法向、禁用状态、Phi。新一步尚未发布时仍读取旧已提交状态，不新增热路径数组。
- opt-in Godot 捕获将这些状态与当前 cull 写入 historyInputs。新增捕获前后的 46 个样本各 24 个求解阶段完全不变。
- 导出工具筛选实际单 box 手部与静态 environment_0，绑定 runtime/setup/capture 哈希，验证同 key、连续 epoch、非空旧流形及容差一致。
- UE 从实际 box 尺寸和 margin 建约束，写入当前保留点和 InitialShapeContactPoints。通过 SetLastShapeWorldTransforms 的公开接口还原其内部存储的位置差和旋转差，随后用实际当前世界姿态执行 TryRestoreManifold。用于还原差值的两个构造姿态不代表历史世界姿态。
- UE 自己决定恢复/拒绝、更新点/Phi，并单独输出最小 Phi 是否在当前 cull 内。不会把 Core 的 capturedRestored 当成原生结果。

| 样本 | 输入数 | 原生恢复 / 拒绝 | 精确接触点 | 下一捕获观察到的发布点 |
| --- | ---: | ---: | ---: | ---: |
| 右手（容差修复前捕获，右手容差本来相同） | 43 | 37 / 6 | 148 | 140 |
| 左手（容差修复后捕获） | 44 | 35 / 9 | 140 | 136 |

两运行时的 Core 重放与原生点序、point0/point1、法向、禁用状态、每点 Phi、minimumPhi、恢复决策一致；Godot 当前检测点和下一帧保留的点/Phi 也一致。旧 72 组/576 帧合成恢复参考、45 姿态冷/同姿态 warm 查询、8 组实际 GJK cache 查询仍通过。

边界：native probe 的 owner identity/epoch 资格是捕获输入，未重建完整 midphase/world 生命周期。这里的手部点均未禁用，且 point0 等于 initial0；Core 测试因此能通过已有 PrepareNew 接口重建恢复所需状态，无需添加可写生产状态导入接口。禁用点仍由旧合成参考覆盖，不能把本批称为所有实际接触的等价证明。

## 验证

所有产物位于 artifacts/physics-restore-capture-20260922：

- Core 定向 21 项，.NET 8.0.28 与 .NET 9.0.17 均通过（含快照 pending/publish/abort/reset、旧零分配和 GJK cache 生命周期）。
- Import 最终定向 5 项，两运行时均通过。首次新测试编译的参数名/命名空间错误已修正，原日志保留。
- 最终 Godot Optimize 构建 0 warning / 0 error；30/60/120 Hz 接触 smoke 全通过，各 manifold_checks=12（旧 5 + 实际 box 7），旧 551 precision、76 polygon 等检查仍通过。
- 整链 12 项重跑：30 Hz 只有高速通过；60 Hz 全部通过；120 Hz 高速、平移、旋转通过，普通失败。失败均为十秒休眠要求，非崩溃。八份成功报告与上一 polygon 变换批次逐字节一致。
- 容差修正前后 46 帧 × 24 阶段逐值不变。普通 120 Hz AnimMan 在 658 步睡眠，Mannequin 到 1200 步仍醒；末秒线速度 2.242934226989746 cm/s、角速度 0.27109295129776 rad/s、累计接触点 96299、最大锚点误差 0.503317728969182 cm 均未变。不能声称本批改善了轨迹或解决了休眠。
- 本批没有重跑 Core/Import 全量；最近全量记录仍为 Core 2860（既有两类排除过滤）、Import 2448 + 1 旧跳过。

UE 按 ue-diagnosing-plugin-build-load 技能完成完整项目 Editor target 构建和插件审计。首次构建的 FRotation3 构造参数错误已修正，完整重建成功；输入 fingerprint 为 7441834B9E89DC5FFA5676808976D6FB9FC265CE7D1E62C9C2F6D36BF3B80F43。两套新参考均独立冷重导字节一致，旧实际 GJK 参考重导 hash 保持 9B18D747C15D0C495763D465B95BF23891220CBE33F9E4EC1E3A001F8F36004A。

DataValidation：0 error / 3 warning。普通 Editor PID 16540 加载导出类，记录 ALS_RESTORE_CAPTURE_EDITOR_RESTART_OK，原生退出码 0。两个旧 Condition failed 仍在；既往间歇 0xC0000005 不能据此次成功判定已修复。

冻结参考：

| 文件 | 字节 | SHA256 |
| --- | ---: | --- |
| assets/config/v4_physics_manifold_capture_reference.json | 429701 | A31013994E491C8F12EC6B922B07CC32681804610EB76EFCE37E1464E4807F22 |
| assets/config/v4_physics_left_manifold_capture_reference.json | 443588 | 7F8B9ED5171D5254E7BE4565150C01B1CF4A0A65433F05EE53E612D8C26F66A2 |

导出 cpp 主仓库和 UE 插件镜像哈希一致：CDCEFE4BE7725B2CDCA74B50C7426B69283EFECA78E1EEDF858BA59A9868E906。没有保存 UE 资产。

## 后续

手部的实际 GJK 和流形恢复已在相同输入下核验；下一步围绕 Mannequin 第 147–150 步周边动态接触，比较真实世界的接触资格、创建/激活顺序及上游状态，必要时向首次分歧前移，避免反复只验证右手公式。普通/平台低频与普通 120 Hz 稳定性、普通 Ragdoll/Get-up/Pose Recovery 接入、Mantle、完整相机与最终十分钟性能预算等总目标仍待完成。

用户 P4 规划文件未纳入本批，内容哈希仍为 78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100。
