# ALS Refactored 相机数据与计算层

工作目录 `D:/GodotALS/main`。承接完整相机目标，本批新增只读导出与 Core 计算层；尚未替换普通 Demo 的 `AlsOrbitCamera`。目标没有缩减为平滑跟随，后续仍需完整图、场景碰撞和视觉验收。

## 来源与版本边界

使用本机 `D:/AdvancedLocomotionSystemV/Plugins/ALS/Source/ALSCamera` 的 `AlsCameraComponent.cpp`、`AlsCameraAnimationInstance.cpp`、`AlsCameraSettings.h` 及相应 `/ALS/ALSCamera` 资源。这是 Refactored 相机，不是 V4 `ALS_PlayerCameraManager` 蓝图公式。当前角色资产仍是 V4；接入时必须显式映射骨骼/socket 与坐标，不能假定两版相机资源名称和单位一致。

`tools/unreal/export_camera_inputs.py` 只读导出 `assets/config/refactored_camera_inputs.json`：

- AB_Als_Camera、B_Als_CameraComponent、CS_Als_Default、Smooth/Quick 两个 CameraBlend 曲线的来源路径/类型。
- 完整 authored AnimGraph 的原生文本，包括嵌套 Look States 与过渡；不把运行时生成的 MERGED/Ubergraph 重复图当作独立动画图。
- CS_Als_Default 实际反射属性（普通 T3D 会省略 C++ 默认值），并检查组件 CDO 确实引用这个设置资源。
- 两条富曲线原生键值文本及各101个实际 `GetFloatValue` 样本。
- UE `UAlsRotation::DamperExactRotation` 实际调用的连续轨迹及边界；不是 Python 重写同一公式生成的答案。

实际设置包括两种视角 FOV=90°、TraceRadius=15 cm、TeleportDistanceThreshold=200 cm、TraceOverrideOffset=(0,0,40) cm、回弹半衰期约0.2秒。原生 TraceChannel=3 是 UE 通道枚举，不是 Godot collision mask，尚未映射。

## Core 实现

`AlsCameraMath` 使用 UE 坐标/厘米、double Rotator角度、float曲线/时间：

- 旋转阻尼使用已有原生 float InvExpApprox，不复用旧通用精确指数 helper；保留整组三轴近零提前返回和 Win64 SIMD 的 >=175° 逆时针选择。
- pivot 在相机 yaw 空间逐轴阻尼，再回世界空间。
- 目标单帧位移超过设置阈值时禁用 lag；阈值0禁用瞬移判断。
- 相机碰撞距离缩短立即生效，延长才使用原生阻尼；无lag/关闭平滑/零距离保留原生分支。
- 纯值函数无引擎对象和共享时钟，非法非有限值提前拒绝。

这里的 TraceDistance 仅消费场景提供的真实球扫结果。它没有伪装成已完成初始穿透调整、碰撞查询或完整相机组件。

## 验证

- 按 `ue-diagnosing-plugin-build-load` 技能完成整个 Editor target 构建与所有项目插件审计。0 build action，ALS/AlsGodotExporter/AutoTestTools/BlueprintLisp全部通过；日志前缀 `20260924T084348879Z-7744f0447ed34465abd8098f0894e1e3`，位于 UE 项目 `Saved/Logs/PluginBuild`。fingerprint `1CFC5657A618E30F849577D7EA003062B54DB04B41EF728389B91D449D0DF5E3`。
- 导出 commandlet 退出0、assets_saved=0。最终来源日志 `artifacts/camera-inputs-boundaries.log`；初次调查/反射默认值/扩大参考范围的日志均保留。
- Core 定向7项通过：30/60/120 Hz 的相机轴映射、瞬移边界、碰撞收缩/回弹及零时间/非法输入。
- Import 定向1项通过：三频×三档半衰期共630帧、另112项±180°/540°/175°附近/零时间/零半衰期边界，旋转各轴误差<=1e-9°；阻尼alpha与原生float完全相等。
- Godot Optimize build通过，0 warning、0 error。没有接入生产相机，本批不声明普通 Demo 新相机已通过，不以数学回归代替整体相机效果。
- 最终重复冷导退出0，`artifacts/camera-inputs-boundaries-repeat.log` / `refactored-camera-inputs-boundaries-repeat.json`：assets（含202曲线样本）、settings、630帧参考和112边界全部一致。原始图只有三个未连接 ErrorTolerance 引脚的加载生成 PinId 不同（共6行差异），排除此字段后图完全相同；不声明整个JSON字节一致。生产JSON SHA256 `2517F308E95A4F9F8FB6BFB93257D9F6C107FAB44695DBA95F6CED5772D24B2A`，保留原生原文。

没有本批Core/Import全量、普通Editor重启或DataValidation；未修改UE插件/配置或保存UE资产。

## 后续接入顺序与验收

1. 编译实际 Camera AnimBP：Look States 的 Velocity/View/Aiming 过渡、Gait/Stance/LocomotionMode 的 tag blends、Shoulder、View Mode、LocomotionAction覆盖，以及缓存/ModifyCurve/两条自定义过渡曲线。读取真实连接和引脚值，不能把 Node 中占位的 CurveValues=0 当作最终参数。
2. 用原生连续相机图输出验证曲线混合和重入；明确相机动画独立于角色动画的时钟及更新阶段。
3. Main 在已提交角色姿态/物理之后采样 root/head/肩部/第一人称 socket；加入相对移动平台历史、瞬移重置、偏移/FOV及完整第一人称分支。
4. Godot 真实球扫与初始穿透修正接入 Core距离平滑，排除自身；相机视觉旋转与输入控制yaw分离，保持已验证的鼠标/WASD语义。
5. 普通Demo接入后验证站立/蹲下/冲刺/跳跃/瞄准/自由观察、换肩、Ragdoll/Get-up、平台、贴墙和瞬移，三频与多线程模式下配对原生输出并截图。

Mantle、静态物理9/12与Flail0/3旧稳定性目标、十分钟性能预算仍保留。头颈拉伸和道具物理仍按用户要求暂缓。用户P4与三份诊断文件不纳入提交。
