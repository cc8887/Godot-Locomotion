最新步枪方向修复（2026-10-04）：普通Main W前进切D右移已复现，Right片段身份正确，ALS挂载组件朝向错误使Orientation额外扭转约90度。模型挂载前Godot Y转-90度，实际组件相对UE yaw=-90，与根轨迹+Y前向一致；Core算法/资源保持。短切换三Hz最终残余1.30/2.14/2.60度，稳定Cycle换源三Hz均<0.14度；手枪/空手及十角色换装回归，两构建18份报告逐帧同、16张新GPU样本检查，另普通地形60Hz两构建各900帧/1800蒙皮发布和完整rows同。18个Core机制/870资产JSON/ALS模型哈希不变，两轮六程序集恢复；夹具/包装失败记录保留。仅关闭本次模型方向错误，整体人工键鼠验收保持开放。见 [步枪前进切右移修复](2026-10-04-lyra-direction-change-fix.md)。

最新输入/接触 Core与当前目标核对（2026-10-04）：方向限长/local-world float旋转/速度比例归AlsCharacterInput；RootMotor备用完整有序接触速度投影/grounded clamp归AlsCharacterContactVelocity。原SweepMath共享scalar Dot/Normalize/ProjectPlane，Core地面控制器也直接复用；Godot保留真实对象/物理查询写入/输入坐标/显示，Lyra保留图与资源设置。Core166（新24）首轮过、Debug/显式Optimize0警告错误；44成功Godot进程+4旧差异诊断。实际Godot4096输入/512任意轴/1024接触逐float位同；三Hz六角色Root每构建10080移动/retry及每Hz五非零受控Root碰撞/末速度投影通过，原动作nonzero仍false。普通ALS/完整Main/Rig/十角色/空中/台阶/floor/地形及普通Emote均过，指定完整报告同前批与两构建；地面8/floor3旧差异及原阈值保持。11源/4680基线（870JSON）、六轮六程序集恢复审计过；额外只读ALS原FBX/编译载荷一致、旧九GPU哈希及contact sheet复核。当前生产通用归属与ALS复用已逐项核对，完整目标仍缺实体键鼠/最终交互验收；URO/全UE调度/额外Provider与原暂缓项后移。无UE启动修改重导、新GPU或实体输入，未提交推送。见 [输入接触与当前目标验证](2026-10-04-lyra-input-contact-core.md)。

最新普通运动子步 Core（2026-10-04）：consumed输入加速度/analog、站立地面jump门控/保持较高upward、站蹲/空中/Root积分选择及实际速度反馈归纯.NET AlsCharacterMotion；继续复用ALS CharacterVelocity/Falling，不新增积分/时钟。原ALS Integrate输入量提取共享InputAmount，两条生产调用各保留原float/clamp顺序。普通玩家/NPC默认接入；Godot保留输入坐标/实际查询/写入/物理owner。Core142（新26）/Debug及实际Optimize0警告错误；36成功Godot进程+4旧差异诊断（含两构建普通Emote各480帧/1实际Root胶囊移动，完整报告同前批），每构建三Hz空中5040/台阶2100/floor1008移动/retry、十角色4800/地形6300发布，完整报告同前批及两构建。112空中查询0mismatch；地面8/floor3旧差异和原阈值保持，6源/4678基线（870JSON）及六轮六程序集恢复审计过。v1限速测试误要求20/40导致2失败，经独立原scalar核对改精确舍入预期，生产算法不变、失败留档，最终v2。无UE启动修改重导、新GPU或实体键鼠；RootMotor备用接触投影/通用协调及输入adapter归属、当前功能汇总和实体键鼠仍待，URO及原暂缓项后移，完整目标未完成。见 [普通运动子步 Core验证](2026-10-04-lyra-motion-step-core.md)。

最新穿透恢复/蹲伏 Core（2026-10-04）：生产 Move/Resolve/安全移动重查、五类恢复顺序/实际位移判定及四类限幅归纯.NET AlsCharacterSweep；完整站蹲净空/地面ray gap/空中sphere回退和scope释放归AlsCharacterCrouch。复用Core floor/ground/air、ALS积分/动画/两武器，原scalar Dot/Normalize/capsule support归共享SweepMath；Godot保留实际查询、opaque Transform3D及原位置/形状/显示写入。Core116（新Sweep21/Crouch19）首轮通过、Debug/实际Optimize0错误警告；34成功Godot进程+4旧差异诊断。每构建三Hz空中5040/台阶2100/floor1008移动及retry、十角色4800/地形6300发布，完整报告同前批及两构建；实际空中112查询0mismatch，恢复teleport16/combined8，另三恢复分支仅单测覆盖。原地面32查询8差异/floor144查询3差异及原阈值保持，未称UE/Jolt等价；9源/4671基线（870JSON）及五轮六程序集恢复审计过。没有UE启动修改重导、新GPU或实体键鼠；下一步普通生产通用归属/ALS复用和当前功能验收及实体键鼠，URO及原暂缓项后移，完整目标未完成。见 [穿透恢复与蹲伏 Core验证](2026-10-04-lyra-recovery-crouch-core.md)。

最新地面探测 Core（2026-10-04）：完整Compute/Find/短胶囊重试/line回退/perch支撑/高度带/Initialize及AfterMove/AfterSweep决策归纯.NET AlsCharacterFloorProbe；typed命中保留opaque collider与原sweep几何，地面/空中/Root生产floor默认接入。AlsCharacterSweepMath共享高度和capsule回拉，Godot保留实际查询/后端载荷/位置写入；原ALS积分/动画与两武器复用。Core76（新32）、Debug/实际Optimize0警告错误、22成功Godot进程+2旧差异诊断；三Hz八角色每构建1008移动/重试、顶棚/高度带/perch拒绝报告同迁移前及两构建。各144floor查询/2250物理/8perch，旧3mismatch/0.01cm门槛保持，除tag外完整报告同前批；各112空中查询0mismatch。十角色4800及地形6300发布/构建、真实Rig完整报告同前批，7源/4667基线（870JSON）和四轮六程序集恢复审计过。首高度预期float字面量错误已独立核对原标量修正，v1失败保留、最终v2，无算法/阈值修改；无UE启动修改重导、新GPU或实体键鼠。下一步Sweep穿透恢复顺序/限幅及蹲伏净空回退归Core，再收尾普通生产归属与实体键鼠；URO及原暂缓项后移，完整目标未完成。见 [地面探测 Core验证](2026-10-04-lyra-floor-probe-core.md)。

