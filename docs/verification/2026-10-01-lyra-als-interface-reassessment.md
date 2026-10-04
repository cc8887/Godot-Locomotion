# ALS 模型复用与 Lyra Animation Interface / Layer 再评估

2026-10-03 最新实际事件：ALS68蒙皮/raw69/logical81与typed合同/十四入口及1/3/4/14实际实例路线保持。已补四类容器、原全部weight后Advance的生产时点，原Emote消费真实实例事件；物理成功发布立即事件、动画retry核对凭据，排队事件在Main资源通知之后。两独立UE21轨迹8823行相同，Debug/实际Optimize24运行、Core200、构建0/0与70源码/869 JSON/710原包/9配置审计通过，普通完整报告与前批同。见 [本批人物、接口与事件验证](2026-10-03-lyra-montage-delegates.md)。**通用bank回调修改、真实Section/NotifyState teardown仍未完成；剩余十三字段、非空左手源、通用整图与完整物理/视觉继续开放，目标active。**以下逐阶段待办保留原时点范围。

2026-10-03 最新实例阶段：ALS68蒙皮/raw69/logical81路线保持；原编译签名生成typed参数/姿态包装，十四实际入口按1/3/4/14实例布局执行并共用角色Source Sync。新增每实例独立空Montage bank的更新/取消/提交/派发阶段，普通角色发布后Linked先于Main派发，Main资源通知先于原Emote Montage消费者；原Main-only探针保持，新参考只追加原实例派发和快照。Debug/实际Optimize最终28次运行、32400参考帧逐帧retry、56项Core及独立哈希审计通过，明确Linked字段34/47。详见 [资源、接口映射与事件阶段](2026-10-03-lyra-linked-montage-events.md)。**完整通用委托容器、剩余十三字段、非空左手Sequence、default/self/Unlink/部分及通用图、原完整物理差异和视觉验收继续开放。**以下逐阶段待办是各记录时点的历史范围。

2026-10-01，基于当前主目录源码、ignored 资源、安装版 UE 5.8 源码和本次 Godot 复跑。按用户要求不展开 5.8 / 5.9 差异。本记录为资源与架构评估，不表示整个 Lyra 已完成移植。

## 当前决策与完整 Main 补充核对

继续复用 ALS 模型和蒙皮骨架，以离线重定向后的 Lyra 动画驱动；在现有 C# 运行时内保留 raw69 / logical81 / skin68 三个布局。接口合同、Provider 实例、源同步和骨骼混合各自保留原职责。固定 Unarmed/Pistol/Rifle Provider 的14个入口现均有执行实现；以下逐阶段记录中的10/11/12/13入口计数为历史进展。

`ILyraItemAnimationLayers` 原资源选择接口不是当前完整执行边界。实际执行由 `LyraItemLayerGraphInstance` 持有，三个输入姿态入口和 Additives 已有 typed 准备、求值、身份校验与共同提交。通用化应把现有合同和实例规则数据化，继续使用这些求值宿主，不新增另一个播放调度系统。

后续已补原Main合成四算子与三级cache更新调度。Provider Aiming BasePose先选主，回到Main Split/Locomotion原queue；同权重保持先到完整context，upper/base保留不同RootMotion modifier。5931次原生更新及14入口组合通过，见 [缓存更新验证](2026-10-01-lyra-main-cache-update.md)。缓存姿态复用与可复用Main求值宿主现亦通过：原Engine作用域4320姿态严格对照，14入口共同宿主的诊断/实际最终曲线两套各11340帧通过，见 [缓存姿态验证](2026-10-01-lyra-main-cache-pose.md)。缓存初始化/重入、活动Slot/Montage、主惯性、最终ControlRig和完整Main/生产入口仍开放。以下逐阶段记录保留各自历史验证边界。

本次重新读取原 Main49节点，最终输出链需保留如下顺序（上身分支在表中合并表述）：

| 阶段 | 原 Main 位置与适配要求 |
| --- | --- |
| 移动与左手 | LocomotionSM → LeftHandPose_OverrideState → Locomotion缓存 |
| 上下身与槽 | UpperBody / UpperBodyAdditive 两支 → UpperBodyLowerBodySplitMask → FullBodyAdditivePreAim → Split缓存 |
| 瞄准与恢复 | FullBody_Aiming → AdditiveHitReact → ApplyAdditive(FullBodyAdditives，原0.65f) → FullBody槽 |
| 最终后处理 | Inertialization → RotateRootBone → FullBody_SkeletalControls → ControlRig → Root |

