# Lyra Main73 FootPlant 原生采集与节点更新

2026-10-02。继续完整 Lyra 移植目标，使用主目录和安装版 UE 5.8。本批关闭范围是 **Main73 的受控更新、输入绑定与事务历史**；Godot Rig Construction、碰撞/弹簧/腿部姿态、完整 Main 和普通 Demo 尚未关闭。按用户要求不展开 5.8/5.9 小差异。

## 资源与启用条件

继续使用 ALS 原网格的 skin68、raw69 和 logical81，不修改蒙皮权重。原 Rig 自带91骨、7控制器和109曲线，其内部辅助项与目标骨架发布分开管理。编译程序含436条指令、40个原生函数，Forwards Solve入口0、Construction入口401；工作内存345个寄存器。

原模型图中的 FootTrace 定义没有出现在本次编译指令 subject 中，其 `VB ik_foot_root_foot_l` 不作为这份程序的目标骨架新增要求。`ik_ball_r` 已在原 Rig 内部层级中确认存在，用于 DrawSlope_2；它不是 ALS 蒙皮骨，也不能作为未知骨静默丢弃。未来求值要保留内部层级和目标骨映射。

**原 Main73 的绑定是 `GetCurveValue("DisableLegIK") <= 0 && !UseFootPlacement`，不是 Main 的 `EnableControlRig` 字段。** 原图 ShouldEnableControlRig 函数与独立原生暴露输入探针一致。96组覆盖正/负/零曲线、两项开关以及站蹲/移动全部组合，启用24组。函数结果先经过原 PropertyAccess 的游戏/工作线程批次缓存，再由节点 handler 读取；仅调用 handler 可能读到初始化留下的旧值。

因此先前“原 Main 权重始终1”的观察只适用于本次 Rig 连续轨迹的固定反馈0、UseFootPlacement=false条件，不能推广到所有配置。Godot resolver 消费已提交 Main 曲线和当前 FootPlacement 选择，Prepare 强制接收解析后的布尔输入。

## 原生轨迹与身份比较

新增外部探针 `AlsLyraFootPlantRigLibrary`、`AlsLyraFootPlantBindingLibrary`，只构建在 artifacts 外部插件包，不部署至 GASP58，不修改 UE 引擎或保存源资产。

实际 Rig 节点使用原 Main73 配置与 isCrouching/isMoving2D 属性映射。临时 Manny carrier 注册之后才绑定 transient ALS81 Skeleton、建立 RequiredBones；注册前绑定会自动合并 Manny 缺失骨，第一轮失败已保留。逐项确认81骨名，不能只校验数量。这个 carrier 用于组件原生计算，不等同于真实 ALS 模型的视觉验收。

六条轨迹为 OriginalMain 与独立 OperatorBool 各30/60/120Hz、每条6秒，总2520帧。OriginalMain保留原 handler和固定上述反馈条件；OperatorBool使用独立未绑定的原生节点、原反射配置和属性映射，接受显式解析后的启停输入，不能算作原 Main 启停玩法覆盖。

真实 UE Box 地面请求包含坡度、台阶和无地面；原动画源、组件位置/朝向、站蹲/移动、隐藏、重新初始化、零/大delta、仅更新均进入请求。**后续核查确认该v1夹具未阻挡自定义Traversable通道，双脚全程未命中，因此没有接触/骨盆偏移/坡度求解覆盖。**已用独立Ground v2修正并重采，见 [Construction与正接触验证](2026-10-02-lyra-footplant-rig-construction.md)；本批更新历史验证仍成立。保存输入和输出的完整81骨姿态、曲线/属性及Rig状态；并显式读取反射遗漏的spring velocity/previous target/valid和ScaleBiasClamp初始化/插值历史。五处弹簧的VM切片独立保存，不能合并为一个全局弹簧。

| 轨迹内容 | 数量 |
| --- | ---: |
| 更新帧 | 2520 |
| UE姿态输出 | 2154 |
| 部分权重输出 | 164 |
| 零权重输出 | 149 |
| 隐藏帧 / 仅更新帧 | 210 / 156 |
| 初始化 | 12 |
| UE输入/输出姿态不同 | 2001 |

第二次原生采集首次严格比较失败：509628处差异全部位于 `FCachedRigElement.ContainerVersion`。本机 RigHierarchy.h 的 GetTopologyVersionHash 将 hierarchy 地址和拓扑版本组合，RigHierarchyCache.cpp 把这个值写入缓存，因此跨进程原始整数不同。重采比较按每个独立Rig建立双向一一身份映射，并精确保留缓存无效性、Key、Index、身份变化历史及所有其他值；不改姿态/曲线/弹簧门槛，不改已固定JSON字节。首次失败日志及原始诊断保留。

复采与绑定两进程退出及最终汇总由 `tools/verify_lyra_footplant_rig_update.py` 检查；实际结果见 `artifacts/lyra-analysis/lyra-footplant-rig-update-verification.json`。Rig基线保护669包/787旧JSON；绑定补探针保护667包/792旧JSON。资源仍被Git忽略，只有代码不能作为运行交付。

最终验证工具实际退出0。Rig两进程各146条Warning、0 Error；绑定两进程各12条Warning、0 Error，日志和源包保持，本批未修这些环境/tag警告。复采身份比较记录509628处地址版本映射，其余值严格相同，原native文件SHA保持。

## Godot 更新宿主

`LyraFootPlantRigUpdateHost`加载原 Main73 bool/Linear/.2秒/属性映射合同。仅实现原节点更新：初始化只重置bool initialized标志、按访问推进alpha、传播两个输入、记录Rig delta。相关Rig执行完成时consume delta到0；alpha零透传保留delta，隐藏/仅更新保留相应历史。

候选携带owner/epoch/frame，Prepare后不发布；Commit预校验求值模式，Cancel恢复已提交状态，旧候选、异角色、异代、重复提交和非有限delta拒绝。重复完成钩子稳定。尚未接到 LyraMainPoseHost 的最终输出，不能称为原Rig姿态执行。

Debug和ExportRelease Optimize构建均0警告0错误。最终两个Godot实际进程各2520帧、逐帧取消重试，布尔混合七字段、alpha、Rig delta和输入历史逐位同原生；含96组绑定条件、32448次非法操作拒绝，无Godot ERROR/WARNING。2154次完成钩子代表对应UE帧的求值模式，**没有在Godot生成2154份Rig骨骼输出**。Optimize加载三份实际Release DLL，结束恢复六份Debug DLL/PDB并逐文件SHA验证。

先前没有绑定条件的Debug/Optimize日志、C++ FLeaf命名冲突/私有NodeData访问/绑定探针SharedRef编译失败、C# FileAccess编译失败、目标骨合并及PropertyAccess旧缓存失败均保留；未以失败进程或诊断退出0代替门禁。

## 后续依赖

接下来按原Construction和436条程序实现内部层级、目标映射、实际球体扫掠、独立SpringV2/AlphaInterp历史、骨盆/父约束/双腿IK及部分权重local additive。先对这份ALS目标原生轨迹完整逐骨对照，再接实际Main73与角色统一事务、最终skin发布。

生产Provider换类/多角色、Notify/Montage共用队列、RootMotion碰撞消费、普通Demo、坡地/平台/握持视觉和性能仍是完整目标。多Layer Group、未绑定self-layer、Unlink和持久实例等通用动画接口分支也未因本批节点更新而验收。整个目标保持进行中。
