# P3A Deterministic Locomotion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现与固定 `ALS-Refactored` C++ 行为对齐的确定性 locomotion 核心、UE golden trace、Godot `CharacterBody3D` motor 和 headless replay 门禁。

**Architecture:** 统一的 `AlsLocomotionCommand` 先在 Godot 主线程解析并驱动 motor，motor 完成碰撞后发布实际物理快照；纯 C# `AlsLocomotionModel` 在 worker 上只消费定宽值类型并输出动画参数。UE trace 每帧分离 `physicalActual` 输入证据、`nativeActual` 审计观测和独立 C++ `portExpected` oracle；golden comparer 只比较后者。UE trace 与 Godot motor replay 分开：前者验证可移植核心语义，后者只验证 Godot 物理的自身确定性和同帧顺序。

**Tech Stack:** C# 12 / .NET 8、xUnit、Godot 4.7.2 .NET、PowerShell、Unreal Engine 5.9 C++ commandlet、ALS-Refactored `b754d6f0f2bb03741d301f8fb88077ebfe561e17`

---

## File Map

| Path | Responsibility |
| --- | --- |
| `reference/als-refactored.lock.json` | 固定仓库、SHA、允许的兼容补丁哈希 |
| `tools/schemas/als_locomotion_trace.schema.json` | golden trace 的严格 JSON schema |
| `scripts/prepare-p3-reference.ps1` | 校验外部 clone、应用只允许的 UE 5.9 兼容补丁 |
| `scripts/generate-p3-golden.ps1` | 构建并运行 UE trace commandlet，原子写入 fixture |
| `tools/unreal/AlsLocomotionTrace/**` | UE 参考角色、固定序列和 trace commandlet |
| `src/Als.Core/Contracts/AlsLocomotionCommand.cs` | player/replay 共用的原始输入命令 |
| `src/Als.Core/Contracts/AlsEnums.cs` | P3 动画状态 enum |
| `src/Als.Core/Contracts/AlsFrameInput.cs` | motor 完成后的不可变实际物理快照 |
| `src/Als.Core/Contracts/AlsRuntimeState.cs` | worker 独占的 locomotion 历史状态 |
| `src/Als.Core/Contracts/AlsFrameResult.cs` | 确定性动画参数输出 |
| `src/Als.Core/Locomotion/AlsLocomotionSettings.cs` | 版本化、只读的 P3 数值设置 |
| `src/Als.Core/Locomotion/AlsLocomotionCommandResolver.cs` | gait、stance、sprint 和输入方向约束 |
| `src/Als.Core/Locomotion/AlsLocomotionModel.cs` | actual gait、状态、旋转和动画参数计算 |
| `src/Als.Core/Locomotion/AlsLocomotionTrace.cs` | fixture 反序列化与逐帧容差比较 |
| `src/Als.Godot/Locomotion/AlsCharacterMotor.cs` | 主线程 CharacterBody3D 移动、碰撞、蹲伏和跳跃 |
| `src/Als.Godot/Locomotion/AlsReplayInputAdapter.cs` | 固定命令序列 adapter |
| `src/Als.Godot/Locomotion/P3aLocomotionHarness.cs` | single/parallel Godot integration replay |
| `scenes/tests/p3a_locomotion_harness.tscn` | P3A headless 入口 |
| `assets/config/p3_locomotion_settings.json` | UE reference settings 的版本化数值 fixture |
| `tests/Als.Core.Tests/Fixtures/P3/*.json` | UE behavior golden fixtures |
| `scripts/verify-p3a.ps1` | pure、golden、Godot 1/10 角色和回归门禁 |

**Execution order:** 按 `1 -> 2 -> 7 -> 3 -> 4 -> 5 -> 6 -> 8 -> 9 -> 10` 执行。Task 7
必须先生成 reference settings 和五条 trace，随后纯 C# settings/model/comparer 才能以真实参考数据完成红绿循环；
编号保留模块分组，不能按数字顺序跳过这项依赖。

在每个新的 PowerShell 会话先定义这些任务专用变量：

```powershell
$godotExe = 'F:\下载\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
$unrealEditorCmd = 'D:\UnrealEngine\Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$uProject = 'D:\AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject'
```

### Task 1: Pin and Validate the Reference Source

**Files:**
- Create: `reference/als-refactored.lock.json`
- Create: `scripts/prepare-p3-reference.ps1`
- Create: `tests/Als.Import.Tests/AlsReferenceLockTests.cs`

- [ ] **Step 1: Write the failing lock-file test**

```csharp
using System.Text.Json;

namespace GodotAls.Import.Tests;

public sealed class AlsReferenceLockTests
{
    [Fact]
    public void P3ReferencePinsTheApprovedCommit()
    {
        var path = Path.Combine(RepositoryRoot.Find(), "reference", "als-refactored.lock.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal("https://github.com/Sixze/ALS-Refactored.git", root.GetProperty("repository").GetString());
        Assert.Equal("b754d6f0f2bb03741d301f8fb88077ebfe561e17", root.GetProperty("commit").GetString());
        Assert.Equal("5.9.0", root.GetProperty("targetEngine").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("compatibilityPatches").ValueKind);
    }
}
```

