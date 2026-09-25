# Refactored 方向状态姿态图与共享缓存边界

## 本批实现

`AlsRefactoredDirectionPoseGraph` 从原 Standing/Crouching 蓝图的 compiled 节点、runtime 属性及 nativeText 编辑图编译方向状态内部接线。绑定已验证的 `AlsRefactoredDirectionResources`，拒绝跨 catalog 使用。每个 stance 六状态，每状态八节点，共 96 个状态内部节点；24 个 UseCachedPose 读取指向六个共享缓存，两 stance 合计 48 个读取和 12 个缓存。

保留原始 propertyIndex，不与倒序 compiledNodeIndex 混用。逐状态核对：StateResult → OnBecomeRelevant SetHipsDirection → ModifyCurve → 四路 MultiWayBlend → UseCachedPose；编辑图与 runtime 链接必须一致，回调参数仍从原外层暴露引脚读取。

四通道始终是 VelocityBlend 的 ForwardAmount、BackwardAmount、LeftAmount、RightAmount；实际读取的缓存如下（省略名称前缀 Move）：

| 状态 | Forward | Backward | Left | Right |
| --- | --- | --- | --- | --- |
| Forward | Forward | Backward | Left Forward | Right Forward |
| Backward | Forward | Backward | Left Backward | Right Backward |
| Right Forward | Forward | Backward | Left Backward | Right Forward |
| Right Backward | Forward | Backward | Left Forward | Right Backward |
| Left Forward | Forward | Backward | Left Forward | Right Backward |
| Left Backward | Forward | Backward | Left Backward | Right Forward |

六状态的 RotationYawOffset 分别绑定 ForwardAngle、BackwardAngle、RightAngle、RightAngle、LeftAngle、LeftAngle。校验原 Blend 模式、alpha=1、唯一曲线、空 curveMap、MultiWay 归一化及非 additive 策略；保留缓存 writer 的源节点身份并交叉核对原编辑图。未知连接表达式与未消费的状态内部动画节点被拒绝。

关键边界：Crouching 六缓存源都是已有 movement SequencePlayer；Standing 只有五个方向缓存直接读 movement player，前向缓存 source=139 是另一个混合节点。这里没有把它错误简化为 Forward BlendSpace，也没有为每个状态复制播放器或时钟。

## 验证

- 新增四项测试：两 stance 的所有状态接线/48 个独立读取/12 个共享缓存，以及 20 次错误资源变异拒绝。覆盖归一化、additive、曲线 alpha/名称、速度与 yaw 属性绑定、缓存误连、Pose 误连、状态回调和 writer 身份错误。
- `artifacts/refactored-direction-pose/graph-initial.trx`：4 通过。
- `related.trx`：42 通过，含方向资源、连续状态机/已有 UE 原生轨迹对照、stance 回调、移动与旋转播放器。随后增加“拒绝未知输入表达式”校验，`graph-final.trx` 新增图测试 4 项再次通过。
- Godot Optimize 构建成功，0 warning / 0 error；末次构建包含新增输入表达式校验。
- 原有用户修改和头颈诊断文件未纳入本批。未修改 UE 插件、重导资产、运行 Godot 场景、全量测试或性能验收。

## 尚未接入

本批是姿态图资源编译，不是姿态输出运行时。下一步仍须完成共享缓存的更新权重/遍历顺序/一次采样生命周期、Standing 前向缓存内部图、六状态实际姿态与曲线混合，以及 Parent SetHipsDirection/ActivatePivot 的实际消费；随后补其余 stance 状态机和统一角色宿主。

普通 Demo 仍未切入完整 Refactored 链，不能据本批声称交错步、换髋或滑步的可见问题已经修复。Ragdoll/Get-up/Pose Recovery、Mantle、Camera、最终十分钟性能等旧缺口继续保留；音频、道具物理及头颈诊断仍暂缓。