最新空中移动 Core（2026-10-04）：完整AirMovement子步/顶点返还/滑动防坡面加速/两墙及侧向脱困/perch随机/着地walking remainder归纯.NET AlsCharacterAirMovement；继续复用ALS下落/速度、SafeNormal及上一批Core地面控制。原通知FRandomStream提取共享AlsRandomStream，两条生产路径共用。Core40（新23）、Debug及实际Optimize共22个Godot进程通过；三Hz六角色每构建5040移动/重试、真实跳跃/落地/净空/墙面回归，空中各112真实查询/20顶点分段/28着地/6多接触、0mismatch，完整报告同迁移前及两构建；十角色4800及地形6300发布/构建、真实Rig物理完整报告同前批。8源/4661基线（870资产JSON）与三轮六程序集恢复审计过。实施/测试/构建/运行v2，v3仅纠正审计新增计数，C#不变；无UE启动/修改/重导、新GPU或实体键鼠。下一步FloorProbe判定/高度调整、Sweep穿透恢复及蹲伏决策归Core，再收尾生产归属与实体键鼠；URO及原暂缓项后移，完整目标未完成。见 [空中控制器 Core验证](2026-10-04-lyra-air-movement-core.md)。

最新地面移动 Core（2026-10-04）：完整MoveAlongFloor/StepUp/Slide控制归纯.NET AlsCharacterGroundMovement，包含斜坡投影、两墙响应、up/forward/down、edge/高度/净空拒绝、完整宿主检查点回滚、接触顺序及实际位移速度；继续复用原ALS速度/加速度与动画链。Godot只适配实际floor/sweep/位置和Transform3D、Collider/CanStep metadata。Core24（新19）、Debug及实际Optimize共20个Godot进程通过；每构建三Hz五角色台阶2100次移动/重试、低/高/顶棚/禁Step均过，台阶/十角色/真实Rig物理/地形完整报告同两构建及各自前批，地形每构建6300发布；7源/4657基线（870资产JSON）及六程序集恢复审计过。v1 Math命名冲突编译日志保留，最终v2；无UE启动/修改/重导、新GPU或实体键鼠。生产归属核对明确下一步完整AirMovement碰撞控制，再FloorProbe判定/Sweep穿透恢复/蹲伏决策，然后收尾Core归属与实体键鼠；URO及原暂缓项保持后移，完整目标未完成。见 [地面控制器 Core与生产归属核对](2026-10-04-lyra-ground-movement-core.md)。

最新 Rig输出/约束 Core（2026-10-04）：可变骨数的AlsRigPoseAdapter归纯.NET，global先于local、不同target/Rig父骨、未映射虚拟骨、独立scratch/失败不发布及重叠span均保留；部分权重与Root属性通过既有ALS LocalDifference/AccumulateAdditive组合，去掉Lyra重复数学。单父权重1/完整TRS/保持初始offset的Transform control约束归已有AlsRigHierarchy；Lyra只保留81骨JSON及原资源配置校验。Core55（新19）、Debug及实际Optimize共22个Godot进程通过；原输出各174474骨/10279440比较、maxVector2.84e-14cm/Q2.22e-16，完整Main＋Rig各7560帧与真实Godot物理2520帧过。十角色各4800发布/地形三Hz各6300发布，十角色/物理/地形完整报告同两构建及前批；8源/4652基线（870 Lyra资产JSON）与六程序集恢复审计过。没有UE修改启动/重导、新GPU或实体键鼠；多父/axis过滤等非原资源模式未实现。仅关闭本批通用输出/单父约束及ALS additive复用，余下生产路径Core归属核对、实体键鼠及完整目标开放。见 [Rig输出 Core验证](2026-10-04-lyra-rig-output-core.md)。

最新压缩采样 Core（2026-10-04）：decoded均匀/稀疏键、Step/Linear、原float向量与四元数归一化策略归纯.NET AlsCompressedTransformTrack；原ALS帧/子帧转换提取AlsAnimationFrameTime，raw pose/raw Root与compressed共用。Root区间继续复用既有AlsRawRootMotionIntervalSampler及PrecisePose.Inverse，Lyra只保留JSON/codec/资源slot与生命周期适配。Core78（新19）、Debug及实际Optimize共22个Godot进程通过；189源Root、起步、Pivot、Montage的Root P/Q/S误差0，完整Main＋Rig各7560帧通过。十角色各4800发布、真实Jolt地形三Hz各6300发布，完整报告同两构建及前批，六程序集恢复；8源/4648基线审计过。首轮新增scale测试预期20应40已核对原公式修正，失败证据保留；原资产JSON/配置保持，无UE启动/重导、新GPU或实体键鼠。仅关闭decoded键播放与ALS公共转换，压缩字节解码及URO仍在当前范围之外；后续Rig输出PoseAdapter/ParentConstraint通用归属与实体键鼠继续开放，完整目标未完成。见 [压缩采样 Core验证](2026-10-04-lyra-compressed-sampling-core.md)。

最新 Rig存储/遍历 Core（2026-10-04）：工作/只读/外部寄存器、typed整值与嵌套字段、不可变扩展、候选Clone/工作重置归纯.NET AlsRigMemory；九opcode/typed控制分支/独立work与lazy缓存/单入口故障重试/循环门禁归 AlsRigTraversal，Lyra只保留JSON/固定436布局/原入口/资源单位适配。继续共用原Core姿态、层级、数学、IK、Spring、Alpha。Core36（新19）、Debug及实际Optimize共22个Godot进程通过；原遍历各2520帧/683343访问、完整输出174474骨/10279440比较和真实Jolt2520帧通过。十角色各4800发布、地形三Hz各6300发布，完整报告同两构建及前批，六程序集恢复；9源/4642基线审计过。实现/构建/运行v2，v3仅修审计报告字段，C#及runner保持；原JSON/配置保留，无UE启动/重导、新GPU/实体键鼠或全量/性能。仅关闭本批通用存储与遍历，非全UE RigVM；其余明确验收及实体键鼠开放，完整目标未完成。见 [Rig运行机制 Core验证](2026-10-04-lyra-rig-runtime-core.md)。

最新 ALS/Rig Alpha 复用（2026-10-04）：既有 ALS Scale/Bias/Map/Clamp/非对称 float 插值提取为 Core AlsInputScaleBiasClamp，ALS 节点保留最终0～1输出，Lyra FootPlant两个 AlphaInterp 共用未钳制内核和原独立候选历史。Core62（新11）/Import9、Debug及实际Optimize共14个Godot进程通过；Rig动态各3360帧/19217调用逐位float与vector差0，完整Main＋Rig各7560帧通过。两构建十角色各4800蒙皮发布、真实Jolt地形三Hz各6300发布，完整报告同两构建及前批，六程序集恢复。6源/4640基线审计过；原JSON/配置保留，无UE启动/重导、新GPU/实体键鼠或全量/性能。仅关闭Alpha内核复用；寄存器/编译遍历通用归属及实体键鼠仍开放，完整目标未完成。见 [Alpha Core复用验证](2026-10-04-lyra-alpha-core-reuse.md)。

最新 Lyra 可见地形（2026-10-04）：普通Demo加入25cm台阶／11.31度斜坡／85cm落差及60cm阻挡，玩家/NPC继续共用原移动服务、完整Main/Rig和ALS68骨输出。Debug/实际Optimize三频每构建6300蒙皮发布、两武器/站蹲/ADS/跳跃/落差通过，完整报告同构建及前版本；九张GPU指定地形/稳定蹲姿/握持图已检查，原十角色两构建报告同前批。最终9成功进程、4源／4638基线及六程序集恢复审计通过。正常窗口已启动，但computer-use两次激活被Windows GetCursorPos 0x80070005拒绝，未发送按键；实体键鼠验收仍开放，完整目标未完成。指定地形样本通过不等于任意复杂地形/连续人工或性能验收；Core余下审计及暂缓项保留。见 [地形与近景验证](2026-10-04-lyra-terrain-course.md)。

