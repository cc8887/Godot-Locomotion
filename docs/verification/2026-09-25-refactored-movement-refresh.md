# 原 Parent 移动刷新与 UE 连续对照

工作目录 `.` / `main`，承接 `1252747`。保留用户工作区，普通 Demo 未切换。

## 实现

Core 新增原 Refactored 移动计算，Import 扩展候选 Parent，直接使用上一批全精度设置/曲线：

- InitializeGrounded 标记下次速度混合直接初始化；InitializeLean 清 Lean。
- RefreshGrounded：世界速度经 double quaternion 逆旋转，转 FVector3f 后 Normalize，再按 abs(X)+abs(Y)+abs(Z) 分配 F/B/L/R；使用原 InvExpApprox 半衰期插值。加速度通过世界 Accel·Velocity 选择 MaxAcceleration/Braking，局部三维 ClampMagnitude01 后取 XY。PendingUpdate 只令 Lean 直接赋值，不代替速度初始化标记。
- RefreshGroundedMovement：HipsLock clamp[-1,1]；float(velocityYaw-viewYaw) 后按原顺序 UnwindDegrees，70°半角/5°阈值依原顺序分类；VelocityDirection 或精确 Sprinting tag 强制 Forward；四 yaw 曲线采样。
- InitializeStandingMovement 只清 SprintTime 和 Pivot，不额外清 SprintAcceleration。
- RefreshStandingMovement：Speed/Scale 后采样 walk/run stride，原 Running/Sprinting 权重插值播放倍率，clamp[.0001,3]；Walking 的 WalkRun=0，其余1；SprintBlock clamp01；冲刺计时 .5 s 窗口及 PendingUpdate 直接置 .5。
- RefreshCrouchingMovement：独立 stride 与 speed/(animatedCrouchSpeed*stride)，clamp[.0001,2]。

Parent 同时接受两种 stance 的原回调表，按完整回调记录校验，避免站立/蹲伏相同 property index 冲突；仍拒绝本批未实现的其它功能。数值状态与 Pivot/Hips 同一个 Prepare/Commit/Cancel 生命周期，初始化、失败和重试均不提前发布。原只传回调表的调用者仍支持上一批 latch-only 路径；完整刷新要求本帧输入及同 catalog 设置。

新增 DirectionInput / PlayerInput / ForwardInput 映射，消费实际候选值；FeetCrossing 仍由上帧动画曲线输入。外层图负责调用顺序、相关性和上帧曲线快照，不在 Parent 中猜测哪些函数应该执行。

## 原生证据

新只读 `ExportMovementParentTrace` 在临时 GamePreview world 中实例化原 `AB_Als_C`，设置明确输入，调用原反射函数；未复制 UE 算法生成期望值。3 个频率 30/60/120 Hz、各5秒，合计1050帧，逐帧保存23字段：速度初始化/四向混合、Lean、方向、HipsLock、四 yaw、standing stride/WalkRun/rate/block/time/acceleration、crouching stride/rate、Pivot/Hips。

请求覆盖双精度三轴旋转、垂直速度、加速/刹车、零速度、三种scale、零dt、临界方向、超出一周的yaw、PendingUpdate、周期初始化、隐藏不刷新、冲刺进出与Pivot消费。资源哈希绑定原 catalog 和 movement settings。不是完整 AnimGraph、Character 运动输入、Notify 派发或姿态反馈的原生对照。

入库 `assets/config/refactored_movement_parent_native.json`，914102 bytes，SHA256 `A0E5AB7AA98E84CAD4AF33994E58447321CA53FA545EDE79DA6D0D4F7F6163EE`。冷启动及普通 Editor PID24956 实际 exit0，两个导出逐字节一致。

三频率的23字段最大绝对误差均 `1.1920929e-7`，预设预算 `2e-6` 未变。每帧取消、重试后所有结果严格相等，未提交状态保持不变。覆盖非零Lean 149/299/598帧、非零SprintAcceleration 17/36/176帧、PivotActive 74/170/372帧，四方向均覆盖。

## 测试和构建

- 新 Core11：八个原方向边界、精确tag、首次速度/零dt/PendingLean不同语义、零半衰期、近零与含Z速度、刹车阈值/三维归一化、scale顺序、播放速率下限及Sprint .5边界。`core-final.trx` 全过。
- 新 Import4：3Hz原生1050帧，以及后段刷新失败/初始化取消/缺少本帧输入/非法scale门禁。`parent-native.trx` 全过。
- 新90帧连接测试：实际 Parent→Details更新→延迟Direction缓存→共享源clock→Direction pose→Lean/PoseMoving 79骨，使用候选方向、速度权重、rate/stride、yaw、Sprint输入与Lean；多种rate与换髋确实发生。尚未将该场景 Details最终pose/119与UE整链比较。
- 最终 Import31通过：上述5项加旧Movement traversal、settings与stance callbacks，`parent-final.trx`。Godot Optimize 0警告/0错误，Python语法与diff检查通过。未跑全量/Godot场景/性能。
- 初 Core11中1失败：测试把 Normalize 后再除分量和误当代数精确 .5，原浮点运算结果为 .49999997。保留原生产运算，测试明确检查 float前驱值；失败 `core.trx` 保留。原生对照没有失败或预算变更。

UE按插件技能完整Editor-target构建，10 actions（含NetCore），四项目插件审计通过，fingerprint `FA68F4872E1F3257ABA5208517AA41A339139838F2C35AE3476C635B880EEB72`，BuildId由构建生成 `f7c75dee-59c9-476c-85d9-645c917dd66f`，未手改/复制DLL。日志前缀 `20260925T050351656Z-159a85bd039f404cbd5b6b4f9199560c`。冷启动0error/0warning；普通两条旧Condition failed等警告保留。DataValidation实际exit0/0error/3旧warning，无打包。

## 未完成与下一步

本批完成移动刷新函数及候选消费，未把外层 Standing65、MovementDetails cache66 与初始化/更新回调自动遍历接通。下一步补这层原图，接真实 Parent 输入与上帧曲线，再导出完整 Movement Details→119 及外层图的连续pose对照。当前 MovementLean 的 update-only提交限制仍待处理。

Notify 激活Pivot、Crouching其余机器、统一角色宿主、普通 Demo 切换、Ragdoll/Get-up/Pose Recovery、Mantle、完整相机和十分钟性能等总目标仍未完成。音频、道具物理、头颈排查继续暂缓。
