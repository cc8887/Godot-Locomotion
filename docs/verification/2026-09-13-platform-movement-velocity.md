# 平台携带速度与 ALS 自身移动速度

第一百五十六批，2026-09-13。工作区 `D:/GodotALS-p5a-events-actions`，
分支 `feature/p5a-events-actions`。未提交、回滚或合并已有工作。

## 已修复的生产差异

上一批 Motor 仍把 Godot `GetRealVelocity()` 传入动画、Character 旋转和
动态移动曲线。它包含平台携带的世界位移；原 ALS `SetEssentialValues`
使用 `GetVelocity`，在非物理模拟 Pawn 上返回 MovementComponent.Velocity。

本地直接证据：

- `assets/config/v4_character_movement_inputs.json` 的 SetEssentialValues
  原图中两个 GetVelocity 调用。
- `D:/UnrealEngine/Engine/Source/Runtime/Engine/Private/Pawn.cpp:252` 的
  APawn::GetVelocity，非物理模拟分支返回 MovementComponent->Velocity。
- `D:/UnrealEngine/Engine/Source/Runtime/Engine/Private/Components/CharacterMovementComponent.cpp:2533`
  的 UpdateBasedMovement 将基座变化用于胶囊位移/旋转；它不是把平台平移
  速度加到地面行走 Velocity 的入口。离开平台的速度继承是另一个行为。

生产原 ALS 路径现改用 MoveAndSlide 后的 `Velocity`；脚锁原先也使用该值，
因此移动参数、动画输入、旋转与脚锁现在使用同一自身移动速度。角色世界
变换继续保留实际平台运输。早期无原动画图的通用 Motor 保留原取值契约。
相机和键鼠适配器没有改动。

修改前的实际物理测试在第 5 帧复现：完全没有输入，平移平台使发布速度
变成约 `(1.249695, 0, -0.5) m/s`，误差 1.346008 m/s。失败证据保留于
`artifacts/movement-platform-156-before.log`。

## 实际物理与完整图验证

`MovementPlatformMotorSmoke` 使用三个实际 Motor：静止地面、平移平台、
旋转平台。平台是真正移动的 AnimatableBody，三个角色使用相同的静止、
起步、跑动和松键指令。校验物理支撑、平台身份、自身速度/加速度、完整
动态移动历史一致，同时要求世界运输速度和累计携带位移确实非零。

| Hz | 总帧 | 平台与静止地面对照数 | 最大自身速度 / 加速度差 |
| --- | --- | --- | --- |
| 30 | 120 | 232 | 0 / 0 |
| 60 | 240 | 472 | 0 / 0 |
| 120 | 480 | 952 | 0 / 0 |

日志 `artifacts/movement-platform-156-{30,60,120}.log`。这些是 Godot 的
平台运输隔离检查，不是 UE 平台上的整角色轨迹配对。

上一批原生连续速度参考的真实 Motor 回归也重新通过；最大速度差分别
为 0.0001215701、0.0001283923、0.0004529953 cm/s。每档 12 次快照恢复、
一次跳跃落地及制动延时检查通过。日志 `movement-motor-156-{30,60,120}.log`。
参考位置仍是原生速度积分，不能解释为 UE 碰撞世界位置。

新增 `MovementPlatformGraphSmoke` 将真实 Demo 的测试地面替换为平台，
执行 Main/Worker/Commit 和完整最终 Foot IK 根。平移用 single，旋转用
parallel，各 360 帧；各有 174 个静止检查帧和 129 个 ShouldMove 帧。
静止时平台携带不再触发走跑。图运行/速度语义检查通过。

该场景另记录双脚 Enable>0、LockAlpha>=0.99、射线命中可行走表面且连续
的 119 个帧对。指标基于平台局部坐标的最终脚骨骼，**只是锁定窗口诊断，
不是已验证的脚底物理接触真值，也没有作为平台脚锁通过门槛**：

| 场景 | 最大相邻帧位移 | 相对窗口起点最大漂移 |
| --- | --- | --- |
| 平移 | 0.051482 mm | 0.051931 mm |
| 旋转 | 1.352136 mm | 142.313990 mm |

旋转场景末帧平台 yaw=2.0999997 rad，而角色 yaw=0。该结果明确留下旋转
平台的未决问题：需核对原 CDO 的 IgnoreBaseRotation、角色 FaceRotation、
控制器旋转与 ALS 自有旋转更新，随后以 UE 同场景决定应如何继承/补偿。
不能仅因平台旋转就擅自把角色和相机强制旋转，也不能用每帧小位移隐藏
累计 14.23 cm 的偏移。

日志 `artifacts/movement-platform-graph-156-{translation,rotation}-drift.log`。
初始测试在 Demo 验证原关卡前禁用了地面，初始化失败；现已按原验证顺序
完成初始化后、首个物理帧前替换测试地面。旧失败日志保留，无生产验证放宽。

## 回归与横移相位

优化 Debug 构建 0 warning/0 error。生产单/并行各 960 帧通过：
结果 `EDD340F3A7BBFAF8`、完整姿态 `60FDBB3F45C29D6F`、归一化结果
`D7FA07E8615E43C8`、采样姿态 `DF6319125211FBB9`、根 `9EBE2D081A09FCF1`。
36 条事件、321 个脚锁帧、910 个 offset 帧，lag/stale=0。
同帧引擎速度检查改成与实际入口一致的速度源；在独立原生/平台测试通过
后更新摘要。旧速度契约/摘要失败日志保留，不把更新摘要当原生等价证据。
日志 `artifacts/movement-production-{single,parallel}-156-final.log`。

横移渲染 720 帧、120 张截图完成，已查看连续脚/上身截图：
`artifacts/movement-visual-156/movement-contact-sheet.png` 和
`strafe-directions.png`。无捕获异常，相邻脚最大转角 11.615°。

同一捕获的等待区间为 277–313 和 457–492 帧；方向过渡开始帧为
61、247、314、427、493、601。输入在 241、421 帧反向。这说明原条件等待
确实在运行，但不是“反向后立即固定等若干秒”；仍需与 UE 同输入、同相位
逐帧配对。起步 61、62 帧之后移动中的脚锁全锁窗口不存在，不能把行走中
每个低位脚帧都当作 FootLock 保持帧。

## 下一步和边界

继续旋转平台的原角色/控制器朝向与基座移动语义，再完成实际接触窗口、
起步/换向相位和 UE 整角色配对。默认仍 BaseLayer；完整根仍通过
`--foot-ik-frame` 进入，未提前关闭 P3/P4 效果验收或切换默认入口。
P5A 剩余项至 P7、已有失败与性能问题仍开放，音频暂缓。

本批未更改或启动 UE 插件；使用本地引擎源码和上一批正式原生数据。
未把历史 UE 构建、3780 帧受控动画配对或插件审计称为本批重新执行。
