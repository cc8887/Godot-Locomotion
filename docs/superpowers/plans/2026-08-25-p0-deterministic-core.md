# P0 确定性核心 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立可由 Godot 4.7.2 .NET 加载的独立工程，并完成不依赖 Godot Node 的帧合同、定长事件缓冲、双缓冲交换、generation 生命周期和确定性数学核心。

**Architecture:** 根目录 Godot C# 工程只包含 `Als.Godot` 适配代码，并引用纯 `net8.0` 的 `Als.Core`。所有跨线程帧类型均为固定布局的值类型；`AlsFrameExchange` 预分配双缓冲并用发布帧号形成 acquire/release 边界；角色槽位通过 generation 防止旧结果污染复用后的角色。

**Tech Stack:** Godot 4.7.2 .NET、C# 12、.NET 8、System.Numerics、xUnit 2.9.3、Microsoft.NET.Test.Sdk 18.9.0、PowerShell、Git

---

## 文件职责

```text
project.godot                              Godot 项目配置和 60 Hz physics
GodotALS.csproj                            Godot C# 入口，仅编译 Als.Godot
GodotALS.sln                               主工程、核心库和测试工程
Directory.Build.props                     统一 net8/C#12/nullable/warnings 规则
.editorconfig                              文本和 C# 格式规则
.gitignore                                 Godot、.NET、IDE 和生成资产忽略规则

src/Als.Godot/HeadlessSmoke.cs             Godot 加载和核心程序集边界冒烟检查
scenes/tests/headless_smoke.tscn           headless 主场景

src/Als.Core/Als.Core.csproj               无 Godot 依赖的确定性核心库
src/Als.Core/Contracts/AlsEnums.cs          固定宽度运行时枚举
src/Als.Core/Contracts/AlsFrameIdentity.cs  Frame/Character/Generation 标识
src/Als.Core/Contracts/AlsSpatialSamples.cs 地面、脚部和 Mantle 固定数据
src/Als.Core/Contracts/AlsFrameInput.cs     主线程发布的不可变输入
src/Als.Core/Contracts/AlsRuntimeState.cs   每角色 worker 独占状态
src/Als.Core/Contracts/AlsRootMotionDelta.cs Root Motion 值对象
src/Als.Core/Contracts/AlsFrameResult.cs    worker 发布的结果
src/Als.Core/Events/AlsAnimationEvent.cs    类型化动画事件
src/Als.Core/Events/AlsEventBuffer.cs       16 项 inline 定长缓冲
src/Als.Core/Exchange/AlsFrameExchange.cs   每角色输入/结果双缓冲
src/Als.Core/Exchange/AlsSlotRegistry.cs    角色槽位和 generation 生命周期
src/Als.Core/Math/AlsMath.cs                角度和 exact damper 数学

tests/Als.Core.Tests/Als.Core.Tests.csproj  xUnit 测试工程
tests/Als.Core.Tests/ContractLayoutTests.cs 值类型和 Godot 依赖边界
tests/Als.Core.Tests/AlsEventBufferTests.cs 容量、顺序和清空行为
tests/Als.Core.Tests/AlsFrameExchangeTests.cs 发布、消费和过期拒绝
tests/Als.Core.Tests/AlsSlotRegistryTests.cs 复用和 generation 行为
tests/Als.Core.Tests/AlsMathTests.cs         数学边界与帧率无关性
tests/Als.Core.Tests/HotPathAllocationTests.cs 热身后零分配检查

scripts/verify-p0.ps1                       一键 restore/build/test/headless 验证
```

### Task 1：建立 Godot/.NET 工程骨架

**Files:**
- Create: `.gitignore`
- Create: `.editorconfig`
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `project.godot`
- Create: `GodotALS.csproj`
- Create: `src/Als.Core/Als.Core.csproj`
- Create: `tests/Als.Core.Tests/Als.Core.Tests.csproj`
- Create: `GodotALS.sln`

- [x] **Step 1：写入仓库基础配置**

`.gitignore` 写入以下完整内容：

```gitignore
.godot/
.mono/
bin/
obj/
.vs/
.vscode/
.idea/
*.user
*.suo
assets/generated/
artifacts/
benchmark-results/
```

`global.json` 固定使用已安装的 .NET 8 SDK，并允许向后选择最新 feature band：

```json
{
  "sdk": {
    "version": "8.0.100",
    "rollForward": "latestFeature"
  }
}
```

