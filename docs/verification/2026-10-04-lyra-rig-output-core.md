# Rig 输出与父级约束复用 Core

当前范围继续按ROADMAP首节：Lyra locomotion思想与运动算法、ALS人物/骨架、Pistol/Rifle；URO及完整UE内部还原后移。本批实际替换生产FootPlant输出和两膝ParentConstraint中的通用执行，资源绑定继续留在Lyra适配层。

## 实现边界

新增纯.NET `AlsRigPoseAdapter`，不固定81骨或Lyra名称。它保存防外部修改的目标映射和父索引，先计算并缓存全部global，再消费已有`AlsRigHierarchy`的local缓存；目标父骨与Rig父骨不同仍使用Rig local，未映射虚拟骨保留输入local。无相关权重直接复制，满权重使用Rig输出，部分权重通过既有ALS姿态内核混合。每宿主独立scratch，成功后才复制结果，支持input/output重叠且失败不写半份output；它不是共享多线程对象。

`AlsPrecisePoseBlender.BlendAdditiveTarget`组合现有`LocalDifference`与`AccumulateAdditive(forceBlend:true)`，去掉Lyra重复的安全scale倒数、delta四元数、identity插值及累积代码，保留原运算和归一化顺序。Root transform属性也共用该入口；当前原Rig没有曲线/整数属性writer，仍保留原完整载荷透传。

`AlsRigHierarchy.ApplySingleParentConstraint`处理一个权重1的父源、完整TRS、保持初始相对offset及Transform control子节点。当前父变换、初始子/父变换、子自身父空间、当前control offset和脏缓存均来自同一候选层级；最终写回仍使用既有Set和child propagation。它没有实现多父权重、axis filter或所有UE约束模式，Lyra JSON适配器继续拒绝超出原资源的配置。

只读检查本机UE5.8 `AnimNode_ControlRigBase.cpp`、`ControlRigHierarchyMappings.cpp`及`RigUnit_TransformConstraint.cpp`，确认原输出global→local、局部additive与单父control offset处理顺序。没有修改或启动UE，也没有重新导出资产。

## 验证

Core相关55项通过，其中新增19项：不同target/Rig父骨、unmapped虚拟骨、global/local脏缓存、部分权重、无权重、不可变mapping、重叠span、失败输出与恢复、非法输入、四元数符号/零及负scale、control offset、候选隔离/重置和父旋转/scale。包含原Hierarchy、LocalAdditive、LayeredBoneBlend、PreciseRetarget和AdditiveSlot native回归；0失败/0跳过。

Debug与ExportRelease构建均0警告/0错误。两种构建分别通过原Rig输入1260帧/1058400变换/274680曲线（P/Q/S差0）、原Hierarchy37批/35776变换（maxP3.55e-15cm、Q/S0）、Solver2520帧/6552576比较，以及完整输出2520帧/2154输出/174474骨/10279440比较（maxVector2.84e-14cm、maxRotation2.22e-16）。输出含156部分权重、153禁用和43080通道比较，每帧取消重试，原门槛不变。

Solver/Output原生门禁使用记录的UE碰撞返回，但先比较计算出来的查询参数，不使用参考姿态或数学结果驱动执行。另行运行的真实Godot物理2520帧/2484姿态/30744命中/2976未命中/9初始重叠/21后期失败通过；它验证实际Godot执行，不宣称与UE物理全轨迹逐位等价。

两种构建分别通过完整Main＋Rig7560帧/7296姿态/逐帧retry、普通ALS1700帧、十角色480帧/4800蒙皮发布。真实Jolt地形30/60/120Hz分别450/900/1800帧、每帧两角色，每构建共6300发布；台阶、斜坡、落差、跳跃、站蹲、ADS和Pistol/Rifle覆盖通过。

十角色、真实物理和各频地形完整报告在两种构建及前批相同，包含逐帧数据。最终`artifacts/lyra-analysis/rig-transfer-core-v1-audit.json`通过：8份本批源码/4652份保护基线（含870份Lyra资产JSON）保持冻结哈希，前批证据保持，22个成功Godot进程，六份Debug DLL/PDB在两轮Optimize切换后恢复。Core、构建、运行和审计均为v1，门槛未放宽，没有运行失败。

仅关闭本批输出适配、单父约束与ALS additive复用。实体键鼠验收和完整目标仍开放；本批没有新GPU、全量managed、十分钟、性能或跨平台验收，没有提交推送。此前computer-use窗口激活两次遭Windows `GetCursorPos 0x80070005`拒绝，尚无环境变化证据；本批未重试实体输入，地形使用进程内逻辑输入，不能据此宣称实体键鼠通过。
