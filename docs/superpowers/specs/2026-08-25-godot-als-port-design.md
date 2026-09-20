# Godot ALS 多线程移植详细设计方案

**状态：** 已批准设计

**日期：** 2026-08-25

**目标引擎：** Godot 4.7.2 .NET

**UE 资产源工程：** `../AdvancedLocomotionSystemV`

**参考实现：** `Sixze/ALS-Refactored`，固定使用 commit `b754d6f0f2bb03741d301f8fb88077ebfe561e17`

## 一、项目目标

本项目要建立一套独立、可运行、可测试、可持续维护的 Godot ALS 示例工程。它不是简单地让一组 ALS 动画在 Godot 中播放，而是要在 Godot 4.7.2 .NET 中重新建立 ALS 的运行时语义，包括角色移动、动画状态、姿态修正、脚部 IK、动作、Root Motion、布娃娃恢复和摄像机等完整链路。

项目使用现有 Unreal Engine 工程中的 ALS V4 骨骼、模型和动画资产。Unreal Editor 只作为离线资产导出工具和行为对照环境，Godot 运行时不依赖 Unreal Engine，也不直接读取 `.uasset`。

最终目标包括：

- Stand、Crouch、Walk、Run、Sprint、Jump、Fall、Land；
- Velocity Direction、Looking Direction、Aiming 三种主要旋转模式；
- Aim Offset、Turn in Place、Rotate in Place、Dynamic Transition；
- Foot IK、Foot Lock、Pelvis Correction；
- Mantle、Roll 和碰撞安全的 Root Motion；
- Ragdoll、face-up/face-down 判断、Pose Recovery 和 Get-up；
- ALS 第三人称和第一人称摄像机、左右肩切换、lag、FOV 和遮挡；
- UE 资产通过 FBX 与版本化 sidecar manifest 自动进入 Godot 资产管线；
- 单线程和多线程模式具有可验证的等价行为；
- 在 Intel i7-10700 上，至少 10 个全质量角色以 60 Hz physics 稳定运行于 60 FPS。

里程碑 B 不包含网络预测和任意异骨架的运行时重定向。完整武器/持物 Overlay 框架按 P5B 实施，依赖 P4 分层姿态和 P5A 事件/动作基础。

## 二、仓库与工程边界

### 2.1 独立仓库

Godot 项目放在独立目录 `.`，使用独立 Git 仓库管理，默认分支为 `main`。

现有 UE 工程 `../AdvancedLocomotionSystemV` 不并入这个仓库。它承担以下职责：

- 提供原始 ALS Skeletal Mesh、Skeleton、Animation Sequence 和相关配置；
- 运行 UE 侧导出工具；
- 生成行为和资产 golden data；
- 必要时用于人工对照 ALS 原始效果。

Godot 仓库承担以下职责：

- Godot 运行时和演示场景；
- 纯 C# ALS 逻辑；
- 多线程动画分发框架；
- Godot 资产导入插件；
- UE 导出工具源码；
- manifest schema；
- 自动化测试、性能测试和诊断工具；
- 架构文档、上游来源记录和基准报告。

### 2.2 计划目录结构

```text
GodotALS/
  project.godot
  GodotALS.csproj
  GodotALS.sln

  addons/
    als_importer/                 Godot EditorPlugin 和导入入口

  assets/
    fixtures/                     可再分发的小型测试资产
    generated/                    本地生成的 UE 导出资产，不进入 Git

  scenes/
    demo/                         最终可操作 ALS 示例
    benchmark/                    1/10/16/32 角色性能场景
    tests/                        Godot 运行时集成测试场景

  src/
    Als.Core/                     不依赖 Godot Node 的纯 C# 逻辑
    Als.Godot/                    SceneTree、动画、物理和摄像机适配层
    Als.Import/                   manifest 验证与运行时数据编译
    Als.Diagnostics/              计时、计数器、追踪和报告

  tests/
    Als.Core.Tests/               纯逻辑单元测试
    Als.Import.Tests/             schema 与导入测试
    Als.Integration.Tests/        Godot headless 集成测试入口

  tools/
    unreal/                       UE 5.9 导出工具源码
    schemas/                      版本化 JSON Schema

  vendor/
    references/                   固定的上游版本和来源元数据

  docs/
    architecture/                 模块和数据合同
    provenance/                   各功能的 UE/ALS-Refactored 来源记录
    benchmarks/                   可复现性能报告
    superpowers/                  设计与实施计划
```

