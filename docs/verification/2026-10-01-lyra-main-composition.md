# Lyra Main 原合成节点与14入口组合

本批推进完整 Main49节点所需的原 node0/3/76/72：上下身 split、动态 local additive、恢复 additive 与根骨旋转。原模型仍为 ALS skin68，完整求值为 logical81。组件和共同调度已验证，完整 Main、普通 Demo 和生产验收保持开放。

## 原节点与运行边界

- `AlsLyraMainCompositionLibrary` 在临时角色的原 Main 类中取得真实编译节点和输入处理器，执行原 node3→node0 与 node76→node72 两段。节点间的 Slot/Aiming 输入由独立 ALS Sequence 叶提供；未包含缓存遍历、活动 Montage、主惯性化、SkeletalControls 或最终 ControlRig。
- node0 原 `UpperBodyLowerBodySplitMask` 按骨名映射到 ALS81，保留全部源 BlendProfile 条目。不能按 Manny 数组索引截取；原 root 权重0，ALS-only虚拟骨0，`weapon_r` 和武器空间虚拟骨按原同名权重绑定。原 Override 曲线、mesh-space rotation、本地位移/缩放和 typed 属性策略保留。
- node0 原 `bUpdateBasePoseFirst=false`，真实更新顺序为 upper→base→动态 additive；node76 为 base→恢复 additive。两段所有上下文权重逐位验证。
- node3 的输入从 Main double `UpperbodyDynamicAdditiveWeight` 转为 float，再经原 `FInputScaleBias::ApplyTo` 钳制0–1。node76 原0.65f。负输入、阈值两侧和超过1的输入均覆盖；没有按序列化alpha或curve名推断运行权重。
- node72 yaw 从 Main double→float；pitch0、MeshToComponent0，`bRotateRootMotionAttribute=false`。根骨改变时，RootMotion属性保持原语义。原 `Update_AnyThread` 在 `BasePose.Update` 前执行输入处理器，故组合使用图前 macro 快照；不能使用较晚 Idle/cache 回调写回的 RootYaw。

`LyraMainCompositionOperators` 不创建播放器、时钟、Sync 或反馈发布点。复用完整数据缓冲，在真实 Slot/Aiming/惯性输出的明确边界执行 Upper、Additive 和 RotateRoot；四个算子向后续完整 Main 宿主提供复用入口。输入骨架布局、完整曲线/属性包、有限值与原资源哈希有门禁。

## 独立原节点连续对照

Unarmed/Pistol/Rifle、30/60/120Hz，每组6秒：

| 项目 | 结果 |
| --- | --- |
| 更新帧 / 有求值帧 | 3780 / 3078 |
| 两段完整姿态 / 骨骼 | 6156 / 498636 |
| 曲线 / 整数属性比较 | 6156 / 24624 |
| 隐藏 / 仅更新 | 189 / 513 |
| 上身改变 / 恢复改变 / 根旋转 | 3078 / 3078 / 1719 |
| 被钳制输入的求值帧 | 594 |
| 最大位置 / quaternion / scale误差 | 1.0073681891326079e-13 cm / 8.196123257993793e-16 / 0 |

位置1e-8cm、quaternion1e-10、scale1e-12原门槛保持。全部输出逐项比较曲线存在性/值/flags、四个原整数属性和RootMotion身份/存在性/TRS；重复Evaluate不推进任何历史。RootMotion使用现有压缩codec采样，不能替换为raw轨迹。

两次独立UE v2采集实际退出0，各0错误/169条原资源与临时依赖警告，fixture语义完全相同。508个源/目标资产包及685份旧JSON逐文件字节保持；workspace/source/package的探针与复用helper源码哈希一致。新ignored数据为 `main_composition_v2_{requests,policy,native}.json`，没有资产保存。

## 14入口共同组合

新增 `LyraMainCompositionScopeSmoke` 将已有自主Main→LeftHand→Upper→Aiming→恢复Additive→RootYaw→SkeletalControls 放入同一候选事务，最后反馈进入原Main/Linked统一提交。Slot边界约束为inactive source/ref-additive；没有原完整缓存宿主、主惯性化或最终ControlRig。解析地面、受控控制曲线覆盖和新增actor yaw/pitch扫描均为测试输入，不能称Godot生产物理或联合UE完整Main验收。

三Provider三Hz共11340帧、9762姿态，14入口共用一次Sync；clean/retry输出全通道相同。207次晚期重试、2604次坏节点/epoch/签名/候选拒绝、84次足部查询故障恢复和foreign角色隔离通过。只将实际非零恢复姿态计为恢复应用，避免把恒等Additive重新规范化造成的舍入变化算作覆盖；仅Idle的轨迹允许恢复计数0。原source/candidate/曲线历史的取消门禁保持。

原Main ALS LocomotionSM的11340帧/9762混合姿态/12595根姿态回归通过。最终Debug和Optimize ExportRelease均0错误0警告；本批没有修改Core/Import或重跑它们的全量测试。验证入口 `tools/verify_lyra_main_composition.py`。

## 失败证据与开放项

新探针首次因`FLeaf`与UE类型重名编译失败，修正为`FCompositionLeaf`；Godot夹具FileAccess名称歧义与误从固定SDK目录构建的失败日志保留。首v1请求使恢复片段的previous超过末尾，触发原UE forward extraction ensure，进程退出1；其三份JSON保持原字节，v2使用片段内区间后两次正常退出。首Godot根属性比较误用raw采样，改为既有compressed codec后严格通过；首组合没有actor yaw变化导致根旋转覆盖失败，补真实观察字段扫描。没有修改算法阈值或跳过失败门禁。

本批只关闭四个原合成节点与其共同角色组合。下一步为原上下身/PreAim缓存和五个真实Slot/Montage、主惯性接收、最终ControlRig及实际最终反馈；继续完整Main联合native、换Provider、统一Notify、Godot地形/平台、生产Demo、画面和性能验收。Foot首次存储初始化的既有原v1重复性失败仍开放。无提交或推送，用户已有修改保留。
