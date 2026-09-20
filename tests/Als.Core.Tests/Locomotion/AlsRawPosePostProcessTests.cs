using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRawPosePostProcessTests
{
    [Fact]
    public void RetargetsOnlyTrackedSkeletonTranslationsAfterVirtualBoneGeneration()
    {
        var reference = Enumerable.Range(0, 5).Select(i => Pose(i * 10)).ToArray();
        var raw = Enumerable.Range(0, 5).Select(i => Pose(i)).ToArray(); var before = raw.ToArray();
        var model = new AlsRawPoseRetargetModel([0, 1, 2, 3, -1], [0, 1, 1, 0], [true, true, false, true, true], reference, true);
        model.Apply(raw, true);
        Assert.Equal(reference[1].Position, raw[1].Position);
        Assert.Equal(before[1].Rotation, raw[1].Rotation); Assert.Equal(before[1].Scale, raw[1].Scale);
        foreach (var index in new[] { 0, 2, 3, 4 }) Assert.Equal(before[index], raw[index]);
    }

    [Theory]
    [InlineData(false, true)] [InlineData(true, false)]
    public void DisabledRetargetOrAbsentSourceReferenceLeavesRawPose(bool enabled, bool hasReference)
    {
        var pose = new[] { Pose(5), Pose(6) }; var before = pose.ToArray();
        var model = new AlsRawPoseRetargetModel([0, 1], [1, 1], [true, true], [Pose(9), Pose(10)], hasReference);
        model.Apply(pose, enabled); Assert.Equal(before, pose);
    }

    [Fact]
    public void RejectsBadTailBeforeMutatingTrackedBoneAndCopiesReferencePlan()
    {
        var reference = new[] { Pose(9), Pose(10) };
        var model = new AlsRawPoseRetargetModel([0, 1], [1, 0], [true, true], reference, true);
        reference[0] = Pose(999);
        var pose = new[] { Pose(5), Pose(6) with { Scale = new(float.NaN, 1, 1) } };
        Assert.Throws<ArgumentException>(() => model.Apply(pose, true)); Assert.Equal(Pose(5), pose[0]);
        pose[1] = Pose(6); model.Apply(pose, true); Assert.Equal(Pose(9).Position, pose[0].Position);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void RawRootLockReplacesFullAtomIncludingScaleWithoutAdditiveAdjustment(int mode)
    {
        var expected = mode switch { 0 => Pose(4), 1 => Pose(7), _ => AlsLocalPose.Identity };
        Assert.Equal(expected, AlsRawRootLock.Apply(Pose(1), Pose(4), Pose(7), mode, false, true, false, false));
    }

    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(true, false, true, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, true, true, true, true)]
    public void IgnoreRootLockOnlySuppressesForceLock(bool enabled, bool force, bool extract, bool ignore, bool locked)
    {
        Assert.Equal(locked ? Pose(4) : Pose(1),
            AlsRawRootLock.Apply(Pose(1), Pose(4), Pose(7), 0, enabled, force, extract, ignore));
    }

    private static AlsLocalPose Pose(float value) => new(new(value, 2, 3),
        Quaternion.CreateFromYawPitchRoll(.2f, .1f, .3f), new(value + 1, 2, 3));
}
