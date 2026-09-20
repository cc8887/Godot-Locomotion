# 真实 Roll 通知与 BaseLayer 动作提交

日期：2026-09-12，第九十八批。承接上一批共同 Montage 请求所有者。

## 正式资产与实现

本机 UE 冷导出确认，当前 P5A 配置引用的 ALS_N_LandRoll_F_Montage_Default
包含一条 MovementAction Notify State；ALS_N_LandRoll_F 片段包含两条
CameraShake 和一条 GroundedEntryState 瞬时通知，共四条。RH 变体还有
OverlayOverride State，不能把它的两条 Montage 通知算到 Default 资产上。
已纠正上一批后续说明；未修改、保存或补写 UE 动画资产。

正式输入 assets/config/v4_action_notify_inputs.json 保留对象路径、类、状态
行为标志、开始/结束偏移、实际触发时间、权重/概率、服务器/LOD/跟随者和
过滤规则。状态结束时间按 UE AnimTypes.cpp 的两次 float 加法计算，包含
起始偏移：Default Roll 的有效范围从 -0.0001 到 0.9300273656845093 秒。
原始 duration 仍为 0.9301273822784424 秒，不能用有效终点替代。

AlsMontageNotifyCompiler 复用严格元数据验证，并将八个 Turn 和正式动作
纳入同一对象/名称空间；来源策略前缀不变，新增句柄不与既有来源/动作冲突。
片段抽取使用映射后的片段时间，回调上下文使用 Montage 时间；State 回调
duration 取原资产值，不沿用旧动作 lane 映射过的 duration。既有类型与 payload
继续来自正式导入器，不按显示名猜测玩法；BranchingPoint 暂不走 Queued 路径。

AlsMontageNotifyRuntime 使用一套 Montage 过滤随机状态，分别保留直接队列
和四个 Slot 队列。每个实例先处理 Montage 自身，再处理片段；Slot 首次访问
顺序、当前/前帧相关性、状态 AddUnique、旧播放的完整身份与全部候选状态
共同提交/丢弃。已中断实例不继续抽取，自然终止仍可处理最后一次遍历。

BaseLayer 新增不接收外部 Slot 权重的动作入口：直接消费 FrameInput 的
ActionRequest，使用共同物理实例计算 BaseLayer 权重、姿势和通知；最终阶段
验证请求、通知、主移动和尾部姿势后一起提交。原显式 Slot 权重入口保留为
组件夹具，并拒绝动作请求，防止真实动作与假 Slot 权重混用。

新增实际动作 Slot 求值器，保持骨骼姿势、按名称映射的曲线值及 presence。
完整 Roll 覆盖时，仅保留 Roll 实际具有的曲线，不继承已隐藏来源的曲线
存在标记；部分覆盖按原权重合成。Main Movement 曲线布局加入动作曲线名称，
所有 BaseLayer 测试库改用完整正式绑定。没有另建动画时钟或单个旧淡出尾部。

## 验证

- Core 相关 365 项通过，含新增 10 项共同通知队列测试：直接队列不受 Slot
  隐藏影响、前帧相关性、同 State 多实例去重、瞬时事件不去重、权重/服务器/
  LOD、终止与中断、片段时间映射、溢出回滚和预热零分配。
  日志 artifacts/montage-notify-core-final.log。
- Import 相关 131 项通过，含新增 13 项：实际四条 Roll 通知绑定、30/60/120 Hz
  实际请求/替换/完成与状态生命周期、每帧丢弃重试，以及缺失/错误/不支持
  元数据拒绝。日志 montage-notify-import-final.log。
- 完整动作组件 30/60/120 Hz 共 1050 帧，1050 次重试；354 帧完整 Roll 姿势
  对实际片段、372 帧来源隐藏、379 次动作通知、6 Begin/6 End、9 接受/
  3 替换/3 取消/3 完成、28 次真实 Slot 求值后的晚期故障通过。完整覆盖时
  逐名称检查曲线 presence。日志 montage-notify-actions-final.log。