`Directory.Build.props`：

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

`.editorconfig`：

```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
indent_style = space
trim_trailing_whitespace = true

[*.{cs,csproj,props}]
indent_size = 4

[*.{json,godot,tscn,yml,yaml}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

- [x] **Step 2：创建三个工程文件**

`GodotALS.csproj`：

```xml
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <RootNamespace>GodotAls</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="src/Als.Godot/**/*.cs" />
    <ProjectReference Include="src/Als.Core/Als.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/Als.Core/Als.Core.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>GodotAls.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

`tests/Als.Core.Tests/Als.Core.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.9.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Als.Core/Als.Core.csproj" />
  </ItemGroup>
</Project>
```

- [x] **Step 3：创建 solution 并加入工程**

Run:

```powershell
dotnet new sln --name GodotALS
dotnet sln .\GodotALS.sln add .\GodotALS.csproj
dotnet sln .\GodotALS.sln add .\src\Als.Core\Als.Core.csproj
dotnet sln .\GodotALS.sln add .\tests\Als.Core.Tests\Als.Core.Tests.csproj
dotnet restore .\GodotALS.sln
```

Expected: restore 成功，solution 中包含 3 个项目。

- [x] **Step 4：写入 Godot 项目设置**

`project.godot`：

```ini
; Engine configuration file.
; Edit through the editor where practical.

config_version=5

[application]

config/name="Godot ALS"
run/main_scene="res://scenes/tests/headless_smoke.tscn"

[display]

window/size/viewport_width=1280
window/size/viewport_height=720

[dotnet]

project/assembly_name="GodotALS"

[physics]

common/physics_ticks_per_second=60

[rendering]

renderer/rendering_method="gl_compatibility"
renderer/rendering_method.mobile="gl_compatibility"
```

- [x] **Step 5：验证空工程构建并提交**

Run: `dotnet build .\GodotALS.sln --no-restore`

Expected: 3 个项目构建成功，0 warnings，0 errors。

```powershell
git add .gitignore .editorconfig global.json Directory.Build.props project.godot GodotALS.csproj GodotALS.sln src/Als.Core/Als.Core.csproj tests/Als.Core.Tests/Als.Core.Tests.csproj
git commit -m "build: scaffold Godot ALS solution"
```

### Task 2：定义固定宽度帧合同

**Files:**
- Test: `tests/Als.Core.Tests/ContractLayoutTests.cs`
- Create: `src/Als.Core/Contracts/AlsEnums.cs`
- Create: `src/Als.Core/Contracts/AlsFrameIdentity.cs`
- Create: `src/Als.Core/Contracts/AlsSpatialSamples.cs`
- Create: `src/Als.Core/Contracts/AlsRootMotionDelta.cs`
- Create: `src/Als.Core/Contracts/AlsFrameInput.cs`
- Create: `src/Als.Core/Contracts/AlsRuntimeState.cs`
- Create: `src/Als.Core/Contracts/AlsFrameResult.cs`

- [x] **Step 1：先写失败的合同测试**

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class ContractLayoutTests
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceGodot()
    {
        var references = typeof(AlsFrameInput).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, name => name.Name?.StartsWith("Godot", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void FrameContractsContainOnlyUnmanagedData()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameIdentity>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameInput>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsRuntimeState>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsFrameResult>());
    }

    [Fact]
    public void IdentityRequiresPositiveFrameAndGeneration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsFrameIdentity(-1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsFrameIdentity(0, 1, 0));
        Assert.Equal(new AlsFrameIdentity(12, 3, 4), new AlsFrameIdentity(12, 3, 4));
    }
}
```

- [x] **Step 2：运行测试确认失败**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter ContractLayoutTests`

Expected: 编译失败，提示 `AlsFrameInput`、`AlsFrameIdentity`、`AlsRuntimeState` 和 `AlsFrameResult` 不存在。

- [x] **Step 3：实现最小固定布局合同**

`AlsEnums.cs` 中的所有枚举显式使用 `byte`：

```csharp
namespace GodotAls.Core.Contracts;

public enum AlsGait : byte { Walking, Running, Sprinting }
public enum AlsStance : byte { Standing, Crouching }
public enum AlsRotationMode : byte { VelocityDirection, LookingDirection, Aiming }
public enum AlsLocomotionAction : byte { None, Mantling, Rolling, GettingUp }
public enum AlsLocomotionState : byte { Grounded, InAir, Mantling, Ragdoll, Recovering }
public enum AlsDriveMode : byte { MotorDriven, AnimationDriven, PhysicsDriven, RecoveryBlend }
public enum AlsRagdollState : byte { Inactive, Active, FaceUp, FaceDown }
public enum AlsAnimationQualityTier : byte { Tier0, Tier1, Tier2 }
```