最新 Rig/Core 复用（2026-10-04）：FootPlant 双骨求解使用既有 AlsRigTwoBoneIk，Aim／Euler／逆变换／Remap／float 角度归 Core；单父骨 TransformControl 当前／初始／offset／dirty／Construction／候选层级归 Core，Lyra 保留资源校验适配。Core148（新26）、Debug14／实际Optimize13共27进程及七张GPU图通过；真实Jolt三Hz2520帧、完整十角色报告同前批，六程序集恢复。16源／4622基线、870JSON／710原包／9配置审计通过。首轮漏传报告路径失败保留，修验证脚本后复跑，C#沿用已验构建。仅关闭本批数学和层级；地形／脚部／近景／真实键盘及完整目标仍开放。见 [Rig Core 复用验证](2026-10-04-lyra-rig-core-reuse.md)。

最新姿态／惯性 Core（2026-10-04）：原 ALS LocalApply／完整权重与 Lyra Main／Montage 共用 AccumulateAdditive；完整 curve flags／RootMotion 速度历史、Log/Exp／衰减、组件 teleport 与 CopyFrom/Reset 归纯 .NET Core，复用原 ALS 惯性内核。RootYaw 数学归 Core，Win64 数值桥可选；两份 DLL 不可用时普通十角色实际运行及进程模块检查通过，文件原哈希恢复。Core102（新12）、Debug10/实际Optimize9及托管回退1，共20个 Godot进程、七张GPU图通过；所有完整十角色报告与前批相同，六程序集恢复。14源/4615基线、870JSON/710原包/9配置审计过。仅关闭本批共用姿态累积、完整惯性载荷及RootYaw数学；插值／BlendWith／角度／轴等已定位通用入口和近景／地形／实际键盘继续开放，目标未完成。URO与精确UE调度仍后移。见 [姿态与惯性 Core 验证](2026-10-04-lyra-pose-inertia-core.md)。

最新移动数学已归 Core 并与 ALS 共用：插值、差值 BlendWith、平方容差 SafeNormal、角度及矩阵方向运算；Core197／Debug14+Optimize13进程和七张GPU图、13源／4619基线／资源与六程序集恢复审计通过。见 [移动数学 Core 验证](2026-10-04-lyra-locomotion-math-core.md)。FootPlant 通用 Aim／Euler／IK、旧预览数学及地形／近景／真实键盘仍开放，完整目标 active。

最新动画数据 Core（2026-10-04）：整数属性身份/载荷及带 flags 的曲线载荷归纯 .NET Core；Source、Main、Idle、Aiming、Additives 和 Montage 实际调用共用运算，并复用已有 ALS 标量 Scale/Accumulate。原始权重/整数截断/缺失与存在零/flags/首贡献负零保持。Core 相关30、Debug12/实际Optimize11个 Godot进程、当前ALS普通1700帧、十角色各4800发布和七张GPU图通过；两构建完整报告与前批相同，六程序集恢复。17源/4605基线及870JSON/710原包/9配置审计通过。仅关闭本批整数属性与曲线运算迁移；Main/Montage通用additive pose、Main惯性完整载荷/RootYaw数学、其余通用审计和近景/地形/实际键盘继续开放，目标未完成。URO及精确UE调度仍后移。见 [动画数据 Core 验证](2026-10-04-lyra-animation-data-core.md)。

最新共享缓存与 Transform 属性已实际归入 Core，并与现有 ALS evaluator 共用控制；Core206、Debug/Optimize21个成功进程及资源/源码/恢复审计通过。整数属性混合和带 flags 的曲线载荷/覆盖仍是下一项通用抽取，完整通用审计与视觉/键鼠保持开放。旧 P4 基线失败与修复前 Grounded 夹具失败均留证。详见 [本批验证](2026-10-04-lyra-scoped-cache-core.md)；当前目标仍以 [ROADMAP](../../ROADMAP.md) 顶部为准。

当前目标按用户 2026-10-04 修订：沿用 ALS 人物，迁移 Lyra locomotion 思路与运动核心算法，武器先覆盖手枪和步枪；通用引擎能力归 Core，优先复用 ALS，URO 和全部私有阶段还原进入后续路线。本批已归 Core 的 Proxy/缓存/骨缓存门控/启动生命周期与现有 ALS 缓存共用；实际 Additives 顺序及 Provider 根入口已修复。Core197、Debug/Optimize29个 Godot 回归、普通十角色和七张渲染图通过，原资源/配置及最终源审计通过。进程内逻辑键诊断已驱动手枪→步枪和蹲姿，原物理键硬件验收及剩余通用控制/近景地形继续开放。当前统一范围见 [ROADMAP](../../ROADMAP.md)，本批证据见 [共享 Core 验证](2026-10-04-lyra-core-reuse.md)。以下历史记录中的完整复刻与 active 口径不覆盖当前目标。

最新Lyra Main延迟启动：继续ALS68蒙皮/raw69/logical81及原十四类型化Interface入口，Main构造不预先进入根；首次实际Prepare进入Initialize0，再按原属性顺序初始化/缓存全部Linked子图（Main骨缓存尚未更新），随后首次CacheBones0。Main根先发布真实Proxy阶段，合法未更新上下文保留；有效缓存重复不遍历，失效后仅一次推进，首次候选取消/重试保留图初始化但丢弃动画候选。原Manny164组件36组两独立UE捕获三JSON逐字同、零counter/globalframe写入/资产保存，528次阶段前缀与27隐藏实例保持；Godot每构建9024比较/684入口/72门控/36retry。Debug/实际Optimize0错误警告、78个Godot进程及1573104联合计数标量通过，普通十角色/Emote完整报告同两构建与前批，四轮六程序集恢复通过。17冻结实施/验证源与其余4568基线、870JSON/710原包/9配置、5探针源/4本机UE源审计过。**仅关闭Main首次根启动、全部Linked deferred子图和Main首次/显式失效骨缓存；39次Provider Update根额外缓存尚未实现，完整阶段/自然全局frame与worker/URO、重初始化/RequiredBones/LOD/Rig Construction继续开放，完整目标active。**全部其它Provider/参数/私有字段、原物理314/1680和暂缓项保持。原UE每次744警告保留，无Error/Fatal/Ensure；编译失败与v4主动结束日志保留，运行期间源码冻结；无原项目/引擎源配置或资产保存重导、GPU/全量/十分钟/性能或提交推送。见[延迟启动验证](2026-10-04-lyra-deferred-startup.md)。

