# 实际场景碰撞与外部运动身体接入

主目录 `D:\GodotALS` / `main`，接续 `6d82cbc`。
Core 现在可以消费外部驱动的零质量身体姿态与速度；Godot 适配器可以从指定环境子树读取实际碰撞形状、过滤条件和平台运动。
两套资产在 demo World 的普通/高速落地、30/60/120 Hz 六项十秒检查通过。
本批仍是物理接入与独立诊断，**普通角色 Ragdoll / Get-up / Pose Recovery 尚未接通**。

## 1. Core 外部运动输入

`AlsIslandBody.ExternallyDriven` 明确区分固定身体与外部运动身体，动态身体不能同时声明此标志。
`AlsIslandKinematicTarget` 按身体索引递增、唯一，提供 actor 世界姿态及 actor 原点速度（cm/s、rad/s）。
目标先完整校验，再进入共享的关节/接触求解。外部身体逆质量为零，不被接触推动，也不会再积分一次。
没有新目标时保持姿态，下一次成功步清零速度。
外部变化唤醒休眠组；外部仍移动时本组不能休眠。失败不发布身体、睡眠或接触历史，原目标可重试。
暂存数组预分配，Core 外部输入热路径零分配测试通过。

`AlsKinematicMotion.PositionTarget` 对齐本机 UE 源码的一次完整 Position target 步：

- `D:\UnrealEngine\Engine\Source\Runtime\Experimental\Chaos\Private\Chaos\PBDRigidsEvolutionGBF.cpp`，约 1188–1298 行：Position / Reset，目标先应用到 X/R 与 P/Q，再 Gather。
- `D:\UnrealEngine\Engine\Source\Runtime\Experimental\ChaosCore\Private\Rotation.cpp`，约 146 行：float 最短弧四元数导数计算角速度。
- 位置差使用 double，粒子旋转与输出速度保持 float 边界；保留 `1e-6f` 最小步长和 `1e-8f` 变化容差。

这里只移植完整 Position 步及下一步速度重置契约，未实现 substep 插值、Velocity target 模式、CCD。
还没有新导出的原生 kinematic 连续轨迹，不能称逐帧 Chaos 等价。

## 2. 场景绑定契约

`AlsSceneContactSet` 仅在 Main 读取显式环境子树，使用 Godot shape-owner API：

- 借用 `StaticBody3D` / `AnimatableBody3D` 实际拥有的球、盒、胶囊、凸包及局部姿态。
- 其他动态集成 owner、非刚体缩放和不支持的形状明确拒绝，不静默漏掉。
- `AnimatableBody3D` 用上次成功发布的姿态与本次场景姿态推导速度；不叠加 ConstantVelocity，也不反向推进场景节点。
- 静态身体使用其 ConstantLinearVelocity / ConstantAngularVelocity，位置跳变刷新 body generation。
- 显式 `MarkTeleported` 清除接触身份并置零速度；只有成功 Step 后 `CommitCapture` 才消费请求，失败重试不会变成巨大的平台速度。
- 形状资源 Changed、局部姿态、layer/mask、disabled 更新推进 shape revision 并重新绑定查询；移出场景或删除 owner 时移除对应注册槽。
- 新增形状超出固定拓扑时明确拒绝，必须由 world owner 重建。资源由场景拥有，Dispose 只退订与注销；求解期间拒绝 Dispose，退出求解后仍可正常释放。

合法调用顺序是 Capture → Island.Step（携带 targets）→ CommitCapture；Step 失败不能调用 CommitCapture。
场景注册表反映当前真实几何，**没有承诺 Capture 对整个场景注册表的变更具有回滚事务**；保证的是物理步状态与历史提交。

实际 demo World 有 13 个身体、13 个有效碰撞形状。隐藏的 `BottomPlan` 下虽然有 CollisionShape3D，
但没有 PhysicsBody owner，不是实际场景碰撞体，因此不纳入。
StartFloor 的碰撞形状具有局部偏移，真实顶面是 Godot Y = -0.4339840412 m（native Z = -43.39840412 cm），
本批按该实际数据查询，未改动用户场景、模型或碰撞体布局。

## 3. 平台与生命周期验证

新增 `scenes/tests/physics_scene_contact_smoke.tscn`，只从 demo 场景取出 World，
不实例化角色逻辑、不运行 demo 的平台 updater；探针显式移动实际平台节点。
30/60/120 Hz 各验证静态地面、平移平台、旋转平台三场景，每场景两秒。
Core 球体受到平台的法向和摩擦响应，外部平台状态逐帧严格等于输入，没有二次积分。
旋转平台校验正确运动方向，不只校验速度绝对值。

每种帧率另有 13 项实际几何/形状局部偏移/坡面法向检查，以及 9 项生命周期检查：
休眠、失败重试/运动唤醒、求解中释放拒绝、teleport 重试、资源变更、禁用/过滤恢复、
非法缩放/新增拓扑拒绝、Worker 访问拒绝、移除支撑。

| Hz | 平移响应峰值 cm/s | 正确方向的旋转切向响应峰值 cm/s | 窄相查询次数 |
| --- | --- | --- | --- |
| 30 | 20.000338 | 9.999926 | 386 |
| 60 | 20.000698 | 9.999983 | 746 |
| 120 | 20.001432 | 9.999994 | 1466 |

