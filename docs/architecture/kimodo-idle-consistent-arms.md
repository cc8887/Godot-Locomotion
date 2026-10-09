# Kimodo Walk 与 Idle 的手臂一致性

放松的 Walk 使用自然低垂手臂及轻微前后摆动，与当前生成的 Idle 保持接近；不冻结手臂、不把跑步、翻滚或道具手型限制成 Idle。

## 生成条件

提示词保留已进入稳态、交替完整步幅与原方向语义，加入 `relaxed shoulders and arms hanging close to the body, making small forward and backward swings`。Fullbody 与左右 Hand 条件使用同一周期姿态、相同根路径，并在三个周期内重复相位和首尾邻域；每种稀疏条件少于20帧。文字说明风格，数值条件提供姿态和时间。

ALS 原版手臂姿态投影到 SOMA 时，必须反演输出重定向的同一个中性基准。`offline_retarget.aligned_neutral` 按 SOMA T-Pose 的骨段方向构造目标中性姿态 `N`。手臂世界旋转的输入映射为 `G_soma = B × (G_als × inverse(N)) × inverse(B)`，输出为 `G_als = (B × G_soma × inverse(B)) × N`。`B` 是 Z-up/Y-up 坐标变换。不能用 ALS A-Pose 的绑定旋转替代 `N`，否则下垂手臂在 SOMA T 骨段上被抬起。本次修正限定左右肩/上臂/前臂/手，下肢保留既有参考及脚趾锚点 IK；绑定骨长、原版原始制品不变。

## 作者检查

`upper_body.audit_idle_relative` 对实际生成的 Idle 测量肩到肘的 FK 段与重力向下夹角，以及手相对胸部的侧向伸出量。去除骨盆朝向与世界平移，逐侧对照，保留 Idle 的非对称姿态。当前放松 Walk 默认限制：

| 指标 | 相对 Idle 的新增允许量 |
| --- | --- |
| 上臂倾角中位值 | 20° |
| 上臂最大倾角 | 35° |
| 手部侧向距离中位值 | 0.12 m |

这是新增的风格防回归，不修改既有脚底/原生对照门禁。阈值为本项目放松 Walk 作者合同，不是 Kimodo 官方保证。它允许有限摆臂，但不能单独认证艺术表现或握点。跑步和低蹲不套用该风格合同。

主 Walk 在 `gait_cycle` 通过既有支撑/接缝检查后执行上身检查；镜像和反向镜像重测输出，对照同一 Idle。Idle 是主 Walk 的显式 DAG 依赖，下游通过源身份和报告继承。失败保存被拒绝运动与报告，阻断交付；不在发布后把手强行拉回。交付绑定上身检查报告字节及运动身份，目标转换拒绝缺失、失败、被修改或对应另一动作的报告。报告随新包进入 `upper-body/` 与载荷摘要。

默认 `directional_gait_rules.json` 的前进/左前/左后 Walk 已接入，左右对应动作继续程序化派生。目标相位修复、Root/Pelvis 分解、逐资产曲线、最终 FBX 回读及 Godot 实测仍为必要后续阶段，作者通过不自动激活资源。

本次生成对照和最终验证见 [2026-10-09 验证](../verification/2026-10-09-kimodo-idle-consistent-arms.md)。
