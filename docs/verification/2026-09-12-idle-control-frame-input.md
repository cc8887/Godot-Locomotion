# 静止 Rotate/Turn 检查、视角速度与图内曲线（第九十一批）

继续原 P3/P4 和前置 P5A。本批移除 BaseLayer 映射入口的 IdleControlOutput
夹具参数，改为源图检查驱动 Rotate 标记、RotateRate、ElapsedDelayTime 和
TurnInPlace 请求。完整 BaseLayer 仍未替换实际 Demo，不能关闭视觉问题。

## 原始规则与正式数据

新增只读导出脚本 export_idle_control_inputs.py 和正式
assets/config/v4_idle_control_inputs.json，包含十张动画蓝图原生图、CDO 文本、
角色 SetEssentialValues、CacheValues、TickGraph 等历史证据。编译器直接
验证原生连接、变量与函数归属、枚举域、输入分量、数学表达式及执行顺序；
不使用可读 DSL 中缺失纯函数表达式的“返回节点”作为实现依据。

- CanRotateInPlace：Aiming 或第一人称。允许时分别检查 AimingAngle.X < -50
  和 > 50；只有至少一个 Rotate 标记成立时才把 AimYawRate 的 90..270
  映射到 RotateRate 的 1.15..3，否则保留倍率。禁止时只清除两个标记。
- CanTurnInPlace：LookingDirection、第三人称、Enable_Transition > 0.99。
  随后要求绝对朝向差 > 45 度，AimYawRate < 50 度/秒；先累加 DeltaTime，
  再严格比较 elapsed > MapRangeClamped(angle,45,180,0.75,0)。任一条件不满足
  时清零延迟，不能用固定等待替代。发出请求后不清零，后续仍可能每帧调用。
- 请求参数为瞄准世界 yaw、PlayRateScale=1、StartTime=0、OverrideCurrent=false。
  请求不是动画已开始；TurnInPlace 函数中的资产选择、IsPlayingSlotAnimation
  和真实播放仍须由后续动作所有者执行。
- CanDynamicTransition 独立要求 Enable_Transition == 1，不能与 >0.99 合并。
  本批只运行其门禁，未实现 DynamicTransitionCheck 的脚部检测和动作播放。
- WhileFalse 的顺序为 Rotate、Turn、DynamicTransition；它只在 Grounded
  且 ShouldMove=false 时调用。移动和空中不会产生本批的静止动作请求。

AimingAngle.X 来源为 AimingRotation 相对角色旋转的标准化 yaw。Godot 与 UE
的 yaw 方向相反，模型在边界转换并保留 UE 的 +180 规则。完整平滑 Aim、
Spine 等 UpdateAimingValues 输出仍待后续上身阶段完成。

## 真实角色输入与事务

角色原图 AimYawRate 为 abs((GetControlRotation.Yaw - PreviousAimYaw) /
GetWorldDeltaSeconds)，没有最短弧修正。CacheValues 保存控制器 yaw；TickGraph
先执行 SetEssentialValues 及状态更新，随后 CacheValues。CDO PreviousAimYaw=0。
本地 UE PlayerCameraManager.cpp 的 LimitViewYaw 将 yaw ClampAxis 到 [0,360)。

新增 AlsAimYawRate.Gather，将 Godot 控制器弧度转为上述 UE 角域。真实 Motor
将其速度和 FirstPerson 写入不可变 AlsFrameInput，控制器历史进入 Motor
生命周期快照，两种恢复路径均恢复它。不能复用旧上身模型经过环绕和平台
修正后的 yaw 速度。这里对照的是普通 PlayerCameraManager 路径；不是对所有
自定义控制器角域及网络平滑模式作兼容性保证。FirstPersonView 是输入接线，
完整第一人称 Camera/UI 仍未实现。

AlsIdleControlInputModel 的结果与全局控制候选共用提交边界。最终姿势失败，
延迟、标记和倍率不发布；请求只供候选动作准备使用，不在检查期间对外播放。
非静止帧显式清空候选请求，避免沿用上一帧的 Turn。RotationScale 保持原值，
待真实 TurnInPlace 动作调用时按原函数更新。第九十二批核对纠正：原图在
调用 PlaySlotAnimationAsDynamicMontage 后无条件写 RotationScale，不检查
返回值；不是“成功播放后更新”。重复播放门控阻止调用时才保持原值。

## Enable_Transition 的实际遗漏

Grounded 曲线表此前取动画资产的曲线名，再额外加入 YawOffset。核验本次
Grounded 来源及 Turn 资产集合，携带 Enable_Transition 的动画数为 **0**；
它由站立/蹲伏 Idle 的 ModifyCurve 生成。现将站立图声明的覆盖曲线名以及
RotationAmount 显式并入曲线表，再由父图按名称映射。没有向所有帧写入 1。