`AlsFrameIdentity.cs`：

```csharp
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFrameIdentity
{
    public AlsFrameIdentity(long frameId, uint characterId, uint slotGeneration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameId);
        ArgumentOutOfRangeException.ThrowIfZero(slotGeneration);
        FrameId = frameId;
        CharacterId = characterId;
        SlotGeneration = slotGeneration;
    }

    public long FrameId { get; }
    public uint CharacterId { get; }
    public uint SlotGeneration { get; }
}
```

`AlsSpatialSamples.cs`：

```csharp
using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFloorSample(
    byte IsGrounded,
    Vector3 Normal,
    int PlatformId,
    Matrix4x4 PlatformTransform,
    Vector3 PlatformAngularVelocity);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFootHit(byte HasHit, Vector3 Position, Vector3 Normal);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsMantleProbeResult(byte HasTarget, Matrix4x4 TargetTransform, int PlatformId);
```

`AlsRootMotionDelta.cs`：

```csharp
using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsRootMotionDelta(Vector3 Translation, Quaternion Rotation)
{
    public static AlsRootMotionDelta Identity => new(Vector3.Zero, Quaternion.Identity);
}
```

`AlsFrameInput.cs`：

```csharp
using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsFrameInput(
    AlsFrameIdentity Identity,
    float DeltaTime,
    Matrix4x4 CharacterTransform,
    Vector3 ActualVelocity,
    Vector3 ActualAcceleration,
    Vector3 InputDirection,
    float DesiredSpeed,
    Quaternion ViewRotation,
    Quaternion AimRotation,
    AlsFloorSample Floor,
    AlsFootHit LeftFootHit,
    AlsFootHit RightFootHit,
    AlsMantleProbeResult MantleProbe,
    AlsGait RequestedGait,
    AlsStance Stance,
    AlsRotationMode RotationMode,
    AlsLocomotionAction RequestedAction,
    AlsDriveMode CurrentDriveMode,
    AlsRagdollState RagdollState,
    AlsAnimationQualityTier AnimationQualityTier)
{
    public static AlsFrameInput CreateDefault(AlsFrameIdentity identity, float deltaTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deltaTime);
        return new AlsFrameInput(
            identity,
            deltaTime,
            Matrix4x4.Identity,
            Vector3.Zero,
            Vector3.Zero,
            Vector3.Zero,
            0f,
            Quaternion.Identity,
            Quaternion.Identity,
            new AlsFloorSample(0, Vector3.UnitY, -1, Matrix4x4.Identity, Vector3.Zero),
            new AlsFootHit(0, Vector3.Zero, Vector3.UnitY),
            new AlsFootHit(0, Vector3.Zero, Vector3.UnitY),
            new AlsMantleProbeResult(0, Matrix4x4.Identity, -1),
            AlsGait.Walking,
            AlsStance.Standing,
            AlsRotationMode.LookingDirection,
            AlsLocomotionAction.None,
            AlsDriveMode.MotorDriven,
            AlsRagdollState.Inactive,
            AlsAnimationQualityTier.Tier0);
    }
}
```

`AlsRuntimeState.cs`：

```csharp
using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public struct AlsRuntimeState
{
    public AlsLocomotionState LocomotionState;
    public Vector3 SmoothedVelocity;
    public Vector3 SmoothedAcceleration;
    public float Lean;
    public byte LeftFootLocked;
    public byte RightFootLocked;
    public float TurnInPlaceTime;
    public float RotateInPlaceTime;
    public float ActionPlaybackTime;
    public float AnimationPhase;
    public float PreviousCurveValue;
    public AlsRagdollState PendingRecoveryState;
    public AlsRootMotionDelta LastCommittedRootMotionFeedback;
}
```

Task 2 的 `AlsFrameResult.cs`：

