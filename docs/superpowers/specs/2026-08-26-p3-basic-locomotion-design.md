# P3 基础 Locomotion 设计

**状态：** 已确认，待实施

**日期：** 2026-08-26

**Godot 工程：** `D:\GodotALS`

**UE 资产源工程：** `D:\AdvancedLocomotionSystemV`

**行为参考：** `ALS-Refactored` main，commit
`b754d6f0f2bb03741d301f8fb88077ebfe561e17`

**源引擎：** Unreal Engine 5.9.0

**目标引擎：** Godot 4.7.2 .NET

## 一、阶段目标

P3 在 P0/P1 的确定性多线程管线和 P2 的完整 `AlsAnimationSet` 上实现第一套可玩的真实
ALS locomotion。阶段交付同时包含固定输入回放和第三人称键鼠演示，覆盖 Stand、Crouch、
Walk、Run、Sprint、Jump、Fall、Land、三种 rotation mode、方向相关速度、locomotion
BlendSpace、stride、play rate 和 lean。

当原始 ALS V4 资产行为与 `ALS-Refactored` C++ 不一致时，以固定 commit 的 C++ 行为为准，
再适配现有 ALS V4 Mannequin 和动画资产。P3 不移植网络预测、复制或移动同步代码。

## 二、范围

### 2.1 纳入范围

- 主线程 `CharacterBody3D` motor；
- 输入命令、requested/actual gait、stance 和 rotation mode；
- Grounded/InAir 状态与 Jump/Fall/Land 转换；
- Standing/Crouching 的 Walk/Run/Sprint 速度语义；
- 前、侧、后方向相关的最大速度；
- Velocity Direction、Looking Direction 和 Aiming 身体朝向；
- locomotion BlendSpace、循环相位、stride、play rate 和 lean；
- 固定 UE 行为 trace、Godot 固定输入 replay 和 cross-engine golden test；
- 单角色交互演示；
- 1/10 角色真实动画串行/并行等价门禁。

### 2.2 明确排除

- 网络预测、移动复制、回滚和客户端校正；
- AimOffset、上半身分层、Turn/Rotate in Place；
- Foot IK、Foot Lock、pelvis correction；
- Overlay gameplay；
- 通用 Notify/Notify State、Sync Runtime 和 ActionPlayer；
- Mantle、Roll、Root Motion；
- Ragdoll、Get-up 和 Pose Recovery；
- 完整 ALS Camera 行为和最终十分钟性能预算。

P3 可以使用最小第三人称 orbit camera 支持演示，但该相机不作为后续 ALS Camera 的行为实现。

## 三、参考版本与可重复性

行为参考固定为：

```text
repository: https://github.com/Sixze/ALS-Refactored.git
commit: b754d6f0f2bb03741d301f8fb88077ebfe561e17
observed date: 2026-08-26
```

第三方仓库放在 Godot 仓库之外，不作为 submodule，也不复制进 Git 历史。Godot 仓库只提交：

- 固定 URL 与完整 commit SHA；
- UE 5.9 兼容补丁的清单与哈希；
- trace 生成命令和 schema；
- 生成的最小 golden fixture。

如固定 commit 需要适配 UE 5.9，只允许修改构建 API、include 或已弃用接口。任何会改变 locomotion
状态、速度、旋转或动画参数的补丁都禁止用于 golden 生成。

## 四、纵向切片

### 4.1 P3A：确定性运动与状态

P3A 先完成纯数据规则、主线程 motor 和 golden replay：

- `AlsLocomotionSettings`；
- `AlsLocomotionCommandResolver`；
- `AlsCharacterMotor`；
- 生产版 `AlsLocomotionModel`；
- UE trace schema/export；
- 固定输入 replay 和状态 golden tests。

P3A 不以“角色能移动”为完成条件。状态、速度规则、旋转规则、同帧顺序和错误合同必须先通过
自动化门禁。

### 4.2 P3B：真实动画与交互演示

P3B 在 P3A 的同一接口上增加：

- `AlsLocomotionAnimationProfile`；
- `AlsAnimationLibraryBuilder`；
- `AlsLocomotionAnimationController`；
- Grounded/InAir 动画图；
- player/replay 输入 adapter；
- 单角色交互场景；
- 10 角色真实动画门禁。

