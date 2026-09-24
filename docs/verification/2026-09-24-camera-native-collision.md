# 相机原生碰撞连续对照

本批补上 UE 实际 CameraComponent 场景查询到 Godot 实际物理查询的连续验证。未修改生产相机算法：现有实现通过本批几何条件，尚不代表复杂场景完全等价。

## 方法

`ALS_CAMERA_COMPONENT_COLLISION=1` 运行原始 `B_Als_CameraComponent` 的 TickComponent，使用真实 QueryOnly UBoxComponent。与移动基座导出模式互斥。角色固定参考姿态，输入采用实际插槽、原生曲线、视角和设置；没有复制 UE 的球扫或 MTD 计算作为期望结果。

每个频率连续四秒：清空路径、往返墙体阻挡、朝角色方向的起点穿透恢复、背离角色的恢复拒绝、撤墙后外放阻尼。Godot 测试创建真实 StaticBody3D，提前一个物理步安装墙体状态，调用生产 AlsCameraCollisionProbe 和 Core Follow。仅初始化历史使用 native initial，之后连续累积自身状态，不逐帧覆盖为 UE 结果。

比较每帧相机位置及 FOV，并检查穿透、调整和收缩/恢复分支计数。位置容差在运行前采用现有查询测试的 0.3 cm（3 mm）预算；纯空间数学测试仍使用更严格的 0.001 cm。TraceRatio 差记录为诊断，不额外设置门槛。

## 结果

| Hz | 帧数 | 起点穿透 | 成功调整 | 最大位置差 cm | 最大 TraceRatio 差 |
|---|---:|---:|---:|---:|---:|
| 30 | 120 | 30 | 15 | 0.17852182911443068 | 0.00064048171043396 |
| 60 | 240 | 60 | 30 | 0.18149873381179277 | 0.0006511658430099487 |
| 120 | 480 | 120 | 60 | 0.18149873381179277 | 0.0006511658430099487 |

三次 Godot 均退出 0，共 840 帧。日志：`artifacts/camera-native-collision-30.log`、`camera-native-collision-first.log`、`camera-native-collision-120.log`。Optimize 构建零警告、零错误。

冻结参考 `assets/config/refactored_camera_collision_reference.json` 的 SHA256 为 `BB67D409F6DE4F99712EC496F200E70A63EC1E32BF38E326DB47306BA2923C95`。冷启动导出及普通 Editor PID 2108 导出逐字节相同，均退出 0。普通 Editor 仍出现两条已知 `LogAutomationTest: Error: Condition failed`，以及 PawnActionsComponent、Navmesh、LineSetComponentMaterial、MotionVectorSimulation 和 CrowdManager 警告，不能宣称 Editor 无错误。没有保存 UE 资产。

完整 Editor 目标构建和插件审计已通过，input fingerprint `5507EC2FCBD1D34CCE8C479248800626CB6EFA1E0DE171EF91A041FCBDA81BC6`，BuildId `c9a68b99-36ee-4db4-8c8b-3aee056a528e`。构建日志位于 UE `Saved/Logs/PluginBuild/20260924T115245130Z-4928b50527e047b2895ee59e883bc1ce*`。Godot 源码与 UE 插件镜像的 CPP SHA256 均为 `11A6CFD179AEDD3B5C25C269AE61EF0D03FB0765086FC6F232ECF26511C6D2CE`。

数据验证 `artifacts/camera-collision-data-validation.log` 退出 0，汇总为 0 error(s)、3 warning(s)，为既有 PawnActionsComponent/Navmesh 加载问题。

## 范围与后续

本批是轴对齐单盒、固定参考姿态、已观测原生曲线下的真实查询对照，无截图或完整角色视觉验收。Godot 接触投影并不是 Chaos MTD 的逐位复刻，复杂凹面、深包围、多接触尚需原生对照；完整 Godot 图到普通场景的联合验收、缩放宿主和时间倍率仍需继续。

导出插件仅用于 Editor，本批不声明游戏打包通过。未重跑无变更的 Core/Import 全量。旧静态物理 9/12、Flail 0/3、Mantle、最终十分钟性能预算等尚未完成；头颈和道具物理按用户要求暂缓。用户 P4 计划修改及三份头颈诊断文件保留。