```csharp
using System.Numerics;
using System.Runtime.InteropServices;
namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public struct AlsFrameResult
{
    public AlsFrameIdentity Identity;
    public AlsLocomotionState ResolvedLocomotionState;
    public AlsDriveMode RequestedDriveMode;
    public AlsRootMotionDelta ProposedRootMotionDelta;
    public Vector3 PelvisTarget;
    public Vector3 LeftFootTarget;
    public Vector3 RightFootTarget;
    public Vector3 MovementIntent;
    public Quaternion RotationIntent;
    public long WorkerElapsedTicks;
    public int ErrorCode;

    public static AlsFrameResult CreateDefault(AlsFrameIdentity identity) => new()
    {
        Identity = identity,
        RequestedDriveMode = AlsDriveMode.MotorDriven,
        ProposedRootMotionDelta = AlsRootMotionDelta.Identity,
        RotationIntent = Quaternion.Identity,
    };
}
```

字符串和引用字段一律禁止。Task 3 再按明确补丁加入事件值字段。

- [x] **Step 4：运行合同测试确认通过**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter ContractLayoutTests`

Expected: 3 tests passed。

- [x] **Step 5：提交**

```powershell
git add src/Als.Core/Contracts tests/Als.Core.Tests/ContractLayoutTests.cs
git commit -m "feat: define unmanaged ALS frame contracts"
```

### Task 3：实现定长类型化事件缓冲

**Files:**
- Test: `tests/Als.Core.Tests/AlsEventBufferTests.cs`
- Create: `src/Als.Core/Events/AlsAnimationEvent.cs`
- Create: `src/Als.Core/Events/AlsEventBuffer.cs`
- Modify: `src/Als.Core/Contracts/AlsFrameResult.cs`

- [x] **Step 1：先写容量和顺序测试**

```csharp
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsEventBufferTests
{
    [Fact]
    public void PreservesInsertionOrderAndRejectsOverflow()
    {
        var buffer = new AlsEventBuffer();
        for (var index = 0; index < AlsEventBuffer.Capacity; index++)
        {
            Assert.True(buffer.TryAdd(new AlsAnimationEvent(index, index * 0.1f, 1f, AlsAnimationEventPhase.Trigger)));
        }

        Assert.False(buffer.TryAdd(new AlsAnimationEvent(99, 0f, 1f, AlsAnimationEventPhase.Trigger)));
        Assert.Equal(AlsEventBuffer.Capacity, buffer.Count);
        Assert.Equal(0, buffer[0].EventId);
        Assert.Equal(AlsEventBuffer.Capacity - 1, buffer[AlsEventBuffer.Capacity - 1].EventId);
    }

    [Fact]
    public void ClearMakesThePreallocatedStorageReusable()
    {
        var buffer = new AlsEventBuffer();
        Assert.True(buffer.TryAdd(new AlsAnimationEvent(7, 0.25f, 0.8f, AlsAnimationEventPhase.Begin)));
        buffer.Clear();
        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.TryAdd(new AlsAnimationEvent(8, 0.5f, 1f, AlsAnimationEventPhase.End)));
        Assert.Equal(8, buffer[0].EventId);
    }
}
```

- [x] **Step 2：运行测试确认失败**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter AlsEventBufferTests`

Expected: 编译失败，事件类型尚不存在。

- [x] **Step 3：使用 InlineArray 实现零分配缓冲**

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Events;

public enum AlsAnimationEventPhase : byte { Trigger, Begin, Tick, End }

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAnimationEvent(int EventId, float AnimationTime, float Weight, AlsAnimationEventPhase Phase);

[InlineArray(AlsEventBuffer.Capacity)]
internal struct AlsEventStorage
{
    private AlsAnimationEvent _element0;
}

[StructLayout(LayoutKind.Sequential)]
public struct AlsEventBuffer
{
    public const int Capacity = 16;
    private AlsEventStorage _storage;

    public int Count { get; private set; }

    public AlsAnimationEvent this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _storage[index];
        }
    }

    public bool TryAdd(AlsAnimationEvent animationEvent)
    {
        if (Count >= Capacity) return false;
        _storage[Count++] = animationEvent;
        return true;
    }

    public void Clear() => Count = 0;
}
```

对 `AlsFrameResult.cs` 应用以下修改：

```diff
 using System.Numerics;
 using System.Runtime.InteropServices;
