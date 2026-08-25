# P1 Process Group 多线程分发 Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Godot 4.7.2 中建立可重复验证的 Gather/Worker/Commit 三阶段 physics pipeline，使用每角色独立 process group 并行驱动合成 Skeleton3D，并证明单线程与多线程结果等价。

**Architecture:** Gather 位于 `MainThread/Order 0`，每个角色 worker 位于 `MainThread` 或 `SubThread/Order 1`，Commit 位于 `MainThread/Order 2`。跨阶段只传递 P0 的 `AlsFrameInput -> AlsFrameResult` 值合同；Harness 分别运行 single 和 parallel 模式，用稳定 digest、事件序列、generation 替换计数和分配计数进行对比。

**Tech Stack:** Godot 4.7.2 .NET、C# 12、.NET 8、SceneTree process thread groups、Skeleton3D、xUnit、PowerShell、Git

---

## 文件职责

```text
src/Als.Core/Simulation/AlsSyntheticInputSource.cs      生成确定性合成帧输入
src/Als.Core/Simulation/AlsSyntheticLocomotionModel.cs  纯数据 worker 求值
src/Als.Core/Diagnostics/AlsResultDigest.cs             跨运行稳定结果摘要
tests/Als.Core.Tests/AlsSyntheticLocomotionModelTests.cs 模型状态、事件与确定性
tests/Als.Core.Tests/AlsResultDigestTests.cs             digest 敏感性与重复性

src/Als.Godot/Dispatch/AlsHarnessMode.cs                 single/parallel 参数
src/Als.Godot/Dispatch/AlsHarnessContext.cs              固定槽位、计数器和帧时钟
src/Als.Godot/Dispatch/AlsHarnessEntry.cs                handle、exchange 和 worker 节点
src/Als.Godot/Dispatch/AlsGatherStage.cs                 Main Order 0 输入发布
src/Als.Godot/Dispatch/AlsVisualWorkerRoot.cs            Order 1 模型和 Skeleton 求值
src/Als.Godot/Dispatch/AlsCommitStage.cs                 Main Order 2 结果提交和门禁
src/Als.Godot/Dispatch/P1DispatchHarness.cs              参数解析、建树和角色替换
scenes/tests/p1_dispatch_harness.tscn                    P1 headless 场景

scripts/verify-p1.ps1                                    1/10/16/32 single/parallel 矩阵
docs/architecture/p1-process-group-harness.md            实际线程、等价和性能记录
```

### Task 1：建立确定性合成输入与 worker 模型

**Files:**
- Test: `tests/Als.Core.Tests/AlsSyntheticLocomotionModelTests.cs`
- Create: `src/Als.Core/Simulation/AlsSyntheticInputSource.cs`
- Create: `src/Als.Core/Simulation/AlsSyntheticLocomotionModel.cs`

- [x] **Step 1：先写模型失败测试**

测试要求：相同输入和初始状态产生相同结果；地面字节决定 Grounded/InAir；每 30 帧产生一个稳定事件；结果 hot path 在热身后为 0 B。