交互角色与 replay 角色只允许输入来源不同，motor、状态模型、动画模型和线程路径必须相同。

## 五、模块边界

| 模块 | 职责 | 线程/依赖 |
| --- | --- | --- |
| `AlsLocomotionSettings` | 冻结速度、加速度、旋转、跳跃和动画参数 | 纯 C#、只读 |
| `AlsLocomotionCommandResolver` | 将原始命令解析为允许的 gait、stance、rotation mode 和移动约束 | 纯 C#、Main |
| `AlsCharacterMotor` | `CharacterBody3D` 移动、碰撞、重力、跳跃、蹲伏和 floor 采样 | Main Order 0 |
| `AlsLocomotionModel` | 根据本帧实际物理结果计算状态和动画参数 | 纯 C#、Worker |
| `AlsLocomotionAnimationProfile` | stable ID 到 P3 动画用途的冻结映射 | 初始化期只读 |
| `AlsAnimationLibraryBuilder` | 将多个 P2 动画绑定到一个 Mannequin/Library | Main 初始化期 |
| `AlsLocomotionAnimationController` | 写 AnimationTree 参数、推进图并输出姿态 | Worker Order 1 |
| `AlsPlayerInputAdapter` | 键鼠到统一命令合同 | Main |
| `AlsReplayInputAdapter` | fixture 到统一命令合同 | Main |
| `AlsP3CommitStage` | 校验结果、诊断、摘要和角色生命周期 | Main Order 2 |

纯 C# 模块不得引用 Godot `Node`、`Resource`、场景对象或动态字符串查找。Godot adapter 只负责
世界读取、物理调用、资源初始化和姿态提交。

## 六、帧数据与顺序

### 6.1 输入命令

统一命令至少包含：

```text
MovementAxes
ViewYaw / AimYaw
RequestedGait
RequestedStance
RequestedRotationMode
JumpPressed
```

输入 adapter 将命令写入预分配结构，不直接操作 motor 或动画节点。

### 6.2 同帧执行顺序

每个 physics frame 固定为：

```text
Main Order 0
  read player/replay command
  resolve command constraints
  run CharacterBody motor and collision
  read actual transform/velocity/acceleration/floor
  publish immutable AlsFrameInput

Worker Order 1
  evaluate AlsLocomotionModel
  update owned AnimationTree
  manually advance animation
  publish AlsFrameResult

Main Order 2
  validate identity/generation
  consume diagnostics and lifecycle requests
  append deterministic digest
```

普通 locomotion 位移不经过 worker 提议。motor 在发布快照前完成移动，worker 因而消费同一物理帧
的实际速度和地面状态，不人为增加一帧延迟。

### 6.3 合同扩展

`AlsRuntimeState` 增加或明确保存：

```text
ActualGait
PreviousLocomotionState
GroundedEntrySpeed
SmoothedLocalVelocity
SmoothedLocalAcceleration
SmoothedLean
AnimationPhase
LandingRecoveryTime
```

`AlsFrameResult` 增加：

```text
ActualGait / Stance / RotationMode
AnimationState
BlendCoordinates
Stride
PlayRate
Lean
AnimationPhase
TargetYaw
```

这些字段都是定宽值类型，不包含 `Node`、`Resource`、`String` 或动态集合。

## 七、运动与状态语义

### 7.1 输入空间

二维移动输入先按相机水平 yaw 转换到世界方向，忽略相机 pitch/roll。零输入不做 normalize；非零
输入先限制长度到 1，再转换方向。

### 7.2 Requested 与 Actual Gait

`RequestedGait` 表示允许的最高 gait，不直接等于动画 gait。`ActualGait` 根据水平实际速度判定：

- Standing 允许 Walking、Running、Sprinting；
- Crouching 最大为 Running，并使用独立蹲伏速度表；
- Sprint 还要求有效移动输入与 rotation mode 允许冲刺；
- `ActualGait` 精确采用固定 C++ 的 `MaxWalkSpeed + 0.1 m/s`、`MaxRunSpeed + 0.1 m/s`
  判定；P3 不额外引入依赖 previous gait 的迟滞状态；