+using GodotAls.Core.Events;

 namespace GodotAls.Core.Contracts;

 [StructLayout(LayoutKind.Sequential)]
 public struct AlsFrameResult
 {
     public AlsFrameIdentity Identity;
     public AlsLocomotionState ResolvedLocomotionState;
     public AlsDriveMode RequestedDriveMode;
     public AlsRootMotionDelta ProposedRootMotionDelta;
     public Vector3 PelvisTarget;
     public Vector3 LeftFootTarget;
     public Vector3 RightFootTarget;
     public Vector3 MovementIntent;
     public Quaternion RotationIntent;
+    public AlsEventBuffer TypedEvents;
     public long WorkerElapsedTicks;
     public int ErrorCode;
 }
```

- [x] **Step 4：运行事件与合同测试**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~AlsEventBufferTests|FullyQualifiedName~ContractLayoutTests"`

Expected: 5 tests passed，`AlsFrameResult` 仍不包含引用。

- [x] **Step 5：提交**

```powershell
git add src/Als.Core/Events src/Als.Core/Contracts/AlsFrameResult.cs tests/Als.Core.Tests/AlsEventBufferTests.cs
git commit -m "feat: add bounded animation event buffer"
```

### Task 4：实现每角色帧双缓冲

**Files:**
- Test: `tests/Als.Core.Tests/AlsFrameExchangeTests.cs`
- Create: `src/Als.Core/Exchange/AlsFrameExchange.cs`

- [x] **Step 1：先写发布、消费和错误身份测试**

```csharp
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;

namespace GodotAls.Core.Tests;

public sealed class AlsFrameExchangeTests
{
    [Fact]
    public void PublishesInputAndConsumesMatchingResultExactlyOnce()
    {
        var exchange = new AlsFrameExchange();
        var identity = new AlsFrameIdentity(2, 4, 1);
        var input = AlsFrameInput.CreateDefault(identity, 1f / 60f);
        exchange.PublishInput(input);

        Assert.True(exchange.TryReadInput(identity, out var readInput));
        Assert.Equal(identity, readInput.Identity);

        var result = AlsFrameResult.CreateDefault(identity);
        exchange.PublishResult(result);
        Assert.True(exchange.TryConsumeResult(identity, out var consumed));
        Assert.Equal(identity, consumed.Identity);
        Assert.False(exchange.TryConsumeResult(identity, out _));
    }

    [Fact]
    public void RejectsAResultFromAnOldGeneration()
    {
        var exchange = new AlsFrameExchange();
        exchange.PublishResult(AlsFrameResult.CreateDefault(new AlsFrameIdentity(5, 2, 1)));
        Assert.False(exchange.TryConsumeResult(new AlsFrameIdentity(5, 2, 2), out _));
    }

    [Fact]
    public void AlternatesSlotsWithoutReturningAnOlderFrame()
    {
        var exchange = new AlsFrameExchange();
        exchange.PublishInput(AlsFrameInput.CreateDefault(new AlsFrameIdentity(8, 1, 1), 1f / 60f));
        exchange.PublishInput(AlsFrameInput.CreateDefault(new AlsFrameIdentity(10, 1, 1), 1f / 60f));
        Assert.False(exchange.TryReadInput(new AlsFrameIdentity(8, 1, 1), out _));
        Assert.True(exchange.TryReadInput(new AlsFrameIdentity(10, 1, 1), out _));
    }
}
```

- [x] **Step 2：运行测试确认失败**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter AlsFrameExchangeTests`

Expected: 编译失败，`AlsFrameExchange` 或 default factory 不存在。

- [x] **Step 3：实现预分配双缓冲和发布标记**

```csharp
using System.Threading;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Exchange;

public sealed class AlsFrameExchange
{
    private const long UnpublishedFrame = -1;

    private readonly AlsFrameInput[] _inputs = new AlsFrameInput[2];
    private readonly AlsFrameResult[] _results = new AlsFrameResult[2];
    private readonly long[] _publishedInputFrames = [UnpublishedFrame, UnpublishedFrame];
    private readonly long[] _publishedResultFrames = [UnpublishedFrame, UnpublishedFrame];
    private readonly long[] _consumedResultFrames = [UnpublishedFrame, UnpublishedFrame];

    public void PublishInput(AlsFrameInput input)
    {
        var slot = Slot(input.Identity.FrameId);
        Volatile.Write(ref _publishedInputFrames[slot], UnpublishedFrame);
        _inputs[slot] = input;
        Volatile.Write(ref _publishedInputFrames[slot], input.Identity.FrameId);
    }

