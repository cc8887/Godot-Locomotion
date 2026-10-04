# Lyra 资源与 Animation Interface / Layer 当前评估

2026-10-04，检查当前主目录、ignored 目标资源、本机 UE 5.8 源码与官方文档；按用户要求不展开 5.8 / 5.9 差异。此记录补充资源和架构决策，当前实施范围仍以 `ROADMAP.md` 顶部为准。

## 结论

继续使用 ALS 的人物网格、材质、蒙皮及原模型骨架。将需要的 Lyra 动画离线重定向到 ALS，并在动画运行时补充控制骨和虚拟骨；已经存在这条实际资源和发布路径。沿用现有 C# 动画运行时，把通用合同、实例绑定和求值机制放在 Core，Lyra 保留图定义与装备配置，Godot 负责输入、物理与模型显示。

## ALS 骨架可以复用到什么程度

当前 `LyraLogicalSourceBank` 校验以下布局，`LyraAlsCharacterBinding` 校验原模型的骨名及父序，最终只写入其 68 根骨：

| 布局 | 当前数量 | 用途 |
| --- | ---: | --- |
| skin | 68 | 原 ALS 模型蒙皮，无须重新制作网格或蒙皮权重 |
| raw | 69 | 原骨架动画通道加 `weapon_r`，用于武器空间计算 |
| logical | 81 | raw 加 12 个虚拟骨，用于姿态求值和 IK 目标 |

`weapon_r` 以 `hand_r` 为父；武器空间左手虚拟骨表示 `weapon_r → hand_l` 的关系。控制通道不要求成为新的蒙皮骨，但其轨迹、参考姿态与生成时机必须随动画资源保留。当前源库在采样插值前处理虚拟骨，不能用最终写模型之后统一重建替换它。

资源复用还要区分模型与动作内容。ALS 模型可以承载 Lyra 动画；既有 ALS 动作资源则须逐项核对用途。Lyra Start、Stop、Pivot 的距离匹配依赖动作的 Distance 数据；武器动作、AimOffset、additive 基底、Sync Marker、Notify、曲线存在性与 typed attributes 也参与执行。不能只把 ALS clip 名称填入 Lyra 配置便认为算法输入完整。离线重定向后须检查曲线距离与目标角色位移、根运动单位是否相符。

ALS 没有 Manny 的 `spine_04/05`。当前 `LyraOrientationWarpingPolicy` 已显式从原五根脊柱骨映射到 ALS 三根脊柱骨及 `ik_hand_root`，骨遮罩也必须按目标骨名与层级重建。脚部 Rig 使用 ALS 参考比例；重定向动画不会自动修改求解器腿长。原目标参考腿长 42.57/40.20 cm 与原 Rig 的 45.75/41.71 cm 不同，相关历史原生检查见 `2026-10-02-lyra-als-layer-design.md`。握持、脊柱扭转、瞄准边界与复杂地形仍需实际视觉验收。

## Interface、Linked Layer 与骨骼混合分别如何实现

Animation Layer Interface 声明姿态函数及参数。Linked Anim Layer 在主图的具体调用位置进入实现实例；实例拥有播放器、状态机和缓存历史。Layer 内部可以调用 additive、BlendMask 或 IK 算子。Layer Group 决定实例共享，Sync Group 决定播放器时间同步，BlendMask 决定骨骼权重。

| 边界 | 当前实现 | 后续扩展方式 |
| --- | --- | --- |
| 接口合同 | Core `AlsAnimationLayerContracts` | 导出函数名、Pose 输入、标量类型、混合配置与组；生成 typed 包装 |
| 实例绑定 | Core `AlsLinkedLayerBindings` | 命名组按角色、实现类及组共享；无组调用按节点独立；保留 self/default/unlink 的明确路径 |
| 图执行 | Core `AlsLinkedLayerExecution` | 沿用 Initialize、CacheBones、Update、Evaluate 与回退规则 |
| Lyra 图和资源 | `LyraGeneratedLayerContract`、`LyraItemLayerGraphInstance` | 共用算法，资源覆盖与实际拓扑变化分别描述 |
| 源播放 | 现有 ALS/Core Source bank、Sync、采样与缓存 | 同角色实际活跃源统一推进，缓存引用不重复 tick |
| 姿态合成 | 现有 Core 混合、惯性及骨控制 | 传递 pose、曲线 presence/flags、属性、RootMotion 和布局身份 |
| 显示与物理 | Godot 适配层、`LyraAlsCharacterBinding` | 完整候选通过后一次发布模型；碰撞与输入留在 Godot |

实际 Lyra 合同是 14 个入口，均属于 `ItemAnimLayers`。其中三个入口接收姿态：Aiming 的 `PreAimPose`、SkeletalControls 的 `InPose`、LeftHand 的 `InputPose`；Aiming 的 yaw/pitch 参数为 double。其余是十个移动状态入口和 Additives。接口必须支持这些输入及有状态子图，单纯返回动画名称的接口不足以承载它们。

可共享的资源和图定义保持不可变，每角色实例单独持有可变历史。每帧先采集运动快照，Prepare 实际访问的子图和源，再统一 Sync、Evaluate、合成后处理；候选预校验通过后 Commit，失败 Cancel。同类重绑保留实例，换类先构造合法目标，再于帧边界替换并更新代际，拒绝旧候选和反馈。Notify 与 Montage 同样保持实例归属和统一消费边界。

