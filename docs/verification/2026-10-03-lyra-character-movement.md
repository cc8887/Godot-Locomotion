# Lyra 真实 CharacterMovement 参考与站蹲、Rig 初始化修正

后续已关闭下文的120 Hz舍入失败：骨遮罩ISPC VectorLerp与Aiming两姿态累加按原融合乘加顺序修正，Debug/实际Optimize三频三边界各5040参考帧及逐帧retry全部通过，原失败帧全骨骼阶段数据精确同，见 [ISPC混合修正](2026-10-03-lyra-ispc-mixing.md)。本篇保留前批失败与定位过程；普通CMC motor接入和整个迁移目标仍开放。

2026-10-03，在主目录推进。继续使用 ALS 模型、68 skin / 69 raw / 81 logical 和十四入口的共享 ItemAnimLayers 实例。**30/60 Hz 的指定真实 CMC 动画对照已通过；120 Hz 最终输出仍有数值差异，整个矩阵与移植目标未关闭。**Godot 的普通角色移动尚未改用本次原 CMC 配置和速度内核。

## 人物、资源与接口

人物与骨架方案保持 [ALS 人物、接口与完整换层验证](2026-10-02-lyra-whole-main-rebind.md)：现有 ALS 模型和蒙皮不变，Lyra Manny 动画离线重定向到目标骨架；新增父为 hand_r 的 weapon_r，以及武器空间虚拟骨。ALS 缺少的 spine_04/05 映射到目标脊柱并去重；BlendMask/Profile 按目标骨名适配，FootPlant 的长度和参考姿态按 ALS compact reference 重算。完整 logical81 求值后统一发布原 skin68。动作交换之外仍需保留 Distance、Marker、Notify、additive 基底、曲线 flags、typed attributes 和 RootMotion 元数据。

Animation Layer Interface 由不可变的 typed 入口合同承载，Main 在原调用位置执行当前 Provider 子图。原十四入口同属 ItemAnimLayers，一个角色共享一个组实例；实例拥有播放器、状态机、缓存和回调历史。Main 持有宏状态、共同 Sync、Montage、惯性与最终 Rig；更换装备只替换组实例，同类绑定保留实例。InputPose、属性参数、姿态/曲线/属性/根运动输出，以及提交/取消身份均保留。

Layer Group 决定实例共享，Sync Group 决定源选主和时钟同步，骨遮罩决定姿态权重。普通 Blueprint Interface 的角色查询与 Notify 回调分别使用 typed 角色服务和事件消费者。Godot AnimationTree 可作为展示和调试入口，当前精确运行链使用已有 C# 宿主与姿态算子。按用户要求忽略5.8/5.9差异。通用多 Group、无组独立实例、self-layer、Unlink/部分覆盖、Shotgun/Feminine 完整图和近景握持仍待。

## 原生移动参考

可选外部探针实际生成 `/ShooterCore/Game/B_Hero_ShooterMannequin.B_Hero_ShooterMannequin_C`，使用其真实 `/Script/LyraGame.LyraCharacterMovementComponent`、胶囊和 PlayerController。隔离世界的控制器显式设为本地；输入、FaceRotation、Crouch/Uncrouch、Jump 和原 TickComponent 驱动移动，再运行原 Main/Linked 根图、共同 Sync 与最终 ControlRig。没有修改 GASP58 的 C++、配置或资产。

每 Provider 八秒，包含起步、反向、松开刹车、45°转向与 ADS、墙体阻挡、跳跃、蹲伏及恢复。墙体为 x=650 cm、半厚25 cm，角色半径35 cm，碰撞中心上限约590 cm。夹具的地面和墙体明确阻挡原 Traversable 查询通道，避免 BlockAll 项目默认响应使脚部查询全部未命中。

实际 Blueprint 覆盖的配置如下，不能用 C++ 构造默认值代替：

| 设置 | 原角色值 |
| --- | ---: |
| 胶囊半径 / 站立半高 / 蹲伏半高 | 35 / 90 / 65 cm |
| 站立 / 蹲伏最大速度 | 600 / 300 cm/s |
| 最大加速度 / 最小模拟输入速度 | 1200 cm/s² / 200 cm/s |
| 地面摩擦 / 独立刹车摩擦 / 系数 | 8 / 3 / 1 |
| 地面减速度 | 1400 cm/s² |
| 刹车子步 | 0.03030303120613098 s |
| JumpZVelocity / GravityScale | 500 cm/s / 1 |
| AirControl / BoostMultiplier / Threshold | 0.4000000059604645 / 4 / 50 cm/s |

新增 `AlsCharacterVelocity` 复现原 CalcVelocity 的非流体、输入驱动路径。原250组内核输入覆盖站/蹲、五种 delta、零输入/反向/模拟输入和超速刹车；一个 managed 测试遍历全部250组，原门槛1e-10 cm/s通过。`assets/generated/lyra_als/character_motor_v1.json` 和 portable test fixture 保存配置及原样本；前者被 Git 忽略，**尚未由普通角色加载，不代表完整 motor、落地、台阶、空中控制或碰撞等价。**

## 修正与仍未通过的边界

旧 Idle 规则矩阵没有提供 Main.CrouchStateChange，先前“IdleStance 两条显式规则恒 false”的结论错误。补六布尔量 × 三 Provider 的192组原 handler 调用，确认两条规则均读取 CrouchStateChange。生产 Idle 输入已接真实 Main 观察；嵌套 Stance 输出使用独立缓冲，保留原 Standing/Transition 叶姿态，并分别混合曲线、整数属性和 RootMotion。旧说明已加修订，旧 JSON 字节不变。

