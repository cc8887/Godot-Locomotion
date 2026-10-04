# ALS 人物复用与 Animation Interface / Layer 实现方案复核

2026-10-03最新补验继续确认ALS模型68蒙皮骨与69 raw/81 logical、十四typed入口共享装备组的路线。骨遮罩与Aiming的原ISPC混合舍入已修正，Debug/实际Optimize在30/60/120Hz三个动画边界及逐帧取消重试全部通过，原精度门槛保持；动作中换层和普通十角色回归通过。详见 [混合修正与验收](2026-10-03-lyra-ispc-mixing.md) 及 [人物与接口复核](2026-10-03-lyra-als-interface-review.md)。普通Godot移动尚未切换原CMC配置；实际UE/Jolt运动、近景握持、地形、通用多Group/self/Unlink等及整个目标仍开放。下方120Hz失败相关描述属于此前阶段。

最新动作中换层的完整Main联合连续对照已通过：继续复用ALS68 skin/69 raw/81 logical与十四入口共享装备实例，真实LinkAnimClassLayers不重建Main/Montage；每Hz24换类/24同类，包含21动作中与6隐藏移动图换类。补原Main身份观察与alpha过滤历史，维持取消重试；Debug/实际Optimize各7560参考帧在三个输出边界全部通过，99managed测试及两构建所有回归通过，普通十角色报告同，旧资产哈希保持。当前资源、Interface/Layer职责与详细证据见 [ALS人物、接口与完整换层验证](2026-10-02-lyra-whole-main-rebind.md)。只关闭指定单组三Provider组合；真实UE/Jolt物理联合、近景握持/地形、通用多Group/self/Unlink和其余完整目标仍开放。下面“动作期间换类联合native待验”为此前状态。

最新完整Main动作已沿同一ALS68/69/81和14入口共享实例完成指定联合连续对照：原五Slot/Montage接完整Main/Linked、惯性与最终FootPlant，修正IsAnyMontagePlaying实例存在性查询。三Provider三Hz、每构建7560参考帧在Debug/实际Optimize三个边界及逐帧取消重试全部通过；两构建七项回归、普通十角色完整报告同，旧资产/配置保持，见 [完整Main动作验证](2026-10-02-lyra-whole-main-actions.md)。当前固定Provider动作覆盖并不证明动作期间换类联合native；原UE物理为受控观察/静态地面，完整装备Ability玩法、真实UE移动、Jolt/GPU近景、多Group/self/Unlink及其余完整目标仍开放。下方动作待补表述属于此前阶段。

最新转向补验继续确认 ALS 人物复用与14入口共享实例路线：明确原Linked函数输出和Main加Lean后的状态输出各自边界，修复多样本BlendSpace归一化及Win64 RootYaw向量三角函数舍入。三Provider三Hz转向/旧移动在Debug和实际Optimize每构建11,340帧参考、三个边界全部通过；两构建六项回归和普通十角色完整报告精确一致，原资产/项目配置哈希保持。见 [完整Main转向验证](2026-10-02-lyra-whole-main-turning.md)。新增无UE依赖的Win64数学DLL需要按README构建；导出复制钩子过语法检查，其他平台原生精度/独立游戏导出未验。**整个迁移目标、动作/换层联合native、多Group/self/Unlink及其余完整验收仍开放。**下方转向待补描述为此前阶段。

最新完整 Main 已修正探针首次 CacheBones 的 Manny Skeleton 残留，以及 Lyra AimOffset/Main Lean 的原生 additive 舍入次序。正确 ALS81 遮罩下，三Provider 30/60/120Hz各12秒、每构建共7,560帧，在 Debug/实际Optimize 三个边界全部通过；两个独立180帧native和60Hz长轨迹前缀精确相同。瞄准/Lean/40,320帧惯性宿主/普通十角色回归通过，Debug首次脚本标记错误保留并审计。见 [遮罩与数值修正](2026-10-02-lyra-whole-main-rounding.md)。ALS人物和14入口typed组实例路线保持；这是受控观察与解析平面连续对照，整个Main完整玩法验收、多Group/self/Unlink等继续开放。旧完整Main诊断因初始遮罩缓存错误，仅保留历史用途。