当前 Core 已有通用绑定和路由基础，但生产 `LyraLinkedLayerGraphSet` 仍明确要求完整的 14 入口支持配置，另有 self 路径。这个边界不代表任意 UE 接口、任意部分覆盖或任意图都能直接执行。大量接口的扩展应继续数据化合同和注册图工厂，并增加多组、无组、多接口组合的实际宿主验证。骨骼混合框架、时钟和模型发布器无需另起一套。

Godot AnimationTree 可用于编辑和预览；当前运行时的 grouped instance、完整姿态载荷、共同 Sync 与候选提交继续由现有 C# 宿主承载。若未来接入 SkeletonModifier3D，要明确它位于 AnimationMixer 后的处理顺序，并确保唯一最终骨架发布职责。

## 本次实际复跑

使用当前已有 Godot 4.7.2 .NET Debug 产物，三个场景均实际退出 0，日志没有 ERROR/WARNING。本次未构建、启动 UE 或重导资产。

| 场景 | 本次结果 | 证明范围 |
| --- | --- | --- |
| `lyra_logical_source_smoke` | 234 源、936 样本；raw69/logical81/skin68；位置/四元数/缩放误差均 0 | 当前目标动画采样、布局、虚拟骨和实例 scratch 隔离 |
| `lyra_linked_layer_binding_smoke` | 八步、14 hooks、同类复用四次、六类坏合同拒绝 | 当前合同绑定及实际模型上的重绑检查 |
| `lyra_idle_recovery_resources_smoke` | 245 源、三 Provider、197 Sequence 绑定、missing=0 | 当前资源闭包；其输出明确为 `runtime=false` |

日志位于 `artifacts/lyra-analysis/resource-interface-review-20261004-100958-{场景名}.log`。这些是当前产物的专项复跑，不是整个 Main、新导出、Optimize、硬件输入或完整视觉验收。已有更广范围的验证与剩余项以当前 ROADMAP 和各验证记录为准。

## 建议继续的顺序

1. 保持 ALS 模型路径，以手枪和步枪为资源验收范围，Unarmed 保留回退。
2. 审计仍留在 Godot/Lyra 的通用算法，优先复用并扩展现有 ALS/Core 能力；生产图和装备参数留在配置层。
3. 在真实角色上验收起停/Pivot、换装备惯性、双手握持、站蹲瞄准与脚部地形适配。
4. 需要更多接口时再推广图工厂和多组组合；URO 与精确 UE 调度按现行 ROADMAP 后移。

## 参考

- 本机 UE 5.8：`Runtime/Engine/Classes/Animation/AnimLayerInterface.h`、`Runtime/Engine/Private/Animation/AnimNode_LinkedAnimLayer.cpp`。
- [Epic：Animation Blueprint Linking](https://dev.epicgames.com/documentation/en-us/unreal-engine/animation-blueprint-linking-in-unreal-engine)，支持接口输入、命名组实例共享与多个接口组合的设计。
- [Godot：SkeletonModifier3D](https://docs.godotengine.org/en/stable/classes/class_skeletonmodifier3d.html)，说明骨骼修改与 AnimationMixer 的执行顺序。

本批仅新增此评估记录和专项运行日志，保留已有运行时代码、资源及未提交修改。

## 最新资源与 Layer 专项复核

再次直接读取当前 calibration，确认 skin68、raw69、logical81、12 个虚拟骨；weapon_r 的父骨为 hand_r，脊柱为 spine_01/02/03。再次核对当前生成合同、本机 UE 5.8 Linked Layer 绑定源码及生产 GraphSet：14 个入口和 double Aiming 参数保持，普通绑定机制与完整图执行范围分别评估。

使用已有 Godot 4.7.2 .NET Debug 产物复跑三场景，均退出 0 且无 ERROR/WARNING：

- `LYRA_IDLE_RECOVERY_RESOURCES_GODOT_OK sources=245 extras=8 additiveBases=3 providers=3 sequenceBindings=197 missing=0 samples=475 oldSamples=960 curveRows=475 curveValues=0 attributeValues=3800 positionCm=0 quaternion=0 scale=0 logical=81 skin=68 runtime=false`，日志：`artifacts/lyra-analysis/resource-layer-recheck-20261004032211-lyra_idle_recovery_resources_smoke.log`。
- `LYRA_LINKED_BINDING_OK steps=8 hooks=14 owners=4 sameClassReuse=4 rejected=6 group=ItemAnimLayers parameters=double,double skeleton=68`，日志：`artifacts/lyra-analysis/resource-layer-recheck-20261004032211-lyra_linked_layer_binding_smoke.log`。
- `LYRA_LOGICAL_SOURCE_NATIVE_OK sources=234 raw=69 logical=81 skin=68 cases=936 additive=180 positionCm=0 quaternion=0 scale=0 virtual=beforeInterpolation occurrenceScratch=isolated stage=rawSource runtimeIntegration=separate`，日志：`artifacts/lyra-analysis/resource-layer-recheck-20261004032211-lyra_logical_source_smoke.log`。

这是资源、采样与绑定的专项验证，未新构建、重导资源或修改运行时代码；完整地形、近景握持与硬件键盘验收仍开放。
