using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Core.Tests;

public sealed class AlsJointMotorTargetTests
{
    private static readonly AlsPrecisePose Identity = AlsPrecisePose.Identity;
    private static AlsPrecisePose Rotation(Vector3 axis, float angle) => Identity with { Rotation = AlsQuaternion.FromAxisAngle(axis, angle) };
    [Fact]
    public void IntermediateGraphicsParentsUseLocalRotationsAndAuthoredConnectorOrder()
    {
        var middle = Rotation(Vector3.UnitY, .3f); var child = Rotation(Vector3.UnitX, .5f);
        var childFrame = Rotation(Vector3.UnitZ, -.2f); var parentFrame = Rotation(Vector3.UnitY, -.1f);
        var poses = new[] { Identity, Identity, middle with { Position = new(5, 9, 12), Scale = new(2, 3, 4) }, child };
        Assert.True(AlsJointMotorTarget.TryEvaluate(poses, [-1, 0, 1, 2], 3, 1, childFrame, parentFrame, out var actual));
        var expected = parentFrame.Rotation.Conjugate() * middle.Rotation * child.Rotation * childFrame.Rotation;
        Assert.InRange(System.Math.Abs(AlsQuaternion.Dot(actual, expected) - 1), 0, 1e-14);
        Assert.True(AlsJointMotorTarget.TryEvaluate(poses, [-1, 0, 1, 2], 3, 1,
            childFrame with { Position = new(100, 0, 0), Scale = new(2, 4, 3) }, parentFrame, out var withoutScale));
        Assert.Equal(actual, withoutScale);
    }
    [Fact]
    public void NativeRootAndCollapsedIntermediateParentSkipRulesArePreserved()
    {
        var poses = new[] { Identity, Rotation(Vector3.UnitY, .3f), Rotation(Vector3.UnitX, .5f) };
        Assert.True(AlsJointMotorTarget.TryEvaluate(poses, [-1, 0, 1], 1, 0, Identity, Identity, out _));
        Assert.False(AlsJointMotorTarget.TryEvaluate(poses, [-1, 0, 1], 2, 0, Identity, Identity, out var skipped));
        Assert.Equal(AlsQuaternion.Identity, skipped);
        Assert.False(AlsJointMotorTarget.TryEvaluate(poses, [-1, 0, 1], 0, 0, Identity, Identity, out _));
        var chain = new[] { Identity, Identity, Identity with { Scale = default }, poses[2] };
        Assert.False(AlsJointMotorTarget.TryEvaluate(chain, [-1, 0, 1, 2], 3, 1, Identity, Identity, out _));
        Assert.Throws<ArgumentException>(() => AlsJointMotorTarget.TryEvaluate(chain, [-1, 0, 3, 2], 3, 1, Identity, Identity, out _));
    }
    [Fact]
    public void TargetEvaluationDoesNotAllocate()
    {
        var poses = new[] { Identity, Identity, Rotation(Vector3.UnitZ, .3f), Rotation(Vector3.UnitX, .6f) };
        int[] parents = [-1, 0, 1, 2];
        for (var i = 0; i < 256; i++) AlsJointMotorTarget.TryEvaluate(poses, parents, 3, 1, Identity, Identity, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2048; i++) AlsJointMotorTarget.TryEvaluate(poses, parents, 3, 1, Identity, Identity, out _);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
