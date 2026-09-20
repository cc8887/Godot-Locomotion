# 完整原 Rig 对照与骨骼写入语义修复（第 192 批）

## 原规划归属与交付边界

本批继续 P3/P4 的脚部整链修复。原桌面方案已经包含 Foot IK、Foot Lock、
pelvis、平台、Mantle/Roll、Ragdoll/Get-up 和完整 Camera；后续正式设计
将全部 Overlay/道具纳入 P5B。没有取消这些功能，音频仍按用户要求暂缓。

当前资产来自 ALS V4，移动/上身主要对照其原 AnimBP，脚部采用
ALS-Refactored。两版资产、虚拟骨和曲线合同存在差异，不能把组合实现
称为某一版完整 ALS 的 1:1 复制。每个移植模块需要同时核实正式数据、
引擎执行语义、帧历史、生产接线和最终角色效果。

## 新增完整 Rig 验证边界

生产采集新增同帧 PreFoot 姿势/曲线、真实候选 RigInput、逻辑骨架及
提交后的原生组件空间骨骼变换。采集仍由显式 production-graph-capture
启用；共享骨架只在采集配置时构造。阶段输出随生产候选提交，不能读取
其他帧的可变对象来拼接输入。

新的 AlsFootRigReplay.cpp 加载原 /ALS/ALS/Character/CR_Als.CR_Als_C，
绑定原 V4 Mannequin 网格参考骨架，执行实际 Construction 和连续
Forwards Solve VM。在独立 UE 物理世界中创建与 Godot 相同的有限盒体，
按每帧平台和角色变换执行原 Rig 的地形查询、pelvis、腿部 IK 和脚旋转。
不将 Godot 计算后的地形偏移、pelvis 输出作为原 Rig 的求解结果。

验证范围仍有明确限制：PreFoot、脚锁目标、pelvis allowance 和时间步长
来自 Godot 捕获；没有让原 UE Character/AnimInstance 自行生成脚锁历史。
本场景 SpineYaw 与前后 VelocityBlend 输入为零；不能外推为全部 Overlay
或完整 AnimBP 已配对。79 根逻辑骨中映射 68 根网格骨，11 根 V4 虚拟骨
在原 Rig 中没有对应项，全部列入报告，不把它们算作已通过。

## 实际生产修复

原生 FRigComputedTransform::Equals 默认使用 float 1e-4 阈值；已读到
当前全局变换后，URigHierarchy::SetTransform 对近似相等的目标提前返回。
Godot 的 pelvis/脚旋转层级写入此前仍更新父骨与子骨，导致三个脚趾
差异：第 32 帧 ball_l/ball_r、第 162 帧 ball_r。

AlsRigHierarchyWrites.SetGlobal 已补齐该判断：位移/缩放分量与四元数
分量使用原阈值，四元数同时接受 q 和 -q。这仅用于当前调用链中先读取
全局变换的 SetTranslation/SetRotation，不盲目替换 TwoBoneIK 的写入。
没有增加脚部补偿、修改混合权重或放宽对照门槛。

来源：UE ControlRig/Public/Rigs/RigHierarchyElements.h 的
FRigComputedTransform::Equals，以及 Private/Rigs/RigHierarchy.cpp 的
SetTransform clean-cache 分支。原生产修复后，三个脚趾偏差消失；旧
platform-192-fixed-comparison.json 仍保留两个验证器输入缩放失败。

## 验证器自身的修复

旧验证器每帧 ResetPoseToInitial 后通过 SetGlobalTransform 注入输入。
第 101/102 帧 ik_foot_root 的输入缩放分别为 1.0000851154/1.0000680685，
小于每分量 1e-4 的修改阈值，被跳过后错误地保持 1。Godot 输入和输出
并没有丢失这些缩放，因此不能据此给生产代码增加缩放截断。

