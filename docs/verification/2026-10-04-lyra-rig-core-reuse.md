# Lyra Rig：复用 ALS 解算与 Core 层级

本批沿用 ROADMAP 顶部的当前目标：ALS 人物、手枪/步枪和 Lyra 运动算法；不要求完整 UE 私有调度、URO 或所有 Provider 的复刻。没有启动或修改 UE，没有重导或格式化资源。

## 实现

- FootPlant 双骨求解直接使用现有 `AlsRigTwoBoneIk.Solve`。当前资源的 root/joint/end、轴、pole、长度和伸展参数在 Lyra 适配器中绑定，不再维护第二份求解公式。
- Core 新增 `AlsRigAim`，保留 primary/secondary Direction/Location 目标、权重和反向旋转约定；复用已有向量归一化、四元数和姿态类型。
- 四元数 Euler ZYX/双精度 AxisAngle、姿态逆变换、float Remap、显式 float 角度归一化归入既有 Core 类型。旧 HipFire 预览改用上一批的 Core 插值，显式保留原 double 平方容差。
- Core 新增 `AlsRigHierarchy`，承载单父骨和 TransformControl 的当前/初始 local/global、offset、dirty cache、子骨传播、Construction 快照、Clone/CopyFrom/Reset 及候选姿态导入。Core 骨数量动态，拒绝缺失/循环父骨、外来拓扑和错误输入映射。Lyra 适配器只校验原 98 元素/7 control、81 逻辑姿态及资源约束并委托 Core。

最终蒙皮发布仍走普通角色的 logical81→skin68 单一 writer。共享 Spring、精确姿态、腿部 IK、源采样、曲线/属性、Sync 和 Layer 绑定继续复用此前 Core。

## 验证

Debug 与 ExportRelease 构建均 0 警告/0 错误；相关 Core 148 项通过，0 失败/跳过，其中新增 Aim/数学20、层级6。证据为 `rig-math-core-v3-{build,optimize-build}.log` 和 `rig-math-core-v3-tests/rig-math-core-v3.trx`。之后只修验证脚本的报告参数；独立审计确认所有已构建 C# 源仍与 v3 冻结版本相同。

v4 Debug14、实际 Optimize13，共27个 Godot进程退出0，日志无ERROR/WARNING：Main组合/最终Rig、解算/层级/输出、实际Jolt碰撞、RootYaw/HipFire旧预览、武器、当前ALS普通1700帧/Aim/Grounded、普通十角色及GPU渲染。

解算对照2520帧、16008 sweep、6552576分量比较，最大向量差2.842170943040401e-14，旋转差0。层级37批/796写/248读/37重试/20拒绝、35776变换比较，最大位置差3.552713678800501e-15，旋转/缩放差0。最终Rig输出2520帧/2154输出、174474骨、10279440分量比较，最大向量差2.842170943040401e-14、旋转差2.220446049250313e-16，原门槛保持。

真实Jolt场景三Hz共2520帧、2484姿态、2520重试、30744命中/2976未命中、9初始重叠、21晚期故障拒绝通过。普通十角色每构建发布4800帧，完整报告与上一批逐项相同。七张FramePostDraw图片已逐张检查，站蹲/瞄准/跳跃/换装可见；平地远景不能代替地形脚部和握持近景验收。

v3首次实际碰撞运行缺少必须的新绝对`--report=`路径，计算后保存报告失败，日志和失败摘要保留。v4脚本补齐参数并校验报告存在/hash，两构建复跑通过；没有改算法或阈值。

独立 `tools/verify_rig_core_reuse.py audit` 通过，记录在 `artifacts/lyra-analysis/rig-math-core-v4-audit.json`：16实施/验证源冻结，其余4622基线、870 JSON、710原UE包、9配置保护；Optimize使用6个ExportRelease DLL/PDB并逐文件恢复Debug。相关GPU图片位于 `rig-math-core-v4-debug-rendered-frames`。

## 后续

仅关闭本批数学复用和单父骨TransformControl层级迁移。通用运行审计、实际地形/脚部/近景握持/真实键盘及整个移植目标仍开放；URO、全部UE内部调度和额外Provider保持后移。没有全量managed、十分钟、性能或跨平台验收，没有提交或推送。