响应来自可滚动球体，不要求球心速度与平台表面完全相等。
最终产物为 `scene-verified-{30,60,120}.json/log`。

## 4. 实际场景中的完整资产链

`PhysicsCoreJointReplay` 新增 `--scene-world`（必须同时 `--chains --drop`）。
每个角色保留独立 island/查询世界，添加 13 个环境身体：合计 66 身体、36 关节、69 形状。
这两个 island 不处理角色彼此接触；环境平台保持 authored 初始位置。
普通/高速初始高度以真实地面顶面为基准，仍使用前批的 100/300 cm 偏移与高速 -1000 cm/s 初速。
保持原生睡眠阈值、迭代次数及验收门槛：锚点 <10 cm、末秒约束残差 <0.1 rad、
每个角色自然入睡且保持至少一秒；睡后身体与 contact epoch 不变。

| 模式 | Hz | AnimMan 入睡帧 | Mannequin 入睡帧 | 最大锚点 cm | 末秒最大约束残差 rad |
| --- | --- | --- | --- | --- | --- |
| 普通 | 30 | 62 | 255 | 1.484637 | 0.050076 |
| 普通 | 60 | 243 | 397 | 0.957243 | 0.019023 |
| 普通 | 120 | 352 | 898 | 6.708990 | 0.030801 |
| 高速 | 30 | 196 | 70 | 4.505661 | 0.064478 |
| 高速 | 60 | 315 | 168 | 1.739565 | 0.012800 |
| 高速 | 120 | 319 | 447 | 0.914909 | 0.005972 |

六项退出 0，末秒线/角速度均为 0。普通 120 Hz 的瞬时锚点误差明显高于厚诊断地板，
虽通过现有粗略门槛，尚未定位差异来源，必须在后续真实角色/多帧视觉验收中继续检查，不能称视觉合格。
旧 pyramid 指标继续保留；例如高速 30 Hz 的旧指标 0.117029 rad 不被删除或用于替代新约束残差。
主场景与旧诊断地板几何不同，没有要求两者轨迹一致。

普通的正式报告是 `chain-scene-normal-30`、`chain-scene-first-60`、`chain-scene-normal-120`；
高速正式报告是 `chain-scene-high-verified-{30,60,120}`。
首轮命令的 PowerShell 条件参数未传入，`chain-scene-high-{30,60,120}` 实际记录 `high_drop=false`；
这些保留作排错产物，**不计入高速验收**。改成显式 `--high-drop` 后重新验证，并核对报告标志。

## 5. 回归与可复现入口

所有日志/JSON/TRX 位于 `artifacts/physics-scene-20260921/`：

- Core Release 固定 JIT：2700 通过（保留旧 P5A Golden/TraceSchema 过滤）。新增 7 项 kinematic 测试。
- Import Release 固定 JIT：2363 通过 / 1 个旧条件跳过。
- Godot 优化构建：0 警告 / 0 错误。
- 原生无接触 144 组 ×12 步：通过；最大位置 1.020660e-6 cm、角度 3.576279e-7 rad，与前批一致。
- 旧厚地板普通/高速三帧率六项：通过，睡眠帧和误差与前批一致，见 `chain-regression-*`。
- 旧接触探针三帧率：每次三场景、六几何、17 精度、五睡眠生命周期通过，总动量误差 0。

首轮 build-chain 的 sleep 类型名与 core-full 的 Math 命名空间编译错误已修正，日志保留。
最终以 `build-final.log`、`core-full-fixed.log/core.trx`、`import-full.log/import.trx` 为准。
本批未更改 UE exporter 或导出资产。

示例（report 必须是尚不存在的绝对路径）：

```powershell
& 'F:/下载/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path D:/GodotALS res://scenes/tests/physics_scene_contact_smoke.tscn -- --hz=60 --report=D:/GodotALS/artifacts/new-scene-60.json
& 'F:/下载/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path D:/GodotALS res://scenes/tests/physics_core_joint_replay.tscn -- --chains --drop --sleep --scene-world --high-drop --hz=60 --report=D:/GodotALS/artifacts/new-chain-scene-60.json
```

## 6. 后续缺口

仍需真实移动平台上的完整角色链、普通角色触发/退出、动画到物理初始化、正确世界基变换与骨架回写、
pelvis 驱动胶囊/相机地面跟随、初始速度限制、flail 驱动、Get-up 与 Pose Recovery。
本批的 host 仍是冻结无碰撞的诊断代理，不能直接作为普通角色世界姿态接口使用。
ALS 原版 `AlsCharacter_Actions.cpp` 的 StartRagdolling / RefreshRagdolling 是下一阶段的行为依据。

本实现还不含凹网格/缩放烘焙、动态场景刚体之间的跨 island 响应、运行时拓扑扩容、完整 Godot 节点 process-disable 语义、
环境物理材质组合、接触岛发现、局部休眠、CCD 或完整 Chaos 窄相/整链原生轨迹对照。
当前环境全部放入同一角色组，**不接触角色的已绑定平台只要运动，也会阻止该组休眠**；
这是明确的保守行为，不能当作已经完成接触图驱动的休眠。
场景轮询和 Godot 查询仍在 Main 分配，O(shape²) 遍历未优化，最终十分钟性能预算未做。
普通 demo 未切换，旧 Jolt 后端历史失败仍保留；Mantle、完整 Camera 等总目标也没有因此关闭。