最新命名通知接收也已沿同一 ALS 角色接生产：Main 与 Montage/source 合并队列在完整 skin 发布后调用 weak typed receiver；Main 注册随换层保留、旧 Linked 随 Layer epoch 退休，14入口共享组和68 skin/81 logical继续沿用。原委托倒序/增删/嵌套及实例分发经两独立UE与两构建55轨迹890帧/229回调对照，三Hz六角色、普通十角色及GPU通过，见 [命名通知验证](2026-10-02-lyra-named-notify.md)。原默认类仍没有两named方法/关闭转发；本批提供显式external注册，没有推测业务玩法。整个Main联合native、多Group/self/Unlink、任意嵌套机器和其余完整目标仍开放，下方external hooks未接为此前阶段。

最新 Main 的 Pivot source-state notify 历史已接同一 ALS 角色：Linked 来源继续保留 provider 身份，并携带包围它的 Main 状态；队列与历史仍由角色统一提交，换层不清上一帧历史。原 scope index7/state4 与首同类引用语义经两构建36,920帧对照、三Hz六角色/普通十角色及GPU验证，见 [Pivot通知历史验证](2026-10-02-lyra-pivot-notify-history.md)。人物68 skin/81 logical和14入口共享组保持。原十类AnimInstance没有两named函数且关闭接收/转发；通用external named hooks、任意嵌套机器与整个Main联合native等完整目标仍开放。

最新原 Emote 已接现有 ALS 角色与普通 E 输入，仍保留 ALS 68 skin/81 logical、14入口组实例与同一角色Montage bank。动作与typed移动委托属于角色生命周期，装备Layer重绑保留它们；两构建54条27,720帧原GA/ASC/Task对照、三Hz六角色各10,080移动/retry、普通十角色与GPU通过，见 [Emote验证](2026-10-02-lyra-emote.md)。自然淡出后的原中断边界保持；本批是该动作本地行为，不是完整GAS或整个Main联合native验收。下方原GA未接为此前阶段，剩余通知与完整目标仍开放。

最新四原 Warp 模板已接实际 ALS component/当前胶囊 feet/初始 base offset 和实际 bank context，在显式挂载的角色物理服务中执行；两构建三Hz六角色各10,080移动/retry、GPU与默认十角色基线通过，见 [Warp物理验证](2026-10-02-lyra-motion-warping-physics.md)。ALS 68 skin/81 logical 与14入口共享Layer实例保持；默认已读Shooter配置不补组件/Align。原GA与整个Main连续native等仍开放，下方未接物理hook为历史阶段。

后续原 Align/SkewWarp 组件已在两构建通过 120 轨迹 / 16,800 帧连续原生对照，含 3,030 非零位移帧、完整 modifier 历史和逐帧取消重试，见 [组件验证](2026-10-02-lyra-motion-warping-component.md)。目标与胶囊应属于角色物理服务，Linked Layer 保持姿态合同与组实例职责；新组件尚未接角色物理 hook。原 GA/Pawn/ShooterCore 已读配置没有提供 Align，默认玩法不能补造目标；全目标仍开放。

最新 Montage-only 根运动已接普通角色真实胶囊更新，继续保持 ALS 68 skin / 81 logical 与角色内共享的 14 个 typed Layer 入口；图的 RootMotionDelta 姿态属性和物理位移分别按原语义消费。两构建原生/实际三Hz/普通十角色验证通过，见 [根运动物理验证](2026-10-02-lyra-root-movement.md)。原四个 MW Emote 的 authored root 全为 identity，Align 非零位移还需原 MotionWarping；不能据此关闭完整动作或整个 Lyra 目标。