最后的 ControlRig73 使用原 `CR_Mannequin_FootPlant`，与 Provider 的 SkeletalControls 中 FootPlacement 是两个不同节点。原 Main CDO 的 EnableControlRig / UseFootPlacement 都为 false，但实际启用引脚、布尔混合、属性和重入历史仍要按原图核实；默认关闭不代表完整图可以忽略。完整 Main 的启用路径尚未完成 Godot 移植和联合原生验收。

Main 上下身遮罩的序列化权重属于 Manny 骨序，不能截取前81项套到 ALS；需按目标骨名、原 BlendProfile 和曲线绑定重建。ALS 缺少的两根脊柱骨还涉及旋转补偿分配与握持效果，当前采样通过不等于该视觉差异已经验收。

下一步按此原拓扑完成真实 Slot/Montage、缓存访问顺序、Additives 应用、主惯性化和 RootYaw 接线，再在最终 Main 输出后复制曲线给 Linked 实例。随后将完整换类、统一 Notify、实际 Godot 碰撞和单一骨架发布接入生产场景。Layer Group 决定共享实例，Sync Group 决定活跃源同步，BlendMask 决定骨骼混合权重，三者不能合并为同一个概念。

本次使用已有 Godot 4.7.2 .NET 产物重新运行三个场景，均实际退出0且无 ERROR/WARNING：logical source 234源/936样本、linked binding 14入口/8步重绑、Idle/Recovery resources 245源/197绑定/missing0。日志为 `artifacts/lyra-analysis/als-interface-{logical,binding,resources}-review.log`。本次只更新评估记录，没有新增资源导出、运行时代码修改、UE采集或完整Main验收。

## 资源结论

可以继续使用原 ALS Mannequin 模型、网格、材质和 skin 权重。动画通过 GASP58 的 Manny→ALS IK Retargeter 离线生成，Godot 完整姿态求值保留额外控制通道，最后只向原模型发布 68 根蒙皮骨。

| 布局 | 用途 |
| --- | --- |
| skin 68 | 原模型实际蒙皮，骨名、父序、参考姿态和权重保持绑定 |
| raw 69 | 原 68 骨加 `weapon_r` 动画控制通道，父为 `hand_r` |
| logical 81 | raw 69 加原 ALS 11 个虚拟骨和 `VB IK_Hand_L_weaponSpace` |

新增控制通道由动画运行时持有，不要求重做网格权重。武器通道需要 retarget pose 的右手空间标定；仅添加同名骨不能搬运其动画。新左手虚拟骨在源关键帧阶段生成，然后参与插值和混合；最终姿态后重新 FK 构造会改变握持目标语义。

需显式保留完整曲线、Distance codec、Sync Marker、Notify、属性、Root Motion 与 additive 基底。普通 FBX 骨骼轨迹不足以交付原 Lyra 执行数据。资源 JSON 有字节哈希依赖，不应统一格式化。

当前 ALS 缺少 Manny 的 `spine_04/05`，身体比例也不同。IK Retargeter 处理动画姿态映射；程序化脊柱控制、Orientation Warping 的补偿分配、骨遮罩和武器挂点还需按目标骨架逐项绑定和验收。不能简单丢弃未找到的骨，也不能以逻辑通道增加代替真实蒙皮变形。继续沿用当前目标骨策略作为基线，若调整权重或补偿分配，应另记适配策略并重做目标骨架对照。

## Interface 与 Layer 的准确对应

Animation Layer Interface 定义姿态函数签名与参数；Linked Anim Layer 在主图的具体调用位置执行 Provider 子图。它与按骨混合、局部/网格空间 additive 是不同概念：一个 FullBody Layer 内部也可以再做上身遮罩与 IK。

当前源编译合同包含 14 个入口，全部属于 `ItemAnimLayers` 组；因此一个角色的该组应共享一个有状态 Provider 实例。这里的 Layer Group 不等于 `Locomotion` Sync Group，前者决定实例所有权，后者决定活跃动画源如何同步。

