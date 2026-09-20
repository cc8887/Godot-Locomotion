# 原动态移动规则接入生产 Motor

第一百五十五批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`。

## 实际变化

`AlsCharacterMotor` 的生产配置现已使用 `AlsCharacterMovementRuntime`。
`AlsMovementGraphDefinition` 创建一次共享只读模型，每个 Motor 独占参数历史。
没有改变键鼠/相机输入适配器。无原图上下文的早期通用 Motor 测试保留旧
积分入口；有完整原移动图的生产 Demo/Worker 使用新入口。

本地 UE `MovementComponent.cpp` 的 bTickBeforeOwner 默认 true，注册 Tick 时
向 Owner 添加组件先行依赖。原 CDO 的 tick_before_owner 也导出为 true，
导入时严格要求该顺序。实现流程为：

1. 使用保存的 MaxWalkSpeed/MaxWalkSpeedCrouched、MaxAcceleration、
   BrakingDecelerationWalking、GroundFriction 和制动倍率积分。
2. Godot 完成实际 MoveAndSlide，取得实际速度、地面和姿态。
3. 原 AllowedGait 决定下一帧限速；GetMappedSpeed/Movement Curve 更新
   下一帧加速度、制动力和摩擦。Character 旋转仍按已验证的先后顺序更新。
4. 实际输入量在旧 MaxAcceleration 被替换前计算，经无托管引用的
   `AlsFrameInput.MovementInput` 发布，Standing 更新直接消费。实际速度差
   加速度与当前动态归一化上限进入动画，输入量不从制动/碰撞加速度反推。

Motor 的已提交和已发布生命周期快照都包含新移动历史，恢复同时还原
参数、AllowedGait、旧 HasMovementInput 和落地延时。临时诊断 Step 不作为
历史恢复源。

原 CMC GetFallingLateralAcceleration 的空中控制、低速 boost 和加速度上限
也用于真实 Motor 水平速度；落地后的动态更新只在 Grounded 执行。Godot
仍拥有碰撞和垂直运动，这不是整个 UE PhysWalking/PhysFalling 的移植。

## 原图细节

- GetAllowedGait 的姿态/模式/目标步态分支已严格校验。CanSprint 使用实际
  CMC 输入量和输入方向相对 ControlRotation，LookingDirection 的绝对角度
  必须小于 50°；Aiming/蹲姿不允许 Sprint。
- Blueprint 的 0.9 引脚标记 bSerializeAsSinglePrecisionFloat，因此实际
  比较阈值是 `double(float(0.9))`，不是精确 double 0.9。原生边界案例
  揭示并修正了这个差异；界面显示的小数不能直接当作运行时双精度常量。
- OnLanded 的普通落地分支先读上一轮 Character 缓存的 HasMovementInput，
  将制动倍率设为 0.5 或 3；共享 RetriggerableDelay 为 0.5 秒，然后重置为
  0。再次落地重新开始倒计时。BreakFall/Roll 的玩法分支继续归 P5C。

## 原生对照

`export_character_movement_runtime.py` 调用原 Character Blueprint 的
GetAllowedGait/CanSprint/UpdateCharacterMovement 和实际 CMC CalcVelocity，
使用临时 Actor，不保存资产。每档 Hz 使用独立 Actor 起始状态。
正式数据在 `assets/config/v4_character_movement_runtime.json`。

- 1260 个步态边界：3 模式 × 2 姿态 × 3 目标步态 × 5 输入量 ×
  2 个 HasInput 值 × 7 个角度；CanSprint/AllowedGait 全部一致。
- 30/60/120 Hz 共 840 帧，包含起步、90°/180°换向、松键、步态变化、
  Aiming 和 Crouching。连续 Core 轨迹始终使用自己的速度与参数历史，
  速度差小于 .01 cm/s；参数另在同一原生速度上配对，加速度/制动力
  绝对门槛 .001、摩擦 .00001。曲线样本没有反向写入连续历史。
- 原生对照范围是受控地面输入的 CalcVelocity → 原 Character 函数，
  不含自动 world Tick、碰撞、坡面、平台和完整 PhysWalking 子步。
- 冷与普通 Editor 的设置、初始值、全部步态案例和连续轨迹完全一致。

首次错误把内部名 NewEnumerator3 当作数值 3；该枚举实际值为 2，已修正。
随后复用 Actor、只重置 CMC 的试验在后续场景出现零参数/原图警告；改为
每个场景完整独立 Actor 后，三档全部有效且无这些警告。无效数据保留于
`artifacts/movement-runtime-155-invalid-reused-actor.json`，不作为通过证据。
先前较大的连续参数误差也保留记录；同速参数和独立闭环速度分开验证，
避免将陡峭曲线对微小速度差的放大误判为同输入曲线求值差异。

## 实际 Godot 物理与回归

`MovementRuntimeMotorSmoke` 在真实平面上驱动实际 Motor，使用原生轨迹的
输入指令；前三档各有四秒原生速度配对和两秒跳跃/落地检查。每档 12 次
恢复已提交快照并重试，参数历史/消耗输入完全一致，落地倍率和延时清除通过。

| Hz | 总帧数 / 原生配对帧 | 最大速度差 cm/s | 最大积分位置差 cm |
| --- | --- | --- | --- |
| 30 | 180 / 120 | 0.000936002 | 0.000171926 |
| 60 | 360 / 240 | 0.001577216 | 0.000071526 |
| 120 | 720 / 480 | 0.002910270 | 0.000085963 |

位置参考由原生速度积分而来，不是 UE 物理世界中的 Actor 位置配对。
日志为 `artifacts/movement-motor-155-{30,60,120}.log`。

Import 新旧专项 25 项通过，含旧 3600 速度案例、原曲线编译、1260 步态
边界、840 连续帧、落地旧输入/重新触发和热身后 10000 次完整移动更新零
托管分配；Core 契约/旋转/输入/速度专项 117 项通过。记录为
`movement-runtime-155-final.trx` 与 `movement-motor-core-155-final.trx`，
位于 `artifacts/test-results`。优化 Debug 构建 0 warning/0 error。

真实 Worker 单/并行各 960 帧通过：结果 `20AE050AC2765814`，完整姿态
`16E2B00FE25FB8BB`，归一化结果 `EAB53CAD0800D6DA`，采样姿态
`5DF5562B3CF7D644`，根 `682155601CB85ABF`。均有 36 条来源事件、320 个
脚锁帧、910 个 offset 帧，lag/stale 为 0。此前基线是旧物理输入，故摘要
改变；先通过独立原生/实际 Motor 验证，再更新回归基线，旧失败日志保留。
日志：`movement-production-single-155-final.log`、`movement-production-parallel-155.log`。

## 渲染检查和剩余范围

完整脚部入口横移回放 720 帧、120 张图通过；目录
`artifacts/movement-visual-155`，已查看 `movement-contact-sheet.png` 与
`strafe-directions.png`。连续图可见胸/髋朝向及摆臂随横移阶段变化，没有
本场景的崩溃或突发整腿翻转。相邻脚旋转最大 11.615°，第 153 批同类
回放为 14.117°；输入轨迹已因新物理改变，不能当同姿态误差的直接比较。

仍须做反向相位、真正支撑接触窗口和平台相对脚轨迹的整角色 UE 配对，
以及空中、坡面、平台和更完整玩法。未用低位脚位移或单帧截图宣称脚锁
完成，也未重复第 153 批 3780 帧受控动画配对。本批实际 Motor 已切换，
完整分层/IK 仍通过 `--foot-ik-frame` 测试；默认动画入口保持 BaseLayer，
待实际接触/平台与人工验收后切换。P5A 剩余项至 P7、既有测试失败和性能
预算问题未关闭，音频继续暂缓。

## UE 构建状态

使用 `ue-diagnosing-plugin-build-load` 技能；完整 Editor 目标和三插件审计
通过，日志前缀
`D:/AdvancedLocomotionSystemV/Saved/Logs/PluginBuild/20260913T061344037Z-9eeae74625d14ed69358f08530e312b1`。
BuildId `a1908aa5-562b-424a-b3cb-43c0cec08df9`，源输入 fingerprint 保持
`DADE0C312865F26DB1A529AF963FCE7C085AB35DFEF613B7E59C60C2787AB355`。
本批未修改原生插件源码/配置，未复制 DLL。冷/普通 Editor 导出均退出 0；
冷运行只有既有 PawnActionsComponent warning，普通 Editor 仍有两条既有
AutomationTest Condition failed。日志为 `artifacts/unreal/movement-runtime-155-fresh.log`
和 `movement-runtime-editor-155.log`。原生插件的 DataValidation/隔离包沿用
第 154 批证据，本批未将其宣称为新的全项目无警告验证。