最新Lyra实际Provider求值入口：继续ALS68蒙皮/raw69/logical81与原十四Interface函数，Skeletal/Aiming/LeftHand/Additives及十状态来源在真实求值回调先继承Main Evaluation，再遍历输入；Main78/83与Provider78读前检查实际实例入口/计数，缓存命中与Slot裁剪保留，角色统一取消/提交，隐藏实例保留历史。原完整UE夹具复用4320帧/120relink，23096根求值/9346隐藏求值帧与334400原计数断言；Debug/实际Optimize0错误警告、70个Godot进程及1573104联合计数标量通过，MainPose两模式各11340帧/23442历史检查/621提前缓存拒绝，普通十角色/Emote完整报告同两构建与前批，四轮六程序集恢复过。11最终冻结源/其余4569基线、870JSON/710原包/9配置/7引擎源审计过。**仅关闭完整Main的Provider Evaluation继承与真实缓存读入口，Initialization/CachedBones绝对启动/完整阶段、自然全局frame/同帧worker/URO、重初始化/RequiredBones/LOD/Rig Construction仍开放；完整目标active。**其它参数/Provider/私有字段与原物理314/1680及暂缓项保持。首次审计误把内部tap当Interface的失败保留，运行源码冻结期间未改；无本批UE启动/资产保存重导、GPU/全量/十分钟/性能或提交推送。见[实际求值入口验证](docs/verification/2026-10-04-lyra-proxy-evaluation.md)。

最新Lyra实际Update计数：ALS68/69/81与十四入口保持，完整Main及实际Provider拥有阶段候选历史；Main在真实根更新前推进Update，访问的Linked根先继承再进入worker，SkeletalControls使用实际计数；Evaluation移到Main owner，取消恢复/统一提交及self/解绑重连保持。pending骨缓存拒绝前不再修改阶段状态，取消后真实缓存恢复通过。原UE最终三装备四布局4320帧/120relink、23144根更新/9334隐藏owner及193168原计数断言；Core170、Debug/实际Optimize0错误警告、70个Godot进程与907560联合计数标量通过，普通十角色/Emote报告与前批同，四轮六程序集恢复审计通过。21冻结源/其余4554基线、870JSON/710原包/9配置/7引擎源通过。**关闭生产Main Update与Provider继承；受控执行frame非自然调度，Initialization/CachedBones绝对启动、Provider Evaluation历史、完整阶段、重初始化/RequiredBones/LOD/Rig Construction继续开放。**新原生启动阶段因访问而异，不能套用已有受控seed验收。全部其它私有/Provider/物理314/1680与暂缓项保留，目标active。原UE构建2旧API警告/捕获各3135警告保留，无Error/Fatal/Ensure；主动结束的v4部分矩阵保留，最终runtime-v5/native-v2-full/package-v1，无原资产保存重导/源码配置修改、GPU/全量/十分钟/性能或提交推送。见[实际Update验证](2026-10-04-lyra-proxy-update.md)。

最新Lyra Proxy求值计数：ALS68/69/81与十四入口保持，Main实际根求值counter已与视图序号分开，角色候选推进/取消恢复/统一提交；修正Rebind新Slot漏传实际CacheOwner，并新增真实初始self/解绑重连断言。原UE两独立进程31步/三Proxy、65536次根回绕、零counter写入，requests/native/closure逐字同；Core170/768计数标量、Debug与实际Optimize0错误警告、62个Godot进程通过，MainPose/反馈各11340帧、Slot47610、四布局每构建4320最终Main帧retry，普通十角色/Emote报告与前批及两构建相同。15源码/其余4339基线、870JSON/710原包/9配置/7原UE源码与三轮六程序集恢复审计通过。**仅关闭原入口counter规则、生产Evaluation候选和换层Slot实际owner；受控外部frame非自然组件调度，生产Update/完整阶段、后续整图重初始化、RequiredBones/LOD与Rig Construction继续开放。**self scalar/部分/任意重复调用/其它Provider、非零Aiming原生传播、全部私有字段、物理314/1680及全部暂缓项保持；完整目标active。失败包/SDK目录与首次测试计数断言保留，无原资产保存重导、GPU/全量/十分钟/性能或提交推送。见[Proxy阶段验证](2026-10-04-lyra-proxy-phase.md)。

最新Lyra缓存统一所有权：ALS68/69/81与十四入口保持，真实Main78/83及Provider78共同历史贯通阶段、延迟选主权重和Main求值作用域，候选统一取消/提交。新作用域同计数重采样、嵌套恢复重算、骨缓存使Evaluation失效；原UpdateCounter不新增相关性时钟。原Main/三Provider两UE进程90行逐字同，Godot1620计数字段/234源求值/36retry/12嵌套精确通过。Debug/实际Optimize0错误警告、Core187、62个Godot进程通过；MainPose增加实际缓存历史、权重、取消/提交、update-only及角色隔离断言，四布局每构建4320最终Main帧retry，普通十角色/Emote与初始/解绑报告保持。27源/其余2198基线、870JSON/710原包/9配置/31原UE副本与三轮六程序集恢复审计过，见[缓存所有权验证](2026-10-04-lyra-cache-owner.md)。**仅关闭固定节点共同历史及Main组合路径；自然Proxy绝对计数、完整阶段与后续重初始化、RequiredBones/LOD、Rig Construction、直接组件/任意图求值仍开放。**self scalar/部分绑定/重复调用/其它Provider、非零Aiming原生传播、私有字段、物理314/1680与全部暂缓项保留，完整目标active。探针构建、入口隐式递增和retry覆盖计数失败证据保留，算法/阈值未改。最终runtime v4/native v2/package-v5，无新GPU/全量/十分钟/性能或原资产保存重导、提交推送。

最新Lyra骨控制初始化：ALS skin68/raw69/logical81与十四入口保持，三个Provider真实八节点Initialize/CacheBones已接原阶段。原Main/Provider两UE进程24节点48种子逐字同，Alpha bool/clamp仅清initialized、ActualAlpha与嵌入值保持；Foot定义插值重置、Delta/短counter/character/root保持，Leg同FK历史保持，真实27骨引用/Provider与参考长度完成缓存。生产延迟绑定、缺缓存先于写姿态拒绝，重复缓存不清历史。Debug/实际Optimize0错误警告、Core163、98个Godot进程通过；骨控制四组3780帧、Main组合/姿态11340帧、Rig7560帧，普通十角色/Emote与初始/解绑计数保持，四布局每构建4320帧及同数retry。24源码/1474基线、870JSON/710原包/9配置、23原UE副本与三轮六文件恢复审计通过，见[骨控制初始化验证](2026-10-04-lyra-skeletal-initialize.md)。**仅关闭固定ALS81八节点定义明确的初始化字段、实际阶段接线及骨绑定；完整Foot/Leg私有存储/全局counter、阶段与Update/Evaluate缓存统一、后续整图重初始化、RequiredBones/LOD及Rig Construction仍开放。**非零Aiming原生传播、self scalar/部分绑定/重复调用/其它Provider、字段34/47及32/47、物理314/1680差异与全部暂缓项保持，完整目标active。构建/骨架投影精度/隐藏参数与完整启动夹具失败证据保留；实际参数传播与阈值未改。最终runtime v6、native v2、probe package-v4。无新GPU/全量/十分钟/性能验收、原资产保存重导或提交推送。