    public bool TryReadInput(AlsFrameIdentity identity, out AlsFrameInput input)
    {
        var slot = Slot(identity.FrameId);
        if (Volatile.Read(ref _publishedInputFrames[slot]) != identity.FrameId)
        {
            input = default;
            return false;
        }

        input = _inputs[slot];
        return input.Identity == identity;
    }

    public void PublishResult(AlsFrameResult result)
    {
        var slot = Slot(result.Identity.FrameId);
        Volatile.Write(ref _publishedResultFrames[slot], UnpublishedFrame);
        _results[slot] = result;
        Volatile.Write(ref _publishedResultFrames[slot], result.Identity.FrameId);
    }

    public bool TryConsumeResult(AlsFrameIdentity identity, out AlsFrameResult result)
    {
        var slot = Slot(identity.FrameId);
        if (Volatile.Read(ref _publishedResultFrames[slot]) != identity.FrameId ||
            Volatile.Read(ref _consumedResultFrames[slot]) == identity.FrameId)
        {
            result = default;
            return false;
        }

        result = _results[slot];
        if (result.Identity != identity)
        {
            result = default;
            return false;
        }

        Volatile.Write(ref _consumedResultFrames[slot], identity.FrameId);
        return true;
    }

    private static int Slot(long frameId) => (int)(frameId & 1L);
}
```

为 `AlsFrameInput` 和 `AlsFrameResult` 增加只填充身份、delta 和单位旋转的 `CreateDefault()` 工厂，测试和 Harness 用它构建完整有效的零值帧。

- [x] **Step 4：运行交换测试确认通过**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter AlsFrameExchangeTests`

Expected: 3 tests passed。

- [x] **Step 5：提交**

```powershell
git add src/Als.Core/Exchange/AlsFrameExchange.cs src/Als.Core/Contracts tests/Als.Core.Tests/AlsFrameExchangeTests.cs
git commit -m "feat: add per-character frame exchange"
```

### Task 5：实现角色槽位 generation 生命周期

**Files:**
- Test: `tests/Als.Core.Tests/AlsSlotRegistryTests.cs`
- Create: `src/Als.Core/Exchange/AlsSlotRegistry.cs`

- [x] **Step 1：先写复用和错误释放测试**

```csharp
using GodotAls.Core.Exchange;

namespace GodotAls.Core.Tests;

public sealed class AlsSlotRegistryTests
{
    [Fact]
    public void ReusingASlotChangesItsGeneration()
    {
        var registry = new AlsSlotRegistry(1);
        var first = registry.Acquire();
        Assert.True(registry.Release(first));
        var second = registry.Acquire();
        Assert.Equal(first.CharacterId, second.CharacterId);
        Assert.True(second.Generation > first.Generation);
        Assert.False(registry.IsCurrent(first));
        Assert.True(registry.IsCurrent(second));
    }

    [Fact]
    public void RejectsDuplicateReleaseAndCapacityOverflow()
    {
        var registry = new AlsSlotRegistry(1);
        var handle = registry.Acquire();
        Assert.Throws<InvalidOperationException>(() => registry.Acquire());
        Assert.True(registry.Release(handle));
        Assert.False(registry.Release(handle));
    }
}
```

- [x] **Step 2：运行测试确认失败**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter AlsSlotRegistryTests`

Expected: 编译失败，registry 类型不存在。

- [x] **Step 3：实现固定容量 registry**

```csharp
namespace GodotAls.Core.Exchange;

public readonly record struct AlsSlotHandle(uint CharacterId, uint Generation);

public sealed class AlsSlotRegistry
{
    private readonly uint[] _generations;
    private readonly bool[] _occupied;

    public AlsSlotRegistry(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _generations = new uint[capacity];
        _occupied = new bool[capacity];
    }

    public AlsSlotHandle Acquire()
    {
        for (var index = 0; index < _occupied.Length; index++)
        {
            if (_occupied[index]) continue;
            _occupied[index] = true;
            _generations[index] = _generations[index] == uint.MaxValue ? 1 : _generations[index] + 1;
            return new AlsSlotHandle((uint)index, _generations[index]);
        }

        throw new InvalidOperationException("No ALS character slot is available.");
    }

    public bool Release(AlsSlotHandle handle)
    {
        if (!IsCurrent(handle)) return false;
        _occupied[(int)handle.CharacterId] = false;
        return true;
    }

    public bool IsCurrent(AlsSlotHandle handle)
    {
        if (handle.CharacterId >= (uint)_occupied.Length) return false;
        var index = (int)handle.CharacterId;
        return _occupied[index] && _generations[index] == handle.Generation;
    }
}
```

- [x] **Step 4：运行测试确认通过**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter AlsSlotRegistryTests`

