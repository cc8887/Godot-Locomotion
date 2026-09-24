# Camera Godot 球扫与起点穿透恢复

本批在主目录 `D:/GodotALS` 的 `main` 新增 Main-only、query-only `AlsCameraCollisionProbe`，供前批 CameraFollow 的场景查询回调使用。普通 Demo 尚未接入，未修改当前鼠标/WASD 行为。

## 查询流程

- 输入为 native 世界厘米的 start/end/球半径，显式 Godot collision mask 与自身 RID 排除列表。UE 的 trace channel 枚举仍不能直接当 Godot mask。
- 零运动球形查询先判断起点是否实际穿透；因为 Godot 的 swept query 会忽略已有重叠，不能只使用 CastMotion/SpringArm。
- 普通 sweep 返回沿运动向量的 safe fraction 对应球心，不把墙面 contact point 当相机中心。使用 Jolt 的安全区间，不宣称与 Chaos 的 TOI 逐位一致。
- 起点穿透时，将半径增加 `1 cm × mesh scale`。由接触法线与 contact point 计算球体沿法线的穿透深度；同一 RID/shape 只取最深一项，随后按 ALS 顺序求和。修正方向若背离角色位置则拒绝。
- 应用修正后以原始半径复查，清空后才重新扫向终点；若仍穿透，则将结果保持在修正后的起点。修正没有有效方向时保持原始起点。CameraFollow 会使用返回的新起点计算距离比。
- overlap 达到 64 项时拒绝并抛错，不静默采用可能截断的恢复结果；上层统一事务负责丢弃候选。动态物理 owner 可通过 SetExcludedBodies 替换排除列表，支持将来的 Ragdoll 激活/退出。
- 禁止 worker 调用，拒绝非法输入或过期 owner；Dispose 允许场景 owner 先销毁。

## 与原生的边界

恢复的流程、膨胀距离、方向门限、复查及失败回退来自本机 ALSCamera 源码。**Godot 接触投影并不等于 Chaos OverlapTest 的 MTD 求解器**。复杂凹面、多接触和深度包围仍需进一步场景覆盖/原生对照；本批不以基础几何通过宣称完整穿透算法等价。

## 验证

- Optimize 构建通过，0 warning/0 error。
- 实际 Godot/Jolt 场景 `scenes/tests/camera_collision_smoke.tscn` 在 30/60/120 Hz 分别通过 13 项：平面球心停靠、miss、初始重叠、双墙角、背离 owner 修正拒绝、mask、自身 RID 排除、更新/撤销排除、worker 拒绝、旋转墙面、不可脱离窄缝、mesh scale=2 的膨胀距离。
- 日志 `artifacts/camera-collision-final-30.log`、`-60.log`、`-120.log` 均退出 0，标记 `ALS_CAMERA_COLLISION_SMOKE_OK cases=13`。之前 8/11 项开发运行日志保留。
- 球心停靠/旋转平面使用预设 0.2 cm 容差，其他几何条件按实际球半径/膨胀距离核对。测试在各物理频率完成注册后执行静态查询，不宣称连续移动或视觉抖动验收。
- 无 Core/Import 生产代码更改，未新增 UE 导出、全量测试、截图或十分钟性能验收。

## 下一步

将 socket sampler、真实基座/胶囊采样、实际 collision mask/自身排除与 CameraRuntime 接到普通 Demo 完成姿态发布后的 Main 阶段。再验证鼠标控制与显示镜头分离、连续墙边移动、平台、Ragdoll/起身和第一人称切换，补多帧截图与碰撞边界。

完整 Camera、Mantle、旧静态 9/12/Flail 0/3 稳定性以及最终性能验收仍未完成；头颈/道具物理继续暂缓。用户 P4 方案和三个头颈诊断文件保留未提交。