- [ ] **Step 2: Run the test and verify the missing lock fails**

Run: `dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter FullyQualifiedName~AlsReferenceLockTests`

Expected: FAIL because `reference/als-refactored.lock.json` does not exist.

- [ ] **Step 3: Add the exact lock and preparation script**

```json
{
  "schemaVersion": 1,
  "repository": "https://github.com/Sixze/ALS-Refactored.git",
  "commit": "b754d6f0f2bb03741d301f8fb88077ebfe561e17",
  "observedDate": "2026-08-26",
  "targetEngine": "5.9.0",
  "compatibilityPatches": []
}
```

`prepare-p3-reference.ps1` must resolve the clone path, reject a dirty clone, compare `git rev-parse HEAD` with the lock, and emit exactly:

```text
P3_REFERENCE_OK commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17 patches=0
```

Its public parameters are:

```powershell
param(
    [string]$ReferenceRoot = 'D:\GodotALS-References\ALS-Refactored',
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)
```

For generation, it also validates or creates the UE-project junction
`D:\AdvancedLocomotionSystemV\Plugins\ALSRefactored` pointing at the locked external clone. It refuses to replace
an existing directory or junction that resolves elsewhere, and updates the `.uproject` plugin list through JSON parsing
rather than text replacement.

- [ ] **Step 4: Run the focused checks**

Run: `dotnet test tests/Als.Import.Tests/Als.Import.Tests.csproj --filter FullyQualifiedName~AlsReferenceLockTests`

Run: `pwsh -NoProfile -File scripts/prepare-p3-reference.ps1`

Expected: test PASS and `P3_REFERENCE_OK commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17 patches=0`.

- [ ] **Step 5: Commit**

```powershell
git add reference/als-refactored.lock.json scripts/prepare-p3-reference.ps1 tests/Als.Import.Tests/AlsReferenceLockTests.cs
git commit -m "build: pin P3 ALS reference"
```

### Task 2: Add the P3 Fixed-Width Contracts

**Files:**
- Create: `src/Als.Core/Contracts/AlsLocomotionCommand.cs`
- Modify: `src/Als.Core/Contracts/AlsEnums.cs`
- Modify: `src/Als.Core/Contracts/AlsFrameInput.cs`
- Modify: `src/Als.Core/Contracts/AlsRuntimeState.cs`
- Modify: `src/Als.Core/Contracts/AlsFrameResult.cs`
- Modify: `src/Als.Core/Diagnostics/AlsResultDigest.cs`
- Modify: `tests/Als.Core.Tests/ContractLayoutTests.cs`
- Modify: `tests/Als.Core.Tests/AlsResultDigestTests.cs`

- [ ] **Step 1: Add failing layout and default-contract tests**

```csharp
[Fact]
public void P3ContractsContainOnlyUnmanagedData()
{
    Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsLocomotionCommand>());
    Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameInput>());
    Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsRuntimeState>());
    Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameResult>());
}

[Fact]
public void DefaultCommandIsStandingRunningLookingDirection()
{
    var command = AlsLocomotionCommand.CreateDefault();
    Assert.Equal(Vector2.Zero, command.MovementAxes);
    Assert.Equal(AlsGait.Running, command.RequestedGait);
    Assert.Equal(AlsStance.Standing, command.RequestedStance);
    Assert.Equal(AlsRotationMode.LookingDirection, command.RequestedRotationMode);
    Assert.Equal(0, command.JumpPressed);
}
```

- [ ] **Step 2: Run and confirm missing P3 types fail compilation**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~ContractLayoutTests|FullyQualifiedName~AlsResultDigestTests"`

Expected: FAIL with `AlsLocomotionCommand` or P3 result fields not found.

- [ ] **Step 3: Define the contracts**

Create this command exactly:

```csharp
using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLocomotionCommand(
    Vector2 MovementAxes,
    float ViewYaw,
    float AimYaw,
    AlsGait RequestedGait,
    AlsStance RequestedStance,
    AlsRotationMode RequestedRotationMode,
    byte JumpPressed)
{
    public static AlsLocomotionCommand CreateDefault() => new(
        Vector2.Zero, 0f, 0f, AlsGait.Running, AlsStance.Standing,
        AlsRotationMode.LookingDirection, 0);
}
```

Add `AlsAnimationState { Grounded, JumpStart, FallLoop, LandRecovery }`. Extend `AlsFrameInput` with `Command`, `CharacterYaw`, `MaxAcceleration`, `MaxBrakingDeceleration`, `JumpAccepted`, and make its `Stance` field explicitly actual stance. Extend runtime state with `ActualGait`, `PreviousLocomotionState`, `GroundedEntrySpeed`, `SmoothedLocalVelocity`, `SmoothedLocalAcceleration`, `SmoothedLean`, and `LandingRecoveryTime`. Extend result with:

```csharp
public AlsGait ActualGait;
public AlsStance ActualStance;
public AlsRotationMode ActualRotationMode;
public AlsAnimationState AnimationState;
public Vector2 BlendCoordinates;
public float Stride;
public float PlayRate;
public Vector2 Lean;
public float AnimationPhase;
public float TargetYaw;
```