三个输入姿态入口是 `FullBody_Aiming(PreAimPose)`、`FullBody_SkeletalControls(InPose)`、`LeftHandPose_OverrideState(InputPose)`；Aiming 的 `AimYaw/AimPitch` 是 double。其余 11 个入口无姿态参数。当前 C# `ILyraItemAnimationLayers` 仍以资源选择与算子工厂为主；合同加载检查完整，不表示已具备全部 typed 图执行入口。现有 Aiming 求值方法仍用 float，完整接口执行时应保持 double 合同直到原图明确的转换边界。

建议在现有实现上继续扩展如下职责，不新建平行的角色调度系统：

| 对象 | 职责 |
| --- | --- |
| 不可变资源 / Provider 定义 | 编译签名、调用节点、继承 CDO、子图拓扑、资源映射、遮罩与控制配置 |
| 角色 Main 实例 | 运动观察、宏观状态、RootYaw、装备选择及上一提交反馈 |
| `ItemAnimLayers` 组实例 | 层内机器、各 source occurrence、回调字段和缓存历史；同角色 14 入口共享 |
| 调用节点 | 保留自己的节点身份、输入姿态、参数、权重、相关性和原图更新顺序 |
| 姿态数据 | 完整 81 骨 local/component pose、曲线存在性、typed attributes、RootMotion 属性；输入只读、输出独立 |
| 角色事务 / Source Scope | 汇总真实源访问，一次共同 Sync，统一验证和提交，失败取消全部候选 |

执行应保留 Update 与 Evaluate 分工：先按实际图遍历触发初始化、BecomeRelevant、Update 与源登记；共同 Sync 后再按原拓扑采样和求值；最后一次发布 skin。缓存重复读取不能重复推进时钟，暂不求值的帧仍需按原语义更新必要历史与事件。

同类重绑保留当前实例和历史；换类先验证并构造候选，再在帧边界替换，处理原 Linked BlendIn/Out 惯性请求。当前 UE 对照中重绑返回旧类也创建新实例，不能擅自全局缓存所有装备实例。多个角色只能共享不可变定义，不能共享可变 Provider 历史。

接口下一步应提供实际图执行入口：例如 Aiming 接收只读 `PreAimPose`、double `AimYaw/AimPitch` 和当前调用上下文，写入独立输出；Cycle/Pivot 等无输入姿态入口由实例内自己的机器和源生成输出。Update 入口负责候选历史、缓存访问、源登记和惯性请求，Evaluate 入口消费统一 Sync 后结果。应覆盖全部原14签名，不能用返回 clip 名的 `ResolveStateClip` 代替层图执行。骨遮罩、additive 基底、曲线存在性、typed 属性与 RootMotion 都随姿态传递。

未绑定时的 self-layer、Unlink、部分接口覆盖、多个 Layer Group 和持久实例配置应按实际需要补独立验证；当前示例直接建立 Unarmed 初始 Provider，尚未证明这些分支。

资源 Notify 的提取与 Linked Instance 的命名 Notify 传播需分别实现。当前导出的接收/传播标志均为 false，这不禁止脚步等资源事件。Montage 是否共用 Main 数据也应消费原配置，不能默认合并。统一事件候选应保留 owner/player/epoch/发生区间，取消重试不能重复触发。

Godot AnimationTree 可以作为图编辑和预览能力；对于要求原生对照的 Linked 调度、距离匹配、惯性化与 typed 属性，继续使用现有 C# 姿态运行时和单一骨架发布点。仅替换 AnimationTree 子树不足以自动得到 UE 的实例、缓存和同步语义。

## 当前已实现与待完成

已实现的基础包括编译合同加载、Router、共同层实例、81 骨姿态缓冲、手部控制及若干原图源/Warp/RootMotion 子链。Start/Cycle/Stop 已有共同 Main/source 对照；Pivot 的原16节点 Provider、双源、两套 Warp 和外层 HipFire 已完成组件对照，详见 `2026-10-01-lyra-pivot-runtime.md`。本批实际 Main Pivot 根/Lean 完整对照通过，详见 `2026-10-01-lyra-main-pivot.md`；四根共用 scope 和实际UE共同执行对照已继续完成，见 `2026-10-01-lyra-main-ground-native.md`；Main状态选择/权重/遍历的连续组件已通过7560帧逐位对照，见 `2026-10-01-lyra-main-machine-runtime.md`；该组件仍消费原生相关源/Sync/通知观察，Godot自主源宿主与最终混合仍待完整LocomotionSM接入。普通独立 Demo 还有旧简化播放路径，不能据此宣称完整原图生产接入。