```csharp
using GodotAls.Core.Contracts;
using GodotAls.Core.Simulation;

namespace GodotAls.Core.Tests;

public sealed class AlsSyntheticLocomotionModelTests
{
    [Fact]
    public void SameInputAndStateProduceTheSameResult()
    {
        var input = AlsSyntheticInputSource.Create(new AlsFrameIdentity(30, 2, 1), 1f / 60f);
        var stateA = default(AlsRuntimeState);
        var stateB = default(AlsRuntimeState);
        var resultA = default(AlsFrameResult);
        var resultB = default(AlsFrameResult);

        AlsSyntheticLocomotionModel.Evaluate(input, ref stateA, ref resultA);
        AlsSyntheticLocomotionModel.Evaluate(input, ref stateB, ref resultB);

        Assert.Equal(stateA.AnimationPhase, stateB.AnimationPhase);
        Assert.Equal(resultA.ResolvedLocomotionState, resultB.ResolvedLocomotionState);
        Assert.Equal(resultA.MovementIntent, resultB.MovementIntent);
        Assert.Equal(resultA.PelvisTarget, resultB.PelvisTarget);
        Assert.Equal(resultA.TypedEvents.Count, resultB.TypedEvents.Count);
        Assert.Equal(1, resultA.TypedEvents.Count);
    }

    [Fact]
    public void FloorSampleControlsLocomotionState()
    {
        var identity = new AlsFrameIdentity(1, 0, 1);
        var grounded = AlsSyntheticInputSource.Create(identity, 1f / 60f);
        var airborne = grounded with { Floor = grounded.Floor with { IsGrounded = 0 } };
        var groundedState = default(AlsRuntimeState);
        var airborneState = default(AlsRuntimeState);
        var groundedResult = default(AlsFrameResult);
        var airborneResult = default(AlsFrameResult);

        AlsSyntheticLocomotionModel.Evaluate(grounded, ref groundedState, ref groundedResult);
        AlsSyntheticLocomotionModel.Evaluate(airborne, ref airborneState, ref airborneResult);

        Assert.Equal(AlsLocomotionState.Grounded, groundedResult.ResolvedLocomotionState);
        Assert.Equal(AlsLocomotionState.InAir, airborneResult.ResolvedLocomotionState);
    }

    [Fact]
    public void EvaluateDoesNotAllocateAfterWarmup()
    {
        var state = default(AlsRuntimeState);
        var result = default(AlsFrameResult);
        Run(ref state, ref result, 100);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(ref state, ref result, 10_000);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Run(ref AlsRuntimeState state, ref AlsFrameResult result, int count)
    {
        for (var frame = 1; frame <= count; frame++)
        {
            var input = AlsSyntheticInputSource.Create(new AlsFrameIdentity(frame, 0, 1), 1f / 60f);
            AlsSyntheticLocomotionModel.Evaluate(input, ref state, ref result);
        }
    }
}
```

- [x] **Step 2：运行测试确认 RED**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter AlsSyntheticLocomotionModelTests`

Expected: 编译失败，`GodotAls.Core.Simulation` 不存在。

- [x] **Step 3：实现合成输入源**

`AlsSyntheticInputSource.Create()` 使用 `FrameId` 和 `CharacterId` 计算相位，不读取时间、随机数或 Node。它设置：

```csharp
var phase = (identity.FrameId * 0.05f) + (identity.CharacterId * 0.17f);
var direction = Vector3.Normalize(new Vector3(MathF.Cos(phase), 0f, MathF.Sin(phase)));
var grounded = (identity.FrameId + identity.CharacterId) % 90 < 72;
var speed = 1.5f + (identity.CharacterId % 3);
```

并返回完整 `AlsFrameInput`：单位变换、确定性速度/输入、Y-up 地面、无脚部命中、无 Mantle、`Running/Standing/LookingDirection/MotorDriven/Tier0`。

- [x] **Step 4：实现模型并确认 GREEN**

`AlsSyntheticLocomotionModel.Evaluate()`：

```csharp
public static void Evaluate(
    in AlsFrameInput input,
    ref AlsRuntimeState state,
    ref AlsFrameResult result)
{
    result = AlsFrameResult.CreateDefault(input.Identity);
    state.LocomotionState = input.Floor.IsGrounded != 0
        ? AlsLocomotionState.Grounded
        : AlsLocomotionState.InAir;
    state.SmoothedVelocity = input.ActualVelocity;
    state.SmoothedAcceleration = input.ActualAcceleration;
    state.AnimationPhase = (state.AnimationPhase + (input.DeltaTime * (0.5f + input.DesiredSpeed))) % 1f;

    var direction = input.InputDirection.LengthSquared() > 0f
        ? Vector3.Normalize(input.InputDirection)
        : Vector3.Zero;

    result.ResolvedLocomotionState = state.LocomotionState;
    result.MovementIntent = direction * input.DesiredSpeed;
    result.RotationIntent = input.AimRotation;
    result.PelvisTarget = new Vector3(0f, MathF.Sin(state.AnimationPhase * MathF.Tau) * 0.05f, 0f);

    if (input.Identity.FrameId % 30 == 0 &&
        !result.TypedEvents.TryAdd(new AlsAnimationEvent(1, state.AnimationPhase, 1f, AlsAnimationEventPhase.Trigger)))
    {
        result.ErrorCode = 1;
    }
}
```

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter AlsSyntheticLocomotionModelTests`

Expected: 3 tests passed。

- [x] **Step 5：提交**

```powershell
git add src/Als.Core/Simulation tests/Als.Core.Tests/AlsSyntheticLocomotionModelTests.cs
git commit -m "feat: add deterministic synthetic locomotion model"
```

### Task 2：建立稳定结果 digest

**Files:**
- Test: `tests/Als.Core.Tests/AlsResultDigestTests.cs`
- Create: `src/Als.Core/Diagnostics/AlsResultDigest.cs`

