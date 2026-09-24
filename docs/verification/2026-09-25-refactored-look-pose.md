# Refactored Look 原始加法姿态与混合

新增 `AlsRefactoredLookPoseCompiler` / `AlsRefactoredLookPoseSource`。严格绑定原始 Forward、Down、Up 三序列及 Stand 第0帧，保留实际79骨/11虚拟骨布局、原始键、retarget规则和厘米单位。采样归一化时间先 clamp 到[0,1]，序列均为一秒；不推进时钟、不提取根运动或通知。

底层复用 `AlsMantlingPoseCompiler` 的原始键构建，新增仅内部的 `CompileRawAdditiveTargets` 入口。该入口返回绝对原始姿态，调用者先验证加法政策，再自行生成差值；旧 public Mantle 与 standalone basepose 入口仍只接受非加法，不放宽旧数据政策。Look 明确限制为 RotationOffsetMeshSpace/AnimFrame/baseFrame0、关闭根锁与rootmotion、空曲线；其他策略拒绝。

每个采样 owner 持有独立 scratch，预采 Stand参考，目标先 raw sample/retarget/VB，再用共享 MeshDifference 生成加法。SampleBlend 消费调用者提供的有序、归一化样本权重，执行原生顺序的加权pose累加、多源旋转归一化。**该类不计算 pitch→样本权重**，此能力仍待实际 BlendSpace 描述绑定；测试中的权重和顺序来自 UE 参考，不能称端到端 BlendSpace 完成。

## 数值与回归

- 初次单源验证25姿态1975骨通过；扩展混合后全部35姿态2765骨通过（前者包含在后者中）。
- 对上一批冻结 UE `GetAnimationPose` 参考，max position `6.434238129413513E-14` cm、四元数分量 `4.440892098500626E-16`、scale差0。预设位置1e-4cm、旋转/scale1e-6，未修改容差。
- 4个独立worker×3序列×31时刻：根锁关闭时，把此前冷/编辑器不一致的 rootLockFirstFrame.scale 从1改0，不影响任何输出；原始键和姿态政策决定结果。该测试不定位辅助导出差异的引擎内部根因。
- 非归一化权重拒绝后仍可正常采样；错误baseFrame、根锁、时长、加法类型、非空曲线策略拒绝。
- 新增7项；Import RefactoredLook/Head/BasePose/Mantling 定向 **119通过、0失败**。Optimize构建 **0 warning、0 error**。
- TRX和最大差值位于 `artifacts/refactored-look-pose/`。没有新UE导出、Godot运行、Core/Import全量或视觉验收。

下一步是独立计算并验证原BlendSpace权重、Head图回调/相关性/求值整合，仍需连续View/Spine/Head状态oracle。普通Demo尚未切换；完整宿主、Mantle/恢复、物理稳定性、Flail、最终预算等缺口及用户暂缓项保留。