最新普通角色武器 Notify、独立 tick、最终 `weapon_r` 挂接和换装/销毁边界已接通 Pistol/Rifle，Debug/实际 Optimize、两次原生探针和实际 GPU 三图通过，见 [最新装备验证](2026-10-02-lyra-weapon-equipment.md)。人物 ALS 68 skin/81 logical 与 14 个 typed Layer 入口共享组实例的路线保持。本文后续同日记录保留历史状态；当前范围以最新验证为准，整个 Lyra 移植目标仍开放。

2026-10-02。依据当前 Godot 主目录代码、已导出的 UE 编译合同、ALS 目标骨架资源、安装版 UE 5.8 源码及本次实际 Godot 复跑。按用户要求不展开 5.8/5.9 差异。整个 Lyra 移植目标仍进行中。

最新三种原武器姿态图与独立Montage bank的连续对照见 [武器图验证](2026-10-02-lyra-weapon-montage.md)。原 Mesh RefPose 与 Skeleton RefPose 存在实际差异，独立bank按原网格参考和骨名采样/混合，保留人物ALS68/81及14入口组实例。原首装备/首Actor typed消费者、角色和武器的tick前后关系、最终weapon_r挂接尚待；不由Layer自行创建人物时钟或发布骨架。

最新武器资源调查确认：人物继续使用 ALS 68 skin / 81 logical，枪械保留原独立七/八骨动画与七骨物理网格。六段动画/六 Montage/三 FBX/十二纹理已导出；Debug/实际 Optimize 原生采样与模型取消发布验证、实际渲染和普通十角色回归通过，见 [武器资源验证](2026-10-02-lyra-weapon-resources.md)。原 AN_PlayWeaponMontage 的 MontageFollower 引脚未连接，两个实际原对象/EquipmentManager 探针均无 follow，不能添加推测的同步关系。独立武器 bank、typed 消费者与普通角色最终 weapon_r 挂接仍待接入，完整目标继续开放。

最新 ContextEffects 已接原 ALS 最终姿态与真实地面 ray、actor/components typed 接口、原 Context 聚合及 DefaultSkin 全匹配选择，和 GameplayEvent 保持原 callback 顺序并在完整角色发布后投递。两个独立 UE 原对象/原组件探针与 Debug/Optimize 原生、三Hz六角色及普通十角色矩阵通过，见 [ContextEffects验证](2026-10-02-lyra-context-effects.md)。原库只有音频，播放暂缓；武器/命名事件/MotionWarping、完整NotifyState dispatch、root物理与全部整链范围继续开放。下方 ContextEffects 尚未接入属于此前状态。

最新已接原Melee/Reload的真实角色GameplayEvent信号：独立原BP对象+ASC与Debug/Optimize取消/多角色验证通过，FootPlant空回调也经原对象执行确认，见 [玩法通知接收边界](2026-10-02-lyra-gameplay-notify.md)。ContextEffects、武器mesh、MotionWarping、完整NotifyState dispatch、RootMotion碰撞与整链验收保持开放。

最新通知推进已将实际五Slot Montage窗口与Source统一到角色队列：直接Montage、源Append、相关Slot桶保持原顺序，两个随机流、共享EndData和取消重试门禁已通过Debug/Optimize原生/生产验证，见 [最新通知汇合](2026-10-02-lyra-montage-notify-queue.md)。Interface实例与姿态调用仍沿原宿主执行；具体Notify玩法、RootMotion碰撞及完整Main验收继续开放。

后续同日实现已完成 Main75 精度修正和 Debug/Optimize 联合原生门禁，见 [Main75 最新验证](2026-10-02-lyra-main-inertia.md)。下方复核时的失败描述保留为历史阶段，当前该组件/宿主范围已通过；完整 Main、最终 ControlRig、生产换类和普通 Demo 仍开放。

本次继续直接读取最终FootPlant RigVM图，两独立进程退出0、结构内容相同，666包/786旧JSON保持。明确发现两个尚需核对真实可达性的目标骨名，以及控制器/腿长/部分alpha适配边界，见 [FootPlant与ALS骨架核查](2026-10-02-lyra-footplant-rig-analysis.md)。这是结构核查，不是Rig执行或移植验收。

