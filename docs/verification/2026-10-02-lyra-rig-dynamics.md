# Lyra FootPlant 五弹簧与两个 AlphaInterp

2026-10-02。直接在Godot主目录推进完整Lyra移植，使用安装版UE5.8。本批完成原FootPlant编译指令242/299/311/348/371的五个弹簧，以及317/326的两个AlphaInterp组件；尚未执行完整RigVM或接普通Demo。

## 原配置和证据

从已固定的footplant_rig_inputs_v1_program.json读取原七条指令的literal操作数，绑定如下：

| owner | 原指令 | 原节点 | 配置 |
| --- | --- | --- | --- |
| 0/1 | 242/299 | 左/右足命中法线VectorV2 | Strength8、Damping1、TargetVelocityAmount0 |
| 2 | 311 | 骨盆FloatV2 | Strength2.5、Damping1、TargetVelocityAmount0.2f |
| 3/4 | 348/371 | 左/右足offset FloatV2 | 同骨盆配置，各自私有历史 |
| 5/6 | 317/326 | 两个AlphaInterp | Scale1/Bias0、Map/Clamp关闭、Interp开启、增减速度5 |

五弹簧均UseCurrent=true、InitializeFromTarget=false、Force0。运行时逐条校验原操作数合同；不能用旧ALS脚部的初始化语义或旧SpringInterp固定子步替代这些V2节点。本批Delta不截断到0.1秒，按原V2直接使用传入delta。

新增外部AlsLyraRigDynamicsLibrary，调用安装版真实FRigUnit_SpringInterpVectorV2、FRigUnit_SpringInterpV2及FRigVMFunction_AlphaInterp::Execute。六条三Hz轨迹的输入数值来自原ground-v2最终work寄存器观察，再加三条保持原配置的遍历压力轨迹。新独立节点从默认状态连续执行，预期输出与历史由这次真实Execute生成，Godot不使用ground私有状态作为答案或下一帧历史。

本次是观察参数重放和受控调用顺序，**不是原VM真实可达性/遍历顺序验收**。ground观察的最终寄存器也不应自动等同于每条指令执行当刻的输入；完整VM必须自行计算即时输入。各owner在新轨迹中独立持有history，隐藏不调用、反序、局部省略和重复调用均由明确requests驱动。

两次独立UE进程实际退出0，全部JSON精确相同，保护669包/806旧JSON，无原UE资产/配置/引擎修改。每进程11条旧环境Warning、0 Error。native SHA256为2d34b770fd862f82df30ec406fe08cc7782e27e02235572df32a70b7cce1b760。

## 运行时

Core新增AlsRigVectorSpringModel，FVector值和速度保留double，SpringDamper的频率、stiffness、delta及InvExp系数保持原float。当前原profile为critical damping/Force0/current input，明确只实现该路径。

LyraFootPlantRigDynamics保存两套Vector spring state、三套Float spring state及两套Alpha状态。FloatV2沿用已验证AlsKismetFloatSpring，以原Strength计算stiffness；没有修改旧AlsRigSpringModel/ALS惯性公式。AlphaInterp保留首次初始化、实际FInterpTo和InterpolatedResult历史。

源码和实际输出一致确认：**VectorV2更新SpringState.Velocity，却不赋值独立Velocity输出引脚**。原输出寄存器默认0，本helper用VectorOutputVelocity单独保留该输出；不能将Vector(owner).Velocity作为该引脚的值。FloatV2则将SpringState.Velocity复制到输出。

Clone/CopyFrom复制全部七节点历史，候选失败不修改已提交实例。Reset独立重建默认状态；无调用帧保留历史，重复调用按实际次数推进。原所有V2都使用外部Current，SimulatedResult只作为原节点输出/私有缓存保留，不能擅自改成自动使用上一Result的播放逻辑。

## 验证

九条轨迹共3360帧/19217次调用，18重置、557空调用帧、254零delta、10极小delta、14大delta。实际5492次vector速度非零、8235次scalar速度非零、2196次alpha结果非零，覆盖真实变化。

Debug和ExportRelease Optimize构建均0警告0错误，实际Godot两配置各比较684328项输出/私有历史：float逐位相同，vector原1e-10门槛下实际最大差0。每帧先取消候选再重试，共3360次一致；27非法调用拒绝且状态不变。

输入桥Debug/Optimize回归各1260帧/1077输入、1058400组TRS全0差；Optimize亦复跑原层级37批/35776组TRS和Construction12次/5040组TRS，通过。实际替换三份ExportRelease DLL，结束恢复六份Debug DLL/PDB并逐文件SHA校验。所有最终Godot运行退出0、无ERROR/WARNING。Core原FootEnvironment/JointSpring相关8项通过，0失败0跳过。

首次外部C++构建因auto MakeShared返回TSharedRef后重复ToSharedRef而失败，修正序列化参数后完整重建成功；失败build/UBA/UAT日志保留。本批无原生算法差异失败，无放宽门槛。验收由tools/verify_lyra_rig_dynamics.py实际退出0生成，见artifacts/lyra-analysis/lyra-rig-dynamics-verification.json，fullVmTraversalAccepted/fullRigPoseAccepted/production均false。

## 曲线检查与下一步

本批检查原编译VM的40函数：无Curve读取函数。当前234源curve bank仅含DisableLegIK、Distance、GroundDistance、RemainingTurnYaw、TurnYawWeight、blendParent1，与原Rig109项curve无交集。上批清除夹具未命中正值是当前资产/图的真实情况，不能据此人为新增corrective curve来称原移植通过。

此证据仅限定当前原FootPlant编译VM与234源库存；完整Main/Montage及其他新增源的曲线、presence/flags、原DisableLegIK节点启用绑定和输出传播仍需生产整链验收。Rig可识别curve正值的通用分支也仍未实测。本批优先完成求解实际使用的七个动态节点。

下一步按原即时指令输入、分支和访问次序接五弹簧/Alpha，完成ParentConstraint/Aim、真实地面trace、骨盆/坡度/双腿IK、PoseAdapter输出空间转换及部分alpha，再接Main73。上批求解前完整快照观察的47个最终输出差异仍未隔离，完整VM继续使用原ground-v2正接触oracle，不替换或放宽门槛。ALS比例目标profile、生产Provider切换/多角色、Notify/root物理、普通Demo/渲染/性能仍属于完整目标。