五个 Air Provider 的实际十源与81骨姿态组件已继续完成，见 [Air 原图验证](2026-10-01-lyra-air-runtime.md)：三Provider三Hz3780帧/7650姿态/619650骨，内部/公开时钟、曲线、typed属性和RootMotion严格对照通过，仍由外部提供Main字段/根访问；尚未接生产Layer实例或与地面共同scope。

原Idle子图组件已继续完成：两机器/五源/六回调与自身已提交TurnYawWeight反馈，两套三Provider三Hz52920帧/30048姿态/2433888骨，全部九外层边及六种取消严格对照通过。当前编译规则192组确认开火立即退出边，未照搬旧DSL的false；组件仍未接生产Layer或共同Main，见 [Idle原图验证](2026-10-01-lyra-idle-runtime.md)。

十根共同资源和Source owner组件现已继续完成，见 [共同Source宿主](2026-10-01-lyra-locomotion-source-scope.md)：194绝对Sequence+3Lean共197序列、三同步组，统一资源编号重放既有UE60480帧/46098姿态通过，受控十根3780帧联合取消/重试及543仅更新帧通过。这更新上文Air/Idle当时未进入共同scope的边界；当前仍外部提供根访问，尚无十根共同UE oracle或完整Main/生产Layer接入。

Main Idle根回调及独立Main反馈事务已继续完成，见 [Main Idle根](2026-10-01-lyra-main-idle-root.md)：2520帧实际UE回调逐位通过，十根prepare有序交错，反馈随角色提交/取消。这里Main最终混合反馈仍由受控夹具提供，不是完整自主Main执行。

自主Main与十根源/混合组件已继续接通，见 [Main自主宿主](2026-10-01-lyra-main-own-locomotion.md)：机器消费Godot真实相关源与双缓冲Sync历史，生成根访问/权重，并求值LocomotionSM混合输出。7560帧覆盖10状态/10根，另3780转身帧的实际混合反馈驱动RootYaw；旧native组件回归保持。仍是固定Provider组件，未取得新的整个组合UE oracle，也未接普通Demo或上身后的最终反馈边界。

后续顺序：补ALS81完整Main与十根共同执行的连续native对照，再接实际14个Layer调用与换类生命周期；完成统一Notify/Montage与原上身之后的惯性；接最终足部和普通角色Gather/Worker/Commit；最后覆盖三种装备、站蹲/ADS、起停/反向/空中、坡地/移动平台及实际握持。

## 再评估初始复跑

再评估初始阶段直接运行已有 Godot 4.7.2 .NET 构建，三项均退出 0，输出无 ERROR/WARNING；该阶段未重建或启动 UE，没有新增动画导出。后续 Air 的 UE 构建/采集与新增资源见上方独立验证记录。

- `lyra_logical_source_smoke.tscn`：234 源，936 原生采样，raw69/logical81/skin68；位置最大 `1.4163191318420816e-13 cm`，quaternion 最大 `6.58317845524286e-16`。
- `lyra_idle_recovery_resources_smoke.tscn`：245 源、三个 Provider、197 个 Sequence 绑定，missing=0；475 新样本与 960 旧样本通过。输出明确为资源验证，`runtime=false`。
- `lyra_linked_layer_binding_smoke.tscn`：八步重绑、14 hooks、四 owner、四次同类复用和六种坏合同拒绝通过。对照已有 UE 原生捕获，本次未重新采集 UE。

这支持继续复用 ALS 模型与骨架扩展方案，尚不证明完整主图、全部装备或人体比例差异的视觉验收。

本次继续核对时，在当前 Debug 构建再次执行上述三项，均退出0且无 Godot ERROR/WARNING。新增日志为 `artifacts/lyra-analysis/als-interface-{logical,binding,resources}-current.log`，结果仍分别为234源/936样本、14入口/8重绑、245源/197绑定missing0。Main Pivot 原生连续对照及其采集失败/Marker合同修订另见上述新验证记录；仍不将组件验证称为完整生产接入。

