# 待机、旋转与转身 Parent 状态刷新

主目录 `.`，main。普通 Demo 尚未切换，本批不构成画面或完整 ALS 验收。

## 原始设置与导出

新增只读 `export_refactored_rest_settings.py`，从原 `/ALS/ALS/Character/AB_Als.AB_Als_C` 默认对象读取 AIS_Als_Default 的完整浮点参数，绑定现有 catalog 摘要，保存八个内嵌转身设置对象/动画引用和四个动态过渡动画引用。没有修改或保存 UE 资产。

`AlsRefactoredRestSettings` 校验来源、catalog、参数范围、原 T3D 对象绑定及对象内 Sequence 引用，拒绝把另一侧转身或普通过渡资源冒充为当前资源。站立四组 ScalePlayRate=true，蹲伏四组=false，AnimatedTurnAngle 为90/180，动画 PlayRate 为原 float1.2。

实际 Rotate 参数：普通 yaw 阈值50，非瞄准第一人称65，速度映射180..460→1.15..3。Turn：yaw45、speed50、delay0..0.75、180度分界130、blend0.2。代码从导出文件读取，不用 C++ 默认值替代资产值。

UE 验证遵循插件构建诊断技能的完整 Editor 事务：

- wrapper 日志前缀 `20260925T065100850Z-bf7f88d2f8204283a4a88c8ef0cb42e4`，0 actions，退出0；ALS/AlsGodotExporter/AutoTestTools/BlueprintLisp 审计通过。
- fingerprint `FA68F4872E1F3257ABA5208517AA41A339139838F2C35AE3476C635B880EEB72`；BuildId `f7c75dee-59c9-476c-85d9-645c917dd66f`。
- 首轮冷导出退出1，`Vector2f` 没有 Python `.x/.y` 属性。根据 UE 反射声明改为 `get_editor_property`，未改参数或精度。首轮失败日志 `refactored-rest-settings-cold.log` 保留。
- 修正冷导出 PID37352 实际退出0，`refactored-rest-settings-cold-fixed.log` 有成功标记；普通 Editor PID41620 实际退出0，`refactored-rest-settings-normal.log` 有成功标记。
- 冷/普通两份3555字节文件 SHA256 均为 `396BB6A501378BB5BA7947EF2FF79361BA713039090F2E1945B1257C500BA2F9`。
- 普通 Editor 保留两个旧 Condition failed，以及 PawnActionsComponent、Navmesh、LineSet 材质、MotionVector、Crowd 警告。本批没有修复这些历史问题；没有 C++ 插件/配置修改，未运行新的 DataValidation。

## 运行时

`AlsRefactoredRestModel` 移植三个原函数的算术：

- Rotate 使用严格角度比较、Moving/Aiming/FirstPerson 开关、原 float range mapping、0.15半衰期 InvExpApprox 阻尼，以及 PendingUpdate 直接取目标值。
- Turn 保留持续条件计时、speed >= 阈值或 abs(yaw) <= 阈值清零、delay 严格超过目标才触发；175度以上按原规则改为逆时针；130分界及八组 stance/方向选择不合并。
- Dynamic 使用缩放后的距离阈值、double 世界空间距离、锁定相关性严格比较；两脚同时符合时选距离更大者，相等选左脚。

`AlsRefactoredRestParentRuntime` 持有角色/代际/帧候选、每函数每帧一次标志、两帧动态过渡延迟及两个原生播放请求队列。Prepare 不擅自递减动态计数，只有实际 Refresh 才减一次；不满足转身条件会清零计时，但不会擅自擦掉已排队请求。InitializeTurnInPlace 仅清计时。非游戏世界不执行三项 Refresh。

播放请求区分动画速度和旋转曲线倍率；原转身请求使用惯性 blend-out。AcceptPlayback 只在宿主已把当前准确请求加入候选 action bank 后确认消费，更新 TurnPlayRate；停止请求排队时不消费。它本身不启动 Montage，不做线程外部副作用。取消恢复状态与队列。

`AlsRefactoredStandingRestTraversal` 实现原 root203 RefreshRotate 与可选 Idle57/59/58 回调作用域：Begin 在状态机 Update 前，Idle 初始化相关性由原 callback counter 控制，源更新返回后反向 Leave，最后 Complete root。cache66 的延迟回调继续属于既有 MovementTraversal。此时仍未实现全角色宿主对这些模块的统一调用，也未补 node68 的最终 PoseStanding 封装。

## 测试与限制

日志 `artifacts/tests/rest-parent`：初次17项全部通过；加入 Idle 作用域测试后的相关53项通过；之后增加 Parent→Standing→真实 Rotate 播放器联动以及默认身份拒绝，最终新模块18项通过。Godot Optimize与Import Release构建均0警告/0错误。

覆盖五类非法资源、两帧延迟/跨stance同帧去重、距离严格边界与平距左优先、八组动画绑定、175度和130度边界、持续delay、排队请求保留/确认、相关性间断重置与取消恢复。30/60/120 Hz 共840帧 Parent 状态逐帧取消重试，实际 Rotate 输出驱动 Standing 状态机和两播放器时钟，验证停止循环后自动退出与79骨采样。

这些是本地算法与现有模块的验证。本批未导出原 UE 连续 Rest Parent 状态 oracle；浮点全轨迹等价尚未证明。真实转身/动态过渡 Montage 和 Slot 尚未消费这些新队列，没有新 Godot 渲染、截图、全量或十分钟性能验收。

下一步：原生 Rest Parent 连续状态对照、播放请求接既有 action/montage bank 与原 Slot 组，再与完整 Standing/118和统一角色宿主联动。StopQuick/停止回调、其他 stance、Lean update-only、普通 Demo、Ragdoll/Get-up/Pose Recovery及所有既定目标仍保留。音频、道具物理、头颈专项继续暂缓；用户现有修改未纳入本批。
