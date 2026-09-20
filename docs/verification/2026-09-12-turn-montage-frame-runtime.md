# 动态转身 Montage 的候选运行与真实姿势接线

日期：2026-09-12，第九十三批。前一批为进展：补 TurnInPlace 决策并纠正蹲姿缩放。
本批继续完整性路线第 2 项；没有缩小原 P3–P7 目标。

## 实现与实际接入范围

新增 Core `AlsDynamicMontageRuntime`，由每个 BaseLayerFrameRuntime 独占。
它维护动态转身的独立实例身份、播放时间、同组替换和完整淡出队列；容量
不足时扩容，不以两条轨道限制或淘汰仍在淡出的实例。复用 ActionLifecycle
的 HermiteCubic 权重计算，并加入明确停止入口。

每次 PrepareFromFrame 先推进已有动作，冻结该帧的 Montage 求值数据；再
执行全局输入、静止检查和 TurnInPlace。播放请求作用于候选动作状态，新
动作从下一帧开始推进，不能在请求帧额外增加一段播放时间。重复查询读取
推进后的真实实例状态。RotationScale 按原图在尝试播放后写候选，不依赖
播放函数成功返回。

状态存储和求值数据都有独立的已提交/候选区。旧求值区在下一帧准备期间
保持可读；重用时换身份，Slot 拒绝持有旧身份的读取。全部子候选先校验，
再统一提交动作、输入和姿势；晚期求值失败后的 Discard 不推进实例 ID 或
时钟。隐藏 BaseLayer 来源时仍推进 Montage。

站姿/蹲姿 Idle Slot 使用真实导出的八种动画，按所有同 Slot 的求值贡献
混合；总权重大于 1 时归一化，非加法 Slot 的剩余权重交给来源姿势。
曲线跟随相同片段/时间/权重采样。站姿 Idle 原来没有实际消费原图要求的
RotationAmount × RotationScale，现补入；蹲姿已有的相同消费者继续使用。

此接线位于完整 BaseLayer 的映射入口，生产 Demo 仍为旧 Standing 主入口。
一般 Grounded Slot、BaseLayer Slot、动态转移和常规 ActionPlayer 还没有
与本实例队列统一，因此它是已接线的动态转身路径，不是通用 Montage 系统完成。

## 原生数据与时序依据

新增 `tools/unreal/export_turn_montage_inputs.py` 和正式配置
`assets/config/v4_turn_montage_inputs.json`，导出原生骨架文本、八动画的
RateScale/RootMotion 设置，没有保存或修改 UE 资产。

骨架 SlotGroups(1) 是 Grounded Group，包含 Grounded Slot、(N) Turn/Rotate、
(CLF) Turn/Rotate。两种转身必须同组互斥；八资产 RateScale=1、RootMotion
关闭。导入器按真实 ObjectPath、骨架、组成员、时长和非加法属性绑定，拒绝
缺失/重复 Slot、非原始倍率和 root-motion 输入。

本机 UE 源码核对：

- AnimInstance.cpp 的 UpdateAnimation：UpdateMontage、同步和
  UpdateMontageEvaluationData 在 BlueprintUpdateAnimation 之前。
- Montage_PlayInternal / StopAllMontagesByGroupName：使用新动作的 BlendIn
  设置停止同组实例；旧淡出仍保留，不能按 Slot 名单独隔离互斥。
- AnimMontage.cpp 的 Play / Stop / UpdateWeight / Advance：先权重、后时间；
  淡出时不立即停止时间，结束时保留末尾姿势，权重归零后终止。
- AnimInstanceProxy.cpp 的 GetSlotWeight：保存总权重，必要时归一化；来源
  权重取决于非加法贡献。AlphaBlend.h 中默认混合为 HermiteCubic。

没有新增 UE 原生生命周期逐帧数值探针。本轮 UE 运行证据是数据导出及普通
Editor 重复导出，不等同于已经将 Godot 队列与真实 UE Montage 做过逐帧 oracle
对照。现阶段范围为原版 Turn 使用的单片段、单段、非加法动态 Montage；
Notify/BranchingPoint 回调、时间伸缩、通用段跳转和 Root Motion 不在此模型中。