Append every new result field to `AlsResultDigest.Append()` in declaration order. Update all existing constructor call sites using named arguments so field order cannot silently corrupt tests.

- [ ] **Step 4: Run core tests**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj`

Expected: all existing and new tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core/Contracts src/Als.Core/Diagnostics tests/Als.Core.Tests
git commit -m "feat: add P3 locomotion contracts"
```

### Task 3: Implement Settings and Command Resolution

**Files:**
- Create: `src/Als.Core/Locomotion/AlsLocomotionSettings.cs`
- Create: `src/Als.Core/Locomotion/AlsLocomotionCommandResolver.cs`
- Create: `tests/Als.Core.Tests/AlsLocomotionCommandResolverTests.cs`
- Create: `tests/Als.Core.Tests/P3TestSettings.cs`
- Modify: `tests/Als.Core.Tests/Als.Core.Tests.csproj`

- [ ] **Step 1: Write failing resolver tests**

```csharp
[Theory]
[InlineData(0f, 2f, 0f, 0f, -1f)]
[InlineData(2f, 0f, 1.5707964f, 0f, -1f)]
public void MovementAxesAreClampedAndRotatedByViewYaw(
    float x, float y, float viewYaw, float expectedX, float expectedZ)
{
    var command = AlsLocomotionCommand.CreateDefault() with
    {
        MovementAxes = new Vector2(x, y),
        ViewYaw = viewYaw,
    };
    var resolved = AlsLocomotionCommandResolver.Resolve(command, AlsStance.Standing);

    Assert.InRange(resolved.WorldDirection.Length(), 0.9999f, 1.0001f);
    Assert.Equal(expectedX, resolved.WorldDirection.X, 4);
    Assert.Equal(expectedZ, resolved.WorldDirection.Z, 4);
}

[Fact]
public void CrouchingRejectsSprint()
{
    var command = AlsLocomotionCommand.CreateDefault() with { RequestedGait = AlsGait.Sprinting };
    var resolved = AlsLocomotionCommandResolver.Resolve(command, AlsStance.Crouching);
    Assert.Equal(AlsGait.Running, resolved.MaxAllowedGait);
}

[Fact]
public void LookingDirectionSprintRequiresInputWithinFiftyDegrees()
{
    var command = AlsLocomotionCommand.CreateDefault() with
    {
        MovementAxes = new Vector2(0f, 1f),
        RequestedGait = AlsGait.Sprinting,
        RequestedRotationMode = AlsRotationMode.LookingDirection,
        ViewYaw = 0f,
    };
    Assert.Equal(AlsGait.Sprinting,
        AlsLocomotionCommandResolver.Resolve(command, AlsStance.Standing).MaxAllowedGait);
    Assert.Equal(AlsGait.Running,
        AlsLocomotionCommandResolver.Resolve(command with { ViewYaw = MathF.PI }, AlsStance.Standing).MaxAllowedGait);
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter FullyQualifiedName~AlsLocomotionCommandResolverTests`

Expected: FAIL because resolver/settings do not exist.

- [ ] **Step 3: Implement the frozen settings and resolver**

Use meters and radians in C#. `AlsLocomotionSettings.Load()` strictly reads
`assets/config/p3_locomotion_settings.json`, converts it once during initialization, and returns a frozen value.
The settings records must expose:

```csharp
public readonly record struct AlsDirectionalSpeeds(float Forward, float Sideways, float Backward);

public readonly record struct AlsStanceSpeeds(
    AlsDirectionalSpeeds Walking,
    AlsDirectionalSpeeds Running,
    AlsDirectionalSpeeds Sprinting);

public readonly record struct AlsResolvedLocomotionCommand(
    Vector3 WorldDirection,
    float InputAmount,
    AlsGait MaxAllowedGait,
    AlsStance RequestedStance,
    AlsRotationMode RotationMode,
    byte JumpPressed);
```

`Resolve()` clamps axes length to one, maps `(x, y)` to Godot `(x, 0, -y)`, rotates it around Y by `ViewYaw`, rejects sprint while crouched or aiming, and applies the fixed `< 50 degrees` Looking Direction sprint condition. Zero input must return `Vector3.Zero` without normalization.

Link the generated settings into the test output and load them once outside the hot path:

```xml
<None Include="..\..\assets\config\p3_locomotion_settings.json"
      Link="Fixtures\P3\p3_locomotion_settings.json"
      CopyToOutputDirectory="PreserveNewest" />
```

```csharp
internal static class P3TestSettings
{
    public static readonly AlsLocomotionSettings Reference = AlsLocomotionSettings.Load(
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "P3", "p3_locomotion_settings.json")));
}
```

- [ ] **Step 4: Run focused and allocation tests**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsLocomotionCommandResolverTests|FullyQualifiedName~HotPathAllocationTests"`

