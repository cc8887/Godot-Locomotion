# 原生基座运输接入实际 Motor

第一百五十八批，2026-09-13。工作区 `../GodotALS-p5a-events-actions`，
分支 `feature/p5a-events-actions`。上一批原生对照为有效进展；本批据此
修改了生产 Motor。未提交、合并或回滚其他已有修改。

## 生产变化

新增 `src/Als.Godot/Locomotion/AlsNativeBasedMovement.cs`。原移动 Runtime
路径关闭 Godot 的 PlatformFloorLayers/PlatformWallLayers 和自动离台
继承，由 Motor 显式拥有基座运输；早期无原 Runtime 的通用 Motor 保持原路径。

运输按 UE UpdateBasedMovement 的旧/新基座旋转和平移变换进行：取角色
胶囊底部在旧基座中的相对位置，转换到新基座，再恢复胶囊半高。基座缩放
不参与计算。与上一批使用角速度携带形成半径漂移的观测相比，新路径直接
使用实际变换。未因平台旋转而修改角色或相机朝向。

运输使用 PhysicsServer3D.BodyTestMotion 做真实碰撞查询，只排除当前基座，
有阻挡时应用 GetTravel；随后原 MoveAndSlide 处理自身移动、支撑和碰撞。
碰撞查询对象和排除列表复用。静止基座不重复做矩阵往返；微小但非零的
位移不按 IsZeroApprox 丢弃，避免低速平台永远不携带角色。

基座传输不是自身 Velocity。`WorldMovementVelocity` 记录整个 Motor Step
的世界位移速度，而 ActualVelocity、动画、动态移动参数继续使用自身速度。
Godot GetRealVelocity 只反映本次 MoveAndSlide，不能再当作包含前置基座
运输的整个 Step 速度。平台诊断已按此更新，物理测试另用连续世界位置差
独立核对；其他 Motor 作为支撑时也读取完整世界位移速度。

关闭引擎平台携带后，不再用 GetPlatformVelocity=0 为平台身份排序。
原 Runtime 路径按实际支撑法线选择；同法线接缝仍有当前基座接触时保持
其身份，否则按完整稳定实例 ID 选择。该策略适配 Godot 的实际接触数据，
不能宣称已经复刻 UE 所有复杂地面选择行为。

## 离台顺序与历史

本地 UE 源码依据：

- CharacterMovementComponent.cpp:6444 的 ControlledCharacterMove 先执行
  CheckJumpInput，再调用 PerformMovement；后者才进入 MaybeUpdateBasedMovement。
- OnMovementModeChanged 的 Falling 分支继承基座速度并清空 Base；原 V4
  四个 XYZ/Angular 继承标志在第 157 批已实际导出为 true。
- GetImpartedMovementBaseVelocity 使用胶囊底部相对基座原点的半径，叠加
  线速度与 angular × radius。

因此成功起跳时，本批先读取当前基座速度，跳过这次基座运输，只继承一次
速度再执行空中积分。最初实现先运输再处理跳跃，源码复核后修正，新增断言
要求起跳帧 BaseTransportDelta=0。走出平台则在实际失去支撑后继承一次。
Godot 侧速度来自实际物理状态，显式设置的 kinematic 速度非零时优先使用。

`AlsMovementBaseHistory` 保存基座对象、旧变换、线速度和角速度，随已提交/
已发布生命周期快照捕获和恢复；这份场景对象历史仅在 Main 使用，未放进
跨线程 FrameInput。基座移除、失去碰撞资格、离台、显式平台释放时清除。
恢复后仍以当前场景变换重做这一帧运输，避免重复携带或沿用错误旧基座。

## 原生位置配对

`MovementBaseOracleSmoke` 继续使用第 157 批实际 UE CMC 输出，先建立静止
平台支撑，再按同一逐帧基座变换运行六秒。未开 diagnostic，最大误差门槛
仍为 0.1 cm。

