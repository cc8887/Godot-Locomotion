# Lyra FootPlant 实际寄存器与求解节点

2026-10-02。继续在主目录推进完整 Lyra 移植。原 436 指令调度已接真实 typed 寄存器、骨层级、父约束、Aim、数学、五弹簧/两 Alpha 和双腿 IK。当前通过范围是受控 ALS81 输入姿态、记录 UE 碰撞结果边界下的实际节点计算；最终 PoseAdapter 输出、部分 alpha、真实 Godot 碰撞、Main73 生产接入和普通 Demo 继续开放。

## 实现

LyraFootPlantRigMemory 保存 Work/Literal/External 分区及原反射子路径，显式保留 FVector/FTransform/FQuat 的 double、节点 float 和外部 double 的转换边界。Transform.Translation、Rotation、Translation.X/Y、Vector.X/Y/Z 和 Aim.Target 实际读写；Literal 不可写，未知寄存器、路径和类型拒绝。候选 Clone 使用不可变值及独立字典，不共享可写姿态。

LyraFootPlantRigExecutor 在原访问顺序执行全部实际数值节点。原 Get/SetTransform、SetRotation、控制 offset、ParentConstraint、AimBoneMath、OffsetTransform、相对变换与显式长度双腿 IK 接入已验证层级；五处弹簧和两 Alpha 消费实际即时寄存器，而非原生节点答案。调试绘制节点保留遍历，不发绘制命令。命名分支仍由原调度负责。

OffsetTransform 在读取前查询原 global dirty 标记，选择当前干净的 local 或 global。ParentConstraint 按原单父 Weight1/全 filter/Average，分别处理初始偏移、父空间与控制 offset。IK 的 B 初始 rotation/scale 来自 A，仅替换 B 的位置，C 保留 effector 旋转；Aim 与 IK 的 FindBetween 使用原两种不同算法。

RequestInit 仅重建 VM 工作内存和私有动态状态，保留外部 UObject 属性；Construction 重置当前层级并实际执行原入口。中途重新初始化不能把当前脚法线/offset等外部属性还原为 CDO。

ILyraFootPlantRigCollision 显式接收 Rig 空间厘米起终点、原 TraceChannel byte2 和 float 半径。当前原固定图每次完整求解八次扫掠、每指令一次；每 entry 清查询缓存。此批测试提供记录命中，实际校验计算出的 query 后返回原 bHit/ImpactPoint/Normal，没有向数学节点提供 Euler、约束或 IK 输出。真实物理世界转换、忽略角色及复杂碰撞待生产 provider 接入。

## 原生数据与验证

新 tools/prepare_lyra_rig_solver.py 仅从固定 ground-v2 和 traversal oracle 生成独立 rig_solver_v1_native.json（100846860字节），不覆盖原 JSON。含六条轨迹的受控输入姿态、请求、访问顺序、234可见工作寄存器、22外部变量和13元素的 global/local；TArray 私有缓存和执行状态不作为本门禁的逐字段覆盖。两种模式 ×30/60/120Hz，隐藏、仅更新、delta0/.35、部分节点 alpha 及中途重初始化保持原请求。

每帧先执行完整候选，再取消 Main73 更新候选，从上一提交 executor Clone 重新执行，比较后统一保留重试结果。没有从原生 after 注入工作寄存器、外部变量或层级；输入姿态与碰撞命中是明确的受控边界。

| 每种构建的最终门禁 | 结果 |
| --- | ---: |
| 总帧 / 真实完整 Forwards Solve | 2520 / 2001 |
| 原指令访问 / Construction | 683343 / 12 |
| 每帧取消重试 / 非法操作拒绝 | 2520 / 6 |
| 扫掠 / 五弹簧访问（不计重试副本） | 16008 / 10005 |
| 比较值（含重试） | 6552576 |
| 最大位置/向量分量差 cm | 2.842170943040401e-14 |
| 已比较 quaternion 分量最大差 | 0 |

float按位、double外部标量精确、向量1e-8cm及quaternion1e-10门槛未改变。Debug 与 ExportRelease Optimize 构建均0错误0警告，实际 Godot 进程均退出0，无 ERROR/WARNING。Optimize 实际替换三份 DLL、恢复六份 Debug DLL/PDB并逐文件SHA核对；旧动态节点3360帧/19217调用/684328输出及历史比较全0差回归通过。

tools/verify_lyra_rig_solver.py 实际退出0，复查原669包/808旧JSON及固定探针依赖、实际运行日志和程序集恢复。报告为 artifacts/lyra-analysis/lyra-rig-solver-verification.json，acceptedImmediateUnits=true；finalPoseAdapterAccepted/realGodotCollisionAccepted/fullRigPoseAccepted/production 均false。

## 失败定位与保留证据

首次结构体常量 Copy 未转换 typed Aim，以及遗漏 literal Aim 类型解析，运行失败；修正后继续。Euler 初始误用 UE handedness，而原默认false，修正。FLinearColor 断言遗漏、System.Environment 名称歧义编译失败均修复，日志保留。

首次中途初始化重置外部属性，30Hz frame90脚法线失败；原 ground-v2 的 before/updated/after证明确实仅重建工作内存，已按此修复。

完整诊断随后只有 OriginalMain120Hz frame271 foot_r.local.q 超限，max7.49246592546271e-9；正常及取消重试各一次失败，未改门槛。新增外部只读 AlsLyraRigIkMathLibrary，实际将该帧两次 Godot IK 输入交给安装版 FControlRigMathLibrary::SolveBasicTwoBoneIK。两次 IK 的 A/B/C输出全部精确同，证明此处的 kernel 一致，需检查其上游。外部构建成功，UE实际退出0，811现有JSON哈希保护，无资产保存或主工程部署。

继续直接核查原 MathTransformMakeRelative/MakeAbsolute，发现都显式 NormalizeRotation。后端先前遗漏，输入只有数个double ULP差，在接近伸直的伸展边界放大。补齐两处后全部门禁通过，最大已比较Q差0。原失败日志/诊断保留：rig-solver-godot-external-history.log、rig-solver-godot-{diagnostic,retry-diagnostic}.log、rig-solver-failures-before-retry.json、rig-solver-ik-input-before-retry.json、rig-solver-ik-native-diagnostic.json及UE日志。

本批只新增外部求解诊断，无 GASP58 资产、原探针或引擎源码修改，无提交/推送。原生输入边界来自已保存UE轨迹，不能把它说成当前 Godot Main 实时输入或新完整 UE 整链采集。

## 后续完整目标

下一步实现原 PoseAdapter 输出空间及 partial alpha，核对全81骨/曲线/属性输出，接实际 Main73 候选与一次skin发布；再用真实Godot碰撞替换记录provider并验证地形/平台。ALS比例配置、生产Provider换类/多角色、统一Notify/Montage、RootMotion碰撞消费、普通Demo、渲染及性能继续按 ROADMAP 推进。当前结果不关闭整个 Lyra 移植目标。
