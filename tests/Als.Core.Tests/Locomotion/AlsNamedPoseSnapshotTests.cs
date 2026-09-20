using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsNamedPoseSnapshotTests
{
    private static readonly AlsFrameIdentity Capture = new(10, 1, 2), Frame = new(11, 1, 2);
    private static readonly AlsLocalPose[] Reference = [At(0), At(2), At(3)];
    private static AlsLocalPose At(float x) => new(new(x, 0, 0), Quaternion.Identity, Vector3.One);
    private static AlsNamedPoseSnapshotRuntime NewRuntime() => new("RagdollPose", "Mannequin", 1, 2, ["root", "foot_l"], [0, 1, -1], Reference);
    [Fact]
    public void SameMeshUsesMeshIndicesAndLeavesVirtualOrMissingBonesAtReference()
    {
        var runtime = NewRuntime(); var output = new AlsLocalPose[3]; var curves = new AlsInertialCurve[] { new(1) };
        var snapshot = new AlsNamedPoseSnapshot(Capture, "ragdollpose", "MANNEQUIN", ["names_are_ignored"], [At(7)]);
        runtime.Evaluate(Frame, snapshot, output, curves);
        Assert.Equal(new[] { At(7), At(2), At(3) }, output); Assert.False(curves[0].Present);
    }
    [Fact]
    public void DifferentMeshMapsNamesCaseInsensitivelyAndPreservesMissingBones()
    {
        var runtime = NewRuntime(); var output = new AlsLocalPose[3];
        runtime.Evaluate(Frame, new(Capture, "RagdollPose", "OtherMesh", ["FOOT_L", "root"], [At(9), At(8)]), output, []);
        Assert.Equal(new[] { At(8), At(9), At(3) }, output);
        runtime.Evaluate(Frame, new(Capture, "RagdollPose", "ThirdMesh", ["foot_l"], [At(5)]), output, []);
        Assert.Equal(new[] { At(0), At(5), At(3) }, output);
    }
    [Fact]
    public void MissingSnapshotUsesReferenceAndSnapshotCarriesNoAnimationCurves()
    {
        var runtime = NewRuntime(); var output = new[] { At(99), At(99), At(99) }; var curves = new AlsInertialCurve[] { new(1) };
        runtime.Evaluate(Frame, null, output, curves); Assert.Equal(Reference, output); Assert.False(curves[0].Present);
        runtime.Evaluate(Frame, new(Capture, "OtherName", "Mannequin", ["root"], [At(7)]), output, curves);
        Assert.Equal(Reference, output);
    }
    [Fact]
    public void PublishedSnapshotCopiesMutablePhysicsBuffersAndRetainsNativeValiditySemantics()
    {
        string[] names = ["root", "foot_l"]; var poses = new[] { At(7), At(8) };
        var snapshot = new AlsNamedPoseSnapshot(Capture, "RagdollPose", "Other", names, poses, false);
        names[0] = "unknown"; poses[0] = At(99);
        var output = new AlsLocalPose[3]; NewRuntime().Evaluate(Frame, snapshot, output, []);
        Assert.False(snapshot.IsValid); Assert.Equal(At(7), output[0]); Assert.Equal(At(8), output[1]);
    }
    [Theory] [InlineData(10, 1, 1)] [InlineData(10, 2, 2)] [InlineData(12, 1, 2)]
    public void RetiredForeignAndFutureCapturesAreRejectedBeforeWriting(long frame, uint character, uint generation)
    {
        var runtime = NewRuntime(); var output = new[] { At(99), At(99), At(99) };
        var snapshot = new AlsNamedPoseSnapshot(new(frame, character, generation), "RagdollPose", "Mannequin", ["root"], [At(7)]);
        Assert.Throws<ArgumentException>(() => runtime.Evaluate(Frame, snapshot, output, []));
        Assert.All(output, pose => Assert.Equal(At(99), pose));
    }
    [Fact]
    public void RepeatedNamedPoseEvaluationDoesNotAllocate()
    {
        var runtime = NewRuntime(); var output = new AlsLocalPose[3]; var curves = new AlsInertialCurve[2];
        var snapshot = new AlsNamedPoseSnapshot(Capture, "RagdollPose", "Other", ["foot_l", "root"], [At(7), At(8)]);
        for (var i = 0; i < 1000; i++) runtime.Evaluate(Frame, snapshot, output, curves);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++) runtime.Evaluate(Frame, snapshot, output, curves);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
    }
}