实际 UE AnimNode 的优化 PoseAdapter 使用 CopyBonesFrom 加脏标记来
传入姿势，不将姿势复制作为图中的 SetTransform 修改指令。本探针改为
精确复制受控组件空间输入，保留 VM 内所有真实写入语义；未宣称已运行
完整 AnimNode PoseAdapter。schemaVersion 升至 2，每帧导出实际进入 VM
前的 inputComponents；C++ 自检 1e-10，再由 Node 独立从局部姿势重建
组件姿势验证输入。不能通过直接排除 ik_foot_root 或放宽缩放门槛通过。

## 完整 VM 对照结果

水平旋转与倾斜/升降各 360 帧，含 359 个有效求解帧，每帧比较全部
68 根已映射骨。两组输入失败和输出失败均为 0，缩放误差为 0。
输出门槛仍为位置 0.001 cm、旋转 0.02°、缩放欧氏差 1e-5，没有
排除问题帧或脚根骨。

| 场景 | 最大位置误差 cm | 最大旋转误差 ° |
| --- | ---: | ---: |
| 倾斜/升降 | 0.000027679435 | 0.000038675098 |
| 水平旋转 | 0.000027008972 | 0.000038712775 |

独立重建输入最大位置差 8.918e-14 cm、旋转差 0.0000038182°、
缩放差 0，均通过更严格的输入门槛。这表明在给定同样脚锁目标与
姿势时两边求解一致，不能反推脚锁来源、曲线历史或最终接触正确。

最终原生输入与修复前捕获的 PreFoot、RigInput、Skeleton、Platform
逐帧相同；旧输出文件和失败报告保留。最终 schema 2 重新独立运行，
未通过改写旧原生输出制造通过结果。

## 已完成的生产回归

- Godot Debug 优化构建通过，0 警告/0 错误；腿部/Rig/compiler 专项
  20 项通过。未宣称全量 Core 通过；旧 23 项失败和 79/68 骨缓存布局
  测试债务仍开放。
- 60 Hz、10 角色 single/parallel 各 3621 个提交帧通过，包含 2 次取消、
  1 次提交等待、2400 个非默认 Overlay 帧。两模式摘要完全一致：
  pose=EF4D11EEA2A0E97C，root=5EA89CAFCC73D6BF，
  result=4D6C8BAD3EBED85F，events=150，rays=6544。
  姿势摘要因真实写入语义修复而改变，不能声称与第 189 批姿势不变。
- 水平旋转 360 帧通过原门槛；倾斜 360 帧仍失败，74 个无约束帧对，
  最大漂移 11.734021 mm。实际同帧射线/平面检查通过，最大平面误差
  0.000161 mm、射线交点误差 0.001921 mm。
- 倾斜平台 120 个锁定帧中，右鞋底固定蒙皮点最低 -22.437513 mm，
  101 帧超过 5 mm 穿地；左侧 -6.084681 mm、30 帧超过 5 mm。
  几何采样用于证明穿地，不是完整表面或接触力验收。
- 最终渲染连续输出 12 PNG。360 帧与无窗口捕获除稳定一一映射的
  平台对象编号外全部相同，包括新增 RigCapture；抽查 240/330 帧。
  截图和 HUD 的 Errors 0 不覆盖结束时的接触失败，更不代替人工签收。

## UE 构建与重复性证据

本批使用 ue-diagnosing-plugin-build-load 技能的完整 Editor-target 构建
与跨插件审计，不以复制 DLL 或仅构建单插件作为运行依据。技能提到的
systematic-debugging / verification-before-completion 未在本机技能目录
找到，按保留首错、核对输入/产物、实际执行复验的方式继续。

最终完整构建和 ALS / AlsGodotExporter / AutoTestTools / BlueprintLisp
四插件审计通过。构建日志前缀
20260913T204703990Z-42da377f23ba4e4f93d2d51b6b10319c，
BuildId=733af6f9-4551-46a0-b567-7db34ff0009f，
state fingerprint=DEE57FB39B26F4602A8188BB4DCC48D58F765FE84C25C0E210AC6529975B6E42。
最终 AlsFootRigReplay.cpp SHA256：
7424F4D177A61C03943C672FBB6051155B63DED33BEEB3F683CB8DB444FE7FB5。