随后已采集原生Rig程序/ALS81轨迹，Godot受控节点更新与输入绑定完成，见 [Main73更新验证](2026-10-02-lyra-footplant-rig-update.md)。原最终启用条件实际是 `DisableLegIK <= 0 && !UseFootPlacement`，不能由 `EnableControlRig=false` 推断关闭；属性访问缓存必须先刷新。Rig内部 `ik_ball_r` 存在，FootTrace定义不在本次编译指令中。原Rig完整姿态、实际Main接入和普通Demo仍开放。

## 资源决策

后续固定三Provider已经通过普通入口执行完整Main并发布到原ALS模型：每角色独立宿主/五Slot bank/实际Jolt及显式ALS参考最终Rig，Debug/Optimize各三Provider三Hz5040帧，三配置GPU渲染21图；68骨局部发布差0，模型/Rig世界最大差1.12462e-6m，当前ALS普通入口1700帧回归通过，见 [Main到模型验证](2026-10-02-lyra-main-model.md)。运行中换类/多角色、Notify/root物理、武器模型/复杂地形/性能与整个Main连续native仍开放；旧P4直接场景夹具AimOffset覆盖失败单独保留。下方未接普通Demo/模型的表述属于历史阶段。

后续ALS目标参考的连续最终Rig对照已通过：补齐原初始化后reference hash失效、二次Construction及当前pose保存/恢复，Debug/Optimize各2520帧/2154完整姿态/174474骨保持原门槛，实际Main/Jolt各2520帧含18初始化回归通过。见 [目标Rig连续原生验证](2026-10-02-lyra-rig-target.md)。这进一步验证ALS资源复用；完整Main联合native、普通Demo、生产换类/多角色/通知/root物理、模型视觉及性能继续开放。下条目标ForwardSolve待验是此前历史。

后续已新增显式ALS参考配置：原native参考姿态绑定桥按69目标骨名写InitialLocal，Construction重新计算42.57/40.20cm腿长与控制offset；89 imported骨/2个Rig自建ik_ball骨区分，保持原拓扑和未匹配参考。两独立UE采集一致，Debug/Optimize各4200项完整层级变换全0差，完整Main真实物理各2520帧含18初始化通过，见 [ALS Rig参考配置](2026-10-02-lyra-rig-reference.md)。目标配置连续ForwardSolve/整Main原生姿态及普通Demo/视觉仍开放；以下“比例profile待建立”是历史阶段。

后续已完成最终Rig真实Godot物理查询和Main73事务组合：实际4.7.2 Jolt、三profile×30/60/120Hz，Debug/Optimize各2520帧通过，见 [真实碰撞验证](2026-10-02-lyra-rig-collision.md)。采用实际组件空间、显式Traversable映射、自体RID排除、原世界半径及逆缩放法线，并补原Box几何法线语义；不称Chaos/Jolt通用接触等价。ALS比例profile、provider另一FootPlacement真实物理、生产入口和普通Demo继续开放，以下真实碰撞待接描述为历史阶段。

后续已完成完整PoseAdapter输出/部分alpha，并作为统一Rig候选显式接入Main73。Debug/Optimize原生受控输入各2154完整姿态/174474骨通过；实际Main三profile三Hz各7560帧含Slot覆盖/惯性/失败取消重试通过，见 [完整输出与Main73验证](2026-10-02-lyra-rig-output-main.md)。Main碰撞目前为解析平面，真实Godot碰撞、ALS比例profile及普通Demo继续开放；以下对应待接描述保留为历史。

后续已接原实际寄存器/父约束/Aim/五弹簧/两Alpha/双腿IK，受控ALS81输入和记录UE碰撞边界下两构建6552576比较通过，位置2.84217e-14cm、已比较Q差0；原变换显式正规化和初始化外部属性保持已修正，见 [即时求解验证](2026-10-02-lyra-rig-solver.md)。原输入边界不等于生产Main，输出桥/真实碰撞/比例适配继续开放。