- [x] **Step 1：先写 digest 失败测试**

```csharp
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Simulation;

namespace GodotAls.Core.Tests;

public sealed class AlsResultDigestTests
{
    [Fact]
    public void EquivalentRunsProduceTheSameDigest()
    {
        var first = EvaluateDigest(120);
        var second = EvaluateDigest(120);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DigestChangesWhenAResultChanges()
    {
        var identity = new AlsFrameIdentity(1, 0, 1);
        var first = AlsFrameResult.CreateDefault(identity);
        var second = AlsFrameResult.CreateDefault(identity);
        second.ErrorCode = 7;
        var firstDigest = AlsResultDigest.OffsetBasis;
        var secondDigest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.Append(ref firstDigest, first);
        AlsResultDigest.Append(ref secondDigest, second);
        Assert.NotEqual(firstDigest, secondDigest);
    }

    private static ulong EvaluateDigest(int frames)
    {
        var digest = AlsResultDigest.OffsetBasis;
        var state = default(AlsRuntimeState);
        var result = default(AlsFrameResult);
        for (var frame = 1; frame <= frames; frame++)
        {
            var input = AlsSyntheticInputSource.Create(new AlsFrameIdentity(frame, 0, 1), 1f / 60f);
            AlsSyntheticLocomotionModel.Evaluate(input, ref state, ref result);
            AlsResultDigest.Append(ref digest, result);
        }
        return digest;
    }
}
```

- [x] **Step 2：运行测试确认 RED**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter AlsResultDigestTests`

Expected: 编译失败，`AlsResultDigest` 不存在。

- [x] **Step 3：实现无分配 FNV-1a digest**

`AlsResultDigest` 使用 `OffsetBasis = 14695981039346656037UL` 和 `Prime = 1099511628211UL`。`Append()` 依次加入 identity 三字段、resolved state、drive mode、Root Motion、pelvis/feet/movement/rotation、事件数量与事件字段、worker error；浮点通过 `BitConverter.SingleToInt32Bits()` 转为位模式。`WorkerElapsedTicks` 不进入 digest，因为它不是行为结果。

- [x] **Step 4：运行 digest 与全部核心测试**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore`

Expected: digest 测试通过，原有 16 项测试继续通过。

- [x] **Step 5：提交**

```powershell
git add src/Als.Core/Diagnostics tests/Als.Core.Tests/AlsResultDigestTests.cs
git commit -m "feat: add stable ALS result digest"
```

### Task 3：先建立 P1 headless RED 门禁

**Files:**
- Create: `scripts/verify-p1.ps1`

- [x] **Step 1：创建验证驱动**

脚本接受 `-GodotExecutable`、`-Frames 90` 和 `-CharacterCounts @(1,10,16,32)`。它先执行 restore/build/test，然后对每个角色数量分别运行：

```powershell
& $GodotExecutable --headless --path $ProjectRoot `
  'res://scenes/tests/p1_dispatch_harness.tscn' -- `
  '--als-mode=single' "--als-characters=$characterCount" "--als-frames=$Frames"

& $GodotExecutable --headless --path $ProjectRoot `
  'res://scenes/tests/p1_dispatch_harness.tscn' -- `
  '--als-mode=parallel' "--als-characters=$characterCount" "--als-frames=$Frames"
```

脚本用以下正则读取标记：

```text
GODOT_ALS_P1_OK mode=(single|parallel) characters=(\d+) frames=(\d+) digest=([0-9A-F]{16}) missing=(\d+) replacements=(\d+) allocations=(\d+) off_main=(\d+)
```

每组必须满足：single 与 parallel digest 相同；`missing=0`；`allocations=0`；90 帧时 `replacements=2`；single 的 `off_main=0`；parallel 的 `off_main>0`。最后输出 `P1_VERIFICATION_OK`。

- [x] **Step 2：运行脚本确认 RED**

Run:

```powershell
.\scripts\verify-p1.ps1 -GodotExecutable 'F:\下载\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
```

Expected: build 和核心测试通过，Godot 因 `p1_dispatch_harness.tscn` 不存在而失败。

- [x] **Step 3：提交 RED 驱动**

```powershell
git add scripts/verify-p1.ps1
git commit -m "test: define P1 dispatch verification matrix"
```

### Task 4：实现 process-group worker 与合成 Skeleton

