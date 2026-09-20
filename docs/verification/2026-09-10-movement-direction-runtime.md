# 第五批：MovementDirection 的源图语义

日期：2026-09-10。工作区：`../GodotALS-p5a-events-actions`。
本批补 P3 的活动移动方向求值，不代表完整 Locomotion 或 P5A 已验收。

## 源行为与实现

读取既有 `artifacts/locomotion-port-audit/graphs.json` 的 CalculateMovementDirection、
CalculateQuadrant、AngleInRange，并新增只读 `AlsMovementDirection` commandlet，
直接对瞬态 SkeletalMeshComponent 所属的 ALS_AnimBP 实例调用编译后的函数。
不重写这些 UE 函数作为期望值，不保存资产，不将函数探针称为完整 AnimBP 回放。

- Walking/Running 的 LookingDirection/Aiming 使用 Velocity.Rotation - AimingRotation。
- Sprinting 或 VelocityDirection 返回 Forward。
- 阈值 FR/FL/BR/BL 为 70/-70/110/-110，Buffer 为 5，范围端点包含。
- 这个 V4 资源的 IncreaseBuffer 是两个枚举不等比较的 OR，恒真。
  按 F、R、L 顺序测试扩展范围后，实际结果是 F [-75,75]，R (75,115]，
  L [-115,-75)，其余 B；不能凭名称将它改成对当前方向的滞回。
- UE 蓝图枚举 NewEnumerator3 是稳定名称，不是数字 3；导出器按枚举反射解析
  RotationMode，避免把 Aiming 错写成无效枚举值。

`AlsMovementDirectionModel` 将 Godot 的相对瞄准 yaw 符号转换为 UE 方向角。
`AlsStandingCycle` 独立保存四方向 MovementDirection 与六状态 Cycle Direction；
四向 VelocityBlend 仍由角色局部速度计算，不能跟随相机一起旋转。
`AlsStandingCycleGraph.Prepare` 消费实际 AimRelativeYaw、ActualGait 和 ActualRotationMode。
新状态随已有候选帧提交或回滚，没有单独的永久事件/同步运行时。

## 原生证据

UE 插件构建检查技能要求先完整 Editor target 构建及插件审计，再运行 commandlet。
最终构建状态 fingerprint：
`6AB869CA76E9E92A901DDD0052D84F860232B8AEE2FA4F8BE6823642C15D6364`。
AlsGodotExporter、AutoTestTools、BlueprintLisp 的 manifest/DLL/BuildId/receipt 一致。

最终日志：`artifacts/movement-direction-native-clean-20260910.log`。
退出码 0，commandlet 汇总 0 errors / 0 warnings，标记：

```text
ALS_MOVEMENT_DIRECTION_OK quadrants=100 movements=1620 assets_saved=0
```

固定 fixture：`tests/Als.Core.Tests/Fixtures/P3/v4_movement_direction_native.json`。
SHA256：`B34BB368EA5AA7AAFB6A8B55CCD4CCC3132F5345D9F678E1A0CADBEB5AC4FE18`。
覆盖四个原方向、三种步态、三种旋转模式、三个世界瞄准角以及方向边界。

初次探针存在两处自身错误：AnimInstance 的 Outer 错用 Package，以及将蓝图
枚举名称后缀误当数字。这两项已修正并重新完整构建/原生导出；初次失败结果不作证据。
没有做常规 GUI Editor 重启、项目数据验证或打包，不宣称插件发布验收完成。

## Godot 验证

- Core Locomotion 筛选：292/292，包含原生 1720 行对照。
- Godot 项目构建：0 warnings / 0 errors。
- StandingCycleSmoke：30/60/120 Hz，各三个起始相位，9 次换髋、231 等待帧，
  9 次普通回滚、9 次中断回滚、36 次新方向组合检查，最大活动过渡 11，稳定循环 0 B 分配。
- 实际 Cycle worker 单/多线程各 180 帧，result `AADE5F6186CD7277`，
  full_pose `E0A29255F0253F5E`，root `309E8D0E0BEEB2CB`，lag/stale 为 0。
- 两种 worker 模式的 late_transaction 故障注入通过，方向、过渡、控制器和姿势回滚。

多帧回放分别为 720 帧 / 120 张截图：

| 回放 | 输出目录 | 最大单帧脚旋转 | 结论 |
| --- | --- | ---: | --- |
| 固定镜头横移 | artifacts/direction-completeness-strafe | 14.122° | 原回归门禁通过；起步低位脚位移峰值仍 5.156 cm |
| 快速转镜头移动 | artifacts/direction-completeness-rapid | 11.532° | 原回归门禁通过；不是完整 UE 姿势对照 |

已检查固定镜头换向前后第 246/294 帧，以及快速移动第 306/354 帧截图。
图片显示角色正常加载、姿势在变化，但上身分层与起停完成度不能由截图或旋转阈值证明。

相机和输入 SHA256 未变：

- AlsOrbitCamera.cs：`6ED2DB72FF9D551C9E2D540BE6EC5889CD0CA9FC8A67425508C9A1D258BED666`
- AlsPlayerInputAdapter.cs：`BEE5F84E9FA5ABCF6D46C5209F222A51F6B10B5C64D1B10517A81779FA3BC8DB`

## 尚未完成

零速保持仍沿用当前 Cycle 行为；源 ShouldMove 对函数调用的门控与状态重置尚未接入。
活动过渡目前仍是标量权重等价，逐层中间姿势、Stop Mesh Space Plant、图内曲线
写入与合成、动态同步 Leader、Pivot/状态事件、动态 Layering 都需继续补完。
本批不宣称交错步/滑步完全修复，不关闭 P5A/P5B/P5C/P6/P7，也不自动提交既有脏工作区。