`.godot`、C# `bin/obj`、IDE 状态、生成的 UE 二进制资产、Godot 导入缓存、性能捕获文件和本机引擎路径必须进入 `.gitignore`。只有具备明确再分发权限且足够小的测试 fixture 才能提交到 Git。

## 三、实施路线选择

### 3.1 候选方案

方案一是先导出全部 UE 资产，再开始 Godot 运行时。这种方法可以较早看到真实资产，但 FBX、骨架、曲线和元数据问题会与多线程架构问题混在一起，失败时难以定位根因。

方案二是先做一个单角色完整功能纵向切片，再补多线程。这种方法能较快得到可视效果，但会让动画逻辑提前依赖 SceneTree 和具体 Node，很可能在后续并行化时重新划分线程所有权和数据结构。

方案三是先完成确定性的底层数据合同、多线程分发、合成测试资产和性能 Harness，再接入 UE 导出资产，最后按模块实现 ALS 功能。

本项目采用方案三。原因是线程所有权、跨线程数据布局和结果提交顺序一旦错误，后续功能越多，修改代价越高。反过来，资产格式和某个动画模块可以在稳定接口后逐步替换。

### 3.2 总体顺序

```text
独立仓库和测试基础
  -> 纯 C# 帧合同与确定性模型
  -> Gather / Worker / Commit 多线程空管线
  -> 合成骨架和占位动画性能验证
  -> UE manifest schema 与导出器
  -> Godot 严格导入器和 AlsAnimationSet
  -> 基础 Locomotion
  -> Aim / Layering / Foot IK
  -> Event / Sync / Action / Root Motion
  -> Ragdoll / Recovery
  -> Camera
  -> 最终性能门禁
```

在多线程基础通过之前，不开始批量 UE 资产导出；在最小资产闭环通过之前，不开始逐功能迁移；每个功能切片通过测试和性能检查后，才进入下一个切片。

## 四、ALS-Refactored 的使用方式

`ALS-Refactored` 只作为算法、状态语义和行为规格，不作为 Godot 的链接库，也不要求安装到当前 UE 5.9 工程。

固定版本为：

```text
Repository: https://github.com/Sixze/ALS-Refactored.git
Commit:     b754d6f0f2bb03741d301f8fb88077ebfe561e17
```

固定提交的目的，是避免开发过程中上游 `main` 更新导致行为依据漂移。每个 Godot 功能都要记录：

- 参考的上游 C++ 文件和函数；
- 参考的 Animation Blueprint、Control Rig 或资产；
- 使用的 tag 和 commit；
- Godot 对应实现文件；
- UE 与 Godot 的坐标、物理或动画差异；
- 为验证该功能建立的测试。

参考模块与本项目的对应关系：

| ALS-Refactored 模块 | 本项目用途 |
| --- | --- |
| `ALS` | 角色状态、动画状态、移动、Foot IK、Mantle、Ragdoll 和数学逻辑 |
| `ALSCamera` | 第三/第一人称摄像机行为 |
| `ALSEditor` | UE 资产设置、元数据提取和导出工具设计参考 |
| `ALSExtras` | 示例和可选能力，默认不属于里程碑 B |

当前 UE 资产源工程是 UE 5.9，而固定的 ALS-Refactored 4.17 面向 UE 5.7。因此不把该插件作为 UE 5.9 工程的强制编译依赖。需要参考的 C++ 算法单独阅读和标注；UE 导出器直接面向当前工程和 UE 5.9 API 编写。

ALS-Refactored 的 C++ 代码采用 MIT 许可证，但 UE Marketplace/Fab 中的模型与动画资产不因此自动获得 MIT 授权。代码和资产许可分别记录、分别处理。

## 五、运行时线程架构

### 5.1 三阶段物理帧

每个 physics frame 分为三个有序阶段：

```text
Main Thread，Order 0
AlsGatherStage
  -> 发布每角色不可变 AlsFrameInput

WorkerThreadPool，Order 1
每角色独立 AlsVisualWorkerRoot
  -> 求值并输出 AlsFrameResult

Main Thread，Order 2
AlsCommitStage
  -> 验证并提交结果
```

SceneTree process group order 负责形成阶段 barrier。Worker 内部不得再提交需要等待同一个 WorkerThreadPool 的嵌套任务，以避免线程池饥饿和死锁。

### 5.2 Gather 阶段

Gather 只在主线程执行，负责所有与场景和物理世界相关的读取：