AlsAnimationInputFeedback 新增 EnableTransition 的值与 presence，读取上一
已提交的最终输出；保留名称重复、外来帧、冷帧及非有限值检查。源图实际
写入才产生其值，缺失时该读取者得到零，门禁关闭。

首次整链回归没有 Turn 请求还暴露了覆盖问题：原回放的停步窗口处于外层
IdleLand，Standing 的最近更新时间仍停在之前移动帧，不能用它验证完整静止
转身。保留这段落地回放，另增加地面静止起始段及静止期间的 Slot 隐藏窗口。
新的正向 Turn 覆盖来自实际图输出，未强制反馈曲线或降低门限。

## 验证

- 导入、宏、Yaw 与新静止控制专项共 51 项通过，其中新增 23 项。包含
  30/60/120 Hz 延迟、持续请求、速度/模式/曲线门禁、倍率保持、九类源数据
  变异拒绝，以及原生角域跨界速度。新增依赖检查时修正了多个 AimingRotation
  getter 的定位方式，改为沿实际输入连接查找。
- Core 契约、帧交换、Standing/方向相关 91 项通过。顶层字段顺序已包含
  AimYawRateDegrees 和 FirstPerson；候选与请求保持 unmanaged。
- Godot 最终构建零警告、零错误。映射 3360 帧通过：882 次静止检查、336 次
  Rotate、33 次 Turn 请求、706 次 Turn 门禁关闭、31 次隐藏时静止检查。
  18 次重入、12 次晚期故障、24 个守卫及每帧重试通过，原状态覆盖 mask=79。
  普通 Editor 和 commandlet 的控制/静止图分别编译，逐帧候选一致。
- 新真实 Motor 场景在 30/60/120 Hz 下分别产生 13/28/57 次 Turn 请求和
  30/60/120 次 Rotate，每档一次已提交物理快照恢复，同帧重试的视角速度与
  静止结果一致。该场景提供 Enable_Transition=1 的测试反馈，只验证真实
  物理/控制输入和恢复，没有求值完整角色姿势。
- 旧 BaseLayer 3360 帧、生产 single/parallel 各 180 帧通过。生产结果仍为
  21E164D829153157，完整姿势 CF9225D4DE9B2C8B；这是旧 Standing 兼容性证据。

主要日志在 artifacts：idle-control-runtime-final-origin.log、
idle-control-motor-30/60/120.log、idle-control-legacy-base.log、
idle-control-production-single/parallel.log。初始失败和诊断日志保留。

UE 完整 Editor 目标/插件审计通过，实际引擎路径 ../UnrealEngine，版本 5.9。
BuildId 仍为 0c423ffb-b3f5-4fb8-9fc9-5db49727eb0b，输入指纹仍为
075B073AD82905F74259F3D9C2CF397369A679200FBD88B18DE6EEA590DB2564。
两个辅助技能本机未找到，使用直接构建、日志及运行结果验证。未改 UE 插件、
配置或加载方式，不重复插件打包/DataValidation；未部署 DLL 或保存 UE 资产。

只读导出期间曾误将角色更新图名写为 UpdateGraph，且 native_text 漏检空对象，
导致 UE UExporter 断言退出（退出码 3）。现已加空对象检查，并枚举角色实际
图，确认图名为 TickGraph 和 CacheValues。失败日志
ue-idle-control-export-character.log 保留。后续 commandlet 退出零，普通 Editor
启动/导出/退出零；后者仍保留两条既有 AutomationTest Condition failed，
不能称整份日志零错误。对应成功日志为 ue-idle-control-export-verified.log
和 ue-idle-control-editor.log。

正式数据 SHA256 为 3E8820326D200906E3DC8FCC5CF3ECFCBBA50ADE358F8C0AA7CCA6D78B1C47D9，
普通 Editor 重复导出为 C85757A575D79CFE0F1EF82B73D71FD35E6A9DC52E136E95063EE4B5D14A0FD2。
Editor 不保留相同生成图/未连接节点集合，文件非字节一致；本批验证的是
所消费图的编译和候选一致，尚非完整 UE 角色逐帧运行对照。

## 下一项与剩余范围

继续 TurnInPlace 的站立/蹲伏、90/180 度资产选择、Slot 是否正在播放判断、
实际播放与 RotationScale 更新。与通用 ActionPlayer/Montage 连接后，再将
完整 BaseLayer 接入 Worker/Demo 晚期提交，消除旧入口与新组件的隔离。
同时补 DynamicTransition 的真实脚部输入、最终 YawOffset 消费，以及动态
Layering/Add/LS、Aim/手部、Foot Lock/pelvis/平台和多帧视觉验收。

原 P5A–P7 其余通用动作、全部 Overlay/道具、Mantle/Roll/Root Motion、
Ragdoll/Get-up/Pose Recovery/完整 Camera、十分钟性能仍未完成；音频暂缓。
本批未提交、回退或合并工作树。