## 本轮资源与接口复核

直接读取当前 calibration 与 interface/Unarmed/Pistol/Rifle 的编译合同，再次确认 raw69/logical81/skin68、12个虚拟骨、`weapon_r` 的父骨为 `hand_r`；目标布局没有 `spine_04/05`。四份合同均为14个入口、一个 `ItemAnimLayers` 组、三个输入姿态入口；Aiming 两个普通参数为 double。

使用当前已有 Godot 4.7.2 .NET 产物复跑 logical source、linked binding、Idle/Recovery resources 三场景，终端均退出0，各有唯一成功标志且无 Godot ERROR/WARNING。分别为234源/936样本、8步重绑/14入口/4 owner/4次同类复用、245源/197绑定/missing0。日志为 `artifacts/lyra-analysis/als-interface-{logical,binding,resources}-recheck.log`。本轮没有重建运行时代码或重采UE；正在实现的Idle宿主不在这三项验收范围内。

本机 UE `UAnimInstance::PerformLinkedLayerOverlayOperation` 进一步确认：默认路径按实现类与命名Group组织实例，未分组入口则逐节点创建实例；另有 SharedLinkedAnimLayers 路径。Godot 当前合同刻意只接受实际 Lyra 的单组拓扑，不能据此宣称支持任意UE Animation Layer Interface。通用化时应将签名/调用节点与实例分组规则数据化，并分别补未绑定self-layer、Unlink回退、部分入口覆盖、多组和显式持久实例配置。当前Lyra先沿已采集的单组实例生命周期接齐实际14个图入口。

姿态包应统一携带骨骼布局身份、local/component姿态、曲线及存在性、typed属性和RootMotion；调用上下文携带角色、实例代际、节点、物理帧、权重、相关性与惯性请求作用域。入口声明和主图调用应保持typed参数，在原节点要求处再转换精度。资源定义跨角色共享，机器、源出现位置、计时、缓存和回调字段按实例隔离。将这些接入现有角色事务，按真实遍历收集源、共同Sync、可选Evaluate及统一提交，避免把每个Layer变成独立推进的播放系统。

## 最新共同执行与反馈验证

固定Provider的完整LocomotionSM移动边界已完成ALS81连续原生对照，见 [Main ALS验证](2026-10-01-lyra-main-als-native.md)：三Provider三Hz、11340帧、9762混合姿态和12595实际根姿态，Godot独立计算规则、十根遍历与共同Sync。上文“自主宿主/最终混合尚待整条对照”已成为该推进前的历史；完整Main后续层和普通Demo生产入口仍待。

接口执行还必须消费一个原生反馈时序：`SkeletalMeshComponent` 在Main最终求值后调用各Linked实例的 `CopyCurveValues(*Main)`。因此组实例不能只保留自身Idle子图曲线；所有入口应观察 enclosing Main 已提交的最终反馈，即使某个入口本帧未访问。当前自主宿主已按明确LocomotionSM边界接入Idle反馈拷贝与取消保护，生产组实例应把这个边界延后到实际完整Main最终输出。Warp的Update访问计数也已与Evaluate姿态历史区分，以支持原图update-only帧。

## 完整图与十入口执行接入

最新完整原图已只读导出为 Main49节点与三Provider各14闭包，共43图；两个独立读取结果一致，508原包及645旧JSON字节保持。十个移动入口现由一个实际执行组持有，并按编译调用节点/实例/epoch与候选姿态身份接入 Main 求值；11340帧/12595根调用保持原生门禁，510次错误调用/旧视图拒绝通过，见 [Layer执行验证](2026-10-01-lyra-item-layer-execution.md)。四个后处理入口仍明确未接完整执行，旧Demo的可选算子连接不能当作新组生产接入。

完整 Main 顺序进一步确认：左手覆盖位于LocomotionSM与Locomotion缓存之间，上身与动作槽/瞄准/FullBodyAdditives之后才到主惯性化，再到RotateRootBone和SkeletalControls。当前三Provider左手覆盖CDO均关闭且Sequence为空；Aiming两套BlendSpace、曲线反馈、缓存和完整足部链仍应保留原Update/Evaluate语义，而不是凭CDO序列化alpha推断为永久旁路。

## 左手输入姿态入口继续接入