- 临界值、单位换算和比较运算符由 golden trace 锁定，不能用模糊 epsilon 改变转换帧。

阈值、滞回和允许条件从固定 `ALS-Refactored` commit 的 locomotion settings/character 逻辑提取，
以版本化数值 fixture 进入 `AlsLocomotionSettings`，禁止运行时读取第三方源码或 UE 资产。

### 7.3 方向相关速度

Standing/Crouching 的每个 gait 保存 forward、sideways 和 backward 三个采样参考速度。motor 根据
相机或角色局部移动角度连续插值，避免只使用单一标量最大速度。固定 C++ 配置只有 forward/backward
端点时，sideways 值必须由同一角度曲线在 90 度处确定性采样得到，不能人为增加第三套行为参数。

实际速度超过当前 requested gait 限制时，由加速度/减速度逐步收敛，不瞬间截断已有速度。无输入时
使用 braking/deceleration；反向输入使用独立转向响应。

### 7.4 Stance 与蹲伏碰撞体

Crouching 在 Main Order 0 修改 capsule 高度并保持脚底位置稳定。请求 Standing 时先执行向上空间
检查；空间不足则保持 Crouching，并在结果中报告实际 stance。stance 动画只能使用实际 stance，
不能使用尚未被物理接受的请求值。

### 7.5 Rotation Mode

- `VelocityDirection`：有可靠水平速度时朝实际移动方向旋转；低速时保持稳定 yaw。
- `LookingDirection`：移动时结合 view yaw 和局部移动偏角得到目标 yaw；停止时不在 P3 执行 Turn in Place。
- `Aiming`：身体朝 aim yaw 旋转；P3 不叠加 AimOffset，上半身保持基础 locomotion 姿态。

三种模式都使用固定 commit 的旋转速度、插值和角度归一化规则。P3 不引入第一人称旋转分支。

### 7.6 Jump、Fall 与 Land

Jump 只在 `Grounded + MotorDriven` 且未被蹲伏空间约束阻止时接受。主线程设置垂直速度并在移动后
发布实际 floor 状态。物理 locomotion 状态只描述角色是否接地：

```text
Grounded --accepted jump / lost floor--> InAir
InAir --floor acquired--> Grounded
```

动画状态在这两个物理状态上进一步细分：

```text
Grounded --accepted jump--> JumpStart --descending--> FallLoop
Grounded --lost floor--> FallLoop
FallLoop --floor acquired--> LandRecovery --recovery complete--> Grounded
```

`LandRecovery` 开始时物理状态已经是 `Grounded`。它是 locomotion 动画内部的一次性恢复子状态，
不使用通用 ActionPlayer。一次落地只产生一次转换，其恢复时长由 profile 固定并在 worker 状态中推进。

## 八、动画运行时

### 8.1 Profile 与 stable ID

`AlsLocomotionAnimationProfile` 明确映射：

- standing locomotion BlendSpace；
- crouching locomotion BlendSpace；
- Jump start；
- Fall loop；
- Land；
- lean additive；
- profile 使用的全部 animation sample。

profile 在导入/编译阶段由 P2 `AlsAnimationSet` 的 stable ID 生成。缺少、重复、骨架不一致或 additive
合同不一致都使编译失败。运行时禁止按 asset name、object path 或磁盘文件名发现动画。

### 8.2 单骨架动画库

`AlsAnimationLibraryBuilder` 实例化一次目标 Mannequin，将所有 P3 clip 的轨道重写到同一个
`Skeleton3D`，再构建单一 `AnimationLibrary`。每个 clip 继续校验：

- 固定 `Unreal Take` 源动画名；
- 时长容差；
- `Skeleton3D:<bone>` 轨道合同；
- 物理骨骼存在性；
- 支持的 position/rotation/scale track 类型。

所有 `PackedScene`、`Animation` 和图节点都在角色加入 worker process group 前创建。

### 8.3 动画图

动画图仍以物理状态组织为两个主分支；`LandRecovery` 是重新接地后的 Grounded 过渡子状态，
不是第三种物理状态：

```text
Grounded
  Land recovery transition
  Standing locomotion BlendSpace
  Crouching locomotion BlendSpace

InAir
  Jump start
  Fall loop
```