## 验证结果

- Core 245 项通过：7 项新动态运行器测试及原 ActionPlayer/TurnRotate/契约
  回归。覆盖请求帧不推进、求值数据先冻结、同组/异组替换、17 个重叠实例、
  自然结束/淡出、起播时间夹取、零倍率播放状态、故障丢弃与身份重试。
  容量热身后连续 1000 帧替换的托管分配为零。
- Turn 导入 33 项通过，含原 29 项和 4 项组/资产数据检查。
- `artifacts/turn-montage-pose.log`：八资产、32 条曲线与直接片段采样一致，
  512 次非参考姿势骨骼输出；八次错误 Slot 不污染来源、八次过期求值区拒绝。
  请求帧保持原求值，满权重 Montage 不保留 Idle 的 Enable_Transition=1。
- `artifacts/turn-montage-base-layer-compiled.log`：映射路径 3360 帧、79 状态、
  18 次重入、12 次晚期故障、逐帧丢弃重试通过。动态转身播放 3 次，Montage
  求值数据非空 389 帧，隐藏期间推进 84 帧。静止请求从上一批 33 次变为 7 次，
  其中 3 次通过重复查询；真实 Slot 输出现在会影响下一帧 Enable_Transition。
  来源通知仍为 68 个；新增 Montage 遍历记录尚未分发 Notify。
- `artifacts/turn-montage-legacy-base.log`：旧入口 3360 帧保持。
- `artifacts/turn-montage-production-single.log` 和 parallel：各 180 帧，结果
  21E164D829153157、完整姿势 CF9225D4DE9B2C8B，事件 10 个；仍为旧生产入口。
- 最终 Godot 构建零警告零错误。

## UE 构建与导出记录

使用 ue-diagnosing-plugin-build-load 技能的完整 Editor target + 全插件审计。
首次 wrapper 没指定引擎自带 .NET，系统缺少 10.0，UBT 未进入编译即失败。
改为 DOTNET_ROOT / DOTNET_ROOT_X64 指向引擎自带 10.0/win-x64 后，完整 target
和三插件审计通过。日志位于源项目 Saved/Logs/PluginBuild 的
20260912T003751631Z-25dad19aed734e349b96986d8bcbbd3c-ubt.log / -audit.log。
状态指纹仍为 075B073AD82905F74259F3D9C2CF397369A679200FBD88B18DE6EEA590DB2564。

首次 Python 读取私有 slot_groups 属性失败，见 ue-turn-montage-inputs.log；
改为 ObjectExporterT3D 后，ue-turn-montage-inputs-native.log 退出 0，八资产
导出完成。首次组列表解析未处理 Slot 名中的括号，被编译门禁拒绝，修正
引号内括号解析后重跑通过。另有两处 C# 局部变量重名编译失败，已修正。

正常 Editor 重启导出 `artifacts/turn-montage-editor-repeat.json`，退出 0；
与正式输入 SHA256 均为
649EDF73B7C8336CFDB236AEFF5C1FBDB0D9D180C317D0C66794981FD1703A33。
`artifacts/ue-turn-montage-editor.log` 保留已有的两条 AutomationTest
Condition failed，不能说整份日志零错误。未更改 UE 插件/配置/加载代码，
因此未重新做插件打包和项目 DataValidation。技能引用的两个辅助技能在本机
技能目录不可用，本轮按实际首错与最终命令输出做验证，没有据此请求确认。

## 下一项与未完成

将 Montage 遍历、NotifyWeight、实例身份和中断信息接入统一候选通知处理，
与已有来源事件一起最终提交；再统一通用 ActionPlayer/动态转移的同组互斥，
并加入 UE 原生生命周期逐帧对照。随后将整套候选输出接入 Worker/Demo，闭合
最终曲线反馈、上身/手臂/髋、完整 Foot Lock/pelvis 与角色旋转。

本轮没有实际角色多帧截图或人工验收，不能关闭交错步、换髋、滑步和上身
问题；完整 Overlay/道具、Mantle/Roll、Ragdoll/Get-up/Pose Recovery、Camera
和 P7 十分钟性能仍按原规划保留。没有提交、合并或回滚用户工作区。
