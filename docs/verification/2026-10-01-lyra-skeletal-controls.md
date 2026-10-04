# Lyra 完整 SkeletalControls 与第14入口共同事务

2026-10-01，在当前主目录完成原12节点 SkeletalControls 层，以及固定 Unarmed/Pistol/Rifle Provider 的第14个 `ItemAnimLayers` 可执行入口。本批关闭独立原层和固定类共同调度/事务范围；完整 Main 上身、Slot、Additives 最终应用、RootYaw 原位置、换 Provider、普通 Demo、Godot 场景碰撞和视觉/性能仍开放。

## 同一组件姿态的原层

本机安装版 UE5.8.1 三类实际编译图，原顺序为：

`LinkedInput112 → LocalToCS111 → HandRetarget103 → CopyBone102 → Root104 → RightIK110 → LeftIK109 → Foot105 → LegIK107 → Weapon106 → CSToLocal108 → Root113`。

新增 `AlsLyraSkeletalControls` 只创建一份 FCSPose，在全部控制节点结束后才导出局部姿态。TwoBoneIK、CopyBone、Root/Weapon、FootPlacement、LegIK 增加共享组件姿态入口，独立调用方式继续复用同一数学实现。HandRetarget 按原 HandFKWeight 分支读骨顺序保留延迟缓存；不能提前计算所有组件骨骼，再将独立算子输出串联冒充原图。

实际 HandFKWeight 从原 CDO 读取，三配置为1；原图 settings 的0.5由原编译 handler在更新时替换，原生采集逐帧核对真实字段。其他参数及骨序绑定原不可变图和 policy，原0.2秒 Root/Foot bool 混合、上一 Main 反馈、各节点 alpha与 Foot 时间/counter规则继续复用已验证更新宿主。

`LyraSkeletalControlsHost` 将更新状态、Foot全部空间/弹簧历史、双腿 bend历史、81 logical骨骼、曲线、typed attributes和RootMotion绑定同一候选；节点不独立提交。完整求值成功才完成 Foot 更新边界。异常候选拒绝提交，取消不发布状态；隐藏、仅更新、重复Evaluate、旧view与异owner候选拒绝均覆盖。

两腿骨盆范围交叉时，Clamp保持UE原分支顺序，避免 .NET Clamp因上下界反转抛异常。仍只实现原 Manual/None曲线/Unlocked等实际可达配置，不扩大为任意 FootPlacement 设置支持。

## 新原生整层采集

新外置 `AlsLyraSkeletalControlsLibrary` 在临时 GamePreview世界创建真实物理 Box/Character/Main/Linked Provider，使用原编译 Root链真实 `Initialize/Cache/Update/Evaluate`，输入由六条原 ALS81 扩展 Sequence独立采样。不是手工调用八个原算子后组合输出。

复用上一批明确初始化的独立 Foot存储前提：第一次Initialize后将两条运行数据赋为原 `FLegRuntimeData()`，再Cache并执行真实原图。**原未初始化v1的126帧复采差异仍是失败证据；本批也不证明未经处理的原Main首次内存语义。**没有修改 UE源码、原探针或任何旧fixture。

Character floor、movement mode、velocity和component为受控观察；Box进行真实坡面球体扫掠。Foot实际求值后，从其真实 unaligned world位置再扫掠同一物理地形，记录查询起点/命中/法线供Godot外部ground接口使用。没有将任何预期骨骼或历史作为Godot求解器输入。

Godot从原资源自主生成输入pose、curve、integer attributes及RootMotion；严格比较完整原层的所有输出通道与before/updated/after历史。查询起点、方向、范围、半径及complex标志同时检查。

三Provider × 30/60/120Hz × 6秒结果：

| 项目 | 结果 |
| --- | --- |
| 更新及每帧取消重试 | 3780帧 |
| 完整姿态/骨骼 | 3051 / 247131 |
| 曲线包/整数属性/RootMotion | 3051 / 12204 / 3051 |
| 隐藏/仅更新/Initialize | 348 / 381 / 39 |
| 原Root/Foot部分alpha | 2250 / 1983帧 |
| Foot实际求值/时间累积 | 1626 / 366帧 |
| Godot原始查询/故障恢复 | 3252 / 60 |
| 无效操作拒绝 | 18672 |
| 完整层最大位置/quaternion/scale误差 | 2.366024625360097e-13 cm / 8.202438911403278e-15 / 0 |

