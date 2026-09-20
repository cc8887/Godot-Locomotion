# 动态转身与 authored Montage 的共同播放所有者（第九十六批）

本批推进 P5A 的实际播放所有权。原动态 Montage 所有者扩展为
`AlsMontageRuntime`，真实 Roll 与动态转身可以使用同一时钟、实例序号、
候选提交、淡出队列和求值快照。尚未迁移旧 ActionPlayer 的请求仲裁、
通知状态及 gameplay 提交，不能将本批写成完整 ActionPlayer 或 Roll 玩法完成。

## 实现

- `AlsDynamicMontageRuntime.cs` 中的运行时类型现为 `AlsMontageRuntime`，
  共享状态/快照类型改为 MontageInstance/Frame/Evaluation/Traversal；文件名
  暂保留。Turn 输入仍使用 AlsTurnSlot，物理快照使用独立 AlsMontageSlot，
  不把 Montage 自己的 slot 数组下标误认为动画图 Slot 身份。
- 新增 authored 动作绑定、PlayAction、按实例 StopInstance 和资产活动实例
  查询。保留每个旧实例的时钟与淡出；新增请求不会改变本帧已冻结的求值快照。
  瞬时 Montage 与 authored Montage 共用上一批原生验证的混合/推进实现。
  事件遍历保留 Montage 时间，姿势求值转换为片段时间，并携带动作定义身份。
- Stop 首次使用传入混合选项；缩短已有 Stop 仍保留原选项，符合 UE 源码。
  同资产重播会更新活动查询的所有者；新实例停止后，查询不会回退到仍在播放
  的旧实例。不同动作定义引用同一原生 Montage 时共用这一查询身份，绑定
  内容必须一致。查询身份与候选一起丢弃/提交。
- `AlsAuthoredMontageCompiler` 将实际 Roll Montage、P5A profile、原生
  Skeleton SlotGroups 和本次只读导出数据严格绑定；核对 section/segment、
  骨架、片段时间映射、混合设置和 root-motion 标记。当前明确拒绝多 section、
  多片段、循环、加法、非单位资产速率、惯性混合、混合 profile/custom curve
  等尚未支持的物理播放配置。
- `AlsMovementGraphDefinition.Load` 正式加载动作绑定，BaseLayer 的共同
  所有者已注册这些资产。当前映射入口仍只发起 Turn 请求；没有提前开启
  不完整的 ActionRequest/gameplay 路径。
- 现有 Slot 采样器支持 authored 片段和通用 Slot 身份；验证只检查当前
  Slot 要消费的贡献，其他 Slot 的资产不会被误报为“未绑定转身动画”。
  新增真实 Roll/Turn 的骨骼与曲线合成 smoke。

## 原生规则与对照

原骨架 `Grounded Group`（索引 1）含 Grounded Slot 和站/蹲 Turn/Rotate；
`MovementActionGroup`（索引 3）含 BaseLayer。Roll 使用 BaseLayer，因此
默认与转身跨组并存。不能把共同所有权误实现成任意动作互相中断。

本机 UE `AnimInstance.cpp::Montage_PlayInternal` 在 bStopAllMontages 为真时
调用 StopAllMontagesByGroupName，约束的是同组。ActiveMontagesMap 按原生
Montage 资产保存最新实例；ClearMontageInstanceReferences 只删除确实指向
被停止实例的记录，不重新寻找旧实例。活动查询规则由源码与专项测试验证，
本批数值探针尚未直接导出该 Map 的查询返回值。

扩展上一批原生探针，用真实 Montage_Play 播放
`ALS_N_LandRoll_F_Montage_Default`，RootMotionMode 为 NoRootMotionExtraction。
新增 11 组、2540 帧：Roll 在 30/60/120 Hz 播放、与转身两种先后次序混合、
同帧重播、不停止同组的重复播放、反向、零速率、按旧实例/活动实例停止。
原有 40 组转身夹具保留，合计 51 组、10606 帧。

原生求值结构本身没有实例 ID；同一个 Roll 资产可同时对应多个实例，不能
继续按 Montage 指针给求值行分配身份。本批按 UE 构建求值列表使用的实例
顺序与权重过滤规则配对，再映射实例 ID。没有以资产名称去重旧播放。

## 验证

- Core 相关 333 项通过，包括 51 组每帧数值/身份对比和丢弃重试、共同
  所有者、同组替换、资产查询、别名一致性、无效请求无副作用、映射与
  预热后零托管分配。原生浮点比较仍为绝对误差 0.000003，身份/顺序精确。
  日志 `artifacts/montage-owner-core-final.log`。
