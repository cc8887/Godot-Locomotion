using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRigHierarchyTests
{
    private static AlsPrecisePose At(double x) => AlsPrecisePose.Identity with { Position = new(x, 0, 0) };
    private static AlsRigElementDefinition Bone(string name, string? parent, double local, double global) =>
        new(name, parent, false, new(At(local), At(global), At(local), At(global), null, null, null, null));
    private static AlsRigHierarchy Chain() => new([Bone("root", null, 0, 0), Bone("child", "root", 2, 2)]);

    [Fact]
    public void ParentMotionPropagatesAndCandidateCopyIsIsolated()
    {
        var committed = Chain(); var candidate = committed.Clone();
        candidate.Set("root", At(10), local: true);
        Assert.Equal(At(0), committed.Get("root")); Assert.Equal(At(2), committed.Get("child"));
        Assert.True(candidate.IsGlobalDirty("child"));
        Assert.Equal(At(12), candidate.Get("child")); Assert.Equal(At(2), candidate.Get("child", local: true));
        committed.CopyFrom(candidate); Assert.Equal(At(12), committed.Get("child"));
        committed.Reset(); Assert.Equal(At(0), committed.Get("root")); Assert.Equal(At(2), committed.Get("child"));
    }

    [Fact]
    public void ParentMotionCanKeepChildWorldPose()
    {
        var value = Chain(); value.Set("root", At(10), local: true, children: false);
        Assert.Equal(At(2), value.Get("child")); Assert.Equal(At(-8), value.Get("child", local: true));
    }

    [Fact]
    public void GenericTwoBoneImportResetsUnmappedBoneAndRejectsBadMappingBeforeMutation()
    {
        var value = Chain(); value.Set("child", At(7), local: true);
        value.ImportAdapterLocalPose(["root"], [At(4)]);
        Assert.Equal(At(6), value.Get("child"));
        Assert.Throws<ArgumentException>(() => value.ImportAdapterLocalPose(["root", "root"], [At(10), At(20)]));
        Assert.Equal(At(4), value.Get("root")); Assert.Equal(At(6), value.Get("child"));
    }

    [Fact]
    public void ForeignTopologyCopyIsRejectedBeforeDestinationMutation()
    {
        var value = Chain(); var foreign = new AlsRigHierarchy([Bone("root", null, 100, 100), Bone("child", null, 7, 7)]);
        Assert.Throws<InvalidOperationException>(() => value.CopyFrom(foreign));
        Assert.Equal(At(0), value.Get("root")); Assert.Equal(At(2), value.Get("child"));
    }

    [Fact]
    public void ControlOffsetAndInitialStateRemainSeparate()
    {
        var identity = AlsPrecisePose.Identity;
        var control = new AlsRigElementDefinition("control", "root", true,
            new(At(3), At(5), At(3), At(5), At(2), At(2), At(2), At(2)));
        var value = new AlsRigHierarchy([Bone("root", null, 0, 0), control]);
        value.Set("control", At(4), local: true, offset: true);
        Assert.Equal(At(7), value.Get("control")); Assert.Equal(At(5), value.Get("control", initial: true));
        Assert.Equal(At(2), value.Get("control", local: true, initial: true, offset: true));
        value.Reset(); Assert.Equal(At(5), value.Get("control"));
    }

    [Fact]
    public void MissingAndCyclicParentsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AlsRigHierarchy([Bone("child", "missing", 2, 2)]));
        Assert.Throws<ArgumentException>(() => new AlsRigHierarchy([Bone("root", "child", 0, 0), Bone("child", "root", 2, 2)]));
    }
}