后续已补五弹簧/两个Alpha独立历史，并完成436指令/35分支调度与Main73更新边界组合。三Hz两配置各683343访问严格同，正确读取宿主记录保持旧ground-v2全部输出值；backend仍重放Euler/扫掠判定输出，完整即时求解开放，见 [真实遍历验证](2026-10-02-lyra-rig-traversal.md)。

继续复用现有 ALS Mannequin 网格、材质、蒙皮权重及骨架。Lyra Manny 动画在 GASP58 经 IK Retargeter 离线转到 ALS；Godot 使用目标骨架的动画数据，最终由现有发布点写入模型。

| 布局 | 数量 | 用途 |
| --- | --- | --- |
| skin | 68 | 原模型蒙皮骨，保持原权重和绑定 |
| raw | 69 | skin 加父为 hand_r 的 weapon_r 控制通道 |
| logical | 81 | raw 加原 ALS 11 个虚拟骨及武器空间左手虚拟骨 |

控制通道参与源采样、插值、Layer、Montage 和 IK，不要求重做网格蒙皮。weapon_r 需要目标右手空间标定；虚拟骨按当前源采样语义生成后参与插值，不能在最终姿态后统一重建替代。

ALS 没有 Manny 的 spine_04/05，身材比例也不同。重定向处理动作姿态，程序化 Warp 的脊柱补偿、原 BlendMask、左右手 IK 和武器挂点还要显式映射目标骨名。不能按骨数组下标直接套用 Manny 配置。现有目标骨原生组件对照支持此路线，但完整握持、比例与地形的视觉验收仍开放。

后续Construction实测确认：原Main73保持Rig参考骨，腿长45.75/41.71cm；ALS目标参考腿长42.57/40.20cm，重定向动画不会自动替换这些Rig参数。已修复原地面夹具自定义通道未命中的缺口，补出2520帧正接触UE轨迹，Godot初始Construction精确通过；完整脚部求解及目标比例适配仍开放，见 [最新Construction验证](2026-10-02-lyra-footplant-rig-construction.md)。

后续已完成内部98项骨/控制层级的真实变化写入、初始/当前缓存、控制offset及取消重试原生对照，见 [Rig层级验证](2026-10-02-lyra-rig-hierarchy.md)。这补齐了ALS输入桥所需的基础，尚不能代表完整脚部求解。安装版原生PoseAdapter输入路径直接复制局部姿态并标脏，父骨名不匹配的空间处理还要分别核对输入后的层级读取及输出转换，不能先对所有输入骨统一转global再称等价。

随后实际输入对照确认69映射/12未映射目标通道、22原Rig重置骨、26父空间标记；Godot输入组件三Hz Debug/Optimize全0差，见 [Main73输入验证](2026-10-02-lyra-rig-input.md)。真实主图曲线正值、完整求解及输出转换尚未验收；新增观察轨迹也不能代替旧完整求解oracle。

资源包应同时保留骨骼轨迹、曲线及存在性/flags、Distance 数据、Sync Marker、Notify、additive 基底、typed attributes 和 RootMotion 数据。仅交付 FBX 不能承载完整执行合同。目标骨名/父序/参考姿态、源和目标包哈希、布局版本必须随资源校验；现有 JSON 字节哈希依赖继续保留。

## Interface 与 Layer 的执行边界

Animation Layer Interface 定义姿态函数签名；Linked Anim Layer 在 Main 的具体调用位置执行装备 Provider 子图。Layer 可以内部使用状态机、距离匹配、additive、骨遮罩或 IK。按骨混合属于其中的算子。

当前原编译合同为 14 个入口，全部属于 ItemAnimLayers 组。三个姿态输入入口为 FullBody_Aiming(PreAimPose)、FullBody_SkeletalControls(InPose)、LeftHandPose_OverrideState(InputPose)；Aiming 的 AimYaw/AimPitch 为 double。另外 11 个入口无输入姿态，包括十个移动状态入口及 FullBodyAdditives。

