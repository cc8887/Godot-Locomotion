# Lyra Main73 的 ALS 输入边界

2026-10-02。继续完整 Lyra 移植，直接在主目录实施，使用安装版 UE5.8。ALS 模型与原蒙皮继续复用；本批补齐最终 FootPlant Rig 的局部姿态输入组件。输出桥、完整求解、真实主图曲线正值输入和普通 Demo 仍开放。

## 原生证据

新增外部 AlsLyraFootPlantRigInputsLibrary，不改安装版引擎、原项目配置或资源。三种模式各30/60/120Hz、每条连续6秒：

- OriginalMain：原编译 Main73 的真实 Evaluate，在 OnPreForwardsSolve 委托中读取原 UpdateInput 后、RigVM 执行前的层级与曲线。
- OperatorBool：原设置/属性绑定的独立原生算子，覆盖部分alpha与禁用；不作为完整原Main启用条件验收。
- TransferOnly：原source Evaluate后调用原Main73的实际虚拟UpdateInput，保持连续Rig历史，不执行VM或UpdateOutput。保留的输出就是原source，本批Godot组件主要对照此模式。

共3780帧、3231 source/output、3078输入边界、2001真实求解前回调、1077独立输入调用；315隐藏、234仅更新、18初始化，部分alpha164、零alpha149。快照包含98骨/控制元素的current/initial local/global及control offsets，以及109项曲线的value/set。

首次整批序列化超过UE 32位TMemoryWriter的2GB限制，进程退出3。随后按整条连续轨迹分次调用和序列化，每次仍保留原完整6秒实例历史，无缩短轨迹/降低精度。失败日志rig-inputs-first-ue.log保留。两次修正采集实际退出0，每次198条旧环境Warning、0 Error；除每实例FCachedRigElement的拓扑地址token双向身份映射外，全部内容精确相同，原JSON字节保留。保护669包/801旧JSON，native SHA256：16b8e587886693abb84633c38b8f2e6de66dc51309e0cbedc42a2ef98e645d64。

descriptor来自原FControlRigPoseAdapter的实际映射函数，用同一节点设置和RequiredBones独立构造；enabled标志读取真实节点映射。没有读取私有adapter指针或假定内存布局。

## ALS映射及输入规则

真实节点使用优化PoseAdapter，nodeTransferGlobal=false、nodeResetInput=true；最终transferLocal=false。81个目标通道中69个映射到原Rig，12个虚拟骨无原Rig骨对应。原Rig的22个未映射骨按initial local重置，包括spine_04/05、neck_02、部分metacarpal/twist及ik_ball_l/r。

26个requiresSpace标记包含大小写和真实父层级差异。FName骨匹配不区分大小写，而原父骨名比较使用CaseSensitive，不能把26项全部解释为缺骨，也不能在导出后随意改骨名大小写。

原输入桥先重置未映射骨local，清local脏位并标global脏；随后直接复制source的局部pose存储。此处没有先把81骨输入统一转global。父差异的标记及输出转换要单独保留到后续输出桥。控制器构成RootCtrl独立树，本profile没有映射骨下的control dependent；新的此类依赖会被拒绝。

## Godot组件与门禁

LyraFootPlantRigHierarchy新增ImportAdapterLocalPose，直接更新缓存，不走SetTransform的早退阈值、正规化和child传播。原Set/Reset实现保持，复跑原层级和Construction门禁。

LyraFootPlantRigInputTransfer从原Rig图、目标layout和初始资产构造映射，并逐项校验native descriptor的parent/骨名/空间标志/reset顺序。初始曲线来自节点尚未输入的原生初始化快照；预期after快照仅用于比较，没有将原生after姿态或上一VM输出喂入Godot组件。

曲线先Unset全部presence但保留存储数值，再以FName匹配真实存在的source曲线；Rig只有value/set存储，source的flags不写入Rig。本批六sequence实际只有Distance曲线，而原Rig没有该曲线，**因此只验证了清除路径，未关闭Rig可识别曲线的正值写入、值保留历史或最终Main曲线输入验收**。

组件Clone/CopyFrom保留骨缓存及曲线；每帧取消一次独立候选再重试，已提交状态不变。三Hz共1260帧/1077输入、1260 retry、6非法输入拒绝；每配置比较1058400组TRS、274680曲线状态，位置/旋转/scale最大差均0。原位置1e-8cm、四元数1e-10、scale1e-12门槛未修改。

Debug/ExportRelease Optimize构建均0警告0错误，实际Godot场景均退出0、无ERROR/WARNING；两配置的原层级37批/35776组TRS与Construction12次/5040组TRS也通过。Optimize真实替换三份DLL，结束恢复六份Debug DLL/PDB并逐文件SHA校验。没有Core源码改动或Demo/渲染/性能运行。

首轮Godot夹具误用Rig的p/q/s解析source的position/rotation/scale，退出1；修正夹具字段解析后通过，失败rig-input-godot-debug.log保留。验收汇总由tools/verify_lyra_rig_input.py实际运行生成，报告artifacts/lyra-analysis/lyra-rig-input-verification.json明确限定poseInputAccepted和sourceCurveClearingAccepted，所有完整求解/输出/生产标志均false。

## 观察影响与余下依赖

新增采集与旧ground-v2的2154个输出比较：47个输出有差异，位置最大绝对分量3.684529772129963e-5cm、quaternion最大分量1.0922583075778647e-6，scale0；source输入、alpha和bool更新完全相同。新增求解前全层级读取和按轨迹拆分调用都可能影响缓存/观察边界，原因尚未隔离，不能把差异确定归因于一个getter。旧ground仅记录部分层级，新捕获完整98项，不直接比较两个整体hierarchy对象的相等性。

这批数据用于本次真实输入组件，**不替代原ground-v2完整求解oracle，也不以它放宽原生门槛**。后续完整VM和部分alpha输出须继续对照旧正接触轨迹，并隔离上述观察差异。

下一步补真实Main曲线正值及持续presence历史输入，再按原VM完成独立弹簧/AlphaInterp、骨盆/坡度/双腿求解、输出空间转换与alpha混合，接原Main73。ALS腿长适配仍需明确目标profile；生产Provider换类/多角色、统一Notify、RootMotion碰撞消费、普通Demo与视觉/性能仍属于完整目标。

后续检查确认原FootPlant编译VM无Curve读取函数，当前234源曲线与Rig109项曲线无交集，故继续优先推进真实求解依赖；五弹簧/两Alpha组件已通过新真实节点连续原生门禁，见 [动态节点验证](2026-10-02-lyra-rig-dynamics.md)。完整Main曲线/启用绑定、通用Rig curve正值分支及本页观察差异仍未关闭。
