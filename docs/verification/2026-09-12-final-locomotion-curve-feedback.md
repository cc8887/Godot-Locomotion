# 最终移动曲线反馈与蹲伏 Yaw 接线

日期：2026-09-12，第九十九批。工作区：`D:/GodotALS-p5a-events-actions`。

## 修复依据与范围

完整 BaseLayer 接入 Worker 前检查发现，映射入口虽然接收上一已提交最终曲线，
但只提取了落地掩码、步态、蹲伏基准和转身许可。Standing 的交错步/髋偏向、
Stop 的选脚仍读取局部 Standing 采样；主状态和蹲伏方向继续接受外部规则里的
脚部值。蹲伏姿势还保留调用方 Cycles.Yaw，未接入已经计算的全局 Yaw。

原 V4 `v4_locomotion_inputs.json` 的方向转换和
`v4_locomotion_detail_graph.json` 的停止转换使用 GetCurveValue。
本地 UE `Engine/Source/Runtime/Engine/Private/Animation/AnimInstance.cpp:2179`
明确从 Proxy 的 AttributeCurve 读取命名曲线；查找失败返回零。
这不是只查某个局部 Sequence 或 Standing 缓存的语义。

本批保留原换髋条件与过渡时间，不新增固定输入等待、不改动画速度来掩盖滑步。
未修改 UE 插件、资产或项目配置，没有执行新的 UE 导出。

## 实现

- `AlsAnimationInputFeedback` 新增 Feet_Position、Feet_Crossing、
  HipOrientation_Bias，按名称读取并保留 presence；重复名字、有效曲线非有限值、
  无提交历史却携带曲线等情况继续拒绝。
- `ApplyTo` 为主移动/地面/蹲伏转换统一提供脚位、交错、髋偏向与 BasePose_CLF。
  缺失时只有 GetCurveValue 消费值为零，快照本身不会变成“存在且为零”。
- 完整 Grounded 路径把最终反馈传到 Standing 的观察及方向更新，消除该入口
  对局部脚部曲线的回退。无映射反馈的旧组件入口仍保留原有参考行为。
- BaseLayer 映射将全局控制的 Yaw 四轴送入 Crouching Cycles，调用方旧值不再覆盖。

HipOrientation_Bias 可能由更后面的 Overlay 层提供，本批没有将这个名字或
虚构的曲线值强加给 BaseLayer 的来源曲线表。最终反馈仍由外围最终图提交者
提供；本批新增完整图夹具用一个受控的后续层验证新增曲线的反馈边界。

## 验证

构建成功，0 警告、0 错误；`git diff --check` 通过。

| 检查 | 结果 | 日志（artifacts/） |
| --- | --- | --- |
| Core：反馈、Standing、地面/蹲伏方向 | 85/85，其中新反馈测试 8 项 | final-curve-input-core.log |
| Import：地面、空中、Idle、Yaw、方向输入 | 71/71 | final-curve-input-import.log |
| 新完整图反馈专项，30/60/120 Hz | 1050 帧/1050 次重试，45 次最终层晚期故障 | final-curve-input-feedback.log |
| 原映射完整图 | 3360 帧，12 次晚期故障，输出/输入重试一致 | final-curve-input-mapped.log |
| 真实 Roll 动作入口 | 1050 帧，28 次真实 Slot 晚期故障 | final-curve-input-actions.log |
| 八资产转身/通知 | 2400 帧，58 次晚期故障 | final-curve-input-turn.log |
| 生产 single/parallel | 各 180 帧，结果与完整骨骼摘要一致 | final-curve-input-single.log、final-curve-input-parallel.log |

新专项结果：821 帧最终反馈不同于局部来源曲线；102 个换髋等待帧；420 个
蹲伏非零 Yaw 输出帧。重试时刻意反转调用方脚部规则和 Yaw 输入，姿势、曲线、
状态和事件仍一致。Core 另验证极小非零交错值连续两秒仍保持等待，归零后才
进入原有 0.75 秒换髋过渡，没有按累计等待秒数提前放行。

新增夹具初次假定 HipOrientation_Bias 已在 BaseLayer 曲线表内、以及零 Slot
权重仍调用 Slot 求值，两者均被运行检查否定；夹具已改为明确的受控后续层和
非零 Slot 权重。蹲伏观测起初选到原 Yaw 曲线为零的一侧，后改用另一视角，
使缺失 Yaw 接线能被非零输出检查检出；没有修改原生曲线或放宽通过条件。

生产摘要：结果 `21E164D829153157`，完整姿势 `CF9225D4DE9B2C8B`，来源事件 10。
这仍是旧 Standing 生产路径的回归证据，不是新 BaseLayer 的 Demo 接入证据。

## 尚未完成

完整 Main Movement/BaseLayer/Montage 输出还需汇入 Worker/控制器/提交阶段，
与最终曲线历史、通知、动作结果和晚期失败恢复统一。实际角色旋转的最终
YawOffset 消费、完整动态 Layering/Add/LS/Overlay、脚部曲线 presence 和 IK/
pelvis 链仍未关闭。没有将起步滑步、上身姿态或真实角色换向视觉标为通过。

后续仍按 P3/P4 基础动作整链验收、P5A 通用运行时、P5B Overlay/道具、P5C
Mantle/Roll/Root Motion、P6 Ragdoll/Get-up/Camera、P7 人工与性能验收推进。
