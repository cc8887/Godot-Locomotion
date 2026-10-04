# 普通运动子步决策归 Core

本批核对普通玩家/NPC 的生产路径后，将 `LyraSceneMovementService` 内的输入量、起跳门控、地面/空中积分选择、Root Motion 积分策略和物理反馈速度选择迁入纯 .NET `AlsCharacterMotion`。继续使用 ALS 人物、Pistol/Rifle、原 Core 地面/空中/floor/穿透恢复/蹲伏和既有动画执行链。

## 所有权与 ALS 复用

`AlsCharacterMotion.Advance` 接收实际 consumed direction、精确初始速度、grounded/crouching/jump/root 状态和物理 delta。Core 生成加速度与 analog，仅站立地面允许单次瞬时 jump，原 upward velocity 大于 jump 时保留其值，随后从该初始速度进入空中积分。

地面清除输入速度的垂直分量，按实际 stance 选择原 `AlsCharacterVelocity` 设置；空中继续复用原 `AlsCharacterFalling`，保留初始/结束速度的 midpoint 位移。Root Motion 在地面保持 planar 速度；空中关闭输入、摩擦与制动而保留重力，仍输出原 consumed acceleration 给动画观察。没有新增速度积分公式、帧时钟或 jump history。

原 ALS `AlsCharacterMovementRuntime.Integrate` 的输入量表达式提取为公共 `AlsCharacterVelocity.InputAmount`，ALS 和 Lyra 两条生产调用共用。完整 double 长度/除法、零 MaxAcceleration 返回零、先转换再 clamp 的原路径保持；ALS 继续按其原顺序在 float 转换前 clamp，Lyra 继续先 float 转换再 clamp。两个 owner 的这一既有区别未被统一为新算法。

`AlsCharacterMotion.ResolveVelocity` 保留完整反馈优先级：实际 native receipt 优先；无 Root、无碰撞且未 grounded 或积分 Z 非负时保留精确积分速度；其余用实际物理速度。Godot 仍按原位置读取发布速度并决定是否重置精确输入，将 `StartVelocity/Acceleration/Analog/Falling` 交给原物理 motor。原 actor/component 变换门禁、Godot 浮点/坐标与输入映射、实际碰撞及写入在宿主，动画取消重试不重新执行物理子步。

Core 不依赖 Godot、UE、JSON、Node、Collider 或 PhysicsServer。Lyra Shooter CDO、瞬时单跳资源限制以及站蹲速度配置仍由 Godot 资源适配层解析。普通玩家与 NPC 的 `LyraSceneMovementService` 默认接入新 Core。

## 验证

最终 v2 Core 142 项通过，新增 Motion 26，原 Sweep21/Crouch19/Floor32/Ground19/Air23/原生 Velocity/Falling fixture2 保持，0 失败/0 跳过。新增覆盖输入 magnitude/analog、站蹲实际限速/去垂直、jump impulse 后 gravity/midpoint、较高 upward 保留、空中/蹲伏不再次 jump、Root 的地面/空中横向保护及重力、native receipt 优先、collision/root/landing 反馈、无碰撞精确速度保留、重复请求无私有时钟、delta/输入/receipt 拒绝和共享 ALS 输入量。

首轮 v1 140 通过/2 失败：新站蹲限速断言误要求精确 20/40。原 ALS 标量 `next *= limit * (1 / sqrt(sizeSquared))` 在该 float delta 下实际为 `19.999999999999996/39.99999999999999`，已用独立 Python float32 delta 与 scalar 计算核对，改为精确字面量预期；没有改生产公式或放宽原验收阈值，失败日志/TRX 保留。实现初始 v1；最终测试、源码冻结、构建、运行与审计为 v2。

Debug 和 ExportRelease 构建各 0 警告/0 错误。两种构建各完成完整 Main＋Rig 7560 帧/7296 姿态、真实 Rig 物理 2520 帧/2484 姿态、普通 ALS 1700 帧和十角色 480 帧/4800 蒙皮发布；十角色与 Rig 物理完整报告同前批。

本机 ExportRelease 的 Optimize 属性为 true；另按项目要求记录 `dotnet build GodotALS.csproj --no-restore -c ExportRelease -p:Optimize=true`，0 警告/0 错误，六份程序集/PDB 字节哈希与已完成的实际优化运行一致。

每构建三频实际 Jolt 空中六角色共 5040 移动/retry，站蹲净空拒绝/释放、jump midpoint/顶点/落地/墙面切向通过；台阶五角色共 2100 移动/retry；floor 八角色共 1008 移动/retry。三个完整矩阵报告同迁移前及两种构建。普通地形三频每构建 6300 最终蒙皮发布，台阶/坡面/落差/站蹲/ADS/两武器通过，完整报告同前批及两种构建。

每构建实际空中 112 query、20 apex split/28 landing/6 多接触，0 mismatch。原地面 32 query 仍为 8 项差异、floor 144 query 仍为 3 项差异；四个诊断按原协议退出 1，保持完整报告与阈值（floor 仅除 evidence tag），没有将它们计作 UE/Jolt 世界等价通过。

另补两种构建各一次普通 Emote 逻辑输入回归：480 物理帧/最终姿态/动画 retry，实际触发 1 次 Root Motion 胶囊移动，Emote activation/end/movement clear 各 1，完整报告同前次 startup v5 及两种构建。这补充验证新子步的生产 Root 分支；不证明任意 Root Motion 轨迹或实体 E 键输入。

基础审计 `motion-step-core-v2-audit.json` 验证上述 38 个进程；最终 `artifacts/lyra-analysis/motion-step-core-v2-final-audit.json` 再核对两个 Emote 进程，共 36 成功 Godot 进程与 4 个保留旧差异的预期诊断，共 40。6 份本批源码和 4678 份保护基线（含 870 份 Lyra 资产 JSON）保持冻结哈希，前批证据保持，六轮 Optimize 后六份 Debug DLL/PDB 恢复。

没有 UE 启动/修改/重导、资产 JSON 格式化、提交推送、新 GPU、全量 managed、十分钟、性能或跨平台验收。实体键鼠未重试；此前 `GetCursorPos 0x80070005` 没有恢复证据，逻辑输入回归不能证明实体键鼠通过。

## 当前目标的剩余核对

本批关闭普通运动子步的上述通用决策与 ALS 输入量复用。后续继续核对实际 RootMovementMotor 的通用协调边界及 Godot 输入适配，汇总当前 locomotion/两武器/Layer 的生产验收，并完成实体键鼠；本批不关闭完整目标。RootMotor 的 legacy MoveAndSlide 路径仍有接触速度投影，普通生产 ground/air 接入原 Core 控制器，两者不能混为完整通用移植结论。

URO、额外 Provider、完整 UE 内部调度/字段还原、全物理轨迹逐位等价与原暂缓项继续后移。
