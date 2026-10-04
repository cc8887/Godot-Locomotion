# Lyra FootPlant 内部层级与控制值存储

2026-10-02。继续完整 Lyra 移植，主目录直接实施、安装版UE5.8。上一批Construction参考资源写入均早退，本批补齐真实变化的单父层级、控制器offset、current/initial缓存和重置。完整 Forwards Solve、PoseAdapter 输入/输出桥、真实Godot地面、Main73/Demo继续开放。

## 原生来源与夹具

新增外部 AlsLyraRigHierarchyLibrary，真实实例化原 FootPlant Rig，读取初始98骨/控制元素，并通过原URigHierarchy API连续写入。每个batch多次写入后才读完整快照，中间穿插定向读取，覆盖未解析的dirty依赖。读取真实父列表、current/initial父约束权重及限幅/AnimationChannel配置，确认原七控制均无多父、权重1、限幅关闭且不是动画通道。

10条轨迹、37批次、796写入、248读取、4次ResetPoseToInitial(All)，覆盖：

- current/initial、local/global、影响/不影响子元素八组合。
- 原骨及七控制值、控制器offset；连续修改父/子及交替修改pose/offset。
- 原 .0001f早退、强制写入、global控制值转换时原强制标志丢弃路径。
- 非均匀、负缩放、单轴/全零缩放，零尺度局部补偿与重置。

两次原生写入采集实际退出0，所有内容精确相同，保护666包/798旧JSON。native SHA256为 6e28e53b61e6e45ba1a42c15955023e845ed5ecd7f160f51da4b8eedccfa0239。独立CDO/Blueprint控制设置两次实际退出0且字节不变，保护666包/800旧JSON；七控制均为原Transform类型，九项限幅全部关闭。四个成功进程各11条旧环境Warning、0 Error，未修改原环境配置。

控制设置首次Python读取失败：Transform为原枚举隐藏值7，Python不能直接pythonize。改为原属性ExportText提取精确名称，强制格式校验，并将Blueprint与生成类CDO设置完整比较；没有猜测控制类型。首次失败日志保留。

## Runtime

LyraFootPlantRigHierarchy在单个候选中保存current/initial的local/global TRS、offset TRS及各自dirty状态。源模板只来自独立ReadProgram初始资产；原生batch输出仅用于比较。FName查找不区分大小写，未知元素拒绝。

SetTransform(global control)先换算带offset的local，再执行Settings.ApplyLimits并委托local写入；原代码明确不传播force。**即使全部限幅关闭，ApplyLimits仍经FRigControlValue的FTransform_Float往返，位置/旋转/缩放先转float，再以double正规化旋转。**首轮Godot遗漏这个往返，首次RootCtrl定向读取位置差4.1506e-7cm、旋转7.5688e-10而失败；按原源码修复后精确通过，原门槛未放宽。

局部骨转换按原NormalizeRotation；单父control逆变换直接GetRelativeTransform；无父control逆变换按原分支正规化。offset更新保留local控制值，initial offset同步current；initial pose写入独立，不自行覆盖current。

传播先采集依赖local/global，再分阶段标脏；影响子元素保留local并递归，不影响子元素保留直接子global。控制器标脏同时更新offset global的脏状态。零尺度补偿保留上一local位置和scale；负缩放沿已验证AlsPrecisePose原仿射路径，不简化成正缩放公式。

Clone/CopyFrom保留所有缓存及dirty状态，独立候选求值后可丢弃/重试；CopyFrom先校验拓扑。Reset复制initial缓存与脏状态到current。该Reset为本次All API对应的98项TRS子集；曲线状态、实际PoseAdapter的部分骨重置及角色事务尚未接入。

Construction已删除原内部重复层级实现，改用本helper；原参数和节点配置不变。控制存储合同从新原生设置加载，拒绝EulerTransform等变化类型，不能把当前Transform处理当成任意ControlRig支持。

## 验证

Debug/ExportRelease Optimize构建和实际Godot运行均0错误0警告。每批取消一次候选后重试，已提交层级不变，37次retry一致；未知元素/骨offset20次拒绝。每配置35776组TRS/定向读取比较，位置门槛1e-8cm、四元数1e-10、scale1e-12，实际maxP=3.552713678800501e-15cm、Q=0、S=0。

Construction回归两配置均12次/5040组TRS精确、四参数2520帧历史精确。Optimize实际加载三份ExportRelease DLL，随后恢复六份Debug DLL/PDB并逐文件SHA校验。无Core源码修改，因此未重复上一批全Core门禁；本批也未进行Demo/渲染/性能验收。

首轮C++ SharedRef序列化编译错误、控制设置隐藏枚举错误、遗漏float控制存储的Godot失败均保留日志。最终证据由tools/verify_lyra_rig_hierarchy.py实际退出0汇总，见artifacts/lyra-analysis/lyra-rig-hierarchy-verification.json。报告明确fullRigPoseAccepted=false、poseTransferAccepted=false、production=false。

## 下一依赖

安装版ControlRigHierarchyMappings/ControlRigPoseAdapter源码确认，输入桥不能当成任意顺序SetLocalTransform：优化适配器只重置未映射骨的初始local，重链接映射骨的pose存储，并更新dependents/dirty；父骨名按CaseSensitive匹配，不匹配时需层级空间转换。旧非适配器路径则ResetPoseToInitial(Bone)后按映射导入。必须核对本次真实节点的实际适配器模式与目标81映射。

下一步新增原生OnPreForwardsSolve边界捕获，验证ALS动画导入后的98项层级及曲线存在性，再实现输入桥和五处独立弹簧/AlphaInterp、骨盆/坡度/双腿求解、部分权重输出及实际Main73。生产Provider换类/多角色、Notify/root物理、普通Demo、完整观感和性能仍为整个目标的验收项。
