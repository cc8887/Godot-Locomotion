# Flail 物理输入归一化与精度修复

定位到实际移植遗漏：本地UE `SkeletalMeshComponent.cpp:864` 的 `FinalizePoseEvaluationResult` 在发布 BoneSpaceTransforms 前执行 `InFinalPose.NormalizeRotations()`。当前 motor adapter 直接使用采样姿态，缺少此边界。neck_01 样本长度平方为1.0000002000929062，原生为1.0000000000000002。只绕过最后的float缓冲未消除该误差；加入对应的旋转归一化后目标对照才恢复。

`AlsNativeMotorPose` 现在先验证输入，再按UE骨骼发布语义归一化旋转。不能在矩阵目标算法末端随意归一化代替此步骤。另增加double局部姿态转换入口，避免帧间插值结果在进物理前被显示用float缓冲截断。

`IAlsPreciseRagdollPoseSource` 可直接提供精确源；RagdollFrame预分配保存候选/已提交double Flail，显示仍由该同一采样转single，不增加时钟。成功后复制、失败/Cancel保持、隐藏/Snapshot提交失效；只有float源时明确没有精确发布，不能伪造精度。生产角色接口增加同身份只读复制，实际场景物理runtime和故障重试回放使用精确入口。

## 原生对照

产物 `artifacts/flail-precision-20260923/`。

- 两模型五时刻共180目标，最大局部旋转分量误差2.77556e-16，目标3.33067e-16；精确模式门槛收紧至1e-12。位置仍有1.17162e-5cm差，不宣称完整TRS无损。
- `CompareFlailWorld -AllFrames` 新增逐帧及身体旋转比较。相同60Hz配置下，**Mannequin全部600帧的身体P/Q/V/W逐值一致**，不再只是选定帧；原生JSON速度恢复float存储后比较。该结论只适用于此模型、配置、时长和捕获字段。
- AnimMan前64帧身体P/Q/V/W一致，第65帧首差：P分量1.19292e-4cm、Q6.36280e-6、V.00329876cm/s、W.000470072rad/s；该帧输入时间/rate/pelvis/K仍同，target仅3.33e-16差。全600帧最大P.213193cm、Q.0148827、V3.915094cm/s、W.539254rad/s，Core209帧睡、native220帧睡。
- 不能把Mannequin匹配推广成全部场景或AnimMan问题已修。下一步重点是AnimMan65帧接触/约束阶段，而非继续盲改整个采样系统。

## 稳定性和回归

| Flail普通落地 | 结果 |
|---|---|
| 30Hz | 通过，AnimMan147/Mannequin163帧睡，末秒V/W=0 |
| 60Hz | 仍失败；末秒V20.743444/W1.790171，Mannequin现与原生一致，但两端均未满足20cm/s和休眠门槛 |
| 120Hz | 回归失败；M476/A1087帧睡，A保持不足120帧；末秒V1.416486/W.185688，未放宽一秒门槛或延长用例 |

新Flail矩阵仍1/3，但通过项由120变30。静态目标旧矩阵9/12本批未重跑，不能与新模式混计。

Core定向18、Import定向8在.NET8/9过；新增验证double保留、失败保持、旧float源拒绝精确发布、归一化和无效输入原子拒绝。Import首次测试编译使用target-typed new乘法错误，改显式类型后通过，日志保留。最终Optimize零错误/警告。

共享源真实资源三频率普通动画回放digest与前批完全相同；精确Flail物理回放三频率单线程/并行逐步故障重试通过，已提交double源逐值保留，新的物理digest两执行方式相同。没有新UE导出/构建或Core/Import全量；原生参照沿用上批同条件冷导。

普通角色Ragdoll生命周期、Get-up/PoseRecovery、Mantle、完整Camera、十分钟性能等目标继续保留；还需核对120Hz原生结果和AnimMan首次分歧。主目录main，用户P4修改未动。
