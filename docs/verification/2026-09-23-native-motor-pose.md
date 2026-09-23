# Flail 到 native 电机输入的骨骼转换

新增 `AlsNativeMotorPose`：按名字绑定全部原生骨骼并核对直接父链，允许逻辑布局包含额外虚拟骨骼；拒绝缺失/重复名字、非法父序和不同父链。将已提交逻辑 FBX 局部姿态转换为 UE 厘米局部姿态：位置 `(X,-Y,Z)*100`、四元数 `(-X,Y,-Z,W)`、保留缩放，不混用世界坐标变换。预分配候选缓冲，全部验证通过才发布。

真实资源 `RagdollFrameSmoke` 已串联已提交独立 Flail→native 转换→AlsRagdollMotorInputs，沿用独立时钟和轴向启用标志，保存本回放的上一目标；这只是动画/输入连接验证，没有调用物理 Step。

## 验证

日志目录 `artifacts/native-motor-pose-20260923/`。

- Godot Optimize 构建零错误/警告。
- Import 八项定向测试在 .NET 8/9 通过。两套真实骨架均验证插入虚拟骨骼后的索引映射、参考姿态转换、父链错误拒绝、末骨 NaN 不部分发布和 2048 次零分配转换；已有真实目标/K/C参考继续通过。
- 共享源真实动画回放通过 30/60/120 Hz，四 owner 单线程与四 owner 并行逐帧 retry，120/240/480 帧每 owner；每个有效 Flail 提交均转换并生成有限、近单位关节目标。原有 pose/curve/clock digest 与前批相同。
- 首轮测试编译误用不存在的 Length 属性，改为 dot/sqrt 后通过；首轮日志保留。转换位置误差门槛 1e-4 cm 仅用于 float FBX 参考姿态往返，不能称无损恢复原始 double 动画。此回放也未证明实际采样 motor targets 与 UE 各帧完全一致。

尚未接普通角色身体积分：下一步实际 host 的驱动提交、骨盆反馈、初态 Seed 与胶囊/角色位置/退出生命周期。普通 demo Ragdoll 未接通，最近物理矩阵仍 9/12（三项 30 Hz 旧失败），Get-up/Pose Recovery、Mantle、完整 Camera、十分钟性能继续保留。无新 UE 导出/构建、物理矩阵或全量测试；主目录 main，用户 P4 修改保持原样。