第11个执行入口 `LeftHandPose_OverrideState` 已进入同一 ItemAnimLayers 组，见 [左手层验证](2026-10-01-lyra-left-hand-layer.md)。原四节点、上一 enclosing Main 曲线、double Update回调和完整81骨/曲线/属性/RootMotion经3780帧独立原生对照通过，含1401次实际覆盖、隐藏和仅更新。Main→Left组合11340帧及调用身份/取消事务通过；正常三个CDO的权重为0，非零反馈由独立左手层对照验证。仍没有新Main+Left联合原生oracle，也未接普通Demo；Aiming/Additives/SkeletalControls三个入口及下游完整拓扑继续开放。

这验证了输入姿态入口在现有C#事务中的实现方式：实例所有权和播放源共用，Update读取已提交Main反馈，Evaluate消费只读姿态包并返回候选输出，失败同时丢弃曲线/字段/姿态。它仍是当前Lyra固定Provider实现，通用多组、部分覆盖和完整换类规则不能由此自动视为完成。

## 完整 Additives 入口与原生规则纠正

第12个入口 `FullBodyAdditives` 已继续进入同一执行组，见 [Additives验证](2026-10-01-lyra-additives-layer.md)。实际编译handler9读取IsOnGround，旧DSL“落地边恒false”的结论已纠正；原三状态、四边、double落地权重、ALS81恢复动画与完整数据通道经7560帧/6075姿态原生对照通过，含1496实际恢复姿态。Main组合11340帧与一次共同Sync/事务通过；Aiming与SkeletalControls两个入口、Additives在完整Main正确位置的应用、联合整图oracle及普通Demo仍开放，旧Demo两状态路径不能当作恢复已经生产接入。

这进一步确认ALS模型资源可以复用：恢复动画保留local additive及原基底，逻辑控制骨与typed属性不改变原skin。播放器使用原DoNotSync配置，同时由角色共同批次tick；Layer Group共享实例不等于所有动画都属于同一命名Sync Group。

## Aiming 图前权重继续验证

原 Provider `Update Blend Weight Data` 与 Aiming 三个编译输入处理器现已完成独立11340帧对照，见 [瞄准权重验证](2026-10-01-lyra-aim-weight.md)。double权重、严格阈值、上一Main曲线反馈和float引脚逐位同，含未访问入口、保留反馈与每帧取消重试；三Provider的蹲姿开火策略差异直接消费CDO。此组件尚未接同一执行组的图前更新，也没有完整BlendSpace源/缓存/姿态求值，不增加12/14已接入口数。ALS模型路线保持，完整Aiming/SkeletalControls/Main与普通Demo继续开放。

## 完整 Aiming 与第13入口

完整 `FullBody_Aiming` 已通过7560帧/6075姿态的原8节点连续组件对照，并进入同一个 ItemAnimLayers执行组和角色唯一Sync/事务，见 [完整瞄准层验证](2026-10-01-lyra-aiming-layer.md)。复用45个ALS81样本和原skin68模型，Unarmed Yaw原0.2秒SpringDamper、两个出现位置的独立滤波/时钟、最大权重缓存输入、mesh additive、曲线/typed属性/RootMotion均保持；此前“仅有权重组件/12入口”已成为历史。

固定类组合已11340帧验证，当前13/14入口；新增2780帧真正由上一Main曲线驱动权重，隐藏与update-only保留反馈，失败整体取消。它显式接收受控PreAimPose，尚没有完整上身/Slot后的真实输入或Main+Aiming联合原生oracle；SkeletalControls剩余入口、普通Demo、换类、完整Main/足部、渲染和性能验收仍开放。

这支持继续使用ALS人物模型和逻辑骨扩展路线，也说明Animation Interface/Layer必须保留源出现位置、缓存更新上下文和全通道候选事务。同一个Layer组实例不能将共享资源合并为一个时钟，更不能把两次缓存访问当作两个输入推进。

## SkeletalControls 双腿组件

原第14入口的12节点闭包已进一步核实，双腿 LegIK node107 在 ALS81 上完成3780帧连续组件原生对照，见 [双腿验证](2026-10-01-lyra-leg-ik.md)。原人物模型、现有 FK/IK 足骨和六条 ALS 动画继续复用；直腿弯曲历史按出现位置保留，重新缓存按足骨身份保持，Evaluate 候选在整帧 Commit 时才发布。曲线、整数属性及 RootMotion完整透传。