- 玩家或 AI 输入；
- 请求的 gait、stance、rotation mode 和 action；
- `CharacterBody3D` 实际位置、朝向、速度和加速度；
- 地面接触、坡度、地面法线和移动平台变换；
- 左右脚 ray/shape query；
- Mantle 墙面、顶面、目标落点和胶囊空间查询；
- 当前 drive mode 和 ragdoll 生命周期；
- 本帧动画质量等级；
- 把上述数据转换成无 Node 引用的 `AlsFrameInput`。

输入一旦发布，本帧内禁止修改。Worker 不能回到主线程对象读取“缺少的字段”；如果某个功能需要世界数据，必须先扩展正式数据合同。

### 5.3 Worker Visual Evaluation 阶段

每个角色拥有一个独立的 `AlsVisualWorkerRoot`，放入 `SUB_THREAD` process group。该 worker 独占：

- 角色的 `AlsRuntimeState`；
- `AnimationTree` 和 `AnimationMixer`；
- `Skeleton3D`；
- `TwoBoneIK3D` 与其他 `SkeletonModifier3D`；
- 本角色动画参数缓存；
- 本角色结果缓冲。

Worker 的主要过程是：

1. 读取已经发布的 `AlsFrameInput`；
2. 调用纯数据 `AlsLocomotionModel.Evaluate()`；
3. 只更新发生变化的 AnimationTree 参数；
4. 手动推进 AnimationTree，避免默认 idle/physics callback 引入隐式顺序；
5. 执行骨架、Modifier 和 IK；
6. 收集 Root Motion、事件、姿态目标和诊断数据；
7. 写入 `AlsFrameResult` 并标记完成。

Worker 禁止：

- 访问其他角色的 Node；
- 执行世界物理查询；
- 调用 gameplay、音频或摄像机回调；
- 修改共享动态集合；
- 创建、销毁或 reparent SceneTree 节点；
- 直接移动 `CharacterBody3D`；
- 在热路径创建临时托管对象。

### 5.4 Commit 阶段

Commit 在主线程执行，负责：

- 校验 `FrameId`、`CharacterId`、`SlotGeneration` 和完成状态；
- 丢弃过期、重复、未来帧或 generation 不匹配的结果；
- 通过碰撞安全接口消费 Root Motion；
- 提交 drive mode 切换；
- 按确定顺序派发动画事件；
- 更新摄像机、音频和其他场景副作用；
- 处理角色创建、移除和槽位回收；
- 汇总主线程和 worker 计时。

事件提交顺序固定为：

```text
FrameId -> CharacterId -> AnimationTime -> EventSequence
```

相同输入反复运行时，事件顺序必须稳定。

## 六、跨线程数据合同

### 6.1 固定求值接口

核心逻辑对外只暴露粗粒度接口：

```text
Evaluate(
  in AlsFrameInput input,
  ref AlsRuntimeState state,
  ref AlsFrameResult result
)
```

`AlsLocomotionModel` 不依赖 Godot Node。它可以在普通单元测试、单线程回放和 worker 环境中使用同一份实现。

### 6.2 AlsFrameInput

主线程写入，Worker 只读，主要包含：

```text
FrameId / DeltaTime
CharacterId / SlotGeneration
CharacterTransform / ActualVelocity / ActualAcceleration
InputDirection / DesiredSpeed
ViewRotation / AimRotation
FloorState / FloorNormal / GroundedState
MovingPlatformId / MovingPlatformTransform / AngularVelocity
LeftFootHit / RightFootHit
MantleProbeResult
RequestedGait / Stance / RotationMode / Action
CurrentDriveMode / RagdollState
AnimationQualityTier
```

### 6.3 AlsRuntimeState

每个角色的 worker 独占，跨帧保存：

```text
LocomotionState
SmoothedVelocity / Acceleration / Lean
FootLockState
TurnInPlace / RotateInPlaceState
Mantle / Roll / GetUp playback state
AnimationPhase / SyncState
PreviousCurveValues
PendingRecoveryState
LastCommittedRootMotionFeedback
```

### 6.4 AlsFrameResult

Worker 写入，主线程只读，主要包含：

```text
FrameId / CharacterId / SlotGeneration
CompletionState
ResolvedLocomotionState
AnimationParameters
ProposedRootMotionDelta
PelvisTarget / LeftFootTarget / RightFootTarget
MovementIntent / RotationIntent
TypedAnimationEvents
RequestedDriveModeTransition
Diagnostics / Timing / ErrorCode
```

### 6.5 数据布局约束

- 使用 `float32`；
- 单位为米、秒和弧度；
- Godot 侧采用右手坐标和 Y-up；
- enum 使用固定宽度整数；
- 事件使用预分配的定长缓冲；
- 不包含 `Node`、`Resource`、`String`、delegate 和动态集合；
- 不包含由多个线程共同修改的引用对象；
- Godot 类型只在 Gather/Commit adapter 转换一次；
- 完成热身后，关键帧路径每帧托管分配必须为 0 B。