Expected: 2 tests passed。

- [x] **Step 5：提交**

```powershell
git add src/Als.Core/Exchange/AlsSlotRegistry.cs tests/Als.Core.Tests/AlsSlotRegistryTests.cs
git commit -m "feat: guard character slot reuse with generations"
```

### Task 6：实现确定性数学和零分配门禁

**Files:**
- Test: `tests/Als.Core.Tests/AlsMathTests.cs`
- Test: `tests/Als.Core.Tests/HotPathAllocationTests.cs`
- Create: `src/Als.Core/Math/AlsMath.cs`

- [x] **Step 1：先写数学行为测试**

```csharp
using GodotAls.Core.Math;

namespace GodotAls.Core.Tests;

public sealed class AlsMathTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(6.2831855f, 0f)]
    [InlineData(4.712389f, -1.5707964f)]
    public void NormalizeAngleUsesMinusPiToPi(float input, float expected)
    {
        Assert.Equal(expected, AlsMath.NormalizeAngleRadians(input), 5);
    }

    [Fact]
    public void ExactDamperIsStableAcrossSubsteps()
    {
        var oneStep = AlsMath.DamperExact(0f, 10f, 8f, 1f / 30f);
        var twoSteps = AlsMath.DamperExact(0f, 10f, 8f, 1f / 60f);
        twoSteps = AlsMath.DamperExact(twoSteps, 10f, 8f, 1f / 60f);
        Assert.Equal(oneStep, twoSteps, 5);
    }

    [Fact]
    public void ExactDamperRejectsNegativeDeltaTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsMath.DamperExact(0f, 1f, 1f, -0.1f));
    }
}
```

- [x] **Step 2：运行测试确认失败**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore --filter AlsMathTests`

Expected: 编译失败，`AlsMath` 不存在。

- [x] **Step 3：实现 exact damper 和角度归一化**

```csharp
namespace GodotAls.Core.Math;

public static class AlsMath
{
    public static float NormalizeAngleRadians(float angle)
    {
        var normalized = MathF.IEEERemainder(angle, MathF.Tau);
        return normalized == -MathF.PI ? MathF.PI : normalized;
    }

    public static float DamperExact(float current, float target, float smoothing, float deltaTime)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(smoothing);
        ArgumentOutOfRangeException.ThrowIfNegative(deltaTime);
        if (smoothing == 0f || deltaTime == 0f) return current;
        return target + ((current - target) * MathF.Exp(-smoothing * deltaTime));
    }
}
```

- [x] **Step 4：增加热路径零分配测试**

测试先热身，再测量 10,000 次事件缓冲与 frame exchange 操作：

```csharp
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Exchange;

namespace GodotAls.Core.Tests;

public sealed class HotPathAllocationTests
{
[Fact]
public void EventAndExchangeHotPathDoesNotAllocateAfterWarmup()
{
    var exchange = new AlsFrameExchange();
    Run(exchange, 100);
    var before = GC.GetAllocatedBytesForCurrentThread();
    Run(exchange, 10_000);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Assert.Equal(0, allocated);
}

private static void Run(AlsFrameExchange exchange, int iterations)
{
    var buffer = new AlsEventBuffer();
    for (var index = 0; index < iterations; index++)
    {
        var identity = new AlsFrameIdentity(index, 0, 1);
        var input = AlsFrameInput.CreateDefault(identity, 1f / 60f);
        exchange.PublishInput(input);
        exchange.TryReadInput(identity, out _);

        var result = AlsFrameResult.CreateDefault(identity);
        result.TypedEvents.TryAdd(new AlsAnimationEvent(1, 0.25f, 1f, AlsAnimationEventPhase.Trigger));
        exchange.PublishResult(result);
        exchange.TryConsumeResult(identity, out _);

        buffer.TryAdd(new AlsAnimationEvent(2, 0.5f, 1f, AlsAnimationEventPhase.Trigger));
        buffer.Clear();
    }
}
}
```

- [x] **Step 5：运行全部核心测试并提交**

Run: `dotnet test .\tests\Als.Core.Tests\Als.Core.Tests.csproj --no-restore`

Expected: 全部测试通过，zero-allocation 测试报告 0 B。

```powershell
git add src/Als.Core/Math tests/Als.Core.Tests/AlsMathTests.cs tests/Als.Core.Tests/HotPathAllocationTests.cs
git commit -m "feat: add deterministic ALS math and allocation gate"
```

### Task 7：加入 Godot headless 冒烟场景和统一验证脚本

**Files:**
- Create: `src/Als.Godot/HeadlessSmoke.cs`
- Create: `scenes/tests/headless_smoke.tscn`
- Create: `scripts/verify-p0.ps1`

- [x] **Step 1：创建会主动退出的 headless 场景**

`HeadlessSmoke.cs`：

```csharp
using Godot;
using GodotAls.Core.Contracts;