| UE 语义 | 当前 Godot 对应及职责 |
| --- | --- |
| Animation Layer Interface | LyraLinkedLayerContracts：不可变签名、参数类型、输入姿态、组与调用节点合同 |
| Main AnimInstance | LyraMainPoseHost/Main：运动观察、主机器、RootYaw、调用顺序及已提交反馈 |
| Linked AnimInstance | LyraItemLayerGraphInstance：同角色同组的可变机器、播放器、缓存与回调历史 |
| Linked Layer 调用节点 | LyraLayerInvocation：入口、原 Main 节点、实例和 epoch 身份，输入/输出独立 |
| BlendMask / additive | 已有姿态算子，显式目标骨映射、基底和空间 |
| Sync Group | 同一角色 source scope 内的播放器选主、marker 和时间推进 |
| 最终 Skeleton 发布 | 完整 logical81 求值后，经 skin 映射只发布一次 |

Layer Group 决定实例共享；Sync Group 决定源时间同步；BlendMask 决定骨骼权重。当前 Lyra 的 14 个入口共享一个组实例，多个角色的可变历史必须各自持有。若推广到其他 UE 图，非默认组共享实例、无组入口按调用节点独立实例；不能把当前单组约束直接当成所有图的规则。

UE 官方语义和本机 AnimInstance.cpp 的 LinkAnimClassLayers/UnlinkAnimClassLayers 支持此区分：

- https://dev.epicgames.com/documentation/unreal-engine/BlueprintAPI/Animation/LinkedAnimGraphs/LinkAnimClassLayers?lang=en-US
- https://dev.epicgames.com/documentation/unreal-engine/animation-blueprint-linking-in-unreal-engine

现有 ILyraItemAnimationLayers 主要服务旧示例的资源选择和算子工厂。完整执行已在 LyraItemLayerGraphInstance 的 typed Prepare/Evaluate/提交边界实现，不能以 ResolveStateClip 的返回值代表整个 Layer 求值。继续扩展当前宿主，避免再建立一套角色播放时钟。后续通用接口可从编译合同生成 typed 调用包装，并让装备定义复用同一套基础算法、只覆盖原类实际变化的资源与配置。

## 每帧执行及换装备

后续普通多人入口及角色身份已完成本批门禁：共享不可变资源、每角色独立Main/Layer/bank/Rig/模型，角色ID进入Main/cache/Slot上下文，LayerEpoch只标装备代际；销毁撤销旧调用/Main写资格。Debug/Optimize各六角色三Hz10080角色帧/普通十角色16800发布帧，逐对完整通道/历史及两配置摘要一致；实际GPU七图修为FramePostDraw并记录提交帧，见 [多角色验证](2026-10-02-lyra-multi-character.md)。只关闭上述交错单物理线程边界，不是纯动画线程并行/完整Main连续native；统一Notify/root物理等继续开放。下方多角色待接为历史阶段。

后续普通完整Main已接Q三Provider生产重绑：Main/机器/RootYaw/直接Lean/Sync/惯性/Rig及模型持续存在，同类复用、换类新14入口组/epoch，旧组撤销写Main资格；初始化与blend请求按原调用边界分开。最终Rig的上一曲线保持在Main，三Hz非零反馈跨重绑验证通过；Debug/Optimize各三初始类三Hz5040帧、GPU480帧及原组件/入口回归通过，见 [生产重绑验证](2026-10-02-lyra-main-rebind.md)。本批只对照既有八步绑定owner协议，不把它当成换层连续Main姿态oracle；多角色及后续范围继续开放。下面“先做生产更换”的描述是该实现前的计划。