原位置1e-8cm、quaternion1e-10、scale1e-12门槛保持，float更新历史逐位比较，其他空间历史继续严格检查。两次独立UE采集实际退出0，各0错误、794条原资产/临时依赖警告，完整新fixture语义一致。

508个源/目标资产包及679份之前JSON逐文件字节哈希保持，含原Foot v1和seeded v2。新probe及复用helper源码在workspace/source/package三处哈希一致。ignored新文件：`skeletal_controls_v1_requests.json` 2811828字节、policy 45560字节、native 138480783字节。验证入口 `tools/verify_lyra_skeletal_controls.py`。

## 同一 ItemAnimLayers 实例的第14入口

`LyraItemLayerGraphInstance` 新增完整Skeletal准备、Main帧绑定、显式输入pose求值、统一预验证/提交/取消。与10个locomotion root、LeftHand、Additives和Aiming为同一固定Provider实例，没有新增播放器、时钟或第二次Sync。更新反馈从上一提交的Main曲线复制，补足Retarget/Leg/Weapon控制曲线登记。

`LyraMainLocomotionHost` 的明确Skeletal选项在Main更新后准备该入口，以同一候选帧counter更新，绑定最终共同source候选；不会在普通LocomotionSM单根接口中隐式应用下游控制。Main帧统一预验证后才发布全部入口历史。当前合同/epoch仍为固定Provider范围，真实换类不在本批关闭项内。

新 `LyraMainSkeletalScopeSmoke` 从现有Main轨迹自主运行LeftHand/Aiming/Skeletal三个输入入口，同时运行Additives源。测试的PreAim/PreSkeletal边界受控：没有原上身/Slot、Additives最终应用和RootYaw完整接线，因此**不是完整Main输出，也没有新Main+全部14入口联合原生oracle**。外部ground使用解析平面受控查询，不是Godot场景物理验收；独立层native对照的ground才来自真实UE物理。

三Provider/三Hz共同事务结果：

| 项目 | 结果 |
| --- | --- |
| Main帧/姿态 | 11340 / 9762 |
| AO共同tick/sample | 13783 / 25864 |
| 双AO分支/真实前帧曲线驱动 | 2608 / 2780 |
| Foot有效/部分alpha更新 | 5115 / 3855 |
| 前帧手/足/武器反馈 | 11304帧 |
| 全局隐藏/晚期取消重试 | 165 / 207 |
| 查询故障后的整角色恢复 | 84 |
| 无效节点/epoch/group/candidate/signature等拒绝 | 2604 |

clean/retry逐帧全通道一致，取消保留Main、所有子层、源/Sync、曲线和控制历史；foreign角色状态不变。故障发生后，即使来源和Aiming已成功求值，也不能提交Main。共同source roster/最大cache context/一次Sync保持。

## 收尾与开放项

最终Debug/Optimize ExportRelease均0错误0警告。Core24/Import16，原FootPlacement3780帧、LegIK3780帧、双手48例、原Update3780帧、ALS Main11340帧及共同Aiming11340帧回归通过。初scope夹具局部变量重名编译失败、从项目目录构建触发global.json固定SDK不可用均修正调用/代码，日志保留；最终从 `..` 构建。没有UE编译或运行失败，未修改原门槛。

只关闭固定Provider的14个可执行接口入口及其共同事务；普通Lyra Demo仍为此前链路，普通ALS Demo保持其既有功能。后续必须完成原Main上下身/Slot/惯性/缓存、Aiming和Additives真实调用位置、RootYaw→Skeletal最终顺序、实际最终曲线反馈和统一通知，再做整链UE对照、Godot物理/平台与换类、生产Demo及画面/性能验收。原Foot首次未初始化边界也继续开放。

无提交或推送，没有资产保存。用户已有修改保留。
