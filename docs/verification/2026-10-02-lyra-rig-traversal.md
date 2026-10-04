# Lyra FootPlant 原编译分支与真实遍历

2026-10-02。在Godot主目录继续完整Lyra移植，使用安装版UE5.8。完成436条原指令执行调度与Main73更新边界组合；完整约束、碰撞、骨骼求解、输出适配和普通Demo继续开放。

## 原生证据

原程序文本和操作数尚缺JumpToBranch命名分支目标、If选中输入的延迟范围及RunInstructions缓存语义。新增外部AlsLyraRigTraversalLibrary，沿用原ground-v2六轨迹一次调用、采样、组件/地面设置与既有层级读取位置；从FRigVMByteCode导出opcode、跳转、Run范围和35条BranchInfo。

访问顺序读取原ControlRig宿主已维护的FRigVMInstructionVisitInfo，每帧清除旧记录。没有新增求解前层级getter或修改原图/资产/引擎。两次独立UE进程实际退出0，program/native JSON精确相同，保护669包/808旧JSON。

两次采集均比较原ground-v2全部六轨迹的输入/输出姿态、曲线、属性、各阶段work/变量/层级与设置。只有缓存ContainerVersion地址身份沿用既有双向映射，其余值精确相同，原oracle字节保持。这不证明上批完整快照导致的47个输出差异原因已隔离。

| 实际访问 | 数量 |
| --- | ---: |
| 三频率、两模式总帧 | 2520 |
| 完整Forwards Solve | 2001 |
| 空访问帧 | 513 |
| Construction | 12 |
| 指令访问 | 683343 |
| 五弹簧访问 | 10005 |
| 八个扫掠访问 | 16008 |

每次完整求解实际访问八个扫掠节点，不能依据局部图的六个探测估计调度整图。

## Godot实现

LyraRigCompiledTraversal支持原图的Execute/Copy/Zero、前后跳转、命名分支、RunInstructions和Exit。ControlFlowBranch首次根据Condition选择True/False，回跳后选择Completed；If只触发选中输入。Run状态按原寄存器身份合并，命名分支与lazy输入分别处理，重复请求不重复执行依赖。

原Completed及两个If False分支确有first=last+1空范围，指令433也有跳至程序末端的目标。保留这些语义；校验入口、操作数数目、分支身份/范围和同一Run寄存器的唯一范围。原图无slice或函数调用，缓存按一次entry执行建立；这是固定FootPlant调度，尚非通用RigVM。

ILyraRigInstructionExecutor提供后续层级、数学、碰撞和IK的即时执行边界。调度器不消费预定访问列表或原生姿态答案。节点可变状态仍须由角色候选持有，本批没有将已有层级和弹簧组合成最终pose。

## 验证边界

Smoke实际调用LyraFootPlantRigUpdateHost，从原requests自主计算blend、initialize、visited、evaluate和alpha，决定Construction/Forwards Solve入口；访问列表仅用于比较。每帧取消候选、重新Prepare、重试后提交。

受控backend计算BoolNot/BoolAnd、VectorIsNearlyZero及相关Copy，只重放每帧单次执行的QuaternionToEuler输出和SphereTrace bHit。22011次producer访问检查无重复；其他transform/math/IK单位未求解。**访问顺序对照不代表完整VM即时数值输入或Rig姿态通过。**

Debug/ExportRelease Optimize构建均0警告0错误。实际Godot两配置各2520帧/683343访问严格同，2520取消重试，六坏合同/入口拒绝。Optimize真实替换三份DLL，结束恢复六份Debug DLL/PDB并逐文件SHA核对。旧动态组件3360帧/19217调用/684328比较全0差回归通过；最终Godot退出0，无ERROR/WARNING。

首次C++误用GetBranchInfo整数参数编译失败，修为GetBranch后重建；首次自行传入的visit对象被宿主替换，2520帧访问全空，计数门禁正确失败。改读宿主记录后通过，失败build/UE/约514MB诊断均保留。两次最终UE既有Warning数量见验证JSON，0 Error。

tools/verify_lyra_rig_traversal.py实际退出0，校验两UE退出、原生/资源/源码依赖及Debug/Optimize真实运行。结果artifacts/lyra-analysis/lyra-rig-traversal-verification.json：acceptedTraversal=true，actualSolverInputsAccepted/fullRigPoseAccepted/production=false。

## 后续完整目标

在此调度实现typed寄存器、ParentConstraint/Aim、八球体扫掠及缓存、原即时弹簧/Alpha输入、骨盆/双腿IK、PoseAdapter输出空间和部分alpha，再接Main73。OffsetTransform按原global/local脏状态选空间，不能统一改global。继续用原ground-v2 oracle和严格门槛。

ALS比例profile、生产Provider换类/多角色、统一Notify/root碰撞、普通Demo、地形/平台/握持渲染、性能及所有完整迁移目标继续开放。本批无提交/推送、普通Demo或视觉验收。