最新Lyra BlendSpace初始化：沿用ALS68/raw69/logical81与十四入口，Provider实际79/74和Main实际22/16/12阶段回调已接真实宿主。原Main+三Provider两UE最终进程逐字同，15源/30非零种子，原Initialize/CacheBones确认时间/样本/滤波重置、Marker索引/full-weight重置、Delta/距离/权重/三角缓存及ActualAlpha/LOD历史保持；无标记源保留独立基础Marker。Debug/实际Optimize0错误警告、Core163、最终78个Godot进程通过，五类瞄准/倾斜连续参考保持；初始/解绑计数与前批同，四布局每构建4320帧retry及普通十角色/Emote完整报告保持。20源码/1463基线、870JSON/710原包/9配置、23原UE副本和三轮六文件恢复审计通过，见[BlendSpace初始化验证](2026-10-04-lyra-blendspace-initialize.md)。**仅关闭指定固定配置的可表示BlendSpace初始化字段与实际阶段接线；private滤波/全部Source私有、非零Aiming参数原生传播、骨控制初始化、阶段/cache统一、后续整图重初始化、RequiredBones/LOD与Rig Construction仍开放。**self scalar/部分绑定/重复调用/其它Provider、字段34/47、物理314/1680差异、近景和全部暂缓项保持，完整目标active。首次二维滤波断言与Marker参数接线失败证据保留；最终runtime v3、原生v2。无新GPU/全量/十分钟/性能、原资产保存重导、提交推送。

最新Lyra Sequence源初始化：继续ALS68/raw69/logical81及原十四入口，真实Provider阶段已进入Sequence宿主。三Provider各26源，原UE两次独立捕获78节点/156种子记录与CacheBones前后，三JSON逐字同、退出0；Evaluator保留内部/explicit、Player重设原起点，Marker距离/Delta/已有权重保留，回调留首次Update。HipFire隐藏reset/取消重试和原null LeftHand源待标记消费已接角色候选。Debug/实际Optimize0错误警告、Core134，最终66个Godot进程通过；初始/解绑计数与前批同，四布局每构建4320帧retry、普通十角色/Emote完整报告保持。33源码、870JSON/710原包/9配置、16原UE副本及三轮六文件恢复审计通过，见[Sequence源初始化验证](2026-10-03-lyra-sequence-initialize.md)。**只关闭指定Sequence时钟/Marker/Delta/资产启动字段及待标记接入；private/full-weight、外置Cycle/Hip权重逐项比较、BlendSpace与骨控制初始化、阶段/cache统一、后续完整重初始化、RequiredBones/LOD和Rig Construction继续开放。**self scalar/部分绑定/重复调用/其它Provider、原字段34/47、物理314/1680差异、近景及全部暂缓项保持，整个目标active。无新GPU/全量/十分钟/性能、原资源保存重导、提交推送。下方源初始化仅首次Prepare待消费的表述属于此前范围。

最新 Lyra Provider启动与持久阶段历史：继续使用ALS68蒙皮/raw69/logical81及原十四入口合同。真实实例按绑定函数执行启动阶段，未访问Pivot在Link后进入0状态；Main控制器跨绑定保留历史，Provider独立保存原BasePose与状态骨缓存counter/global frame，同类保留、新类/重连新建。原Main+三Provider受控阶段两UE进程0退出、request/native/closure字节同；每类Initialize56观察项、十个缓存上下文56/34访问，涵盖重复/回退/同counter异帧/有符号回绕，机器状态及阶段末26源时钟读取，四个非反射StateResult探针限制明确。Debug/实际Optimize0错误警告，Core131及最终48个Godot进程通过；初始/解绑计数与前批同，四布局完整Main每构建4320帧retry、普通十角色/Emote完整报告保持。36源码、870JSON/710原包/9配置、12原UE副本和三轮六文件恢复审计过，见 [Provider阶段验证](2026-10-03-lyra-provider-graph-phases.md)。**只关闭固定ALS81的真实启动接入与持久阶段计数；源初始化仍有首次Prepare待消费指令，阶段与Update/Evaluate缓存统一所有权、后续完整重初始化、自然组件绝对计数/RequiredBones/LOD、Rig Construction及新ALS默认最终native继续开放。**self scalar/部分绑定/重复函数调用、原字段34/47、物理314/1680差异、近景及全部开放/暂缓项保持，整个目标active。无新GPU/全量managed/十分钟/性能、原资源保存重导、提交推送。下方阶段控制器只构造期、Provider尚未接入的描述属于此前批次。

最新 Lyra 初始 self 与 Main 启动：沿用 ALS68 蒙皮/raw69/logical81及原十四入口合同，真实角色构造可直接零 Linked 实例，首次 Link、后续 Unlink/reLink保留 Main/装备/物理Montage与最终Rig。原 Main 编译拓扑驱动 Initialize/首次CacheBones/同counter重复CacheBones，实际Idle初始化与惯性/缓存重置；原Manny164受控阶段两UE进程0退出，三份参考逐字同，观察30/30/15项，StateResult8探针限制显式保留。Debug/实际Optimize0错误警告，Core131与最终46个Godot进程通过；每构建初始六配置14040角色提交/7020默认Rig与retry/180切换，旧六解绑计数与前批同，四布局外部完整Main4320帧及十角色/Emote完整报告保持。18源码、870 JSON/710原包/9配置、九份原UE副本与三轮六文件恢复审计过，见 [初始self与资源/Layer方案](2026-10-03-lyra-initial-self-main.md)。**仅关闭初始self真实角色路径与指定Main启动遍历；阶段控制器只跨构造期三次遍历保存缓存历史，完整Provider阶段、后续重新初始化、RequiredBones/LOD、Rig Construction、自身scalar/部分绑定及新ALS默认最终姿态native仍开放。**字段34/47、物理314/1680差异与全部其他开放/暂缓项保留，整个目标active。无新GPU/全量managed/十分钟/性能或原资产保存重导，无提交推送。下方初始self尚未接入的描述为前批范围。

最新 Layer 初始化/缓存调用阶段：按原 UE 节点新增 Initialize/CacheBones 与独立 SubGraph 入口，真实调用点路由保持 Root 在前、全部 Pose 输入在后；LinkedInputPose 在此阶段只同步计数，不递归输入。原 Main 初始self/实际Link/Unlink/reLink各十四点加十二unbound组合共68原生用例，两独立UE进程退出0，request/native/closure逐字同，各256原加载/GameplayTag Warning保留、无Error/Fatal/Ensure。Core新72+原59共131通过，Debug/实际Optimize0错误警告、最终六Godot进程通过，每构建3360阶段调用/360输入访问及既有默认Main1260帧retry、普通Unlink1080角色提交保持。13源码、870 JSON/710原包/9配置、七份原UE逐字副本和六文件程序集恢复审计通过。见 [初始化调用阶段验证](2026-10-03-lyra-layer-initialize-cache.md)。**仅关闭调用阶段API与受控原生对照；普通Main完整初始化/CacheBones调度、状态机/缓存/惯性/RequiredBones与Proxy counter、初始self、ALS81默认最终姿态新连续native仍开放。**下一步接完整Main的阶段遍历和实际输入子图；scalar/部分绑定、字段34/47、物理314/1680差异及全部其它开放/暂缓项保留，整个目标active。没有引擎/原项目源码配置或原资产保存/重导，没有新ALS默认native/GPU/全量managed/十分钟/性能验收。