当前仍13/14共同执行入口。本轮验证输入为探针记录的受控骨骼包，只关闭 LegIK operator/事务宿主；FootPlacement、Root/Weapon控制、同一组件姿态完整链与最终Main接入仍待。Main CDO 的 UseFootPlacement=false 不能用于省略节点或关闭完整目标。公共 FCSPose BlendWith端点和四元数运算次序已按原生严格对照修正，Core31/Import88及Main/Aiming/双手回归通过。

### SkeletalControls更新与Root/Weapon后续进展

原12节点的Update和全局手权重现已取得3780帧逐位对照，Root/Foot各自的布尔混合、隐藏初始化及Foot累计时间/重入counter保留；ALS81六源自主采样输入与Root下移/Weapon缩放的3051姿态严格通过，见 [更新与两个算子](2026-10-01-lyra-skeletal-control-update.md)。68根原蒙皮骨与模型继续复用，完整曲线、typed属性和RootMotion保持。

本轮是独立更新组件和两个算子，不增加共同执行入口数。Foot成功Evaluate完成钩子只提交更新时间边界，没有足部几何求解；完整原图启用Foot的输出已在受控组件地面回退上保存，供后续移植参考，未验碰撞地形。仍13/14，完整FootPlacement、同一组件姿态12节点闭包、Main最终位置与普通Demo继续开放。上文Root/Weapon“待实现”是先前边界，其完整链组合仍待。

## 完整 SkeletalControls 与14入口范围

原12节点闭包现已在同一FCSPose上实现，并通过3780帧更新/3051姿态的独立整层原生对照；第14入口进入固定Provider的同一个ItemAnimLayers候选、一次Sync与共同提交，11340帧组合和207次晚期重试/84次故障恢复通过，见 [完整SkeletalControls验证](2026-10-01-lyra-skeletal-controls.md)。68根原skin继续复用，81 logical布局完成手部、足部、骨盆、LegIK与武器缩放；上文13/14和独立算子边界成为历史。

Foot首次运行存储在本机原UE实现中未确定初始化，原独立v1复采126帧输出不同。本批整层对照使用显式初始化的独立探针，原失败证据保留，不证明原Main首次内存语义已解决。共同组合的PreAim/PreSkeletal为受控输入，完整Main上身/Slot/Additives应用/RootYaw真实位置、联合原生oracle、换Provider、统一通知、Godot场景碰撞、普通Demo和视觉/性能仍开放。14个可执行入口不等于完整Main生产验收。

## 来源

Main原合成node0/3/76/72已继续实现并完成ALS81连续组件对照，见 [Main合成验证](2026-10-01-lyra-main-composition.md)。真实上身split按源BlendProfile骨名映射，动态alpha按原处理器钳制，恢复additive实际应用于Aiming后，RotateRoot读取图前快照；共同14入口组合保留一次Sync和取消事务。真实Slot/Montage、完整缓存、主惯性和最终ControlRig及联合Main/生产验收仍待，不能用该四节点对照关闭全部49节点。

- 当前代码：`LyraItemAnimationLayers.cs`、`LyraItemLayerInstance.cs`、`LyraLinkedLayerContracts.cs`、`LyraPoseBuffers.cs`、`LyraMainSourceScope.cs`。
- 本机 UE：`AnimNode_LinkedAnimLayer.cpp`、`AnimNode_LinkedAnimGraph.cpp`、`AnimInstance.cpp`。
- 既有验证：`2026-09-30-lyra-linked-layer-contracts.md`、`2026-09-30-lyra-logical-controls.md`、`2026-09-30-lyra-logical-layers.md`、`2026-10-01-lyra-idle-recovery-resources.md`。
- [Epic Lyra Animation](https://dev.epicgames.com/documentation/unreal-engine/animation-in-lyra-sample-game-in-unreal-engine)
- [Epic Animation Blueprint Linking](https://dev.epicgames.com/documentation/en-us/unreal-engine/animation-blueprint-linking-in-unreal-engine)
- [Godot AnimationTree](https://docs.godotengine.org/en/stable/tutorials/animation/animation_tree.html)
