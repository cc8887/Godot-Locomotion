using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRigPoseAdapterTests
{
    private static AlsPrecisePose At(double x) => AlsPrecisePose.Identity with { Position = new(x, 0, 0) };
    private static AlsRigElementDefinition Bone(string name, string? parent, double local, double global) =>
        new(name, parent, false, new(At(local), At(global), At(local), At(global), null, null, null, null));
    private static AlsRigHierarchy Rig() => new([Bone("root", null, 10, 10), Bone("child", "root", 3, 13)]);

    [Fact]
    public void MismatchedTargetParentUsesRigLocalAndUnmappedVirtualKeepsSourceLocal()
    {
        var adapter = new AlsRigPoseAdapter(["root", null, "child"], [-1, 0, 1]);
        var source = new[] { At(1), At(7), At(99) }; var output = new AlsPrecisePose[3];
        adapter.ExportLocalPose(Rig(), source, 1, output);
        Assert.Equal(new[] { At(10), At(7), At(3) }, output);
    }

    [Fact]
    public void DirtyGlobalsMaterializeBeforeLocalExport()
    {
        var rig = Rig(); rig.Set("root", At(20)); rig.Set("child", At(25));
        var output = new AlsPrecisePose[2];
        new AlsRigPoseAdapter(["root", "child"], [-1, 0]).ExportLocalPose(rig, [At(0), At(0)], 1, output);
        Assert.Equal(new[] { At(20), At(5) }, output);
        Assert.False(rig.IsGlobalDirty("child"));
    }

    [Theory]
    [InlineData(0.25f, 4, 3)]
    [InlineData(0.5f, 6, 4)]
    [InlineData(0.75f, 8, 5)]
    public void PartialWeightBlendsLocalTargetsAndUnmappedTranslation(float alpha, double root, double child)
    {
        var rig = Rig(); rig.Set("child", At(6), local: true);
        var output = new AlsPrecisePose[3];
        new AlsRigPoseAdapter(["root", "child", null], [-1, 0, 1]).ExportLocalPose(rig, [At(2), At(2), At(7)], alpha, output);
        Assert.Equal(new[] { At(root), At(child), At(7) }, output);
    }

    [Fact]
    public void ZeroWeightDoesNotResolveMissingRigMapping()
    {
        var source = new[] { At(7) }; var output = new AlsPrecisePose[1];
        new AlsRigPoseAdapter(["absent"], [-1]).ExportLocalPose(Rig(), source, 1e-5f, output);
        Assert.Equal(source, output);
    }

    [Fact]
    public void MappingIsDefensivelyCopied()
    {
        string?[] mapping = ["root", "child"]; int[] parents = [-1, 0];
        var adapter = new AlsRigPoseAdapter(mapping, parents); mapping[1] = "absent"; parents[1] = 1;
        var output = new AlsPrecisePose[2]; adapter.ExportLocalPose(Rig(), [At(0), At(0)], 1, output);
        Assert.Equal(At(3), output[1]); Assert.Equal(2, adapter.BoneCount);
    }

    [Fact]
    public void ExportSupportsShiftedOverlappingSpans()
    {
        var buffer = new[] { At(2), At(7), At(9) };
        new AlsRigPoseAdapter(["root", null], [-1, 0]).ExportLocalPose(Rig(), buffer.AsSpan(0, 2), .5f, buffer.AsSpan(1, 2));
        Assert.Equal(new[] { At(2), At(6), At(7) }, buffer);
    }

    [Fact]
    public void MissingMappingFailurePreservesOutputAndNextExportCanSucceed()
    {
        var adapter = new AlsRigPoseAdapter(["root", "child"], [-1, 0]); var output = new[] { At(90), At(91) };
        var incomplete = new AlsRigHierarchy([Bone("root", null, 10, 10)]);
        Assert.Throws<ArgumentException>(() => adapter.ExportLocalPose(incomplete, [At(0), At(0)], 1, output));
        Assert.Equal(new[] { At(90), At(91) }, output);
        adapter.ExportLocalPose(Rig(), [At(0), At(0)], 1, output); Assert.Equal(new[] { At(10), At(3) }, output);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    public void InvalidAlphaPreservesOutput(float alpha)
    {
        var adapter = new AlsRigPoseAdapter(["root"], [-1]); var output = new[] { At(90) };
        Assert.Throws<ArgumentException>(() => adapter.ExportLocalPose(Rig(), [At(0)], alpha, output));
        Assert.Equal(At(90), output[0]);
    }

    [Fact]
    public void InvalidTopologyAndPoseAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AlsRigPoseAdapter([], []));
        Assert.Throws<ArgumentException>(() => new AlsRigPoseAdapter(["root"], []));
        Assert.Throws<ArgumentException>(() => new AlsRigPoseAdapter(["root"], [0]));
        Assert.Throws<ArgumentException>(() => new AlsRigPoseAdapter([" "], [-1]));
        var output = new[] { At(90) };
        Assert.Throws<ArgumentException>(() => new AlsRigPoseAdapter(["root"], [-1]).ExportLocalPose(Rig(),
            [At(double.NaN)], 1, output));
        Assert.Equal(At(90), output[0]);
    }

    [Fact]
    public void AdditiveTargetUsesShortestQuaternionPathAndMultiplicativeScale()
    {
        var basis = At(2) with { Scale = new(2, 0, -2) };
        var target = At(10) with { Rotation = new(0, 0, -1, 0), Scale = new(6, 7, -6) };
        var result = AlsPrecisePoseBlender.BlendAdditiveTarget(basis, target, .5f);
        Assert.Equal(new AlsDoubleVector(6, 0, 0), result.Position);
        Assert.Equal(new AlsDoubleVector(4, 0, -4), result.Scale);
        Assert.Equal(-System.Math.Sqrt(.5), result.Rotation.Z, 14);
        Assert.Equal(System.Math.Sqrt(.5), result.Rotation.W, 14);
    }

    [Fact]
    public void OppositeQuaternionRepresentationsPreserveOrientation()
    {
        var pose = At(3) with { Rotation = new(0, 0, 0, -1) };
        var result = AlsPrecisePoseBlender.BlendAdditiveTarget(pose, pose with { Rotation = AlsQuaternion.Identity }, .4f);
        Assert.Equal(pose.Position, result.Position); Assert.Equal(pose.Scale, result.Scale);
        Assert.Equal(1, System.Math.Abs(result.Rotation.W)); Assert.Equal(0, result.Rotation.X);
        Assert.Throws<ArgumentOutOfRangeException>(() => AlsPrecisePoseBlender.BlendAdditiveTarget(pose, pose, float.NaN));
    }
}

