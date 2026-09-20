# Main Movement 源图与状态规则

日期：2026-09-12。工作区 `D:\GodotALS-p5a-events-actions`。

## 本轮范围

按移植完整性恢复规划，补 Main Grounded 外面的 Main Movement 状态机。
源资产仍是本地 ALS V4；没有将另一版本 ALS-Refactored 的状态图代替它。
本轮不改变已确认的键鼠实现，也不声明当前 Demo 已完成 1:1 移植。

`AlsStopGraph -IncludeMovement` 输出 `assets/config/v4_main_movement_graph.json`。
包含总计 161 个图体，其中 37 个属于 Main Movement 及嵌套 Jump 图；共十个
baked machine。嵌套 Jump 图体虽已导出，它自己的 baked machine 尚未纳入，
不能据此称跳跃子图已完整导出或运行。

`AlsGroundedMachineCompiler.CompileMovement` 编译五个姿势状态和三个 conduit：

| 索引 | 状态 | 关键行为 |
| --- | --- | --- |
| 0 | Grounded | 初始状态，离开时保留源 Notify |
| 1 | Fall | InAir 分支进入时请求 0.75 秒惯性化 |
| 2 | Jump | Jumped 优先进入，请求 0.1 秒惯性化；每次进入强制初始化 |
| 3 | Land | 无输入落地；离开、移动、动画结束三种规则按原优先级判断 |
| 4 | MovementState conduit | 按源 MovementState 分发 |
| 5 | InAir conduit | Jump 优先于 Fall |
| 6 | Land Movement | 有移动输入或原地旋转时落地；按来源原始时间自动退出 |
| 7 | Land conduit | 仅 MovementState 为 Grounded 时允许通过 |

两个落地入口均为 0.1 秒惯性化，不是普通交叉混合。Land 的移动条件使用
HasMovementInput、Rotate_L、Rotate_R 或 Speed 严格大于 650 cm/s；运行时单位
转换为 6.5 m/s。不能用 ShouldMove 替代输入判断。

Land 动画结束使用 GetRelevantAnimTimeRemaining 等于零，退出混合 0.8 秒并使用
QuickFeet。该值由调用方显式提供，必须来自实际相关播放器的速率调整后时间；
没有可用来源时应提供原生无来源哨兵 float.MaxValue，不能把默认零当已播放结束。
Land Movement 的自动规则使用原始来源时间、0.3 秒混合窗口及超出窗口量修正。
两种时间语义没有合并，也没有增加第二个动画时钟。

共享状态机增加 AlwaysResetOnEntry。对照本地 UE 的
`Engine/Source/Runtime/Engine/Private/Animation/AnimNode_StateMachine.cpp` 中 SetState：
目标仍有权重时，强制重入设置仍触发 Initialize；清理播放器缓存权重的行为保留。
候选初始化日志和失败重试不修改先前已提交状态。

## 验证

- Import 新专项 14/14：30/60/120 Hz 状态路径、惯性化请求、输入与速度边界、
  自动退出、通知身份，以及错误 getter、枚举、阈值、优先级和 Notify 数据拒绝。
- Core 新专项 2/2：有权重状态重新进入时，分别保留或强制初始化；混合历史与
  候选重试保持一致。
- Core 常规 1952/1952，通过既定过滤排除 AlsP5aGoldenTests 和
  AlsP5aTraceSchemaTests；不称这两个历史集合也通过。
- Import 全套 1211/1211。TRX 在 `artifacts/test-results/main-movement/`，包含
  首次失败记录：遗漏 Jump 重置设置、错误的落地混合测试预期及测试选择器过宽。
- Godot 优化构建零警告、零错误；Standing、Detail、Pivot 实际资产回归通过。
  Main Grounded 六缓存回归 1680 帧、2360 次原始姿势检查与失败重试通过。
- Worker 单/并行各 180 帧通过，结果 `21E164D829153157`、完整骨骼
  `CF9225D4DE9B2C8B` 一致，每模式十个来源事件；晚期来源事件故障没有泄漏
  回调，来源同步、控制器、运行时、姿势和 P4 缓冲区整体回滚通过。

UE 项目完整 Editor target 构建和插件审计通过，输入指纹
`6AA5459436A7CD6C1FA590E8D3950134C88EB6A105E56FDA7C979E074C9DD6EF`。
独立 BuildPlugin 验证包位于
`artifacts/unreal/AlsMainMovementPluginValidation-20260912`，没有部署其中的 DLL。
DataValidation 退出零，零错误、三个旧资产警告（AI PawnActionsComponent 和导航版本）。

主移动重复导出 SHA256 均为
`B727712814F6C683EA807F247F56239CA44149DFFDEDC1671031DB95FC19CA23`。
旧 Grounded 导出和正式文件 SHA256 均为
`B0B5DFB8BFDE3059FA25BF9E8685AAC14CF71FD9F0C795C67A46A224AE589EF2`。
导出均零错误、零警告，assets_saved=0。

普通 Editor 首次启动成功，执行 Yaw 只读导出后正常写完关闭日志，但进程返回
`-1073741819`（访问异常）。没有新的项目 crash dump，也未查到对应 Windows
应用错误事件；原因未定位，不能将此轮记为普通 Editor 退出验证通过。
保留 `artifacts/ue-main-movement-editor-restart.log` 与导出数据。同配置重启复查
正常导出并退出零，日志为 `artifacts/ue-main-movement-editor-retry.log`；未修改
插件或关闭功能来通过复查。两次都有之前已存在的两个 AutomationTest 条件错误。
本轮有成功的普通 Editor 冷启动、导出和退出证据，但首次退出异常仍是未定位
的间歇性问题，不因为复查通过而删除该失败记录。

## 后续接线与验收边界

此状态机尚未驱动 Demo 的完整姿势。后续必须补 Main Movement 五个内容状态的
真实来源身份、时间、同步、曲线和求值，再接嵌套 Jump、真实 Slot/Montage 与
最终惯性化。Grounded 和 Land Movement 分别读取同一 Main Grounded 缓存；后者
在其上施加 mesh-space additive，不能用一个通用淡入淡出代替源图。

最终曲线统一后，再把 YawOffset 反馈到角色旋转，补动态上身分层、Overlay
权重和完整脚部反馈。原 P5A/P5B/P5C、P6/P7 范围保留，音频暂缓。
本轮没有 UE 全图逐帧姿势对照、人工移动截图、平台脚锁或十分钟性能验收。
滑步、换髋和上身视觉问题均保持未关闭。
