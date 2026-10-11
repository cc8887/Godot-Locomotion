# ALS 呼吸层动态幅度

普通 ALS Demo 组合 Refactored Locomotion／Layering 与 V4 Overlay。实际呼吸入口是 V4 的 `ALS_N_SecondaryMotion`；包内的 Refactored `A_Als_Idle` 虽然也是 Additive 资源，并非这条普通播放链的呼吸采样入口。

`assets/config/als_animation_settings.json` 的 `secondaryMotionRotationGains` 配置指定骨骼的动态旋转倍率。当前三段脊柱为 4，双肩、颈部和头部为 2。增强作用于 Default／Feminine／Masculine 的 SecondaryMotion 节点，沿用这些节点原有的播放时间、Sync、相关性、Overlay 权重和角色事务。它不负责添加待机变体。

Core 的 `AlsPoseRotationVariation` 按当前旋转与动画第 0 帧旋转之间的最短相对旋转进行放大，再还原到参考旋转上。倍率允许 0–4；1 原样返回，0 保留参考旋转。四元数符号不影响结果。放大的是动态旋转，不是相对基础站姿的整段固定偏移；局部平移、骨长、缩放、曲线和非目标骨骼均不由该操作改写。

Godot 精确 Overlay 采样器在构造时读取骨骼配置、采样参考帧，并为三个明确的源节点建立只读映射。工作帧只在既有源采样之后调整选定旋转，没有新增时钟、求值链或提交链。参考采样仅用于初始化数据，不推进动画时间。

此配置是普通 Demo 的运行时调节，不写回生成 FBX／raw，也不是新 Kimodo 生成。直接的原生源诊断默认不传调节参数，保持原采样；普通入口可用 `--native-secondary-motion` 临时恢复倍率 1 进行同包对照。原生图中的 Additive 权重仍按原资源执行。

验证入口为 `scenes/tests/secondary_motion_variation_smoke.tscn`，源检查与普通 Demo 的真实提交、物理骨架和蒙皮脚底观察使用同一场景。报告对照工具为 `scripts/analyze-secondary-motion-variation.py`。实际覆盖和边界见[本批验证](../verification/2026-10-10-secondary-motion-variation.md)。