1. 主线程采集真实运动、输入、地面和组件变换，建立角色候选帧。
2. Main 按原图遍历 Layer/Slot/cache，执行 Initialize/BecomeRelevant/Update，登记真实访问的动画源与惯性请求；重复 cache 读取不重复 tick。
3. 角色 scope 对实际活跃源共同 Sync，再按原依赖顺序采样和 Evaluate；pose、曲线、属性及 RootMotion 共用候选身份。
4. 完成 Main 外层槽、惯性、RootYaw、SkeletalControls 和最终 ControlRig 路径，记录最终曲线反馈。
5. 预校验 Main、Layer、Montage bank、事件及物理消费的候选；成功提交后统一发布 skin，失败整帧取消。

同类重绑保留实例与历史；换类先验证资源并构造新候选，在物理帧边界更换实例、绑定新 epoch，拒绝旧候选和反馈。已采集的 UE 八步序列中返回旧类也创建新实例，因此当前配置下不应把所有装备实例永久缓存。惯性请求须进入真实接收节点；层混合配置、Montage 数据共享、Notify 接收/传播分别消费原合同。

资源 Notify 与 Linked Instance 的通知传播是不同路径：当前 Provider 的传播标志为 false，不会取消 Sequence 脚步事件。统一队列需要保留 source owner/player/epoch 和触发区间，取消重试不能重复消费。普通地面移动保持角色运动驱动；RootMotion 的提取、图内属性传播及角色碰撞消费各自按原模式接入。

后续已完成通知资源合同只读导出与两独立UE复核：344原Sequence/Montage、300 ALS目标、1515原事件，保留真实对象/载荷/状态flags和原生时间，源目标及旧189clip/1450事件逐值同，见 [通知合同验证](2026-10-02-lyra-notify-contract.md)。本机源码进一步确认Receive/Propagate控制命名事件传播，类通知直接调用对象；角色统一事务仍须保留Main/Linked实例归属和生命周期。完整Main运行队列/typed消费者与连续native尚未接入。大量接口扩展应把这些执行语义纳入合同，不能只生成同名姿态函数。

AnimationTree 可用于后续可视化编辑/预览。当前原生对照要求的 Linked 实例、缓存调度、距离匹配、完整属性和事务由现有 C# 执行宿主负责，沿用当前单一骨架发布点。

## 面向大量 Animation Interface / Layer 的扩展方式

后续已实现真实 Source 通知读取/提取桥，见 [Source通知验证](2026-10-02-lyra-source-notify.md)。它消费共同 Sync 的实际 dispatch 顺序及 sample 区间，保留 Main 直接 Lean 和 Linked 装备源的不同生命周期；三Hz换类/取消重试检查不新增源时钟。原资源通知合同与 Layer typed 姿态调用已相接到此边界，运行队列、Montage汇合、NotifyState和玩法消费者仍须按原实例归属进入角色统一事务，不能把只读提取当完整通知执行。

最新源队列推进见 [队列验证](2026-10-02-lyra-notify-queue.md)。当前 Linked 根 Sync 实际归 Main：源通知共用 Main 代理过滤/随机流，保留源 provenance；Main PostUpdate 再 Append 到 AnimInstance，Montage 另用其随机流。重绑保留 Main 源队列历史，不能按每个 Layer 重置。已接 Source 状态候选到角色事务；五 Slot 通知汇合、原 BP/类副作用、全局 NotifyInstanceID 对齐及完整连续 native 仍须补齐。

当前实现有意绑定实际Lyra的单一ItemAnimLayers组，合同加载会拒绝其他组/调用拓扑。固定类14入口执行通过不等于任意UE动画接口兼容。下一步通用化应从已有编译合同及宿主中抽取以下边界，保持本次完整Main接入优先。