BlendSpace 参数范围和 sample 坐标直接来自编译的 `AlsBlendDefinition`。每个 sample 使用编译后的
integer animation ID 和 rate scale。图构建后缓存所有参数路径，steady state 不进行字符串查找。

### 8.4 Stride、Play Rate、Lean 与 Phase

- `Stride` 使用实际水平速度除以当前方向/gait 的参考速度，再做 profile 定义的限幅；
- `PlayRate` 由 stride、sample rate scale 和平滑规则计算，禁止通过无限加速掩盖错误动画选择；
- `Lean` 根据角色局部加速度与 yaw angular velocity 计算，限幅后驱动 additive 节点；
- `AnimationPhase` 由 worker 独占并以 60 Hz 推进；
- gait 或方向改变时保持 locomotion 循环相位连续；
- Grounded/InAir 主状态切换可以重置对应一次性动画，但不能重置无关 locomotion phase。

## 九、交互演示

### 9.1 控制

| 输入 | 行为 |
| --- | --- |
| `WASD` | 相机相对移动 |
| `Alt` 按住 | RequestedGait = Walking |
| `Shift` 按住 | RequestedGait = Sprinting |
| 无 gait 修饰键 | RequestedGait = Running |
| `Ctrl` | 切换 Standing/Crouching |
| `Space` | Jump request |
| `V` | 切换 Velocity/Looking Direction |
| 鼠标右键按住 | 临时 Aiming，释放后恢复前一 rotation mode |
| 鼠标移动 | orbit camera |
| `Esc` | 释放/重新捕获鼠标 |

### 9.2 场景

`p3_locomotion_demo.tscn` 使用真实 Mannequin、简单工作场地、方向光和最小 SpringArm/orbit camera。
场地覆盖平地、可跳过的低障碍和用于验证 motor 的缓坡。楼梯、移动平台和 Foot IK 视觉验收属于 P4。

HUD 只显示 state、gait、stance、rotation mode、实际速度、动画参数和性能计数，不显示操作教程。
交互场景不是自动门禁的输入源。

## 十、Golden Trace 与 Replay

### 10.1 UE 参考 trace

固定 commit 的 ALS plugin 放入 UE 参考环境，使用 60 Hz 固定步长运行实际 character/animation 逻辑。
trace 至少包含五条序列：

1. idle -> walk -> run -> sprint -> idle；
2. forward/sideways/backward 和方向连续变化；
3. crouch、受阻 uncrouch、成功 uncrouch；
4. Velocity/Looking/Aiming rotation mode；
5. jump -> ascend -> fall -> land -> grounded。

每帧记录：命令、实际速度、实际加速度、floor、view/aim yaw、state、actual gait、actual stance、
rotation mode、target yaw、stride、play rate、lean 和 phase。

### 10.2 两类 replay

Cross-engine behavior replay 直接将 UE trace 中记录的实际速度、加速度和 floor sample 送入纯
`AlsLocomotionModel`，用于比较 C++ 与 C# 的状态/参数语义。它不要求 Godot 与 UE 的物理引擎产生
相同轨迹。

Godot integration replay 将统一命令送入真实 `AlsCharacterMotor`，验证 Godot 内固定输入可重复、
同帧无额外延迟以及串行/并行一致。物理位置只与同一 Godot fixture 的基线比较。

### 10.3 容差

- enum、bool、transition frame、animation ID：完全一致；
- 米和米/秒字段：绝对误差 `<= 0.001`；
- 归一化 blend/stride/play-rate/lean：绝对误差 `<= 0.0001`；
- yaw：最短角误差 `<= 0.1` 度；
- Godot 单线程/多线程摘要：完全一致。

## 十一、错误处理

- settings/profile/stable ID/动画/BlendSpace sample/图参数缺失时启动失败；
- 禁止使用 idle、walk 或任意其他动画作为静默替代；
- Debug/headless 中 worker 异常、非法 Node 访问、stale、missing、generation mismatch 立即失败；
- 交互 Release 中 worker 失败时冻结最后有效视觉姿势，motor 保持安全 Grounded/InAir 逻辑并输出
  结构化诊断；
- CharacterBody 物理错误不允许由 worker 修正；
- 初始化失败不得让角色以部分动画图进入场景；
- UE 参考 SHA 或 compatibility patch hash 不匹配时拒绝重新生成 golden fixture。