### 6.6 双缓冲和角色 generation

每个角色槽位包含两组输入和结果缓冲：

```text
Frame N:
  Main   写 Input[N % 2]
  Worker 读 Input[N % 2]
  Worker 写 Result[N % 2]
  Main   读 Result[N % 2]

Frame N+1:
  使用另一个槽位
```

角色移除只能在 Commit 完成后回收槽位。槽位每次复用时递增 `SlotGeneration`，从而保证晚到的旧 worker 结果不会误提交到新角色。

## 七、移动、姿势和物理所有权

### 7.1 Drive Mode

| Drive Mode | 位移写入者 | 姿势写入者 | 典型状态 |
| --- | --- | --- | --- |
| `MotorDriven` | 主线程 Character Motor | Worker Visual Rig | 普通移动、Jump、Fall |
| `AnimationDriven` | Worker 提议，Main 消费 | Worker Visual Rig | Mantle、Roll、Get-up |
| `PhysicsDriven` | 主线程物理 | Physical Bone Simulator | Ragdoll |
| `RecoveryBlend` | 主线程固定胶囊 | Worker Visual Rig | 捕获姿势到 Get-up 的混合 |

Drive Mode 只能由 Commit 切换，同一 physics frame 最多发生一次切换。任何时刻，骨架只能有一个姿势写入者。

### 7.2 普通 Locomotion

普通移动由 `AlsCharacterMotor` 在主线程根据输入和碰撞移动角色。Gather 读取移动后的真实速度、地面状态和移动平台信息，再交给 worker 求动画。这可以避免动画长期依赖未经碰撞验证的预测速度。

### 7.3 Root Motion 动作

Mantle、Roll 和 Get-up 的 worker 只输出 `ProposedRootMotionDelta`。主线程统一调用：

```text
ConsumeRootMotion(
  proposedDelta,
  actionState,
  collisionState
) -> RootMotionCommitResult
```

返回值包含：

- 实际消费的平移和旋转；
- 消费比例；
- 碰撞或中断原因；
- 未消费的残余误差；
- 下一帧动作修正反馈。

移动平台上的 Mantle 目标保存为平台局部变换。Gather 每帧根据平台当前世界变换重新生成目标，避免平台运动后角色追逐旧的世界坐标。

### 7.4 Ragdoll 和恢复

进入 Ragdoll：

1. Commit 停止 AnimationTree 推进；
2. 将当前 Skeleton pose 交给 Physical Bone；
3. 启动物理模拟；
4. Worker 停止写骨骼；
5. drive mode 切换为 `PhysicsDriven`。

退出 Ragdoll：

1. 主线程捕获完整 local bone pose；
2. 根据 pelvis、spine 或 chest 方向判断 face-up/face-down；
3. 将 CharacterBody 胶囊对齐到恢复位置；
4. 停止 Physical Bone；
5. Worker 从捕获姿势混合到对应 Get-up 动画；
6. 混合阶段使用 `RecoveryBlend`；
7. Get-up 完成后切回 `MotorDriven`。

Godot 没有与 UE Pose Snapshot 完全等价的现成功能，因此项目实现 `AlsPoseRecoveryModifier`。首版使用 C# 和 Godot 原生骨架接口；只有 Profiler 证明该模块超预算时，才替换为粗粒度 GDExtension。

## 八、UE 资产导出与 Godot 导入

### 8.1 总体资产流

```text
UE 5.9 Asset Source
  -> ALS Exporter
  -> FBX + als_manifest.json
  -> Godot ALS Importer
  -> Strict Validation
  -> Compiled AlsAnimationSet
```

FBX 只承载 Skeletal Mesh、Skin、Skeleton 和动画轨道。不能可靠包含的 UE 运行时语义全部写入 sidecar manifest。

### 8.2 manifest 内容

`als_manifest.json` 至少包含：

- schema version 和 exporter version；
- 源 UE 版本、ALS 变体和源资产稳定标识；
- 骨架层级、rest pose hash、必要骨骼和 socket；
- virtual bone 定义；
- 单位、forward/up axis 和变换转换规则；
- 每个动画的长度、采样率、循环策略和 Root Motion policy；
- ALS curves；
- Notify 与 Notify State；
- Sync Marker；
- BlendSpace 和 Aim Offset sample 布局；
- Montage section、blend、interrupt 和 action 规则；
- 上下半身与分层骨骼 mask；
- PhysicsAsset body、constraint 和骨骼引用；
- Mantle、Roll、Get-up 和摄像机配置。