- Import 相关 57 项通过，覆盖原生 Roll 绑定、跨组并存、原生字段与支持
  范围的拒绝，以及已有 Turn/Notify 编译。日志 `montage-owner-import-final.log`。
- Godot 最终构建零警告、零错误。新 mixed smoke 240 帧、240 次同帧重试，
  含 46 次多 Roll 实例贡献采样、340 次命名曲线采样；两种计数均包含重试，
  不是唯一帧数。实际骨骼及所选命名曲线均按完整实例队列合成。
  日志 `montage-owner-mixed-final.log`。它没有应用角色 Root Motion 或 gameplay。
- 映射 BaseLayer 3360 帧、12 次晚期故障；转身通知 2400 帧、50 次回调、
  58 次有事件晚期故障；旧入口 3360 帧、6 次晚期故障通过。日志分别为
  `montage-owner-mapped.log`、`montage-owner-turn.log`、`montage-owner-legacy.log`。
- 生产 single/parallel 各 180 帧通过，结果摘要 `21E164D829153157`，完整
  姿势摘要 `CF9225D4DE9B2C8B`，来源事件各 10。生产仍使用旧 Standing
  输出，不能据此宣称完整新图或 Roll 已在 Demo 接线。

## UE 产物与失败记录

按 ue-diagnosing-plugin-build-load 技能完成完整项目 Editor 构建、插件审计、
冷导出、普通 Editor 重启导出、DataValidation、隔离 BuildPlugin 和包后审计。

- 成功构建记录前缀 `20260912T020325760Z-bb5a360e8fda45948a6aa81869bdf5e8`，
  fingerprint `8F83BB343C52E314287C7FD84B32F8AEE442C0323DD0E88E49C23D43FF4442D6`，
  BuildId 保持 `369675c0-434c-4633-b2aa-532acf57bb8f`。
- 冷导出和普通 Editor 均退出 0，混合轨迹 SHA256 相同：
  `0887E1FF56BCA48220104C459CB3DCF05DCEF253D33905026D16DFB6E06E48D2`。
  正式夹具 `tests/Als.Core.Tests/Fixtures/P3/v4_mixed_montage_native.json`。
- 扩展探针后重新冷导出原 40 组转身，退出 0，SHA256 仍为
  `5A91250DAD185040AA2833080EA1046D9610DFA7539FB772163AD70932EFE1DA`，
  与上一批正式夹具逐字节一致。
- 正式动作输入 `assets/config/v4_action_montage_inputs.json` 来自普通 Editor
  原生导出，SHA256 `92642863091272D5E3F454DC91304223ACBD36E4EA54E0E267E172A65E2208E3`。
  没有修改或保存 UE 动画资产，没有改变原资产导出锁。
- DataValidation 退出 0，688 资产、0 error、3 warning；普通 Editor 仍有
  两条既有 AutomationTest Condition failed，不称全局无错误启动。
- `artifacts/unreal/AlsMontageOwnerPluginValidation-20260912-96` 隔离插件包
  构建退出 0，未部署包 DLL，之后项目插件审计再次通过。
- 首次 C++ 构建因 FMontageBlendSettings 不支持两个参数构造而失败，改为
  设置真实结构字段；日志 `20260912T020203360Z-93a1dd67589c483883a380ffa822c91d-ubt.log`
  保留。首次 Core 构建暴露通知队列仍使用旧 Turn Slot 存储类型，已修正。
  中途补丁匹配失败没有覆盖文件，之后逐项修正并同步两份 UE 插件源码。
  最终比对发现仓库头文件的一行缩进与 UE 项目副本不同，已仅补齐缩进，
  三个相关源码文件随后逐字节一致；没有改变已经构建的函数声明语义。

## 未完成与下一项

旧 `AlsActionPlayer` / `AlsP5Runtime.TryBuildActionLane` 仍保留单逻辑动作与
单旧尾部的历史流程。本批为替换它建立了共同物理所有者，并用实际动作
证明其时钟、队列与姿势能力；没有把旧请求仲裁、优先级、完成/中断回调、
Montage 与片段 Notify State 搬迁到新所有者。因此下一项就是该迁移和统一
事件提交，而不是再另建一套播放时钟。

仍需通用多 section/片段、Root Motion 及其唯一拥有者/运动应用、动态脚部
Transition，以及完整 Worker/Demo 输出、最终曲线反馈、动态上身/手部、
FootLock/pelvis/平台。Overlay/道具、Mantle/Roll 完整玩法、Ragdoll/Get-up/
Recovery、完整 Camera 与最终十分钟预算均按原 P5A–P7 保留。基础滑步、
换髋和上身问题尚未完成新的多帧移动截图与人工验收。未改已确认键鼠，未
提交 Git，未回滚用户已有改动。
