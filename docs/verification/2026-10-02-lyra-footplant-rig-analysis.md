# Lyra 最终 FootPlant Rig 与 ALS 骨架适配核查

2026-10-02。继续用户要求的 ALS 人物复用及 Animation Interface / Layer 方案，读取 GASP58 原 `CR_Mannequin_FootPlant`，运行端仍为主目录 Godot-Locomotion。安装版 UE 5.8 为本次读取环境；按用户要求不展开 5.8/5.9 小差异。

后续真实编译程序/原生轨迹及节点更新见 [Main73更新验证](2026-10-02-lyra-footplant-rig-update.md)。以下为首次结构核查历史。现已确认启用条件为 `DisableLegIK <= 0 && !UseFootPlacement`，并非 `EnableControlRig`；`ik_ball_r` 在原Rig内部存在，FootTrace定义没有出现在编译指令subject中。Godot完整Rig姿态和生产Main接入仍未关闭。

## 本次事实与验证边界

原 BlueprintLisp 对 Rig Graph / ProcessFootOffset 的记录只是 `No event nodes found`，无法提供 RigVM 执行语义。本次增加只读 Python 导出器，直接读取原模型图、引脚默认值、连线、成员变量默认描述及层级。没有保存、改写或主动重编译 UE 资产，没有修改 Godot 运行时代码。

两次独立 UE commandlet 实际退出0，导出结果内容严格相同；各20条现有环境/tag等 Warning、0 Error。日志是 `artifacts/lyra-analysis/lyra-footplant-rig-graph{-repeat}-ue.log`。导出前后666个包和786份已有JSON逐文件SHA保持，其中包含原 Rig 和 Main 包。本批新捕获 `assets/generated/lyra_als/footplant_rig_graph_v1.json` 被忽略，不能以仅代码检出替代资源交付。

原资产有13个模型/函数/折叠图，共330节点；层级包含91骨、7控制器、109曲线，共207项。22个成员变量包含左右脚偏移/法线/命中、骨盆偏移、腿长、站蹲/移动/坡度字段。Main CDO 实读 `EnableControlRig=false`、`UseFootPlacement=false`。

这些是结构和初始描述，不是 Rig 的原生执行轨迹；图包含函数库及调试节点，330不能作为每帧实际执行节点数。曲线条目的层级 transform 不表示其曲线数值。函数调用身份、编译后的VM指令/类型转换、初始化后控制器偏移、碰撞观察及运行状态仍需原生探针核对。

## ALS 可复用范围与缺口

当前模型继续使用原68根蒙皮骨，69条raw/81条logical动画布局保持。按 UE FName 不区分大小写的绑定规则，Rig显式引用的14个非空骨名中12个在logical81存在：root、Pelvis、spine_03、双侧Thigh/calf/Foot、ik_foot_root、ik_foot_l/r。

| 未直接匹配项 | 当前结构证据 | 后续处理 |
| --- | --- | --- |
| `VB ik_foot_root_foot_l` | 出现在本地 FootTrace 定义的默认引脚；原 Main 模型中的函数引用是 ProcessFootTrace / ProcessFootOffset / DrawSlope | 先确定编译后的真实可达路径，再决定是否需Rig内部目标辅助通道；不能把未访问定义计成每帧需求 |
| `ik_ball_r` | Main 模型 DrawSlope_2 的 Item 默认；该调用处于执行连线中 | 核对真实原生查找/缺失处理与返回值是否参与姿态；如需适配，显式记录ALS脚掌目标或内部辅助项，禁止静默丢弃 |

原7个Control不是蒙皮骨，不要求修改ALS网格权重。其初始偏移、父约束和膝部PoleVector仍应从目标参考姿态建立。Manny与ALS长度/比例不同，不能原样复制原层级207项参考变换来证明ALS控制正确。是否使用原Rig参考姿态与Main `bSetRefPoseFromSkeleton=false` 的实际行为也须通过目标骨架原生轨迹确认。

## 最终节点的实现方案

Main 的实际输出顺序仍为 FullBody Slot → Main75惯性 → RotateRoot → Provider FullBody_SkeletalControls → ControlRig73 → Root。Main75已验证完成；本次核查不关闭ControlRig73。

1. 读取编译后的Main73启用绑定与 `isCrouching/isMoving2D` 属性传播，保留0.2秒布尔alpha的初始化、隐藏/重入、仅更新与取消历史。
2. 分别核对Rig Construction和Forwards Solve真实可达指令；按原执行顺序实现地面球体扫掠、命中法线/偏移平滑、骨盆补偿、父约束、双腿TwoBoneIK及依赖的坡度处理。图内有debug绘制，需保留其数据依赖再决定绘制输出。
3. Rig内部控制器/临时项与logical81姿态适配分开；匹配骨按名字绑定，目标长度/轴向/参考变换版本随资源校验。输出仅覆盖匹配的目标骨，原曲线与typed属性按节点合同处理。
4. 碰撞查询由角色主线程采集并冻结到候选；UE原生探针使用真实地形查询作对照。Rig状态随Main/Provider/Montage同帧提交或取消，最后一次发布skin。
5. 按原 `AnimNode_ControlRigBase.cpp` 保留alpha求值：不相关时透传；全权重执行Rig；部分权重由Rig输出减输入得到local additive，再按alpha积累。不能直接假定为普通Transform.Lerp。
6. 先做ALS目标骨架的Rig组件原生连续对照，再接实际Main73，覆盖启停过渡、站蹲/移动、台阶/坡地、无命中、隐藏/重入、仅更新、失败取消与同帧重试。完整Main、Godot真实碰撞、普通Demo和视觉/性能仍分别验收。

Provider FootPlacement与最终FootPlant ControlRig是两个独立节点；Main默认关闭其中某项不能替代其可启用路径的实现。资源重定向只覆盖动画数据，仍需移植这些程序化算子。

证据核对工具：`tools/analyze_lyra_footplant_rig_graph.py`；报告 `artifacts/lyra-analysis/footplant-rig-graph-analysis.json`。本批未运行Rig/新Godot场景、未建立新原生姿态oracle，不能称为FootPlant移植或完整Main验收。