### 8.3 AlsAnimationSet

Godot 导入完成后生成只读运行时数据 `AlsAnimationSet`，包含：

- 骨架版本和 rest pose hash；
- 必要骨骼的稳定整数 ID；
- `AnimationLibrary` 与稳定 clip ID；
- 连续存储的 curve、event 和 sync 数据；
- BlendSpace/Aim Offset sample 表；
- Root Motion policy；
- track filter 和分层 mask；
- 动作、Ragdoll、Recovery 和 Camera 配置。

运行时禁止每帧按字符串查找骨骼、动画、曲线和 AnimationTree 参数。所有稳定 ID 和查找表在导入阶段生成。

### 8.4 导入失败条件

以下情况直接令导入失败：

- 缺少 root、pelvis、foot 或其他必要骨骼；
- 骨架层级或 rest pose hash 不一致；
- 单位、缩放或坐标轴不符合契约；
- 循环动画首尾不连续；
- Root Motion 动作没有有效 root track；
- 必需 curve、Notify、Notify State 或 Sync Marker 缺失；
- Aim Offset additive 基准姿势不一致；
- PhysicsAsset 引用了不存在的骨骼；
- 同名资产产生稳定 ID 冲突；
- manifest schema 版本不受支持。

导入器不静默猜测默认值。错误报告必须标出源资产、字段路径、期望值和实际值，并输出动画时长、轨道数、骨骼数、曲线数和预估运行时成本。

## 九、功能模块划分与迁移顺序

### 9.1 基础模块

| 模块 | 主要职责 | 线程 |
| --- | --- | --- |
| `AlsFrameExchange` | 双缓冲、FrameId、generation、发布状态 | 跨线程合同 |
| `AlsGatherStage` | 世界状态、物理查询和输入收集 | Main Order 0 |
| `AlsLocomotionModel` | 纯数据 ALS 状态和参数求值 | Worker 或测试线程 |
| `AlsVisualWorkerRoot` | AnimationTree、Skeleton 和 IK 求值 | Worker Order 1 |
| `AlsCommitStage` | Root Motion、事件、所有权和生命周期提交 | Main Order 2 |
| `AlsCharacterMotor` | Godot CharacterBody 与 ALS 移动语义 | Main |
| `AlsRootMotionSolver` | 动作位移、碰撞和误差反馈 | Worker 提议、Main 消费 |
| `AlsPerformanceGovernor` | 计时、重要性和第 11 个后的动画预算 | Main |
| `AlsAnimationSet` | 导入后冻结的动画运行时数据 | Read-only |

### 9.2 项目补齐模块

| 模块 | 补齐能力 |
| --- | --- |
| `AlsCurveRuntime` | ALS curve 采样和混合 |
| `AlsEventTimeline` | Notify、Notify State、阈值、去重和顺序 |
| `AlsSyncRuntime` | Sync Marker、左右脚相位和循环同步 |
| `AlsActionPlayer` | Montage section、动作混合和中断 |
| `AlsPoseCorrectionModifier` | Mesh-space Aim Offset 和 per-bone rotation blend |
| `AlsFootPlacementModifier` | Pelvis、Foot Lock、脚掌偏移和旋转约束 |
| `AlsPoseRecoveryModifier` | Ragdoll pose capture 与 Get-up 混合 |
| `AlsRootMotionSolver` | Mantle/Roll/Get-up Motion Correction |
| `AlsCameraRig` | 摄像机模式、肩位、lag、FOV 和遮挡 |

### 9.3 功能实施顺序

1. Character Motor、移动状态、gait、stance 和 rotation mode；
2. Locomotion 混合、stride、play rate、lean、Jump、Fall 和 Land；
3. Looking Direction、Aiming、Aim Offset、Turn/Rotate in Place 和分层姿态；
4. Foot IK、Foot Lock、pelvis、斜坡、楼梯和移动平台；
5. curve、typed event、Notify State、Sync Marker、Dynamic Transition 和 ActionPlayer；
6. Mantle、Roll、碰撞中断和 Root Motion 反馈；
7. Ragdoll、face-up/down、pose capture、recovery 和 Get-up；
8. 第三/第一人称 Camera、肩位、lag、FOV 和遮挡。

每项功能都作为完整纵向切片完成，必须同时包含纯逻辑测试、Godot 集成测试、对照数据、可操作演示和 10 角色性能检查。只有画面看起来正确，不算完成。