Expected: PASS and zero measured allocations.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core/Locomotion tests/Als.Core.Tests/AlsLocomotionCommandResolverTests.cs
git commit -m "feat: resolve deterministic locomotion commands"
```

### Task 4: Implement Actual Gait and Directional Speed

**Files:**
- Create: `src/Als.Core/Locomotion/AlsLocomotionModel.cs`
- Create: `tests/Als.Core.Tests/AlsLocomotionGaitTests.cs`
- Create: `tests/Als.Core.Tests/AlsDirectionalSpeedTests.cs`

- [ ] **Step 1: Write boundary tests from the pinned C++ behavior**

```csharp
[Theory]
[InlineData(1.8499f, AlsGait.Walking)]
[InlineData(1.85f, AlsGait.Running)]
[InlineData(3.8499f, AlsGait.Running)]
[InlineData(3.85f, AlsGait.Sprinting)]
public void ActualGaitUsesTheTenCentimeterPerSecondMargin(float speed, AlsGait expected)
{
    Assert.Equal(expected, AlsLocomotionModel.CalculateActualGait(
        speed, 1.75f, 3.75f, AlsGait.Sprinting));
}

[Fact]
public void SprintCannotBecomeActualWhenMaxAllowedIsRunning()
{
    Assert.Equal(AlsGait.Running, AlsLocomotionModel.CalculateActualGait(
        6.5f, 1.75f, 3.75f, AlsGait.Running));
}

[Theory]
[InlineData(0f, 4f)]
[InlineData(90f, 4f)]
[InlineData(125f, 2f)]
[InlineData(180f, 2f)]
public void DirectionSpeedSamplesPinnedForwardBackwardCurve(float degrees, float expected)
{
    var speeds = new AlsDirectionalSpeeds(4f, 4f, 2f);
    Assert.Equal(expected, AlsLocomotionModel.SampleDirectionalSpeed(
        speeds, AlsMath.DegreesToRadians(degrees),
        AlsMath.DegreesToRadians(100f), AlsMath.DegreesToRadians(125f)), 4);
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsLocomotionGaitTests|FullyQualifiedName~AlsDirectionalSpeedTests"`

Expected: FAIL because production model is missing.

- [ ] **Step 3: Implement exact comparisons and interpolation**

```csharp
public static AlsGait CalculateActualGait(
    float speed, float maxWalkSpeed, float maxRunSpeed, AlsGait maxAllowedGait)
{
    if (speed < maxWalkSpeed + 0.1f)
    {
        return AlsGait.Walking;
    }
    if (speed < maxRunSpeed + 0.1f || maxAllowedGait != AlsGait.Sprinting)
    {
        return AlsGait.Running;
    }
    return AlsGait.Sprinting;
}

public static float SampleDirectionalSpeed(
    in AlsDirectionalSpeeds speeds, float localYaw, float forwardEnd, float backwardStart)
{
    var angle = MathF.Abs(AlsMath.NormalizeAngleRadians(localYaw));
    var amount = 1f - Math.Clamp((angle - forwardEnd) / (backwardStart - forwardEnd), 0f, 1f);
    return speeds.Backward + ((speeds.Forward - speeds.Backward) * amount);
}
```

The public `Evaluate()` method initializes the result from `AlsFrameInput`, computes horizontal speed, selects stance settings and calls these helpers. It must not read desired speed when calculating actual gait.

- [ ] **Step 4: Run all core tests**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core/Locomotion tests/Als.Core.Tests
git commit -m "feat: calculate P3 gait and directional speed"
```

### Task 5: Implement State, Rotation, Stride, Play Rate, Lean, and Phase

**Files:**
- Modify: `src/Als.Core/Math/AlsMath.cs`
- Modify: `src/Als.Core/Locomotion/AlsLocomotionModel.cs`
- Create: `tests/Als.Core.Tests/P3TestInput.cs`
- Create: `tests/Als.Core.Tests/AlsLocomotionStateTests.cs`
- Create: `tests/Als.Core.Tests/AlsLocomotionRotationTests.cs`
- Create: `tests/Als.Core.Tests/AlsLocomotionAnimationParameterTests.cs`
- Modify: `tests/Als.Core.Tests/HotPathAllocationTests.cs`

- [ ] **Step 1: Add failing transition and rotation tests**

Create the shared input builder first:

```csharp
internal static class P3TestInput
{
    public static readonly AlsFloorSample AirborneFloor = new(
        0, Vector3.UnitY, -1, Matrix4x4.Identity, Vector3.Zero);

    public static AlsFrameInput Grounded() =>
        AlsFrameInput.CreateDefault(new AlsFrameIdentity(1, 0, 1), 1f / 60f) with
        {
            Floor = new AlsFloorSample(
                1, Vector3.UnitY, -1, Matrix4x4.Identity, Vector3.Zero),
            Stance = AlsStance.Standing,
            RotationMode = AlsRotationMode.LookingDirection,
        };

    public static AlsFrameInput Moving() => Grounded() with
    {
        ActualVelocity = new Vector3(0f, 0f, -3.75f),
        ActualAcceleration = new Vector3(0f, 0f, -2f),
        InputDirection = new Vector3(0f, 0f, -1f),
        DesiredSpeed = 3.75f,
    };
}
```

```csharp
[Fact]
public void AcceptedJumpEntersJumpStartWithoutAnExtraFrame()
{
    var input = P3TestInput.Grounded() with { JumpAccepted = 1, Floor = P3TestInput.AirborneFloor };
    var state = default(AlsRuntimeState);
    var result = default(AlsFrameResult);
    AlsLocomotionModel.Evaluate(input, P3TestSettings.Reference, ref state, ref result);
    Assert.Equal(AlsLocomotionState.InAir, result.ResolvedLocomotionState);
    Assert.Equal(AlsAnimationState.JumpStart, result.AnimationState);
}

[Fact]
public void LandingRecoveryStartsOnTheGroundedFrame()
{
    var state = new AlsRuntimeState { LocomotionState = AlsLocomotionState.InAir };
    var result = default(AlsFrameResult);
    AlsLocomotionModel.Evaluate(P3TestInput.Grounded(), P3TestSettings.Reference, ref state, ref result);
    Assert.Equal(AlsLocomotionState.Grounded, result.ResolvedLocomotionState);
    Assert.Equal(AlsAnimationState.LandRecovery, result.AnimationState);
}

[Theory]
[InlineData(AlsRotationMode.VelocityDirection, 1.5707964f)]
[InlineData(AlsRotationMode.LookingDirection, 0f)]
[InlineData(AlsRotationMode.Aiming, -1.5707964f)]
public void RotationModeSelectsExpectedTargetYaw(AlsRotationMode mode, float expected)
{
    var input = P3TestInput.Moving() with
    {
        RotationMode = mode,
        ViewRotation = Quaternion.Identity,
        AimRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f),
        ActualVelocity = Vector3.UnitX,
    };
    var state = default(AlsRuntimeState);
    var result = default(AlsFrameResult);
    AlsLocomotionModel.Evaluate(input, P3TestSettings.Reference, ref state, ref result);
    Assert.Equal(expected, result.TargetYaw, 4);
}
```

- [ ] **Step 2: Run and verify behavioral failures**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter "FullyQualifiedName~AlsLocomotionStateTests|FullyQualifiedName~AlsLocomotionRotationTests|FullyQualifiedName~AlsLocomotionAnimationParameterTests"`

