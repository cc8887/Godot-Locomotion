# Mantle 宿主骨架与曲线采样适配

本批完成宿主采样接口与共享 PostLocomotion Slot 的组合验证，普通角色入口尚未接通完整 Mantle。

## 实现边界

`AlsMantlingHostPoseProfile` 接收上一批映射后的 Montage profile、实际宿主 raw skeleton 和显式曲线布局。按实体骨名字、父子关系和参考姿态容差验证兼容性，保留原生物理骨关键帧、重定向参数和锁根策略。它是同一实体骨皮肤的兼容适配，不是任意骨架重定向。

两套骨架的 11 个虚拟骨定义不同，因此使用宿主虚拟骨定义，在两个原始关键帧分别生成虚拟骨，再插值。最终双精度姿态统一从原生厘米转换为 FBX 米；没有先转成 float 姿态再采样。重定向模式与源参考按实体骨索引，目标参考按逻辑骨索引。

曲线按真实名字映射，要求宿主明确包含 PoseStanding 和 PoseGrounded；不存在的曲线保持 absence，没有伪造 FootLock 或隐式套用 V4 别名。每个角色采样实例拥有独立 scratch，拒绝同实例并发重入；输出尺寸或资源身份无效时不部分写调用方缓冲。

## 验证与失败记录

- 首轮 3 项失败来自测试硬编码骨架 ID 0，改为从真实资源请求的动画读取 SkeletonId。
- 第二轮 3 项失败暴露适配器把重定向数组错误分配为逻辑骨数量。改为实体骨数量，并通过 PhysicalToLogical 保存源参考；未放宽误差门槛。
- 最终 Import Release 定向 110 项通过，0 失败、0 跳过。包含既有原生 Mantle 姿态和两种编号下的 branching/queue 对照。TRX：`artifacts/mantle-host-pose/import-final.trx`。
- 新物理骨测试：六动作 × 61 时刻，366 姿态、24888 次实体骨比较；转换后与未适配的原始采样逐值一致，曲线值与 presence 一致。
- 新虚拟骨测试：三源 × 29 个非整数键时刻 × 11 骨，共 957 次。独立从原始实体骨键计算两端虚拟骨再插值；位置平方误差小于 1e-24、四元数 dot 偏差小于 1e-10。这不是新的 UE 宿主虚拟骨 oracle。
- 四个独立采样 owner 各 100 次并行采样与串行逐值一致；非法身份不改变输出，缺少或重复曲线布局被拒绝。
- 六动作各 240 帧、60 Hz，使用共享 Slot 验证淡入、全权重、淡出与最终精确返回宿主原始姿态/曲线。不是普通角色完整图验收。
- Optimize 构建 0 警告、0 错误。
- 既有 Godot headless MantlingVisualSmoke 退出码 0：两模型、三源、186 姿态、12648 次骨检查、24 次拒绝检查，最大位置误差 5.749995e-7 m。日志：`artifacts/mantle-host-pose/visual-regression.log`。该场景回归原有原生姿态绑定，不验证新适配器的渲染或攀爬运动。

本批没有 UE 修改/重新导出、Core 全量或新渲染截图。用户 plan 哈希保持 78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100；project.godot、头颈诊断和外部 uid 不纳入提交。

## 后续

接入普通宿主的曲线布局、正确 PostLocomotion 图位置及 typed notify dispatch，再串接障碍探测、移动基座、独立 Mantling motion 生命周期与进入/中断/Ragdoll 转换。映射后的动画编号仍不能索引旧 V4 set.Animations。

其余未完成项保留：物理稳定性 9/12、Flail 0/3、复杂相机、非恒等 OrientAndScale 原生对照、旧移动 oracle 闭包、完整视觉和十分钟性能验收。头颈、道具物理和音频暂缓；脚步粒子/贴花尚未执行。