## 十、实施阶段和阶段门禁

### P0：仓库、Godot 工程和确定性核心

任务：

- 创建 Godot 4.7.2 .NET 工程、C# solution 和测试工程；
- 建立 `.gitignore`、格式化规则和可复现命令；
- 定义 `AlsFrameInput`、`AlsRuntimeState` 和 `AlsFrameResult`；
- 实现定长事件缓冲、双缓冲和 generation 校验；
- 实现与 Godot Node 无关的确定性数学辅助；
- 按 TDD 方式先建立失败测试，再写实现。

门禁：

- 所有合同测试通过；
- `Als.Core` 不引用 Godot Node 或场景对象；
- 固定输入重复执行得到稳定输出；
- 缓冲槽位生命周期和 generation 测试覆盖创建、删除和复用。

### P1：多线程动画分发 Harness

任务：

- 建立 Gather、Worker 和 Commit 三阶段；
- 使用合成骨架、程序化动画或可再分发 fixture；
- 实现单线程和多线程两种调度模式；
- 建立 1、10、16、32 角色测试场景；
- 记录阶段墙钟时间、每角色耗时、分配和无效结果计数；
- 压测角色反复创建、移除和槽位复用。

门禁：

- 单线程和多线程的状态、事件和 Root Motion 结果一致；
- 姿势在明确的浮点误差范围内一致；
- 不出现过期、重复、丢失或 generation 错配结果；
- Debug 线程访问检查无非法 Node 访问；
- 热身后关键路径没有托管分配。

### P2：UE 导出器和 Godot 导入器

任务：

- 先定义 manifest JSON Schema 和 golden fixture；
- 编写面向 UE 5.9 的导出器；
- 先导出一个最小骨架和少量 locomotion 动画；
- 实现 Godot 严格验证和 `AlsAnimationSet` 编译；
- 建立 UE/Godot cross-engine golden data；
- 最小闭环通过后再批量导出 ALS 资产。

门禁：

- 从记录的外部输入可以重复生成相同 manifest；
- 合法 fixture 导入成功；
- 所有预设非法 fixture 都以明确原因失败；
- 骨骼、动画、curve、event 和 marker 数量与 UE 源一致；
- 运行时不依赖反复字符串查找。

### P3：基础 Locomotion

任务：

- `AlsCharacterMotor`；
- gait、stance 和 rotation mode；
- Stand、Crouch、Walk、Run、Sprint；
- Jump、Fall、Land；
- locomotion BlendSpace、stride、play rate 和 lean。

门禁：

- UE/Godot 状态转换 golden tests 通过；
- 10 个角色保持 60 Hz 全频动画；
- 输入、实际速度和动画状态之间没有人为增加一帧延迟。

### P4：Aim、分层姿态和 Foot IK

2026-09-10 完整性复核：下文是历史自动门禁记录，不代表当前 Demo 已达到原版动作
等价。P3 基础图、P4 动态分层及 P5A Demo 接线仍有缺口；补完顺序见
`../plans/2026-09-10-locomotion-completeness-recovery.md`。人工发现的滑步、换髋与
上身问题继续作为未完成项，不以历史测试通过关闭。

状态（2026-08-30）：功能实现和 Task 17 clean-worktree 自动闭环已完成。正式
`10/parallel` 短时证据为 Gather+Commit p95 `998 us`、Worker p95 `2036 us`、
整体 p99 `3937 us`，完整 verifier 唯一输出 `P4_VERIFICATION_OK`。Godot Editor
八项手工观感仍待签收，P7 的 30 秒热身/10 分钟 Release 认证保持独立未完成。

任务：

- Looking Direction 和 Aiming；
- Aim Offset 与 mesh-space correction；
- 上下半身 track filter 和 per-bone rotation；
- Turn/Rotate in Place；
- Foot Lock、pelvis correction、脚部 IK；
- 楼梯、斜坡和移动平台处理。

门禁：

- 双脚锁定不漂移、不扭曲；
- 移动平台旋转时目标空间正确；
- 10 角色全质量性能仍满足阶段预算；
- 根据 Profiler 数据决定 Modifier 是否需要 GDExtension，不能凭感觉提前下沉。

### P5：事件、Overlay、动作和 Root Motion

P5 按依赖顺序拆为三个可独立验收的纵向切片。

#### P5A：事件与动作基础

任务：

- Curve Runtime；
- Notify、Notify State 和 Typed Event；
- Sync Marker 和左右脚相位；
- Dynamic Transition 和 ActionPlayer。

门禁：

