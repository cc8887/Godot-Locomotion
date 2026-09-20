# Overlay 完整旋转精度链（第 117 批）

承接第 116 批：修复实际采样/混合到惯性化的精度传递。本批完整 Overlay
求值通过原生对照，包括惯性化后的旋转；不再只依赖原生输入隔离模式。
正式 Demo 最终上身、来源时钟/跨图同步/通知事务仍未接入完成。

## 实现

增加 `AlsPrecisePose`、`AlsPreciseRawSequenceSampler` 及精确姿势混合运算。
原始关键帧旋转仍保留 UE binary32 的原值，进入运算后提升到 double。
骨架参考姿势、实际选中的 retarget 参考变换和 root-lock 第一帧直接从
原生 JSON 读取 double；不先经过 float 再重新归一化。资源共享现在也
比较精确参考姿势，避免两个只在 double 低位不同的骨架被错误合并。

虚拟骨骼仍在两个原始键各自展开后再插值。缺失物理轨道读取复制的参考
快照，显式虚拟轨道不重建；虚拟来源指向另一个虚拟骨骼时保留原生一次
raw-source 替换。组件旋转归一化位置不变。正/负非均匀和退化缩放使用
原生 TRS/desired-scale 矩阵路径，未替换为普通矩阵分解或剪切正交化。

新增精确 GetBonePose/GetAnimationPose 和 Overlay 来源采样入口。
保留原生时间选择器、Animation/Skeleton retarget、root-lock 门禁、
AnimScaled/AnimFrame/LocalAnimFrame/RefPose 附加基准、局部和网格空间
差量及曲线 presence。没有新增播放器时钟，时间仍由外围所有者提供。

`AlsOverlayPoseRuntime` 的 295 个节点改用精确姿势缓冲区。两路/多路
混合、状态中断栈、QuickFeet、局部/网格附加和归一化之间不再截断旋转。
`IAlsPreciseOverlayPoseSink` 直接提供精确来源；已有单精度来源接口仍
可用于受控调用，不能借此宣称该调用拥有原始 double 输入。本批原生
对照的来源明确使用精确入口，并在误调用单精度回调时直接失败。

惯性化使用第 116 批 `EvaluatePrecise`，保留 double 旋转与历史以及
原生 float 差量边界。最终输出给现有消费者时显式投影到 AlsLocalPose。
候选在整个求值成功且最终投影通过校验后发布；错误姿势/曲线不发布，
允许同帧取消/重试。

精确姿势结构也使用 double 位置/缩放进行运算，但原始位置键沿用已编译
的单精度米制数据，惯性化位置/缩放历史仍沿用现有接口。本批不声称所有
TRS 位模式均与 UE 相同；旋转链和既定位置/曲线误差门槛由对照验证。

## 原生对照

消费第 113/115 批已有原生夹具，没有修改 UE C++、重新导出资产或改写
夹具。普通 Editor 一项使用当时独立导出的文件，不表示本批重新启动了
Editor。原生数据的 schema、来源/骨骼身份、binding/index hash 按对应
入口校验。

| 检查 | 结果 | artifacts 日志 |
| --- | --- | --- |
| 原始 GetBonePose | 36 资产、848 姿势、66,992 骨检查；最大四元数距离 4.545902364027965e-16，位置 1.4699351635497893e-7 m，曲线误差 0 | `precise-source-raw-final.log` |
| 附加 GetAnimationPose | 13 附加资产、676 姿势、53,404 骨检查；最大四元数距离 7.322421347268439e-16，位置 1.71155310362145e-7 m，曲线误差 0 | `precise-source-additive-final.log` |
| Overlay 独立来源节点 | 148 节点、444 姿势、35,076 骨检查；最大四元数距离 6.193910274793601e-16，曲线误差 0；4 所有者/592 节点一致，10,000 次热采样分配 0 | `precise-overlay-sources.log` |
| 完整 Overlay，冷启动夹具 | 886 姿势通过，包含惯性化后输出；最大位置误差 1.0071215e-6 m、四元数距离 7.90745e-7、曲线 2.3841858e-7 | `precise-overlay-native.log` |
| 完整 Overlay，普通 Editor 独立夹具 | 同样 886 姿势通过，误差相同 | `precise-overlay-editor.log` |
| 原生负缩放等变换 | 15 组 compose/relative 原生案例通过 double 旋转门槛；另有缺失轨道、虚拟骨骼顺序、4 所有者/10,000 次无分配采样测试 | `precise-pose-unit.log` |
| Core Locomotion 命名空间 | 528 通过 | `precise-overlay-core-regression.log` |
| Release Import 全集 | 1,922 通过、1 既有跳过；含两种来源入口的 4 所有者/10,000 次 Prepare/Evaluate/Commit 零分配、精确来源失败不发布及同帧重试 | `precise-overlay-import-regression-final.log` |
| 正式 Worker single / parallel | 各 600 帧通过，两模式 result EAAF62E4D0A80A76、full pose EE519FBE375F4A2B、root A4F6C26CBAB8A0E7；事件 28、lag/stale 0 | `precise-overlay-production-single.log`、`precise-overlay-production-parallel.log` |
| Godot Debug Optimize=true 构建 | 0 错误、0 警告 | `precise-overlay-build-final.log` |

来源检查直接比较 double 旋转，门槛为 2e-10；完整图沿用原来的最终
单精度输出四元数距离 5e-5 门槛，没有放宽容差。完整图对照仍是 54 组/
938 帧中的 886 个可求值姿势、52 个隐藏帧，覆盖 148/148 来源及全部
886 次候选取消/重试。4 个通知和 366 个惯性化请求来自图内更新；这些
计数不等于已验证 gameplay 通知消费者。

第 116 批留下的 1,615 个超限骨骼条目在这组完整回放中已清零。失败
日志保留作为历史记录，没有删除回归用例或改写预期姿势。

Import 的既有跳过项仍为
`AlsLayerBlendingRuntimeTests.NormalEditorRepeatsTheConsumedGraphAndInputSemantics`。
本批新增 3 个 Core 案例、1 个精确参考/共享校验案例及 2 个精确图事务/
并行案例。Worker 结果保持第 116 批摘要，证明已有默认移动路径未回归；
该 Worker 尚未消费新 Overlay 最终上身，不能将这个结果作为上身接入证据。

## 完整性边界与下一步

下一项推进来源初始化/独立播放历史、SecondaryMotion/IdleAdditive/
Locomotion 同步组及通知，与外围帧事务一起提交或回滚。当前完整姿势
图仍使用原生供给的来源时间，这不是独立时钟或跨图同步已经实现的证据。

随后将 Aim/Overlay/BasePoses/LayerBlending 接入正式最终层，连接脊柱/
手部、最终曲线和 Foot IK/Lock/pelvis/平台约束，再做上身、侧身、换髋、
交错步及起步滑步的同输入/同脚相位多帧和人工验收。本批没有运行最终
上身 Demo 的人工视觉验收，不能宣称这些视觉问题已全部修好。

全 Core 的既有 23 个 P4/P5A 失败和性能 p95 2.559 ms 超出 2.5 ms
仍未关闭。新精确 Overlay 缓冲区比原单精度缓冲区大，正式整合后仍须
纳入 P7 的 10 角色/十分钟预算；零分配不等于已经通过 CPU/内存预算。
P5A 其余动作、P5B 全 Overlay/道具 gameplay、P5C Mantle/Roll/Root Motion、
P6 物理恢复/完整 Camera、P7 均保留。音频仍暂缓，未 commit/revert/merge。