**Files:**
- Create: `src/Als.Godot/Dispatch/AlsHarnessMode.cs`
- Create: `src/Als.Godot/Dispatch/AlsHarnessContext.cs`
- Create: `src/Als.Godot/Dispatch/AlsHarnessEntry.cs`
- Create: `src/Als.Godot/Dispatch/AlsVisualWorkerRoot.cs`

- [x] **Step 1：定义固定 Harness 状态**

`AlsHarnessContext` 构造时创建固定长度 `AlsHarnessEntry[]` 和 `AlsSlotRegistry`，保存 `Mode`、`TargetFrames`、`WarmupFrames=30`、`MainManagedThreadId`、`PublishedFrameId=0`、digest、missing、replacements、allocations 和 off-main worker 数。数组长度在运行期间不变，生命周期操作只替换单个 entry 引用。30 帧热身用于覆盖首次类型化事件分支，依据见 P1 实测文档。

`AlsHarnessEntry` 保存：

```csharp
public required AlsSlotHandle Handle { get; init; }
public required AlsFrameExchange Exchange { get; init; }
public required AlsVisualWorkerRoot Worker { get; init; }
public long StartFrame { get; init; }
```

- [x] **Step 2：实现 worker 配置和合成骨架创建**

`AlsVisualWorkerRoot.Configure()` 在节点入树前设置 context、handle、exchange、`ProcessThreadGroup` 和 `ProcessThreadGroupOrder=1`。single 使用 `MainThread`，parallel 使用 `SubThread`。

入树前 `BuildSyntheticRig()` 创建一个 `Skeleton3D` 子节点：

```csharp
var root = skeleton.AddBone("root");
skeleton.SetBoneRest(root, Transform3D.Identity);
var pelvis = skeleton.AddBone("pelvis");
skeleton.SetBoneParent(pelvis, root);
skeleton.SetBoneRest(pelvis, new Transform3D(Basis.Identity, new Vector3(0f, 1f, 0f)));
```

- [x] **Step 3：实现 worker physics callback**

`_PhysicsProcess()` 使用 `Volatile.Read(ref context.PublishedFrameId)` 构造当前 identity，从 exchange 读取输入，调用 `AlsSyntheticLocomotionModel.Evaluate()`，用 `result.PelvisTarget.Y` 设置 pelvis bone pose，然后发布结果。

计时使用 `Stopwatch.GetTimestamp()`，托管分配使用 `GC.GetAllocatedBytesForCurrentThread()`。只有 `frame > context.WarmupFrames` 且 `frame > entry.StartFrame + 2` 时累计分配。parallel 模式下，如果当前 managed thread id 不等于 main thread id，使用 `Interlocked.Exchange(ref entry.ObservedOffMainThread, 1)`。

- [x] **Step 4：编译 Godot 工程**

Run: `dotnet build .\GodotALS.csproj --no-restore`

Expected: 0 warnings，0 errors；尚未有可运行 P1 场景。

- [x] **Step 5：提交 worker 基础**

```powershell
git add src/Als.Godot/Dispatch
git commit -m "feat: add process-group visual worker"
```

### Task 5：实现 Gather、Commit、生命周期与场景

**Files:**
- Create: `src/Als.Godot/Dispatch/AlsGatherStage.cs`
- Create: `src/Als.Godot/Dispatch/AlsCommitStage.cs`
- Create: `src/Als.Godot/Dispatch/P1DispatchHarness.cs`
- Create: `scenes/tests/p1_dispatch_harness.tscn`

- [x] **Step 1：实现 Main Order 0 Gather**

`AlsGatherStage` 配置为 `MainThread/Order 0`。每次 `_PhysicsProcess()` 将 `PublishedFrameId + 1` 作为新帧，为固定数组中每个 entry 创建确定性 input 并发布，最后才使用 `Volatile.Write` 更新 `PublishedFrameId`。计时和分配只在 warm-up 后累计。

- [x] **Step 2：实现 Main Order 2 Commit**

`AlsCommitStage` 配置为 `MainThread/Order 2`。它按 entry 数组顺序消费当前帧结果：缺失则递增 `MissingResults`，成功则调用 `AlsResultDigest.Append()`。每 30 帧处理完结果后调用 root 的 `ReplaceCharacter(0, frame)`；90 帧共替换两次，最后一帧不替换。

达到目标帧时，Commit 汇总 off-main worker、allocation、missing 和 replacement，输出严格标记并以 0/1 退出：