- 混合和循环情况下事件无重复、无乱序；
- worker 不直接调用 gameplay；
- 动作中断返回稳定、可测试的原因码。

#### P5B：Overlay gameplay

任务：

- Overlay state、配置和动画 profile；
- Overlay locomotion 与 P4 上下半身分层的组合；
- Rifle、Pistol 等道具的确定性挂点、显示和生命周期；
- 基于 ActionPlayer/Typed Event 的装备、收起和切换；
- Overlay、stance、gait、rotation mode 与 action 的组合规则。

门禁：

- 已导出的全部 Overlay 配置和道具引用可追溯到 stable ID；
- 无道具、Rifle 和 Pistol 代表路径均通过 UE/Godot golden 回放；
- 快速切换、动作中断和角色销毁不会遗留道具节点或过期事件；
- 1/10 角色 single/parallel 结果一致，前 10 个角色保持完整 Overlay 质量。

#### P5C：Mantle、Roll 和 Root Motion

任务：

- Mantle detection、Motion Correction 和移动平台目标；
- Roll、动作中断和 Root Motion 碰撞反馈。

门禁：

- Mantle/Roll 不能穿透碰撞；
- 多角色交错动作压力测试通过。

### P6：Ragdoll、Get-up 和 Camera

任务：

- PhysicsAsset metadata 导入；
- Animation/Physics 姿势所有权切换；
- Ragdoll pose capture；
- face-up/down 判断；
- RecoveryBlend 和 Get-up；
- 第三/第一人称 camera；
- 肩位、lag、FOV、遮挡和 ragdoll target。

门禁：

- Ragdoll 期间不存在 Skeleton 双写；
- 10 个角色同时 Ragdoll 并交错 Get-up 测试通过；
- Camera 切换不改变角色动画确定性；
- Pose Recovery 达到质量和性能要求。

### P7：最终性能门禁

任务：

- 运行完整里程碑 B 场景；
- 运行 1、10、16、32 角色扩展曲线；
- 消除分配、字符串查找和重复参数写入；
- 合并 Foot IK、pelvis 和 pose correction 的骨骼遍历；
- 只把有 Profiler 证据的热点替换为 GDExtension；
- 生成可复现的 Release 基准报告。

门禁：

- i7-10700 上通过 30 秒热身和 10 分钟采样；
- 前 10 个角色保持 Tier 0 全质量；
- p95、p99、分配、事件和线程错误全部满足性能合同。

## 十一、测试策略

### 11.1 纯 C# 单元测试

覆盖：

- 状态切换；
- dampers、角度处理和坐标转换；
- curve 采样和混合；
- Notify State begin/tick/end；
- Sync Marker 跨循环顺序；
- 事件去重和定长缓冲溢出；
- FrameId 和 generation；
- Root Motion 提议、消费比例和残差；
- drive mode 转换规则。

### 11.2 导入和 Schema 测试

为每种失败条件建立最小非法 manifest。测试必须验证失败原因，而不只验证“发生异常”。合法 golden manifest 必须验证骨骼、动画、曲线、事件、marker 和配置的具体数量及 ID。

### 11.3 Godot Headless 集成测试

覆盖：

- process group 顺序和 barrier；
- 单线程/多线程切换；
- AnimationTree manual advance；
- Skeleton 和 Modifier 所有权；
- IK 和姿态求值；
- 角色创建、删除和复用；
- 长时间确定性 replay；
- Debug 线程访问错误。

### 11.4 Cross-engine Golden Tests

UE 导出以下 golden data：

- clip 长度和采样率；
- 关键时间点骨骼 local transform；
- root transform 和累计 Root Motion；
- curve 值；
- Notify/Notify State 时间；
- Sync Marker 顺序；
- locomotion 状态和动作选择结果。

Godot 使用同样的输入时间和角色状态采样，并在声明的浮点误差范围内比较。状态、动作、事件顺序和 ID 要求精确一致；姿势变换允许小范围数值误差，不要求跨引擎 bitwise 相同。

### 11.5 必测运行场景

1. 10 个角色同时在斜坡、楼梯和移动平台 locomotion；
2. 多角色交错 Jump、Land、Turn、Mantle 和 Roll；
3. 逐步增加到 10 个角色同时 Ragdoll，再交错 Get-up；
4. 固定输入的单线程/多线程等价测试；
5. 1、10、16、32 角色扩展曲线；
6. Worker 活跃时反复创建、移除和切换角色资产；
7. 长时间运行的事件顺序、generation 和内存分配测试。

## 十二、性能合同

### 12.1 基准环境