Expected: FAIL on state/target yaw/animation parameter assertions.

- [ ] **Step 3: Implement the production evaluation order**

Within `Evaluate()` use this exact order:

```text
1. derive Grounded/InAir from input.Floor
2. detect previous -> current physical transition
3. select JumpStart/FallLoop/LandRecovery/Grounded animation state
4. calculate actual gait from actual horizontal speed
5. transform actual velocity and acceleration into character-local space
6. select and damp target yaw using shortest-angle interpolation
7. calculate stride, play rate, two-axis lean and continuous phase
8. copy all resolved values into AlsFrameResult
9. store current state as history for the next frame
```

Use `AlsMath.DamperExactAlpha(deltaTime, halfLife) = 1 - exp2(-deltaTime / halfLife)` and shortest-angle interpolation. Clamp standing play rate to `[0.0001, 3]`, crouching to `[0.0001, 2]`, and clamp stride/lean with settings values. `AnimationPhase` advances only for locomotion cycles and wraps to `[0, 1)`.

- [ ] **Step 4: Prove determinism and zero allocation**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj -c Release --filter "FullyQualifiedName~AlsLocomotion|FullyQualifiedName~HotPathAllocationTests"`

Expected: PASS with allocation delta `0` after warmup.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Core tests/Als.Core.Tests
git commit -m "feat: evaluate P3 locomotion state and parameters"
```

### Task 6: Add Strict Golden Trace Loading and Comparison

**Files:**
- Modify: `tools/schemas/als_locomotion_trace.schema.json`
- Create: `src/Als.Core/Locomotion/AlsLocomotionTrace.cs`
- Create: `tests/Als.Core.Tests/P3Fixture.cs`
- Create: `tests/Als.Core.Tests/AlsLocomotionGoldenTests.cs`
- Fixture: `tests/Als.Core.Tests/Fixtures/P3/trace_idle_gaits.json`
- Fixture: `tests/Als.Core.Tests/Fixtures/P3/trace_directions.json`
- Fixture: `tests/Als.Core.Tests/Fixtures/P3/trace_crouch_clearance.json`
- Fixture: `tests/Als.Core.Tests/Fixtures/P3/trace_rotation_modes.json`
- Fixture: `tests/Als.Core.Tests/Fixtures/P3/trace_jump_land.json`
- Modify: `tests/Als.Core.Tests/Als.Core.Tests.csproj`

- [ ] **Step 1: Write failing schema and comparator tests**

Define the fixture locator explicitly:

```csharp
internal static class P3Fixture
{
    public static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", name);
}
```

```csharp
[Theory]
[InlineData("trace_idle_gaits.json")]
[InlineData("trace_directions.json")]
[InlineData("trace_crouch_clearance.json")]
[InlineData("trace_rotation_modes.json")]
[InlineData("trace_jump_land.json")]
public void ReferenceTraceMatchesTheProductionModel(string fixture)
{
    var trace = AlsLocomotionTrace.Load(P3Fixture.Path(fixture));
    Assert.Equal("b754d6f0f2bb03741d301f8fb88077ebfe561e17", trace.ReferenceCommit);
    Assert.Empty(AlsLocomotionTrace.Compare(trace, P3TestSettings.Reference));
}

[Fact]
public void EnumMismatchIsNeverHiddenByNumericTolerance()
{
    var trace = AlsLocomotionTrace.Load(P3Fixture.Path("trace_idle_gaits.json"));
    trace.Frames[0] = trace.Frames[0] with { ActualGait = AlsGait.Sprinting };
    Assert.Contains(AlsLocomotionTrace.Compare(trace, P3TestSettings.Reference),
        issue => issue.Field == "actualGait" && issue.Frame == 0);
}
```