```text
GODOT_ALS_P1_OK mode=parallel characters=10 frames=90 digest=0123456789ABCDEF missing=0 replacements=2 allocations=0 off_main=10
```

- [x] **Step 3：实现 Harness 建树和参数解析**

`P1DispatchHarness._Ready()` 读取 `OS.GetCmdlineUserArgs()`：

- `--als-mode=single|parallel`，缺省 `parallel`；
- `--als-characters=N`，范围 `1..32`；
- `--als-frames=N`，范围 `30..600`。

它捕获 `Environment.CurrentManagedThreadId`，创建 context、Gather、Commit 和初始 worker。`ReplaceCharacter()` 只由 Commit main-thread callback 调用：在 barrier 后 `QueueFree()` 旧 worker、释放旧 handle、重新 acquire、创建 exchange 和 worker、替换固定数组槽位并把新 worker 加入场景。

- [x] **Step 4：创建场景并运行 1 角色 single/parallel**

场景：

```ini
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Als.Godot/Dispatch/P1DispatchHarness.cs" id="1_harness"]

[node name="P1DispatchHarness" type="Node"]
script = ExtResource("1_harness")
```

Run two commands with `--als-characters=1 --als-frames=90`。

Expected: 两者都输出 P1 marker，digest 相同，parallel `off_main=1`，single `off_main=0`。

- [x] **Step 5：提交三阶段 Harness**

```powershell
git add src/Als.Godot/Dispatch scenes/tests/p1_dispatch_harness.tscn
git commit -m "feat: add Gather worker Commit harness"
```

### Task 6：运行扩展矩阵并处理真实线程问题

**Files:**
- Modify when evidence requires: `src/Als.Godot/Dispatch/*.cs`
- Modify when evidence requires: `scripts/verify-p1.ps1`

- [x] **Step 1：运行完整验证矩阵**

Run: `scripts/verify-p1.ps1` with the local Godot 4.7.2 console executable。

Expected: 1、10、16、32 角色的 single/parallel digest 全部一致；无缺失结果；无 thread-access error；每次运行 replacement=2；steady-state allocations=0。

- [x] **Step 2：若失败，按 systematic-debugging 修复**

任何 build、thread access、missing result、digest、allocation 或 lifecycle 失败都先保留输出并建立最小复现。只修复已确认根因，修复后重跑触发失败的单场景，再重跑完整矩阵。

- [x] **Step 3：回归 P0 与全部核心测试**

Run:

```powershell
.\scripts\verify-p0.ps1 -GodotExecutable <GodotExecutable>
dotnet test .\GodotALS.sln --no-build --no-restore
```

Expected: `P0_VERIFICATION_OK`，所有核心测试通过。

- [x] **Step 4：提交矩阵修正**

仅在 Step 2 产生代码修改时提交：

```powershell
git add src/Als.Godot/Dispatch scripts/verify-p1.ps1
git commit -m "fix: stabilize P1 process-group harness"
```

若没有修改，不创建空提交。

### Task 7：记录 P1 结果并完成阶段门禁

**Files:**
- Create: `docs/architecture/p1-process-group-harness.md`
- Modify: `docs/superpowers/plans/2026-08-25-p1-process-group-harness.md`

- [x] **Step 1：记录实际架构和限制**

文档记录 process group 属性、barrier、跨线程共享对象、Skeleton 所有权、single/parallel digest、1/10/16/32 结果、lifecycle replacement、steady-state allocation 和 P2 前提。明确 P1 的合成骨架不代表真实 ALS AnimationTree 成本。

- [x] **Step 2：勾选真实完成步骤并运行最终验证**

Run:

```powershell
.\scripts\verify-p1.ps1 -GodotExecutable <GodotExecutable>
.\scripts\verify-p0.ps1 -GodotExecutable <GodotExecutable>
git diff --check
git status --short
```

Expected: P1/P0 标记都成功；只有预期文档和计划清单未提交。

- [x] **Step 3：提交完成记录**

```powershell
git add docs/architecture/p1-process-group-harness.md docs/superpowers/plans/2026-08-25-p1-process-group-harness.md
git commit -m "docs: record P1 process-group harness"
```

- [x] **Step 4：最终审计**

Run:

```powershell
git status --short --branch
git log --oneline --decorate -12
git ls-files | Select-String -Pattern '(^|/)(\.godot|bin|obj)/'
```

Expected: feature 分支干净；没有生成文件进入 Git；P1 由多个小提交组成。
