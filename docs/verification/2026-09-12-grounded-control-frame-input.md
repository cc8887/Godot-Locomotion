# 全局移动方向、Yaw 输入与移动入口重置（第九十批）

本批继续原 P3/P4 和前置 P5A 的完整性修复。统一 BaseLayer 映射入口现在拥有
条件宏历史、MovementDirection、四项 Yaw 输入和移动入口重置。实际 Demo
仍走旧 Standing 入口；本批不关闭起步滑步、交错步、换髋或上身视觉问题。

## 源图与实现

ML_DoWhile(TrueFalse) 内部两个 DoOnce 初始均开放。首次在地面执行，即使
ShouldMove=false，也会触发对应 Changed 分支；空中不调用此宏，也不清除
历史。ChangedToTrue 依次清除 ElapsedDelayTime、Rotate_L、Rotate_R，然后
执行 WhileTrue。它不重置 RotateRate 或 RotationScale。

新增 AlsMovementUpdateGate 与 AlsGroundedControlInputModel。导入编译器检查
条件宏、引擎 DoOnce、重置链的节点、连接、默认值及变量归属；全局候选与
地面/空中输入、姿势一起提交，取消和同帧重试不改变已提交历史。

只读 UE 导出新增 v4_grounded_control_inputs.json：六项真实默认值及
CalculateMovementDirection、UpdateRotationValues 两张原生图。默认方向为
Forward，RotateRate=1，RotationScale=0，两个 Rotate 标记为 false，延迟为零。
严格检查方向先更新、四项 Yaw 随后更新，以及步态/旋转模式、象限缓冲条件。
复用已有 UE 原生夹具核对过的 CalculateQuadrant；没有另加换向等待常数。

两个角度不能混用：MovementDirection 使用速度相对 AimingRotation；Yaw
曲线使用速度相对 GetControlRotation。当前帧契约通过 Command.AimYaw 和
Command.ViewYaw 提供对应世界 yaw；真实 Motor 同时用这些值构造旋转快照。
本模型没有从 quaternion 反解，也不声称支持命令角度与旋转快照不一致的输入。

全局控制值在节点相关性判断前更新。来源被遮盖时，地面移动仍更新方向和
Yaw；停步和空中保留它们。Standing 映射入口消费统一值，绕过旧局部重复
计算；Grounded/Crouching 同时读取对应旋转状态。节点来源时间和局部采样
缓存仍遵循各自相关性，不能以全局值更新代替来源推进。

IdleControlOutput 当前是外部静止检查结果的明确边界。本批没有实现
CanRotate/CanTurn、RotateInPlaceCheck、TurnInPlaceCheck、DynamicTransitionCheck，
也没有接入真实 Montage 或最终 YawOffset 角色消费者。测试给这些静止输出
提供夹具，不能据此认定原地转身完整移植。

## 验证与限制

- 新增 14 项输入/宏/变异测试，加既有 Yaw 编译检查共 28 项通过；覆盖
  30/60/120 Hz、首次 false/true、空中保持、重入、重置范围和源图变异拒绝。
- Core 契约、帧交换、Standing 和方向相关 73 项通过；Godot 构建零警告、
  零错误。新状态保持 unmanaged，适用于候选帧传递。
- 映射 BaseLayer 3360 帧通过：9 次移动入口重置、126 帧隐藏时 Yaw 更新、
  2010 帧保持、3089 次 Standing 消费检查。每帧重试、12 次晚期失败和
  24 个守卫通过。日志：artifacts/grounded-control-runtime-fixed.log。
- 新静止旋转夹具最初使非旋转落地分支失去覆盖。补齐两类落地输入后，原有
  states=79 覆盖断言通过，没有降低覆盖要求；首次失败日志保留。
- 旧 BaseLayer 3360 帧通过。生产 single/parallel 各 180 帧结果仍为
  21E164D829153157，完整姿势 CF9225D4DE9B2C8B。这证明旧入口兼容，
  不证明新 BaseLayer 已在 Demo 中运行。

UE 完整 Editor 构建和插件审计通过；输入指纹保持
075B073AD82905F74259F3D9C2CF397369A679200FBD88B18DE6EEA590DB2564，
BuildId 为 0c423ffb-b3f5-4fb8-9fc9-5db49727eb0b。只读脚本最初将 UE Python
枚举直接转 int 失败，改用其 value 后 commandlet 退出零，保存资产数为零。

正式 commandlet 数据 SHA256：
E8D8248A3117E8FFFBCC3379CB78E8E418803C128C0C4831FA0B2C0C4011D86B。
两次普通 Editor 导出 SHA256 均为：
E7F5FD9ED1F4ABC9BFED927E899FFE439B6A2E66C88E811A81B1CC1160E84310。
Editor 原生文本与 commandlet 并非逐字节一致，观察到未连接调试节点被移除；
默认值和可读图一致。3360 帧测试分别编译两份原生图并比较完整控制候选，
结果一致。这是语义回归，不能写成两种启动模式导出哈希相同。

首次普通 Editor 完成导出并记录正常关闭日志，但进程退出码为 0xC0000005，
原因尚未确定。相同启动方式重试退出零，导出标记及文件复核通过；不宣称
首次异常已修复。两次日志分别为 ue-grounded-control-editor-restart.log 和
ue-grounded-control-editor-retry.log。重试仍有两条既有 AutomationTest
Condition failed，不能声称整份 UE 日志零错误。没有改 UE 插件/加载配置、
保存 UE 资产或部署 DLL，也未重复不受本批改动影响的插件打包。

## 后续完成标准

继续按原图补静止 Rotate/Turn 条件、延迟和动作输出，以及最终曲线反馈，
再将统一 BaseLayer 接入真实 Worker/Demo 的晚期提交，保留一个动画推进者。
随后闭合动态上身 Layering/Add/LS、Aim/手部、Foot Lock/pelvis/平台，使用真实
移动、多帧画面、状态权重和支撑脚轨迹验收。组件对照不能代替这一验收。

P5A 通用动作、P5B 全部 Overlay/道具、P5C Mantle/Roll/Root Motion、P6
Ragdoll/Get-up/Pose Recovery/完整 Camera、P7 十分钟性能仍属原计划且未完成。
音频暂缓。本批没有提交、回退或合并工作树。