- [ ] **Step 2: Run and verify missing schema/fixture failures**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter FullyQualifiedName~AlsLocomotionGoldenTests`

Expected: FAIL because trace loader and fixtures are missing.

- [ ] **Step 3: Implement strict trace DTOs and tolerances**

The schema requires `schemaVersion`, `referenceCommit`, `fixedDeltaTime`, `sequence`, and `frames`, sets `additionalProperties: false` at every object, and requires every frame field from design section 10.1. It locks exact frame counts by sequence name: `idle_gaits=240`, `directions=240`, `crouch_clearance=210`, `rotation_modes=240`, and `jump_land=240`. The comparer must use exact equality for enum/bool/frame/animation ID, `0.001` for metric fields including the m/s `blendCoordinates`, `0.0001` for normalized stride/play-rate/lean/phase, and shortest-angle `0.1` degree for yaw. Configure fixtures to copy to output:

```xml
<ItemGroup>
  <None Update="Fixtures\P3\*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Do not hand-author expected numeric frame arrays. Because the declared execution order runs Task 7 first, all five files
must already exist and carry the locked SHA; a missing file is a hard failure rather than a reason to synthesize data.

- [ ] **Step 4: Run all five golden comparisons**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter FullyQualifiedName~AlsLocomotionGoldenTests`

Expected: five fixture theories PASS and no comparison issues.

- [ ] **Step 5: Commit**

```powershell
git add tools/schemas/als_locomotion_trace.schema.json src/Als.Core/Locomotion tests/Als.Core.Tests
git commit -m "test: add P3 ALS behavior golden traces"
```

### Task 7: Generate Golden Traces from Unreal

**Files:**
- Create: `tools/unreal/AlsLocomotionTrace/AlsLocomotionTrace.uplugin`
- Create: `tools/unreal/AlsLocomotionTrace/Source/AlsLocomotionTrace/AlsLocomotionTrace.Build.cs`
- Create: `tools/unreal/AlsLocomotionTrace/Source/AlsLocomotionTrace/Public/AlsLocomotionTraceCommandlet.h`
- Create: `tools/unreal/AlsLocomotionTrace/Source/AlsLocomotionTrace/Private/AlsLocomotionTraceCommandlet.cpp`
- Create: `tools/unreal/AlsLocomotionTrace/Source/AlsLocomotionTrace/Private/AlsTraceCharacter.h`
- Create: `tools/unreal/AlsLocomotionTrace/Source/AlsLocomotionTrace/Private/AlsTraceCharacter.cpp`
- Create: `scripts/generate-p3-golden.ps1`
- Modify: `scripts/prepare-p3-reference.ps1`
- Create: `assets/config/p3_locomotion_settings.json`
- Create: `tools/schemas/als_locomotion_trace.schema.json`
- Create: `tests/Als.Core.Tests/Fixtures/P3/trace_idle_gaits.json`
- Create: `tests/Als.Core.Tests/Fixtures/P3/trace_directions.json`
- Create: `tests/Als.Core.Tests/Fixtures/P3/trace_crouch_clearance.json`
- Create: `tests/Als.Core.Tests/Fixtures/P3/trace_rotation_modes.json`
- Create: `tests/Als.Core.Tests/Fixtures/P3/trace_jump_land.json`

- [ ] **Step 1: Add a failing ready-check invocation**

Run:

```powershell
pwsh -NoProfile -File scripts/generate-p3-golden.ps1 `
  -UnrealEditorCmd 'D:\UnrealEngine\Engine\Binaries\Win64\UnrealEditor-Cmd.exe' `
  -UProject 'D:\AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject' `
  -ReadyCheck
```

Expected: FAIL because the trace plugin/commandlet does not exist.

- [ ] **Step 2: Add the trace plugin and derived reference actor**

`AlsLocomotionTrace.Build.cs` depends on `Core`, `CoreUObject`, `Engine`, `Json`, `JsonUtilities`, and `ALS`. `AAlsTraceCharacter` derives from `AAlsCharacter` and exposes a const snapshot method for protected animation/locomotion state without changing ALS source behavior. The commandlet must:

```text
- reject an ALS module whose git SHA does not match the lock
- force 60 Hz and run exactly the five named sequences
- drive public desired gait/stance/rotation/aiming APIs and movement input
- tick the real character and UAlsAnimationInstance
- serialize post-tick physical evidence as `physicalActual`
- serialize native AnimInstance observations as non-gating `nativeActual`; label the commandlet phase as `synthesizedAnimationPhase`, and the physical jump/airborne/landing-derived audit enum as `observedAnimationState`
- lock native `jump_land` LandRecovery to frames 81..93 (13 frames) independently of portExpected frames 81..92 (12 frames)
- evaluate a stateful, independent C++ port oracle from the same physical evidence and serialize all model outputs as `portExpected`
- permit native observations and port expectations to differ; only `portExpected` is a golden pass/fail oracle
- serialize the resolved movement/animation settings as `p3_locomotion_settings.json`
- convert centimeters to meters and degrees to radians at the boundary
- sort JSON properties and sequence files deterministically
- write to a temporary directory before replacing repository fixtures
```