| 边界 | 建议落地方式 |
| --- | --- |
| 接口声明 | 从 `FAnimBlueprintFunction` 导出函数名、Pose参数、普通参数类型、组和混合配置，生成C# typed调用包装；原double保持到明确转换点 |
| 实现定义 | 不可变资源描述承载基础算法、实际子图和继承后的资源/配置覆盖；Unarmed/Pistol/Rifle共用已验证算法，不能把拓扑变化只当资源换名 |
| 实例创建 | 默认按角色/实现类/命名组管理可变历史；无组入口按调用节点建实例。self-layer、Unlink、部分覆盖及持久共享策略分别按原配置实现 |
| 调用与执行 | 每个调用保留节点、实例代际、物理帧、weight/relevance及只读输入pose；Prepare登记真实源与历史，Evaluate消费角色共同Sync后的结果，Commit/Cancel沿用现有事务 |
| 姿态包 | 连同布局身份传递骨骼、曲线presence/flags、typed属性及RootMotion；输出独立，缓存引用不得增加source tick |
| 换装 | 同类重绑复用，换类先验证再在帧边界切换，原惯性接收节点处理过渡；拒绝旧代候选/反馈，补多角色身份隔离和生产换类门禁 |
| 通知及动作 | 分别实现资源Notify、Linked命名Notify传播和Main Montage配置；不由每个Layer创建独立角色时钟或骨架发布点 |

这样可以继续增加武器/姿态类和接口入口，保留原Main主图的宏观状态与调用顺序。普通业务接口若只传状态/发请求，可以映射typed服务；带Pose输入/输出的Animation Layer Interface必须进入上述动画执行合同，二者不要共用一个仅返回clip名的API。

首批交付应是现有Lyra的固定完整Main、实际装备重绑、多角色与普通Demo，再为多Group/未绑定/Unlink等扩展建立独立语义门禁。需要可视化编辑时，可在这套执行数据上增加Godot编辑器入口；AnimationTree的混合节点本身不承载全部UE Linked实例生命周期。

## 本次验证与尚未关闭的范围

当前 Debug 构建成功，0 警告/0 错误；以下三个场景均实际退出0，无 Godot ERROR/WARNING：

| 场景 | 本次结果 |
| --- | --- |
| logical source | 234 源、936 原生采样，raw69/logical81/skin68；位置差0，最大 quaternion 差3.764949453935611e-16 |
| Idle/Recovery resources | 245 源、三 Provider、197 Sequence 绑定，missing0；475 新样本和960旧样本通过，属于资源验证 |
| linked binding | 14入口、8步重绑、4 owner、4次同类复用、6坏合同拒绝通过 |

日志为 artifacts/lyra-analysis/als-interface-{logical,resources,binding}-20261002.log。本次未重新导出或修改 UE 资源；原生数据来自已有采集。

当前 14 入口和活动五 Slot 已进入实际 Main 宿主，最新已关闭范围见 2026-10-02-lyra-main-slot-pose.md。本轮正在推进 Main75：已接入候选惯性和完整通道，但联合原生门禁仍在 frame162/bone52 失败，quaternion 差3.1274041148882107e-9，超原1e-10门槛。按实际 ISPC 算术顺序补齐插值后该失败仍在；不得标为原生验收通过。失败证据为 main-inertia-godot-ispc-lerp.log，720帧诊断有64次失败，不能把诊断进程退出0当成测试通过。

插值改动后的回归已实际完成：Core 数学/惯性52项、Standing原生6组均通过；旧 Main Slot 联合47,610帧/19,944姿态通过，实际 Main75 宿主40,320帧/39,375姿态、395惯性请求及4,263活动惯性求值通过，两个 Godot 进程均退出0且无 ERROR/WARNING。宿主取消/重试与候选门禁通过不代表其完整原生姿态精度通过。本轮没有 Optimize 配置复跑或新渲染。日志为 main-inertia-ispc-lerp-{math,standing}.log、main-inertia-slot-composition-ispc-lerp-regression.log 和 main-inertia-host-godot-ispc-lerp.log。

完整 Main 惯性精度、ControlRig73 启用路径、生产换类/多角色身份、统一 Notify、RootMotion 碰撞消费、普通 Demo、坡地/平台/握持渲染及性能继续开放。未绑定默认 self-layer、Unlink、部分接口覆盖、多个 Layer Group 和持久实例配置也需要针对性补验证。继续按 ROADMAP 推进，不以资源或组件通过替代完整迁移验收。