| Hz | 帧数 | 第 157 批最大位置误差 | 本批最大位置误差 | 本批最大半径偏差 |
| --- | --- | --- | --- | --- |
| 30 | 180 | 3.505208 cm | 0.000156455 cm | 0.000154972 cm |
| 60 | 360 | 2.233718 cm | 0.000264301 cm | 0.000238419 cm |
| 120 | 720 | 0.775651 cm | 0.000271839 cm | 0.000238419 cm |

三档均 position_match=True。每档 12 次同帧重试，交替恢复 committed 和
published 快照；整个候选生命周期记录、实际变换/速度和基座位移完全一致。
日志 `artifacts/movement-base-oracle-158-{30,60,120}-final.log`，最终小位移
处理修改后另复核 60 Hz，日志 `movement-base-oracle-158-60-complete.log`。
该对照仍限于原生受控空输入旋转基座的水平轨迹，不代表完整 UE 物理世界。

## 实际生命周期与图回归

新增 `MovementBaseLifecycleSmoke`，七个实际场景分别覆盖平移/下降平台
起跳、旋转平台起跳、走出台面、平台销毁、平台推向墙壁、两个基座间切换、
极慢平移。

- 30/60/120 Hz 各 120/240/480 帧，通过两种起跳、一次走出、一次移除、
  一次基座切换；阻挡帧分别 86/173/348，未穿墙，也未把运输写成走跑速度。
- 下降平台在起跳前分别保持 27/57/117 帧支撑；平台下降 0.5 m/s 时，
  5 m/s 起跳后的自身竖直速度为 4.5 m/s，保留向下继承。
- 极慢平台 0.00003 m/s 的逐帧位移小于通常 IsZeroApprox 门槛，四秒后
  仍正确累计约 0.000119 m，不会逐帧被吞掉。
- 日志 `artifacts/movement-base-lifecycle-158-{30,60,120}-complete.log`。
  离台/阻挡/切换是实际 Godot 场景加独立几何/速度检查；未称 UE 整场景配对。

平移/旋转平台与静止地面同输入的三档旧物理对照仍通过，速度/加速度差为
零，日志 `movement-platform-158-{30,60,120}.log`。60 Hz 原生动态移动轨迹、
12 次恢复及跳跃落地回归通过，`movement-motor-158-60.log`。原脚部 Gather
接缝/多接触/移除测试通过，`movement-base-foot-gather-158.log`。

优化 Debug 构建 0 warning/0 error。生产 single/parallel 各 960 帧通过，
日志 `movement-production-{single,parallel}-158-final.log`，未更新回归摘要：
结果 `EDD340F3A7BBFAF8`、完整姿态 `60FDBB3F45C29D6F`、归一化结果
`D7FA07E8615E43C8`、采样姿态 `DF6319125211FBB9`、根 `9EBE2D081A09FCF1`。
36 条来源事件、321 个脚锁帧、910 个 offset 帧，lag/stale=0。

实际完整 Demo 平移 single / 旋转 parallel 各 360 帧仍通过图/速度检查，
日志 `movement-platform-graph-158-{translation,rotation}.log`。锁定窗口
诊断中平移最大累计相对漂移约 0.05170 mm，旋转为 128.85831 mm，原为
142.31399 mm。胶囊轨迹修复没有关闭脚锁问题，不能把减少的一部分漂移
当作完整脚部验收。这些是无界面模式的实际图运行，本批未重拍静态地面
720 帧截图，也未重复 UE 构建/导出；原生数据仍来自第 157 批已验证输出。

## 下一步

进入 ALS-Refactored 的基座空间脚锁、切换/传送、重新锁定、移动/空中释放
及大腿/脚防扭转约束，并明确与 V4 曲线/骨骼和原静态地面路径的版本适配。
剩余约 12.89 cm 的锁定窗口漂移继续开放。平台倾斜/复杂多支持接缝、完整
UE 碰撞/空中/Root Motion 行为及性能预算也未由本批短场景关闭。
默认仍 BaseLayer，完整根仍通过 `--foot-ik-frame` 测试；P3/P4 效果与
P5A 剩余项至 P7 的完整目标保持，音频暂缓。
