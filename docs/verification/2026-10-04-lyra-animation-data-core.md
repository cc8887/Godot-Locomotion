# ALS / Lyra 共用整数动画属性与曲线运算

2026-10-04。在当前主目录直接推进，目标以 `ROADMAP.md` 顶部为准：复用 ALS/Core，覆盖手枪和步枪，迁移 locomotion 核心能力；URO、精确 UE 调度和额外 Provider 后移。

## 本批代码

- `AlsAnimationAttributeIdentity`、`AlsIntegerAnimationAttribute` 和 `AlsAnimationCurveSample` 放入纯 .NET Core；Godot/Lyra 通过类型别名继续使用同一载荷，没有类型转换缓冲。
- 整数属性的 additive 转换、累积、uniform / per-bone 混合和 override 共用 Core 算子。保留逐项整数截断、原始权重、缺失与存在零的区别、并列 override 选择以及不同混合路径的端点行为。
- 曲线的 additive 转换、Scale、状态机混合、Additive、Montage 加权贡献与 Override 共用 Core。标量计算复用已有 `AlsStandingCycleCurves.Scale/Accumulate`；新增共享状态机标量入口，供既有标量载荷和带 flags 载荷使用。
- Curve flags 与 presence 一同传递。Montage 首个贡献直接复制加权结果，保留负零；不能统一替换成从零累加。已有惯性缓冲的 `ToInertial` 继续拒绝非零 flags，防止无意丢失信息。
- Source bank 保留解析、资源身份和采样职责。Main、Idle、Additives、Aiming、Slot 保留图遍历、骨权重和节点相关性决策，调用共享运算。

本批没有另写同构算法测试。复用已有标量曲线、方向和缓存测试；整数属性使用已有原生资源夹具验证实际 Lyra 调用路径。

## 本批验证

Debug / ExportRelease 构建均 0 错误、0 警告。Release 相关 Core 测试 30 通过、0 失败、0 跳过，TRX 位于 `artifacts/lyra-analysis/animation-data-core-v3-tests/`。

这 30 项实际分布为标量曲线 14、方向输入 7、共享缓存 9；整数属性的主要证据来自 Godot 原生载荷对照，不能将这 30 项说成新增整数属性单测。

Debug 12 个、实际 Optimize 11 个 Godot 进程共 23 个均退出 0，无 ERROR/WARNING。相同的 11 个回归场景各运行一次，另有 Debug 实际 GPU 渲染场景。

| 验证范围 | 每构建实际结果及证明范围 |
| --- | --- |
| 源曲线与整数属性 | 234 clips；73650 rows、139227 curve values、589200 attribute values；原资源对照 error=0 / flags=0；采样运行时 allocation=0 |
| Cycle、Idle、Aiming、Additives | 原姿态／曲线／整数属性夹具通过，相应图含 72 项原生 per-bone 属性与 curve override 探针；保留 hidden、update-only、取消重试和原比较门槛 |
| Main 组合 | 3780 frames / 3078 poses，Main 节点 0/3/76/72 完整载荷组合通过；不是完整 Main 原生姿态验收 |
| Main / Slot | 47610 frames / 19944 poses、35952 root checks；RootMotion P/Q/S 差值均 0，完整载荷和 retry 通过 |
| 完整 Main Pose 与最终曲线反馈 | 11340 frames / 9762 poses / 58572 feedback curve checks；真实 owner、统一提交、取消重试和角色隔离通过；这个夹具不含最终 Rig |
| 当前 ALS 普通场景 | 60Hz / 1700 frames，3 Pivot、4 dynamic requests，现有生产入口回归通过 |
| 普通 Lyra 十角色 | 每构建 4800 角色发布，玩家 480 retry；实际 Godot 移动、最终 Rig、ALS skin、武器与通知消费者运行通过 |

普通十角色的 Debug / Optimize 完整报告相同，并与上一批共享缓存的报告相同。Optimize 确实使用 ExportRelease 程序集；结束后六份 Debug DLL/PDB 按 SHA256 完整恢复。专项图夹具与普通生产场景的证明范围不同，不将其中 `production=false` 或 `nativeCombined=false` 的结果扩大为完整原生角色等价。

Debug 七张 FramePostDraw 截图已查看联系表，并查看手枪瞄准和步枪蹲姿原图；全身和装备可见，画面仍偏白。这是基本渲染回归，不能据此关闭近景握持、复杂地形或人工连续观感验收。原图位于 `animation-data-core-v3-debug-rendered-frames/`。

最终 `animation-data-core-v3-audit.json` 通过：17 份实施／验证源冻结，4605 份其余基线保持，870 份导出 JSON、710 原 UE 包与 9 配置保持；原 UE 探针及所引用安装引擎源码同样通过已有保护审计。初始快照 `animation-data-core-v1-before.json` 捕获本批开始时的当前 dirty 主目录，既有用户修改保留。所有本批日志和最终报告以 `artifacts/lyra-analysis/animation-data-core-v3-*` 为前缀。

## 仍需推进

这批关闭的范围是通用整数属性与曲线运算迁移，不代表引擎通用能力迁移或完整 locomotion 验收已经完成。

- Main / Montage 仍有通用 additive pose 运算。既有 `AlsPrecisePoseBlender.LocalApply` 和 ISPC 路径可复用，但原调用的低权重门控、profile 强制混合和归一化位置不同，需抽出共同的低层累积入口，保持调用策略。
- Main 惯性适配器仍持有通用 curve flags 历史及 RootMotion 速度差分、Log/Exp/衰减，需在复用现有 `AlsInertialization` 的基础上归入 Core；原 Main 节点配置与资源绑定留在 Lyra。
- RootYaw 旋转数学及其可选数值后端仍在 Godot/Lyra 包装内，需要按通用 Core 运算与平台适配进一步拆分。当前 Win64 数值桥不依赖 Unreal 引擎，但完整原生舍入不是当前交付必需条件。
- ALS 比例下的近景握持、站蹲瞄准、连续起停/Pivot、坡面/台阶脚部和实际键盘输入仍需验收。既有 action 输入夹具与真实键盘输入的证明范围分别保留。

现有骨架、资源 JSON、原 UE 包及工程配置不因这次运算抽取修改。没有本批 UE 启动、资产保存或重导，没有提交或推送。