public sealed class AlsRigParentConstraintTests
{
    private static AlsPrecisePose At(double x) => AlsPrecisePose.Identity with { Position = new(x, 0, 0) };
    private static AlsRigHierarchy Rig() => new([
        new("root", null, false, new(At(10), At(10), At(10), At(10), null, null, null, null)),
        new("driver", "root", false, new(At(2), At(12), At(2), At(12), null, null, null, null)),
        new("control", "root", true, new(At(3), At(15), At(3), At(15), At(2), At(12), At(2), At(12)))
    ]);

    [Fact]
    public void ParentMotionPreservesInitialRelativePoseAndCurrentControlOffset()
    {
        var rig = Rig(); rig.Set("driver", At(20)); rig.Set("control", At(4), local: true, offset: true);
        rig.ApplySingleParentConstraint("control", "driver");
        Assert.Equal(At(23), rig.Get("control")); Assert.Equal(At(9), rig.Get("control", local: true));
        Assert.Equal(At(15), rig.Get("control", initial: true)); Assert.Equal(At(4), rig.Get("control", local: true, offset: true));
    }

    [Fact]
    public void CandidateConstraintIsIsolatedAndResetRestoresInitialControl()
    {
        var committed = Rig(); var candidate = committed.Clone(); candidate.Set("root", At(100));
        candidate.ApplySingleParentConstraint("CONTROL", "DRIVER");
        Assert.Equal(At(105), candidate.Get("control")); Assert.Equal(At(15), committed.Get("control"));
        committed.CopyFrom(candidate); committed.Reset(); Assert.Equal(At(15), committed.Get("control"));
    }

    [Fact]
    public void ParentRotationAndScaleDriveControlRelativeOffset()
    {
        var rig = Rig(); rig.Set("driver", At(20) with { Rotation = new(0, 0, 1, 0), Scale = new(2, 2, 2) });
        rig.ApplySingleParentConstraint("control", "driver");
        var global = rig.Get("control");
        Assert.Equal(new AlsDoubleVector(14, 0, 0), global.Position);
        Assert.Equal(new AlsDoubleVector(2, 2, 2), global.Scale);
        Assert.Equal(new AlsQuaternion(0, 0, 1, 0), global.Rotation);
    }

    [Fact]
    public void InvalidChildOrParentIsRejectedBeforeChangingPose()
    {
        var rig = Rig();
        Assert.Throws<ArgumentException>(() => rig.ApplySingleParentConstraint("driver", "root"));
        Assert.Throws<ArgumentException>(() => rig.ApplySingleParentConstraint("control", "absent"));
        Assert.Equal(At(15), rig.Get("control"));
    }
}