## 十二、测试与门禁

### 12.1 纯 C# 测试

至少覆盖：

- 输入归一化和相机相对方向；
- requested/actual gait 与滞回；
- crouch 对 sprint 的限制；
- forward/sideways/backward 速度插值；
- 三种 rotation mode 和角度 wrap；
- Grounded/InAir/Jump/Fall/Land 转换；
- stride、play rate、lean 和 phase 连续性；
- 相同输入/状态的确定性；
- 热身后零托管分配。

### 12.2 Godot Headless 集成

至少覆盖：

- CharacterBody 平地移动、减速、跳跃、落地、蹲伏和受阻 uncrouch；
- input -> motor -> frame snapshot -> animation 的同帧顺序；
- profile 完整性和单骨架多动画绑定；
- Grounded/InAir 真实动画状态切换；
- 实际姿态在运动过程中发生变化；
- 角色替换和 generation 复用；
- 无线程访问错误和退出期资源错误。

### 12.3 P3 运行矩阵

P3 headless gate 使用 120 帧 warmup 和 600 帧 measurement：

| 角色数 | 模式 | 质量 | 要求 |
| ---: | --- | --- | --- |
| 1 | single | Tier 0 | golden/replay/digest 通过 |
| 1 | parallel | Tier 0 | 与 single 一致，worker 离开主线程 |
| 10 | single | Tier 0 | 60 Hz、无 missing/stale |
| 10 | parallel | Tier 0 | 与 single 一致，10 workers 离开主线程 |

warmup 后 Gather、worker model、AnimationTree/Skeleton、exchange 和 commit 的托管分配都必须为
`0 B`。P3 记录阶段墙钟 p95/p99，但最终 i7-10700 十分钟预算仍在 P7 验收。

### 12.4 回归

P3 完整门禁必须同时运行：

- P3 pure/golden/motor/real-animation gate；
- `verify-p2b.ps1`；
- `verify-p1.ps1`；
- `verify-p0.ps1`；
- `dotnet test GodotALS.sln -c Release --no-restore`；
- 仓库边界与 `git diff --check`。

## 十三、完成定义

P3 只有同时满足以下条件才完成：

1. 固定 `ALS-Refactored` commit 和兼容补丁可追溯；
2. UE/Godot behavior golden tests 通过；
3. Stand/Crouch/Walk/Run/Sprint/Jump/Fall/Land 可在真实 Mannequin 上运行；
4. 三种 rotation mode 的身体朝向符合固定 C++ 参考；
5. locomotion BlendSpace、stride、play rate、lean 和 phase 连续；
6. 单角色键鼠演示可操作；
7. 1/10 角色 single/parallel 摘要一致；
8. 10 个角色保持 60 Hz Tier 0 真实动画；
9. 输入、实际物理结果和动画状态之间没有人为增加一帧延迟；
10. warmup 后关键路径零托管分配；
11. 无 missing、stale、generation mismatch 或线程所有权错误；
12. P2B/P1/P0/Release 回归通过；
13. 没有修改 Godot Core，没有未经 profiler 证据引入 GDExtension。

P3 完成后，P4 才开始 AimOffset、分层姿态、Turn/Rotate in Place、Foot IK、Foot Lock、pelvis、
楼梯和移动平台视觉处理。P5A 实现事件与动作基础，P5B 基于 P4/P5A 实现完整 Overlay gameplay
和道具生命周期，P5C 实现 Mantle、Roll 和 Root Motion。P6 实现 Ragdoll、Get-up、Pose Recovery
和完整 ALS Camera，P7 执行最终十分钟性能门禁。这些能力只排除在 P3 之外，不从项目目标中删除。

## 十四、参考

- `docs/superpowers/specs/2026-08-25-godot-als-port-design.md`
- `docs/architecture/p2b-godot-import-closure.md`
- https://github.com/Sixze/ALS-Refactored/commit/b754d6f0f2bb03741d301f8fb88077ebfe561e17
- https://docs.godotengine.org/en/4.7/classes/class_characterbody3d.html
- https://docs.godotengine.org/en/4.7/classes/class_animationtree.html