最新普通角色 Unlink/reLink：实际角色支持完整 Provider→零 Linked 实例/self→同类重连，保留 Main owner、装备身份/同类武器对象、物理 Montage bank 与最终 ALS Rig；空源经共同 Sync 双缓冲提交，停止上游图遍历，通知与曲线反馈仍随角色统一取消/提交。前任 BlendOut 在发布前按实际 Main/Provider 编译元数据解析。Debug/实际 ExportRelease Optimize 均0错误0警告，Core59及最终34个 Godot 进程通过；每构建六种解绑配置14040角色提交/4680默认帧/7020 retry/744实际下落默认帧，所有默认帧完成 Rig，两构建计数精确一致。四布局外部 Main 原生最终 Rig 每构建4320帧/同数retry，普通十角色/Emote完整报告同前批；870 JSON/710原包/9配置、运行源码和三轮六文件恢复审计通过。见 [普通角色解绑验证](2026-10-03-lyra-character-unlink.md)。**仅关闭完整 Provider 与全self 的普通角色绑定分支；完整 Initialize/CacheBones、初始self、ALS81默认最终姿态新连续 native、scalar参数与部分绑定仍开放。**原字段34/47、物理314/1680差异、近景及全部其它开放/暂缓项保持，整个目标active。空中夹具为明确抬高2米后的实际下落，不作自然Jump等价证据；本批无UE启动/重导、新默认native、GPU观感、全量managed、十分钟或性能验收。以下“普通Unlink尚未接入”的描述为前批状态。

最新默认 Main worker 与零源事务：原完整根三频初始self/Link/Unlink/reLink/同类Link及全self六轨迹2520帧，两独立UE request/native/closure字节同；2100 self帧保持Main worker更新、停止上游Update/Evaluate并冻结状态机/Lean。新增共享Main owner未遍历提交与零Linked帧宿主；三频1260帧/同数retry、35字段88200比较及ALS81 pre-Rig通过，源/图历史保持。原最终Rig在1805 self求值均改变参考姿态，普通接入须继续执行Rig；Initialize/CacheBones仍遍历全部输入，不能由零Update推导跳过初始化。Debug/实际Optimize0错误警告，Core59/最终Godot22进程及四布局完整外部Main每构建4320帧通过，普通十角色/Emote报告同前批；28源码、原870 JSON/710包/9配置和三轮六文件恢复审计过。见 [默认 Main 连续验证](2026-10-03-lyra-default-main-worker.md)。**仅关闭受控零Linked worker/pre-Rig帧宿主；普通角色实际Unlink、默认图完整Initialize/CacheBones、空Sync双缓冲/缓存/反馈交接及Godot默认最终Rig尚未接入。**下一步真实绑定分支与普通Unlink/reLink→ALS81完整默认整链；原34/47字段、物理314/1680差异、近景及全部开放/暂缓项保持，整个目标active。本批无默认Montage/GPU/完整物理/全量managed/十分钟/性能验收。

最新默认 Layer 资源与调用点路由：原 Main 十四个空 Root 已只读导入正式资源；普通生产 External 查找改为实际调用点、实例/组/Main owner/epoch 校验，受控 Self/Unbound 执行覆盖 ALS81骨、普通/加法 Pose 及曲线/属性/root 保留，无虚假 Linked 实例。两独立 UE closure 字节同，新增1份JSON，原869 JSON/710原包/9配置保持。Debug/实际Optimize两构建0错误警告，Core59及最终Godot20进程通过，每构建完整Main4320帧/逐帧retry和普通十角色/Emote报告保持，14源码与三轮六文件恢复审计过。见 [默认 Layer 路由验证](2026-10-03-lyra-default-layer-routing.md)。**仅关闭默认资源、生产External调用点登记和受控默认根执行；普通完整Main仍要求全部External，实际Unlink/部分绑定、self scalar参数传播及按选中根停止上游source/cache未完成。**下一步零Linked候选/真实Main遍历→普通Unlink/reLink与新原生整链；字段34/47、完整物理314/1680差异、近景及全部开放/暂缓项保留，整个目标active。本批无GPU/完整物理/全量managed/十分钟/性能重验。下方默认闭包尚未导入/生产查找未接的描述属于前批范围。

最新默认 Layer 根：原 Main 十四函数虽 bImplemented=false，仍各有编译根；真实闭包均为 Result 未连接的单 Root。初始 self 与实际 Unarmed Link→Unlink 后 self 各十四次原生求值一致，三带 Pose 参数也不遍历输入。另十二无效目标组合验证仅第一个输入遍历、无输入只重置 Pose 保留 Curve/Attribute、加法单位姿态。新增 managed 调用路由基础；两个独立 UE 进程 40 场景/164 骨及 request/native/closure 各字节相同，新 41+旧18共59 Core 全过，869 JSON/710 原包/9 配置与三份原 UE 源逐字保护通过。见 [默认根验证](2026-10-03-lyra-layer-default-roots.md)。**仅关闭受控默认根/无效目标原生参考与 managed 路由基础，尚未接普通 Godot self/default/Unlink；本批未跑 Godot/完整 Main/新 ALS 姿态/GPU或物理重验，整个目标 active。**下一步导入 Main 默认闭包、按调用点执行 external/self/unbound、停未遍历源并统一 Sync，再原生整链及普通 Unlink/重新 Link/部分绑定重试。全部既有开放项与暂缓项保留。

# ALS 人物复用与 UE Animation Interface / Layer 的 Godot 对应

最新长待机/双向Turn与左手设置继续确认当前资源和实例路线：原 ALS 68 skin/69 raw/81 logical保持，角色新增实际 Linked 布尔设置入口，原生变化参考与Debug/实际Optimize最终30进程、41040参考帧及普通三频十角色通过；明确私有字段检查范围33/47，十二图字段均有实际变化证据。见 [长待机与设置验证](2026-10-03-lyra-linked-idle-turn.md)。非空左手Sequence、余下十四配置/引擎字段、同函数多调用点/default/self/Unlink等通用图及完整物理仍开放。

2026-10-03，复核当前主目录、实际导出布局和本机 UE 5.8 源码。按用户要求不展开 5.8/5.9 的差异。本页汇总当前方案；详细原生验收见各验证记录。

## 人物与资源

可以继续使用现有 ALS Mannequin 的网格、材质、蒙皮权重和 68 根物理骨。当前 `LyraAlsCharacterBinding` 实际加载原 ALS Mannequin，按骨名及父骨校验后，从完整逻辑姿态映射到 68 根骨并统一发布。资源位于本地 ignored 目录，代码检出不能代替资源准备。

| 层级 | 通道数 | 当前职责 |
| --- | ---: | --- |
| 蒙皮布局 | 68 | 原 ALS 模型的真实蒙皮骨 |
| raw 布局 | 69 | 蒙皮骨加右手下的 `weapon_r` 控制通道 |
| logical 布局 | 81 | raw 加 11 个原 ALS 虚拟骨及武器空间左手虚拟骨 |