冷 commandlet 退出 0，输出两个 360 帧结果，日志 0 error/0 warning。
最终普通 Editor 通过 Start-Process -PassThru -Wait 跟踪实际进程退出 0，
两组完整 JSON 与冷启动解析后完全一致，均 assets_saved=0。普通 Editor
仍有既有两条 LogAutomationTest Condition failed，不称为干净日志。
早期 platform-192-editor-vm 只记录正常关闭，没有捕获实际 GUI 进程
退出码；不将启动它的 PowerShell 退出 0 冒充 Editor 退出 0。

最终 DataValidation 退出 0，0 error/3 warning，保留原 ALS AI 的
PawnActionsComponent 缺失及旧 NavMesh 警告。与本次 Rig 修改无关的
原资产没有保存或改写。

最终 BuildPlugin 含 ALS 依赖打包退出 0，BUILD SUCCESSFUL，耗时
2 分 14 秒。UBA 内存压力曾终止并重试一次编译，最终 UBT/UAT 均成功；
这不是运行时性能验收。命令中的 -Package=$packageOutput 被原生命令
按字面量传入，生成包暂位于 D:/UnrealEngine/$packageOutput。构建结束
后核实路径、非 reparse、源码哈希及目标不存在，使用单一 PowerShell
Move-Item 完整移至 artifacts/unreal/foot-rig-192-exact-package；未删除
包内容，也未重写原打包日志。后续命令应传完整 -Package=绝对路径字符串。

打包后的引擎与项目 receipt BuildId 再次确认一致；仓库、部署及最终
包内的 AlsFootRigReplay.cpp 均与上述 SHA256 相同。

首错没有隐藏：初期反射变量名、事件名和骨架绑定不正确；最终输入复制
改动初次误用 Get(Key) 而非 Get(index)，修正后重新完整构建。另一次
构建漏设 DOTNET_ROOT，改指引擎自带 .NET 10 后继续，没有安装运行时。
失败构建中 NetCore 更新引擎 BuildId，后续 wrapper 正确拒绝旧 receipt。
核实后仅将项目生成的 receipt/四插件 manifests/映射 DLL/PDB 移到
Saved/BuildReceiptBackup/20260913T201145796Z 与
Saved/BuildReceiptBackup/20260913T204703590Z，可恢复；未删除源码或资产。

主要产物：platform-192-native-exact.json、horizontal-native-exact.json、
对应 editor-exact.json、exact-comparison.json、horizontal-exact-comparison.json；
生产输入为 platform-192-tilt-fixed.json / horizontal-fixed.json，最终连续
截图为 artifacts/platform-192-visual-fixed/。旧 schema 1 报告保留，但
最终比较器严格要求 schema 2 输入转移证据。

## 下一步的具体修复顺序

1. 在同一连续 UE 角色链中，由上一帧原生最终骨骼/曲线生成本帧脚锁
   输入，执行 RefreshFeetOnGameThread、RefreshFeet、Rig，再反馈到
   下一帧。现有基于逐帧 Previous 注入的 BasedFootLockProbe 和本批受控
   Rig 探针，都不能单独证明这个反馈闭环完整。
2. 逐帧比较有效性/重置、平台基变换、IK 目标采样身份、最终锁曲线、
   锁量及世界/平台/组件目标，修复首个分歧。若相同接触缺陷也由原链
   产生，再核实 V4 网格/虚拟骨与 Refactored 参考和图设置的适配合同，
   不预先认定是重复修正或引擎精度。
3. 闭合地面起步/停止、A↔D 各脚相位换髋、上身及脚部整角色对照；
   做 30/60/120 Hz 与真实输入/相机回归，完成后切换默认完整入口并
   提供人工验证。当前显式开关路径不能算默认 Demo 已完成。
4. 继续 P5A 通用事件/动作消费者、P5B 全部 Overlay/道具生命周期、
   P5C Mantle/Roll/Root Motion、P6 物理恢复/完整 Camera，最后 P7
   十个全质量角色、30 秒热身与十分钟 Release 性能和人工验收。

本批保留全部旧失败产物与用户工作区修改，不提交、回滚或合并分支。