完整 Main 的首次 Evaluate 才执行 VM 初始化，其 OnInitialized 回调会在本帧 Update 之后使参考姿态绑定失效。下一 visited Update 再绑定参考并请求 Construction。原生执行事件确认第0、1帧都存在 Construction，第2帧开始普通 Solve；在此时序下，前两次 Solve 的插值 delta 为零。独立 Rig 夹具已提前初始化，只有首次 Solve 为零，不能统一套用同一初始化阶段。

生产完整 Main 现显式选择 Evaluate 初始化阶段，pending 回调、参考绑定和 Construction 随角色候选提交/取消；update-only 保留待执行状态。独立 Rig 的原生命周期不变。修正消除了60 Hz 首帧骨盆约0.057646 cm的差异，没有修改弹簧公式或阈值。

120 Hz 仍在 Pistol 第451帧 bone50 失败：位置差约2.56e-14 cm，归一化 quaternion 差1.6216930576451093e-10，超过原1e-10门槛。Debug 与实际 Optimize 同样失败。惯性前和 Rig 前全部通过，但通过这些边界的容差不能证明输入逐位相等。

隔离诊断将 Godot 的两次实际 IK 输入交给原 UE SolveBasicTwoBoneIK，A/B/C 的位置、旋转、缩放逐项完全相同。该帧 Cycle Layer 的所查骨也精确相同；Main_InertiaInput 的 pelvis 位置相差2.84e-14 cm、bone50位置相差7.11e-15 cm。仅此帧换用原 Rig 前姿态的反事实通过该帧最终比较，随后故意仍保留该姿态导致下一帧查询输入失配，不能当成连续验收。**已定位到上游输入舍入的影响，尚未证明具体算子根因。**临时姿态替换/观察入口和诊断程序集全部撤回，三源码与六 DLL/PDB 的恢复 SHA 已核对；原 solver 和门槛保持。

## 验证范围与证据

完整参考为三 Provider × 三 Hz，各八秒，共5040帧。每种构建比较三个边界并逐帧取消重试。原生物理观察及查询结果被重放，Godot 独立计算状态、时钟、权重、姿态和查询起止点；查询起止误差须小于1e-8 cm、通道和半径须相同，才能消费原物理命中结果。没有使用原动画姿态、时钟或状态作为正常运行输入。GroundDistance 是原 Lyra 的 CMC.GetGroundInfo 物理距离，读自其实际 NativeUpdate 输出。

| Hz | 参考帧 | 惯性前 / Rig前 | 最终 Rig |
| --- | ---: | --- | --- |
| 30 | 720 | 两构建通过 | 两构建通过 |
| 60 | 1440 | 两构建通过 | 两构建通过 |
| 120 | 2880 | 两构建通过 | 两构建同一帧失败 |

墙体接触每 Provider 为45/93/188帧，最大x分别589.9563/589.983/589.9990 cm；空中29/60/121帧、蹲伏30/60/120帧；每 Provider 贴地 Rig 命中约209–210/421–423/846–850帧。

两构建各七项 Main/Aiming/Lean/惯性/通知/普通十角色回归，以及60 Hz 原动作中换层三个边界通过。独立 Idle 原26460帧、ALS target Rig 完整输出、三Hz2520帧实际 Jolt 接触/取消/18次初始化也在两构建通过。普通十角色与 Rig 场景的完整报告在两构建精确相同。构建0警告0错误，所有成功 Godot 进程无 ERROR/WARNING，Optimize 六文件恢复通过；120 Hz 的两个失败保留。

三个最终 UE 采集进程退出0、0资产保存。866个旧生成 JSON、709原资产包、9项目/配置文件和3个 LyraCharacter 源文件逐文件 SHA 保持。60 Hz 新事件探针与此前地面探针的物理观察、字段、全部输出、源时钟和查询逐帧相同，证明新增事件观察没有改变执行。审计脚本 `tools/verify_lyra_character_movement.py` 将通过项与已知失败分别核对，结果保存为 `artifacts/lyra-analysis/character-movement-integrity.json`；`auditPassed=true / comparisonPassed=false / completeAcceptance=false / goalComplete=false`。

首次无本地控制器导致静止、墙体过窄未碰到、原查询通道忽略地面、IdleStance 缺字段、编译错误和最初 Rig 时序失败均保留。`cmc60-collision-replay` 曾在编译失败后误用旧 DLL，明确排除出验收；修复后的独立重放另有证据。

## 后续实施

先定位 Main 上游 additive/混合的具体舍入来源并关闭120 Hz最终门槛，再把已捕获配置和速度内核接到玩家/NPC共用物理服务。保持 LyraRootMovementMotor 单物理帧只移动一次、动画重试复用 receipt 的协议，分别实现空中控制、跳跃、蹲伏净空和 capsule 尺寸，补同输入的实际 UE/Jolt 运动轨迹。Jolt solver 与 CMC sweeps/substeps 需要按接触、轨迹、落地和动画行为独立验收，不能用重放动画对照替代。

整个目标仍 active；复杂地形、近景武器握持、通用 Layer 绑定、多平台数学/独立导出、性能及全部原暂缓项保持开放。本批没有新的 GPU 或人工全矩阵验收。