- 映射主移动 3360 帧、12 次晚期故障；八资产转身通知 2400 帧、50 次回调、
  58 次有事件晚期故障通过。日志 montage-notify-mapped.log、montage-notify-turn.log。
- 旧 BaseLayer 入口 3360 帧、6 次晚期故障通过，日志 montage-notify-legacy.log。
- 生产 single/parallel 各 180 帧，结果摘要 21E164D829153157、完整姿势
  CF9225D4DE9B2C8B、来源事件各 10，与上一批一致。生产仍输出旧 Standing，
  不能把此项写成新 BaseLayer 已在 Demo 接线。日志 montage-notify-single.log、
  montage-notify-parallel.log。Godot 最终构建零警告、零错误。

原生对照边界：本批对照的是实际资产元数据、UE 队列源码和已有底层 oracle；
没有新增完整 AnimInstance/Roll Notify State 原生逐帧回调探针。动作组件输入
仍为受控物理数据，没有应用 Root Motion，没有完成 gameplay 或移动截图验收。

## UE 构建、导出与首错

按照 ue-diagnosing-plugin-build-load 技能，完整项目 Editor 构建及插件审计通过。
构建记录前缀 20260912T025234556Z-848859ebfabc4acfa61698dce1a7e1f5，
fingerprint 8F83BB343C52E314287C7FD84B32F8AEE442C0323DD0E88E49C23D43FF4442D6，
BuildId 369675c0-434c-4633-b2aa-532acf57bb8f。导出后再次审计通过，见
artifacts/montage-notify-ue-audit.log。本批无 UE 插件源码、描述符或配置改动，
没有重新打包插件或运行项目 DataValidation。

冷 commandlet 退出 0。前两次普通 Editor 均完成导出、正常日志关闭后返回
-1073741819；记录 action-notify-editor.log、action-notify-editor-retry.log，
未将其认作成功退出。检查 UE EditorPythonExecuter.cpp 后改用执行器下一 Tick
调度的 QUIT_EDITOR，移除新脚本中的直接 quit_editor 调用；复查普通 Editor
退出 0，见 action-notify-editor-autoexit.log。此为导出退出方式修正，没有
声称定位或修复引擎内部的全部退出异常。启动仍有两条既有 AutomationTest
Condition failed，不能称全局干净启动。

冷输出、两次异常退出前输出及最终普通 Editor 输出 SHA256 相同：
A710F887C3FB3F82602152205B6454D1D90E2B3C828DB3752225D9844E8CC184。

首次 Import 测试遗漏 Inspection 命名空间，已修正。替换测试最初错误地假定
三档频率都在第二条相机通知前发出替换；实际半秒请求前已采样到 0.4667/
0.4833/0.4917 秒。按物理 tick 先于请求的规则，改为逐事件比较应跨过的实际
触发时间，未改动画数据或延迟输入来通过测试。技能引用的调试/完成验证辅助
技能本机未安装，本批直接执行构建、日志、源码、产物与运行结果检查。

## 仍未完成

完整 Worker/Demo 尚未改用新 BaseLayer 输出；旧 ActionPlayer/P5 lane 尚未
从该生产路径移除。下一步汇入完整输入、动作结果/事件、最终姿势和晚期失败
回滚，再闭合最终曲线 feedback、YawOffset 角色消费、动态上身/手部以及脚部。
滑步、交错步、换髋和上身外观仍需实际移动、多帧截图和人工验证。

通用多 section/片段、BranchingPoint、动态脚部 Transition、Root Motion 唯一
所有权与运动应用继续保留。原 P5B 全 Overlay/道具、P5C Mantle/Roll、P6
Ragdoll/Get-up/Recovery/完整 Camera 和 P7 十分钟预算不缩减，音频仍暂缓。
未提交 Git，未回滚用户改动，未更改已确认的键鼠输入。