`-ReadyCheck` validates module load, settings assets, animation instance class, five sequences, output writability, and commit/patch hashes without writing fixtures. It emits `P3_TRACE_READY_OK`.

- [ ] **Step 3: Implement the guarded generation script**

The script accepts `-UnrealEditorCmd`, `-UProject`, `-ReferenceRoot`, `-ProjectRoot`, and `-ReadyCheck`. It calls
`prepare-p3-reference.ps1`, synchronizes only the repository-owned trace plugin into
`D:\AdvancedLocomotionSystemV\Plugins\AlsLocomotionTrace` while the ALS plugin remains the validated junction,
builds the editor target, invokes `-run=AlsLocomotionTrace`, validates all generated JSON against the schema,
atomically replaces the settings and five trace fixtures, and refuses replacement when the locked SHA or patch hash differs.

- [ ] **Step 4: Generate twice and compare byte-for-byte**

Run the generation command twice, preserving the first output under a temporary directory, then run:

```powershell
Compare-Object `
  (Get-FileHash "$firstOutput\*.json" -Algorithm SHA256 | Sort-Object Path | Select-Object -ExpandProperty Hash) `
  (Get-FileHash 'tests\Als.Core.Tests\Fixtures\P3\*.json' -Algorithm SHA256 | Sort-Object Path | Select-Object -ExpandProperty Hash)
```

Expected: no `Compare-Object` output and
`P3_TRACE_GENERATION_OK sequences=5 commit=b754d6f0f2bb03741d301f8fb88077ebfe561e17`.

- [ ] **Step 5: Validate generated artifacts and commit**

Run: `pwsh -NoProfile -File scripts/generate-p3-golden.ps1 -UnrealEditorCmd $unrealEditorCmd -UProject $uProject -ReadyCheck`

Expected: `P3_TRACE_READY_OK` and five trace files plus one settings file with the locked SHA. The .NET golden comparison runs in Task 6 after its loader exists.

```powershell
git add tools/unreal/AlsLocomotionTrace scripts assets/config/p3_locomotion_settings.json tools/schemas/als_locomotion_trace.schema.json tests/Als.Core.Tests/Fixtures/P3
git commit -m "feat: generate P3 Unreal locomotion traces"
```

### Task 8: Implement the Main-Thread Godot Character Motor

**Files:**
- Create: `src/Als.Godot/Locomotion/AlsCharacterMotor.cs`
- Create: `src/Als.Godot/Locomotion/AlsMotorSettings.cs`
- Create: `src/Als.Godot/Locomotion/AlsReplayInputAdapter.cs`
- Create: `src/Als.Godot/Locomotion/AlsMotorReplay.cs`
- Create: `scenes/tests/p3a_motor_smoke.tscn`

- [ ] **Step 1: Add a failing headless motor smoke**

Create the scene with a `P3aMotorSmoke` root script that runs these assertions and exits nonzero on failure:

```text
flat movement reaches non-zero actual velocity
released input decelerates rather than stops instantly
jump publishes InAir and JumpAccepted on the same frame
landing publishes Grounded exactly once
crouch keeps the capsule foot position fixed
blocked uncrouch keeps ActualStance=Crouching
clear uncrouch restores ActualStance=Standing
```

Run: `& $godotExe --headless --path D:\GodotALS res://scenes/tests/p3a_motor_smoke.tscn`

Expected: nonzero exit because `AlsCharacterMotor` is absent.

- [ ] **Step 2: Implement motor ownership and snapshot contract**

`AlsCharacterMotor` derives from `CharacterBody3D`, sets `ProcessThreadGroup=MainThread` and order `0`, and exposes:

```csharp
public void Configure(in AlsMotorSettings settings, IAlsLocomotionCommandSource source);
public AlsFrameInput Step(long frameId, int characterId, int generation, float deltaTime);
```

`Step()` resolves the command, performs the crouch clearance `ShapeCast3D`, updates horizontal velocity with acceleration/braking, applies gravity or accepted jump velocity, calls `MoveAndSlide()`, reads actual transform/velocity/floor, calculates actual acceleration from previous actual velocity, and returns the immutable frame input. No worker result may modify motor position or velocity.

- [ ] **Step 3: Make the smoke pass**

Run: `& $godotExe --headless --path D:\GodotALS res://scenes/tests/p3a_motor_smoke.tscn`

Expected: `GODOT_ALS_P3A_MOTOR_OK cases=7` and exit `0`.

- [ ] **Step 4: Verify no Godot APIs leaked into Core**

