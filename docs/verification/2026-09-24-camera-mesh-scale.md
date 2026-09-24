# 相机模型缩放接入

原普通相机将 MeshScale 固定为 1，旋转提取还复用了只允许刚性变换的物理传输。两个条件会分别丢失缩放和拒绝合法缩放模型。新相机转换明确分离缩放与旋转，普通宿主将真实局部 Z 缩放传入 Follow 及碰撞 probe。

## 原生依据与实现

本机 ALS `Plugins/ALS/Source/ALSCamera/Private/AlsCameraComponent.cpp` 的 CalculatePivotOffset、CalculateCameraOffset、CalculateCameraTrace 和 TryAdjustLocationBlockedByGeometry 均使用 `GetComponentScale().Z`。最后一个使用 `(TraceRadius + 1) * MeshScale` 作为恢复查询半径。

新增 `AlsCameraMeshPose.FromWorld`：分解 FBX 骨架世界矩阵的三列长度，验证正长度、有限值、正向正交基，提取旋转后复用已有坐标系转换。导入骨架轴为 native `(X,-Y,Z)`，所以相机标量取局部 Z，不能取 Godot 世界 Y 或最大轴。插槽仍由完整骨架世界矩阵采样，保留各轴缩放；仅相机偏移/半径按原生规则统一乘 Z。

非法非有限、奇异、剪切和反射矩阵明确拒绝；不会以正交化静默吞掉剪切。通过验证后才用正交化去除浮点残差。物理姿态历史的刚性检查没有放宽。

## 先红后绿与实际资源验证

将原相机的刚性转换及固定 1 行为提取到同一入口后，新测试在 0.5 倍模型立即触发旧的 rigid transform 异常，退出 1：`artifacts/camera-mesh-scale-before.log`。

最终测试加载 Mannequin 和 AnimMan 的实际导入模型，各三个倾斜/旋转方向，每个方向测试 0.5、1、2 倍及 `(1.2,0.7,1.6)` 非均匀缩放，共 24 个变换。检查 native 旋转、局部 Z 选择、均匀缩放下插槽距离、Follow 偏移和球扫半径；每例还使用真实 Godot 墙体查询验证穿透恢复余量。另有非有限、零缩放、反射和剪切四种拒绝。

结果退出 0：`ALS_CAMERA_MESH_SCALE_OK models=2 poses=24 rejected=4 max_cm=0.0002132480599880018`，日志 `artifacts/camera-mesh-scale-after.log`。预设门槛分别为旋转作用向量 0.0001 cm、插槽/偏移 0.001 cm、实际碰撞恢复 0.03 cm；没有失败后放宽。

Optimize 构建零警告、零错误。没有修改 Core/Import/UE 或冻结参考，没有启动 UE 或重跑无变更的全量测试。

普通未缩放 demo 60 Hz 并行八秒回归退出 0，共 480 次相机提交；移动/冲刺、倒地/起身、人称/肩侧及 control yaw 检查通过。日志 `artifacts/camera-mesh-scale-demo.log`。本批无新截图。

## 明确边界与后续

这证明相机转换、输入接线及受控真实资源/查询的缩放行为；不是新 UE 缩放组件逐帧 oracle，也不是缩放角色完整 Ragdoll 生命周期验收。当前角色物理姿态历史和 Ragdoll 入口仍要求刚性变换，直接缩放整个普通角色还不能视为受支持。后续必须单独处理身体几何、质量属性、胶囊与姿态历史的缩放关系，不能删除这些检查来冒充支持。

复杂相机碰撞、完整视觉验收、旧静态物理 9/12、Flail 0/3、Mantle 和最终十分钟性能预算继续保留；头颈和道具物理暂缓。工作直接位于 `D:/GodotALS` main。用户 P4 计划 SHA256 仍为 `78EEA1410FF3EBC93A5EAEAA224ABFD74565B54090DBA3670C08A37BED60B100`，三份头颈诊断文件保留。