namespace GodotAls;

public partial class HeadlessSmoke : Node
{
    public override void _Ready()
    {
        var identity = new AlsFrameIdentity(0, 0, 1);
        GD.Print($"GODOT_ALS_P0_OK frame={identity.FrameId} generation={identity.SlotGeneration}");
        GetTree().Quit();
    }
}
```

`headless_smoke.tscn`：

```ini
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://src/Als.Godot/HeadlessSmoke.cs" id="1_smoke"]

[node name="HeadlessSmoke" type="Node"]
script = ExtResource("1_smoke")
```

- [x] **Step 2：编译并运行 Godot headless**

Run:

```powershell
dotnet build .\GodotALS.csproj --no-restore
& 'F:\下载\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path .
```

Expected: 进程退出码为 0，输出包含 `GODOT_ALS_P0_OK frame=0 generation=1`。

- [x] **Step 3：创建不提交本机路径的一键验证脚本**

`scripts/verify-p0.ps1` 接受必填 `-GodotExecutable` 参数：

```powershell
param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable not found: $GodotExecutable"
}

dotnet restore (Join-Path $ProjectRoot 'GodotALS.sln')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $ProjectRoot 'GodotALS.sln') --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test (Join-Path $ProjectRoot 'GodotALS.sln') --no-build --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$godotOutput = & $GodotExecutable --headless --path $ProjectRoot 2>&1
$godotOutput | Write-Output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not ($godotOutput -match 'GODOT_ALS_P0_OK')) {
    throw 'Godot smoke marker was not emitted.'
}

Write-Output 'P0_VERIFICATION_OK'
exit 0
```

- [x] **Step 4：运行完整 P0 门禁**

Run:

```powershell
.\scripts\verify-p0.ps1 -GodotExecutable 'F:\下载\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
```

Expected: restore、build、全部测试、Godot headless 均通过，脚本最终输出 `P0_VERIFICATION_OK`。

- [x] **Step 5：提交 P0 集成**

```powershell
git add src/Als.Godot/HeadlessSmoke.cs scenes/tests/headless_smoke.tscn scripts/verify-p0.ps1
git commit -m "test: add Godot P0 verification gate"
```

### Task 8：P0 完成检查和文档记录

**Files:**
- Create: `docs/architecture/p0-deterministic-core.md`
- Modify: `docs/superpowers/plans/2026-08-25-p0-deterministic-core.md`

- [x] **Step 1：记录实际工具链与核心合同**

文档必须记录：

- Godot 完整版本 `4.7.2.stable.mono.official.ed1daf0bf`；
- 实际使用的 .NET SDK；
- `Als.Core` 无 Godot 引用的验证结果；
- 双缓冲发布/消费规则；
- generation 生命周期；
- 当前事件容量 16 及溢出策略；
- 已知的 P1 接口前提。

- [x] **Step 2：勾选本计划中已完成步骤**

只将已经执行且验证通过的 `[ ]` 改为 `[x]`，未运行的步骤保持未完成。

- [x] **Step 3：再次运行完整验证**

Run:

```powershell
.\scripts\verify-p0.ps1 -GodotExecutable 'F:\下载\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
git diff --check
git status --short
```

Expected: `P0_VERIFICATION_OK`；diff 无空白错误；只有预期的文档修改未提交。

- [x] **Step 4：提交 P0 完成记录**

```powershell
git add docs/architecture/p0-deterministic-core.md docs/superpowers/plans/2026-08-25-p0-deterministic-core.md
git commit -m "docs: record P0 deterministic core"
```

- [x] **Step 5：最终审计**

Run:

```powershell
git status --short --branch
git log --oneline --decorate -10
```

Expected: `feature/p0-deterministic-core` 工作区干净；P0 由多个小提交组成；没有生成文件进入 Git。
