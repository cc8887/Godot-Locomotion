using System.Numerics;
using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class ContractLayoutTests
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceGodot()
    {
        var references = typeof(AlsFrameInput).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            name => name.Name?.StartsWith("Godot", StringComparison.Ordinal) == true);
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
        Assert.Equal(0f, command.ViewYaw);
        Assert.Equal(0f, command.AimYaw);
        Assert.Equal(AlsGait.Running, command.RequestedGait);
        Assert.Equal(AlsStance.Standing, command.RequestedStance);
        Assert.Equal(AlsRotationMode.LookingDirection, command.RequestedRotationMode);
        Assert.Equal((byte)0, command.JumpPressed);
    }

    [Fact]
    public void IdentityRequiresNonNegativeFrameAndPositiveGeneration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsFrameIdentity(-1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsFrameIdentity(0, 1, 0));
        Assert.Equal(new AlsFrameIdentity(12, 3, 4), new AlsFrameIdentity(12, 3, 4));
    }
}