```text
CPU:         Intel i7-10700，8 核 16 线程
Build:       Godot .NET Release Export
Physics:     60 Hz
Characters:  10 个全质量 ALS 角色
Warm-up:     30 秒
Measure:     10 分钟
```

固定分辨率、相机、渲染设置和轻量材质，避免 GPU 波动掩盖动画 CPU 成本。使用墙钟时间衡量 worker 关键路径，不能把多个 worker 的 CPU 时间简单相加。

### 12.2 指标

| 指标 | 要求 |
| --- | ---: |
| Gather + Commit 主线程 p95 | `<= 1.5 ms` |
| Worker 动画阶段关键路径 p95 | `<= 2.5 ms` |
| ALS 整体关键路径 p99 | `<= 4.0 ms` |
| 基准场景 CPU frame p99 | `<= 16.67 ms` |
| 热身后每帧托管分配 | `0 B` |
| 过期、重复或丢失结果 | `0` |
| Notify/Typed Event 重复或乱序 | `0` |

### 12.3 前 10 个角色的质量要求

前 10 个角色必须保持：

- 60 Hz AnimationTree；
- 完整 Aim Offset 和骨骼分层；
- Foot IK、Foot Lock 和 pelvis correction；
- Mantle、Roll、Ragdoll 和 Pose Recovery；
- 完整 curve、Notify 和 Sync Marker；
- 相同的动画图和功能路径。

不得通过动画降频、关闭 IK、简化图或移除功能让前 10 个角色通过基准。只有第 11 个及以后的角色可以启用动画 LOD、降频、错峰和预算策略。

### 12.4 性能未达标时的处理顺序

1. 消除托管分配、字符串查找和重复参数写入；
2. 减少同时活跃的 clip、轨道和 Modifier；
3. 合并 Foot IK、pelvis 和 pose correction 的骨骼遍历；
4. 将明确的热点 SkeletonModifier 替换为 GDExtension；
5. 将 curve、event 或完整 pose 运算作为粗粒度模块下沉；
6. 仅对第 11 个以后角色应用动画预算。

禁止通过修改 Godot Core、维护引擎 fork、每角色创建 OS 线程或降低前 10 个角色质量解决性能问题。

## 十三、错误处理和诊断

### 13.1 Debug 行为

以下问题立即失败：

- worker 异常；
- 非法 Node 访问；
- 过期或 generation 不匹配结果；
- drive mode 或 Skeleton 所有权冲突；
- 定长事件缓冲溢出；
- 必需资产或 metadata 缺失；
- 受监控热路径出现意外托管分配。

### 13.2 Release 行为

运行时错误时：

- 保留最后一个有效视觉姿势；
- 丢弃无效结果；
- 保持或恢复到安全的 MotorDriven 状态；
- 写入有界诊断记录；
- 不允许错误处理本身制造持续分配或日志风暴。

资产导入错误在 Release 中仍然是致命错误，不能使用猜测的默认值继续运行。Root Motion 因碰撞被中断属于正常的类型化结果，不作为异常处理。

每条诊断至少包含：

```text
FrameId / CharacterId / SlotGeneration
Stage / DriveMode / StateId / ActionId
ElapsedTime / AllocationDelta
StableReasonCode
```

## 十四、明确不做的事项

- 不修改 Godot Core；
- 不维护 Godot 引擎 fork；
- 不为每个角色创建独立操作系统线程；
- 不实现通用 `.uasset` 读取器；
- 不实现任意异骨架运行时重定向；
- 里程碑 B 不实现网络预测；
- worker 不直接调用 gameplay 或修改外部 SceneTree；
- 没有 Profiler 证据时不提前重写为 GDExtension；
- 不把平均 FPS 当成唯一验收指标。

## 十五、完成定义

只有同时满足以下条件，项目才算完成：

- 固定的 UE ALS 资产可以通过 FBX 和版本化 manifest 自动进入 Godot；
- Godot 中可运行完整的里程碑 B 示例；
- 单线程和多线程模式通过等价测试；
- i7-10700 上 10 个全质量角色通过 60 FPS 性能合同；
- Mantle、Roll 和 Get-up 的 Root Motion 经过碰撞安全消费；
- Ragdoll 和 Animation 不会同时写入 Skeleton；
- curve、Notify、Notify State 和 Sync Marker 具有确定性语义；
- worker 不直接产生 gameplay 副作用；
- 热身后关键帧路径每帧托管分配为 0 B；
- 没有修改 Godot Core；
- 所有 GDExtension 落点都有可复现的 Profiler 证据；
- 文档记录上游来源、资产契约、测试方法和最终基准结果。
