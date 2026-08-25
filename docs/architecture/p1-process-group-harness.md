# P1 Process Group 多线程分发 Harness 实现记录

## 阶段目标

P1 在不引入 UE 动画资产和完整 ALS 行为的前提下，验证 Godot 4.7.2 中以下基础能力：

- 主线程 Gather、每角色 Worker、主线程 Commit 的确定阶段顺序；
- `SubThread` worker 对自身 `Skeleton3D` 的线程所有权；
- single 与 parallel 两种调度模式的行为结果完全等价；
- 角色销毁、槽位释放、generation 递增和 worker 重建不会产生旧结果污染；
- 核心求值、骨骼写入和双缓冲交换在完整热身后保持 0 B 托管分配。

本阶段使用合成输入和两骨骼 rig。它是线程架构门禁，不是完整 ALS 演示，也不代表真实 AnimationTree、IK、曲线采样或 UE 资产的运行成本。

## Process Group 拓扑

```text
Physics frame N

MainThread / Order 0
  AlsGatherStage
    -> 为每个当前 generation 发布 AlsFrameInput

SubThread / Order 1              MainThread / Order 1
  AlsVisualWorkerRoot x N   或    AlsVisualWorkerRoot x N
    -> 读取本角色 input
    -> 运行纯数据 locomotion model
    -> 写入本节点下的 Skeleton3D pelvis pose
    -> 发布 AlsFrameResult

MainThread / Order 2
  AlsCommitStage
    -> 按稳定槽位顺序消费 result
    -> 追加行为 digest
    -> 在 barrier 后执行角色替换
    -> 完成门禁并退出
```

`single` 和 `parallel` 只改变 Worker 的 `ProcessThreadGroup`：前者使用 `MainThread`，后者使用 `SubThread`。Gather 固定为 `MainThread/Order 0`，Commit 固定为 `MainThread/Order 2`。Godot 的 group order 屏障确保双缓冲槽位不会在复制期间被跨阶段覆盖。

## 跨线程边界与所有权

跨阶段只共享普通 C# 对象中的固定槽位和 P0 unmanaged 值合同：

- `AlsHarnessContext.Entries` 在初始化时固定长度，运行期只在 Commit barrier 后替换一个 entry 引用；
- 每个 entry 独占一个 `AlsFrameExchange`、当前 `AlsSlotHandle` 和一个 worker；
- Gather 不访问 worker 节点或骨架，只发布 `AlsFrameInput`；
- Worker 不访问角色列表、Gather、Commit 或其他节点，只使用自己的 exchange、runtime state 和子级 `Skeleton3D`；
- Commit 不写 worker 内部状态，只消费 `AlsFrameResult` 并执行主线程生命周期操作。

Worker 创建的合成 rig 包含 `root` 和 `pelvis`。`Skeleton3D` 是 worker 子节点，因此继承同一 process group；骨盆 pose 只在该 worker 的 `_PhysicsProcess()` 中写入。

## 确定性输入、模型与摘要

`AlsSyntheticInputSource` 只根据 `FrameId`、`CharacterId` 和固定 `DeltaTime=1/60` 构造输入，不读取系统时钟、随机数或 Godot Node。

`AlsSyntheticLocomotionModel` 解析 Grounded/InAir 状态、移动意图和骨盆周期偏移，并每 30 帧产生一个类型化动画事件。核心层 xUnit 覆盖相同输入/状态的重复结果、地面状态分支、事件节拍和热路径零分配。

`AlsResultDigest` 使用固定字段顺序的 64 位 FNV-1a。摘要包含 identity、状态、驱动模式、Root Motion、骨盆/双脚/移动/旋转、事件和错误码；`WorkerElapsedTicks` 明确排除，因为真实调度耗时不是行为结果。

## 生命周期压力

90 帧运行在第 30、60 帧 Commit 完成后替换槽位 0 的角色，共 2 次：

1. `QueueFree()` 旧 worker；
2. 释放旧 `AlsSlotHandle`；
3. 从 registry 重新 acquire 同一 CharacterId，generation 递增；
4. 创建新的 exchange、runtime state、合成骨架和 worker；
5. 替换固定 entry 槽位，再加入场景树。

替换发生在 Order 2 barrier 后，下一物理帧的 Gather 才会看到新 generation。完整矩阵中 `missing=0`，说明没有旧 generation 结果被新角色消费，也没有替换帧丢失。

## 分配测量

测量使用 `GC.GetAllocatedBytesForCurrentThread()`，并分别累计 Gather、模型、Skeleton 写入、exchange 和 Commit。

初始 20 帧热身没有覆盖每 30 帧才执行一次的事件分支；诊断显示唯一的 304 B 冷启动分配发生在第 30 帧首次事件求值。P1 因此把全局热身设为 30 帧，并在角色新建后额外排除 2 帧。这样测量区间已经执行过所有模型分支，之后第 60、90 帧事件分支仍纳入门禁。

所有最终矩阵运行的 Gather、模型、Skeleton、exchange 和 Commit 分配均为 `0 B`。这里的数值是托管分配，不包含 Godot native 内存活动。

## 2026-08-25 验证结果

环境：Godot `4.7.2.stable.mono.official.ed1daf0bf`、Godot.NET.Sdk `4.7.2`、.NET SDK `8.0.100`、Windows headless、physics `60 Hz`。

| 角色数 | single digest | parallel digest | single off-main | parallel off-main | missing | replacements | allocations |
|---:|---|---|---:|---:|---:|---:|---:|
| 1 | `9FB4A92A32246B70` | `9FB4A92A32246B70` | 0 | 1 | 0 | 2 | 0 B |
| 10 | `7795F67258906A20` | `7795F67258906A20` | 0 | 10 | 0 | 2 | 0 B |
| 16 | `0EFF0D7EACFEFF42` | `0EFF0D7EACFEFF42` | 0 | 16 | 0 | 2 | 0 B |
| 32 | `E392494097928FC1` | `E392494097928FC1` | 0 | 32 | 0 | 2 | 0 B |

统一入口：

```powershell
.\scripts\verify-p1.ps1 -GodotExecutable <Godot 4.7.2 Mono console executable>
```

脚本执行 restore、build、23 项核心测试，以及 1/10/16/32 角色的 single/parallel 共 8 个独立 Godot 进程。最终输出 `P1_VERIFICATION_OK`。P0 回归入口也继续输出 `P0_VERIFICATION_OK`。

## P2 前提与限制

P1 已建立可以承载后续 ALS 模块的线程骨架，但 P2 仍需单独验证：

- UE 骨架、动画、曲线、notify 和 Root Motion 的导出清单与自动审计；
- Godot 导入后的骨名、父子关系、rest pose、比例和坐标系一致性；
- 真实 AnimationTree、IK 和 AnimationMixer 在 worker process group 中的线程访问规则；
- ALS-Refactored 各功能模块的语义映射和 golden trace；
- 真实角色数量下的 CPU 时间、调度开销、负载均衡和帧预算。

因此 P2 应先建立 UE 资源导出/导入审计工具和最小真实骨架 smoke，再开始逐功能模块移植；不能仅凭 P1 合成 rig 的结果判断真实性能。