Lyra Manny 的动画在 UE 中离线 IK Retarget 到 ALS；Godot 采样目标骨架的数据。Manny 多出的脊柱骨、武器挂点、手部 IK 和脚部控制链需按目标骨名映射。不能按原骨索引直接复制 Manny 的遮罩和 Rig 配置。

重定向动作不会自动改变 ControlRig 的参考姿态和腿长。当前已有显式 ALS reference profile，Construction 按目标参考重算控制 offset 与约 42.57/40.20 cm 的腿长。应继续保留原模型蒙皮，额外控制通道参与动画求值；虚拟骨在源采样阶段生成后参与插值。

资源导出同时携带骨骼轨迹、曲线及 presence/flags、Distance、Sync Marker、Notify/NotifyState、additive 基底、typed attributes、RootMotion、Montage 轨道和 BlendProfile。FBX 单独不足以执行 Lyra 的完整移动逻辑。源/目标包、骨架布局与依赖 JSON 的哈希需要校验，保留现有字节级依赖。

## Interface 与 Layer

普通 Blueprint Interface 的角色状态查询、目标请求和事件回调，映射为 C# typed 角色服务与消费者。Animation Layer Interface 则定义带 Pose 输入/输出的动画函数，调用必须保留姿态、曲线、属性和根运动的完整数据。

当前原编译合同有 14 个入口，默认全部归入 `ItemAnimLayers`。其中 Aiming、SkeletalControls、LeftHandOverride 接收输入 Pose；另外 11 个入口包括十个移动状态入口及 FullBodyAdditives。默认布局在同一角色内共享一个 Linked 实例；当前执行器也已支持命名多组和无组逐调用点的 1/3/4/14 实例布局。每个实际实例持有自己的状态机、播放器、缓存和回调历史。

| UE 元素 | 当前 Godot 实现 | 负责的语义 |
| --- | --- | --- |
| Animation Layer Interface | `LyraLinkedLayerContracts` | 原编译签名、参数类型、输入 Pose、组和混合配置 |
| Main AnimInstance | `LyraMainPoseHost` | 宏状态、原调用顺序、共同 Sync、Montage、惯性和最终 Rig |
| Linked AnimInstance | `LyraItemLayerGraphInstance` | 按命名组或调用点分配的可变状态与子图 |
| Linked 预更新 / worker 更新 | `LyraLinkedPreUpdateHost` / `LyraLinkedWorkerHost` | 全实例游戏线程缓存与首次实际根访问状态各自的历史 |
| Linked Layer 调用节点 | `LyraLayerInvocation` | 原 Main 节点、入口、实例与 epoch 身份 |
| BlendMask / additive | 已有精确姿态算子 | 目标骨权重、局部/mesh 空间及 additive 基底 |
| 最终模型输出 | `LyraAlsCharacterBinding` | logical81 到 skin68 的单次发布 |

执行顺序为：采集真实移动观察及提交本帧动作请求 → Linked 游戏线程预更新读取 Main 更新前的历史和 Montage 推进前快照 → Main 当前更新 → 按实际根访问执行各实例 worker、图回调并登记源 → 角色共同 Sync → 按原依赖顺序 Evaluate → 预校验全部候选 → Commit/发布与事件消费。失败时统一 Cancel，动画重试复用已经完成的物理移动凭据。预更新覆盖全部实例；worker 只在该实例本帧首次实际根访问执行，隐藏实例的这两类历史不能合并。

Layer Group 管实例共享，Sync Group 管源选主及时间同步，BlendMask 管骨骼权重。这三类配置各自保留。换装时同类重绑保留实例，换类在帧边界替换组实例并增加 epoch，Main/Montage 保留；旧候选与反馈拒绝进入新实例。

AnimationTree 可用于编辑器预览和可视化调试；当前精确执行沿用 C# 宿主和算子，避免另一个 AnimationMixer 同时写骨架。通用编辑器应围绕同一份运行合同构建。

## 大量接口和 Layer 的扩展

通用合同模型、生成器和当前 Main 的生成参数包装已接入。普通实例归属策略为 `AlsLinkedLayerBindings`，通用合同生成后负责按原元数据构造绑定输入。当前 `LyraLinkedLayerGraphSet` 已进一步按真实绑定身份分配私有图宿主，路由十四入口，并将实际来源统一收集到角色 Sync；命名组共享实例、None 逐调用点独立。最新接入与验收边界见 [多实例运行实现](2026-10-03-lyra-multi-owner-runtime.md)，合同生成见 [接口生成与验证](2026-10-03-lyra-layer-contract-codegen.md)。

自动化分为合同生成和图执行两部分：签名、注册表、参数校验和调用包装可以从导出数据生成；Provider 内部图仍须由节点库和执行器支持。共用基础图的装备继承可以复用图定义、只覆盖不可变资源/参数；拓扑发生变化则保留新的图定义。遇到尚未支持的节点或参数类型，加载时明确拒绝，避免静默丢失动画语义。

例如，原 `FullBody_Aiming` 合同是 `PreAimPose` 加 `double AimYaw`、`double AimPitch`；SkeletalControls 和 LeftHandOverride 各有一个 Pose 输入，其他十一入口没有输入 Pose。生成器应从这些实际字段产生签名和参数校验，保留 double 类型；运行调用再携带角色、调用点、目标实例、epoch 和当前候选帧。姿态传递复用现有完整值类型，图的 Prepare/Evaluate/Commit 仍遵守角色统一事务，不能用普通接口方法调用绕过播放器更新和取消边界。

大量入口使用生成的 Interface/Layer 身份和签名表，调用点保留 Main 节点身份，实例管理器按实际组合同建立可变历史。原手工十四入口 enum 已由生成代码替代，保留稳定编号注册表；签名 Pose/参数分支也改为生成期望合同。当前执行器支持这十四入口的 1/3/4/14 实例布局；任意函数集、任意 Provider 拓扑及全部实例 worker 字段仍需扩展，不能只增加新的函数名。

当前 Lyra Main 恰好每入口一个调用节点，因此专用宿主可以由 Hook 定位调用点。推广到任意图时，路由键应使用稳定的调用节点身份：同一个函数出现两次、且 Group 为 None 时，需要两个独立实例；函数签名身份只能确定参数与输出，不能取代调用点身份。Core 绑定模型已保留节点身份，通用执行器需沿用它，再让生成包装携带节点、实例及候选帧。这个边界尚未由当前十四入口矩阵关闭。

命名多 Group、无组逐调用点、self/default、Unlink/部分覆盖及通知标志的普通实例归属已有 10 布局、93 步真实 UE 参考。随后增加命名多组和无组的原生完整图参考，并接到上述运行实现；default/self/Unlink/部分覆盖整图、不同 Provider 拓扑及 Shotgun/Feminine 全图仍开放。资源可以共享；角色和 Linked 实例的播放器、状态及 worker 历史各自持有。

