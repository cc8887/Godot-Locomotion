# 方向转换完整性补完验证

日期：2026-09-10。基线 `d6b45e3` 加保留的未提交 Cycle 工作。
本批未提交 Git，未修改镜头/输入，未保存 UE 资产。

## 实际保留的改动

- 普通方向转换按源图 18 条边选择目标和时长，不再一律 0.5 秒，也不跨越不存在的直接边。
- ChangeDirection 腿部 WeightFactor 同时用于普通和自定义转换。
- 四条偏向换髋规则的符号、严格 ±0.5 边界、Feet_Crossing == 0 与源规则一致。
- 总规划/P5A 计划新增完整性补完入口，区分历史自动测试、当前 Demo 接线和人工验收。

本批只核对稳定源状态下的边规则。原版转换中断栈、方向角坐标与 Quadrant、Pivot
事件、ShouldMove/Stop、动态分层和完整 P5A 生产接线均未完成。

## 测试结果

| 检查 | 结果 |
| --- | --- |
| Godot C# build | 0 warning / 0 error |
| AlsStandingCycleTests | 35 passed |
| Core Release，排除 Golden/TraceSchema | 1310 passed |
| StandingCycle smoke | 30/60/120 Hz × 3 相位，9 次换髋，222 等待帧，9 次 rollback，稳态 0 B，603 曲线采样校验 |
| 横移可视回归 | 720 帧 / 120 截图，最大脚旋转 20.049°，turn 101 帧 |
| 快速转视角可视回归 | 720 帧 / 120 截图，最大脚旋转 16.598°，turn 101 帧 |
| 跑步横移最终回归 | 720 帧 / 120 截图，最大脚旋转 14.046°，moving 552 帧 |

横移与快速转视角使用 Walking，先前一次离散步态实验不改变这两组的 GaitWeight。
撤回该实验后重新运行最终 build、StandingCycle smoke 和跑步回归。
跑步用例 turn=0，仅证明移动路径；Turn 覆盖来自另外两组及 smoke。
本批没有运行新的 UE 动态同输入对照、完整 P5A verifier 或十分钟性能预算。

## 未通过的实验与保留问题

直接把 WalkRunBlend 改为原函数的 0/1 输出后，跑步第 249 帧脚旋转达到 34.672°，
超过现有 30° 回归门禁。撤回了这个单项运行时代码及其立即切换断言，保留原兼容
插值并加注释。后续必须整体对齐原生 BlendSpace 采样/平滑，不能以恢复临时插值
宣称已完成原版步态语义，也没有放宽脚旋转门禁。

横移起步低位脚峰值为 6.823568 cm/帧，仍与前一批相同；换髋开始于 frame 292/499，
等待 53 帧。未设置滑移通过阈值，`StartupAccepted=false`，不得把 smoke PASS
解释成起步滑步已消除。低位脚水平位移是代理指标，不等于已确认的支撑脚接触。

## 可复查材料

- `artifacts/completeness-direction-strafe/`：frames.json、120 截图、cycle-metrics.json、接触图。
- `artifacts/completeness-direction-rapid/`：frames.json、120 截图与接触图。
- `artifacts/completeness-direction-run-verified/`：最终跑步回归帧与接触图。
- `artifacts/completeness-direction-run/`：失败实验的部分截图，不能用于最终通过证据。

人工查看了横移多帧接触图和最终跑步方向图：模型和动作非空、可见换向前后的姿势。
未进行 UE/Godot 对应帧人工签收，不能据此标记原版等价。