Run: `dotnet test tests/Als.Core.Tests/Als.Core.Tests.csproj --filter FullyQualifiedName~CoreAssemblyDoesNotReferenceGodot`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Locomotion scenes/tests/p3a_motor_smoke.tscn
git commit -m "feat: add P3 CharacterBody locomotion motor"
```

### Task 9: Add the P3A Single/Parallel Replay Harness

**Files:**
- Create: `src/Als.Godot/Locomotion/P3aLocomotionHarness.cs`
- Create: `src/Als.Godot/Locomotion/AlsP3aHarnessContext.cs`
- Create: `src/Als.Godot/Locomotion/AlsP3aWorkerRoot.cs`
- Create: `src/Als.Godot/Locomotion/AlsP3aCommitStage.cs`
- Create: `scenes/tests/p3a_locomotion_harness.tscn`
- Create: `scripts/verify-p3a.ps1`

- [ ] **Step 1: Write the verification script before the harness**

The marker regex is:

```powershell
$markerPattern = 'GODOT_ALS_P3A_OK mode=(single|parallel) characters=(1|10) warmup=120 frames=600 digest=([0-9A-F]{16}) missing=(\d+) stale=(\d+) generation=(\d+) off_main=(\d+) lag=(\d+) allocations=(\d+)'
```

The script runs `(1,10) x (single,parallel)`, requires identical single/parallel digests per character count, `missing/stale/generation/lag/allocations=0`, `off_main=0` for single, and `off_main=characterCount` for parallel.

- [ ] **Step 2: Run and verify the missing scene fails**

Run: `pwsh -NoProfile -File scripts/verify-p3a.ps1 -GodotExecutable $godotExe -SkipRegression`

Expected: FAIL because `p3a_locomotion_harness.tscn` does not exist.

- [ ] **Step 3: Implement the ordered replay pipeline**

The harness reuses `AlsFrameExchange` and `AlsSlotRegistry`. Main order `0` steps every motor and publishes that same frame's post-move snapshot. Worker order `1` calls `AlsLocomotionModel.Evaluate()` and records its thread affinity. Main order `2` consumes the same `FrameId`, rejects missing/stale/generation mismatch, verifies `result.Identity.FrameId == motor snapshot FrameId`, appends `AlsResultDigest`, and replaces character zero once during measurement to exercise generation reuse.

Allocation counters separately cover gather/motor, model, exchange, and commit after the 120-frame warmup. The harness runs 720 total physics frames but prints `frames=600` measurement frames.

- [ ] **Step 4: Run the P3A matrix**

Run: `pwsh -NoProfile -File scripts/verify-p3a.ps1 -GodotExecutable $godotExe -SkipRegression`

Expected: four `GODOT_ALS_P3A_OK` markers followed by
`P3A_FOCUSED_VERIFICATION_OK regression=skipped`. Only the default non-Skip gate may emit
`P3A_VERIFICATION_OK` after the complete regression closure.

- [ ] **Step 5: Commit**

```powershell
git add src/Als.Godot/Locomotion scenes/tests/p3a_locomotion_harness.tscn scripts/verify-p3a.ps1
git commit -m "test: add P3A locomotion replay gate"
```

### Task 10: Close P3A with Regression and Documentation

**Files:**
- Modify: `scripts/verify-p3a.ps1`
- Create: `docs/architecture/p3a-deterministic-locomotion.md`
- Modify: `README.md` if present; otherwise do not create a repository overview solely for this task

- [ ] **Step 1: Make regression execution mandatory by default**

After the P3A matrix, `verify-p3a.ps1` runs:

```powershell
& (Join-Path $ProjectRoot 'scripts\verify-p2b.ps1') -GodotExecutable $GodotExecutable
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $ProjectRoot 'scripts\verify-p1.ps1') -GodotExecutable $GodotExecutable
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $ProjectRoot 'scripts\verify-p0.ps1') -GodotExecutable $GodotExecutable
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test (Join-Path $ProjectRoot 'GodotALS.sln') -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

`-SkipRegression` remains only for focused red/green development and is forbidden for completion evidence.

- [ ] **Step 2: Document verified ownership and commands**

The architecture note records exact reference SHA, unit conversions, main/worker/commit ordering, why UE physics is not compared to Godot physics, marker format, fixture regeneration guard, and the four matrix rows. It must also distinguish the compiled `ExportRelease` artifact from the executed optimized `Debug`/`TOOLS` editor-host assembly and record that full exported-runtime verification remains an environment boundary when Godot export templates are unavailable.

- [ ] **Step 3: Run the complete P3A gate**

Run: `pwsh -NoProfile -File scripts/verify-p3a.ps1 -GodotExecutable $godotExe`

Expected: `P3A_VERIFICATION_OK`, `P2B_VERIFICATION_OK`, `P1_VERIFICATION_OK`, `P0_VERIFICATION_OK`, all Release tests PASS, and no `SCRIPT ERROR`/`ERROR:` lines.

- [ ] **Step 4: Check repository boundaries**

Run: `git diff --check`

Run: `git status --short`

Expected: only the intended P3A documentation/script changes before commit; no external reference clone, UE build products, `.godot`, `bin`, or `obj` files are tracked.

- [ ] **Step 5: Commit**

```powershell
git add scripts/verify-p3a.ps1 docs/architecture/p3a-deterministic-locomotion.md
git commit -m "docs: close P3A deterministic locomotion"
```

P3A is complete only after the full command in Step 3 passes from the feature worktree. Proceed to the separate P3B plan only after merging or rebasing that verified P3A result.