本机 UE 的 Linked 根调用先传播输入参数，再进入目标 Proxy。实例线程安全更新在该帧首次实际根访问执行一次；没有实际根访问的实例保留上一 worker 字段，游戏线程的全实例预更新不等于全实例 worker 更新。当前新增 `LyraLinkedWorkerHost`，按每实例首次访问读取当刻 Main 输入，在子图源登记前准备候选，并与同一角色统一提交或取消。Aiming/移动根/手部控制借用各自实例的权重，Additives 借用本实例下落计时；根图回调与 worker 更新各有原顺序。同组其它函数借用该候选，隐藏实例保留历史，Aiming 参数在对应根访问时传播。五个通用字段、两个参数及 LandRecoveryAlpha 图字段已有三频四布局的原生对照；全部私有字段及更完整开火/隐藏恢复范围仍开放，见 [首次访问更新记录](2026-10-03-lyra-linked-worker.md)。

随后已补齐原 `CanPlayIdleBreak` 的三个 `Batched_GameThreadPreEventGraph` 缓存：Montage 存在性取本帧 prerequisite 播放/停止请求后、advance 前的 bank 快照，HasVelocity/IsJumping 取 Main 上次提交状态。三值属于每个实际 Linked 实例，即使图隐藏也执行预更新；Idle 图消费这些缓存，原非批量的 Crouching/ADS/Firing 仍在访问时读取。新原生参考实际覆盖开火、ADS、蹲伏、移动图隐藏恢复和 Montage 重叠，详细结果及仍开放的 47 字段库存边界见 [预更新记录](2026-10-03-lyra-linked-preupdate.md)。此前八字段验收继续保留；这次仅扩展指定三个缓存和上述轨迹范围。

接着已将 Pivot/Stop 的九个移动组件缓存纳入每实例预更新，并让两个图实际消费对应实例的快照。初始化保留原 CDO 与首批读取的区别，生产传入 Shooter 的真实物理配置，诊断传入受控 ACharacter 配置；两者不能混用。详见 [移动缓存接入](2026-10-03-lyra-linked-movement-cache.md)。现有图支持之外的通用 PropertyAccess 批次调度、完整私有字段及动态绑定组件生命周期仍需扩展。

随后增加实际Idle/Turn/Pivot/Start/Cycle/左手图状态的候选只读视图和十二字段逐实例门禁。两构建最终24进程、32400参考帧逐帧retry和18403200新增字段比较通过；指定快照累计32/47字段，新十二项只有七项实际变化，剩余五项需长待机/转身/启用左手的完整图轨迹。余下十五配置/引擎字段及Linked Montage队列生命周期继续开放，详见 [图状态对照](2026-10-03-lyra-linked-graph-fields.md)。该项核验已有图状态；完整移植、物理和通用图目标继续active。

本机 `AnimInstance.cpp` 的普通分组路径为每类每命名组创建实例，无组为每个调用节点创建实例；同类复用按原节点/命名组首节点条件执行。Unlink 接受空类，先按请求类函数和当前目标选择分桶，再恢复匹配类的默认目标。普通共享实例退休会令其他节点读到空目标，不能假设部分覆盖始终保留旧类。两独立捕获字节相同，Core 已按原运行结果修正，见 [原生绑定矩阵](2026-10-03-lyra-layer-binding-native-matrix.md)。这项验收限普通实例归属；共享/持久实例子系统与任意图执行仍开放。

当前 ALS 人物、三 Provider 完整 Main、动作与运行中换层已具备连续原生对照证据，见 [完整换层](2026-10-02-lyra-whole-main-rebind.md)。最新真实 CMC 参考及开放边界见 [CharacterMovement](2026-10-03-lyra-character-movement.md)。实际 UE/Jolt 同输入移动、复杂地形、近景握持、通用 Layer、多平台数学/独立导出和性能仍开放，原暂缓项保留。

本轮已修正骨遮罩与Aiming的原ISPC混合舍入，Debug/实际Optimize的三频、三个动画边界各5040参考帧及逐帧retry全部通过，动作中换层和普通十角色回归通过，详见 [混合修正与验收](2026-10-03-lyra-ispc-mixing.md)。这是指定动画对照范围。随后普通玩家/NPC已加载原Shooter配置并共用速度/空中积分服务，425组原计算、两构建18场景及真实渲染通过，见 [场景移动接入](2026-10-03-lyra-scene-character-motor.md)；完整Chaos/Jolt运动等价及上述通用Layer边界仍开放。

最新实际同输入轨迹已完成三频核验，修正近正面撞墙的切向抑制，Debug/实际Optimize各九项场景回归通过；完整轨迹比较仍失败，地面间隙、命中后积分和落地时序继续开放，见 [物理轨迹记录](2026-10-03-lyra-character-trajectory.md)。ALS人物和现有十四入口组实现已接入，通用多Group/Unlink/default/self仍需独立实现和原生验收。

随后原地面查询、Perch与高度策略已接入普通共用移动服务，详情见 [胶囊地面记录](2026-10-03-lyra-character-floor.md)。复用ALS人物与十四入口组的决定保持；查询原精度和完整同输入运动仍有明确失败，不关闭复杂地形、通用Layer及完整移植目标。

最新普通地面路径已接原MoveAlongFloor/StepUp顺序与真实扫掠，Debug/实际Optimize共30项玩法回归通过，完整报告相同；人物和十四入口共享实例方案保持，详见 [地面扫掠与台阶](2026-10-03-lyra-character-ground-sweep.md)。原生32项仍有8项精度失败，空中剩余时间/顶点/着陆、基座/完整地形及通用接口实例语义继续开放。

随后普通玩家/NPC的空中路径也已接原PhysFalling顺序，顶点分步、有限AirControl、碰撞/着陆剩余时间与Walking分步保持同一物理凭据。原生96组通过88组，8组初始穿透失败；完整轨迹从841降至314/1680帧失败，Grounded/蹲姿全部相同，仍不能关闭完整物理等价。见 [空中移动记录](2026-10-03-lyra-character-air.md)。本批没有更换ALS人物或十四入口组实现；通用多Group/无组逐调用点、default/self/Unlink仍需按上述实例语义实现与原生对照。

最新原MTD/组合恢复接入后，原96组及新增初始墙面/双墙16组全部通过，原8组穿透差异消除。资源准备新增character_penetration_v1.json；没有重导人物或替换Layer组实现，见 [恢复验证](2026-10-03-lyra-character-penetration.md)。完整轨迹314/1680仍失败，复杂凹面/多shape/代理与通用Layer继续开放。

随后已抽取普通 Layer 绑定策略并接现有 Main 创建与换类发布，详见 [绑定策略验证](2026-10-03-lyra-linked-layer-binding-policy.md)。多组、无组、部分实现、default/self/Unlink 和通知标志决策有八项 Core 测试；原 Lyra 单组八步绑定及两构建四十项场景/原连续姿态对照通过。资源与人物保持当前布局。加载器和图宿主仍只接受原十四入口单组，新一般分组的实际 UE 生命周期/回调/混合参考、通用签名生成与图执行及共享/持久策略仍需完成；本批没有关闭完整物理或通用 Layer 目标。
