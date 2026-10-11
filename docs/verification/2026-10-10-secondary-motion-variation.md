# 呼吸叠加变化量增强

## 本批行为

按用户要求增加当前呼吸叠加动画的变化量。普通 ALS Demo 实际使用 V4 `ALS_N_SecondaryMotion`，不能仅根据 Refactored 站立底层推断呼吸来自 `A_Als_Idle`。保留原 Additive 权重，在既有精确源采样结果上放大相对第 0 帧的动态旋转：三段脊柱 4 倍，双肩、颈部和头部 2 倍。Default／Feminine／Masculine 三个呼吸节点采用该配置；其他源和骨骼保持原样。

运行时直接使用当前活动包 `als_nonprop_20261010_air_combined_v1`。本批没有 diffusion、资产重导、指针切换或新的 FBX。相关稳定契约见[呼吸层动态幅度](../architecture/als-secondary-motion-variation.md)。

## 命令与证据

证据目录为 `artifacts/verification/2026-10-10-secondary-motion-variation-v1/`，正式结论以其中 `summary.json` 为准。每个运行使用独立 `.json` 与 `.log`。初次渲染的镜头未完整覆盖腿部，其报告和四张截图另存为 `enhanced-60-render*`；改用 Root／Head 中点取景后的两个 `*-render-framed` 报告及八张完整取景截图进入最终矩阵，旧证据未覆盖。

系统 dotnet 只有 SDK 10.0.300，直接命令因 global.json 要求 8.0.100 未能启动；不改 global.json，改用机器现有的 `C:/Users/yuchencui/.dotnet/dotnet.exe`。优化构建命令是 `dotnet build GodotALS.csproj -p:Optimize=true --no-restore`（使用该完整可执行文件路径），零错误、零警告。

Release 测试过滤 `AlsPoseRotationVariationTests|AlsLocalAdditivePoseTests|AlsPreciseRawPoseTests`，25/25 通过。包含相对旋转放大、非交换参考旋转、常量姿势保持、四元数符号、平移／缩放保持以及非法倍率拒绝。

真实场景命令模板：

```powershell
& ./Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe `
  --headless --path . res://scenes/tests/secondary_motion_variation_smoke.tscn -- `
  --idle-hz=60 --idle-output=<独立报告路径>
```

对照加 `--native-secondary-motion`；并行加 `--parallel`；30／120 Hz 改 `--idle-hz`。渲染使用 `--rendering-method gl_compatibility --audio-driver Dummy` 替代 `--headless`，并加 `--capture-dir=<独立目录>`。最终分析命令：

```powershell
python scripts/analyze-secondary-motion-variation.py `
  --directory artifacts/verification/2026-10-10-secondary-motion-variation-v1
```

## 最终观察

源检查覆盖 148 个源节点、每节点 81 个时间点；只有三个基本 Overlay 呼吸节点的七根配置骨骼变化。曲线、局部平移和缩放完全相同，其余 145 节点逐样本相同。10,000 次热采样零分配，重复采样完全一致；增强后的目标旋转首尾接缝峰值约 0.00000242°。

最终矩阵为五个 headless 场景（60 Hz 原幅度、60 Hz 增强单／并行、30／120 Hz 增强单线程）和两个 60 Hz GPU 渲染场景（原幅度／增强）。每次观察八秒，覆盖 3,586 个已提交观察帧、八张同帧完整取景截图。60 Hz 单／并行记录完全一致；增强与原幅度的 Root／骨盆／双脚以及实际蒙皮脚底记录逐帧完全相同。GPU 与 headless 的骨骼和脚底几何也逐帧相同；只排除 Godot 进程局部的 SurfaceId 数值，并在每个进程中独立检查其稳定性。

最终本地骨骼旋转相对稳定窗口首帧的最大变化：

| 骨骼 | 原幅度 | 增强 | 实测倍率 |
| --- | ---: | ---: | ---: |
| spine_01 | 0.331° | 1.317° | 3.983 |
| spine_02 | 0.189° | 0.755° | 3.984 |
| spine_03 | 0.295° | 1.183° | 4.007 |
| clavicle_l | 0.460° | 0.919° | 2.000 |
| clavicle_r | 0.915° | 1.830° | 2.000 |
| neck_01 | 0.557° | 1.123° | 2.015 |
| head | 0.630° | 1.259° | 2.000 |

脚底支撑高度不低于原 -2 cm 门槛。当前活动包全部 1,536 个载荷摘要重新校验通过，包与指针不改写。摘要结果保留于 `asset-integrity.json`。

## 覆盖边界

本批关闭当前 Default 站立待机的幅度与脚底稳定性检查，并检查三个呼吸源节点的选取。Feminine／Masculine 的完整角色显示、移动／蹲伏／转身／动作混合、全量原生最终姿态、长时间和性能验收没有重新运行；零分配采样不代表完整性能基准。没有增加 Idle Break 或随机待机变体。已有诊断包的资源缺口和正式生产门禁保持开放。
