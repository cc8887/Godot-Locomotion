using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsBasePoseRetargetModelTests
{
    [Fact]
    public void ReplacesOnlyTrackedSkeletonTranslationsAndKeepsGeneratedVirtualAtoms()
    {
        var reference = Poses(); var model = new AlsBasePoseRetargetModel(Definition(), reference);
        var pose = Poses();
        for (var bone = 0; bone < pose.Length; bone++) pose[bone] = pose[bone] with { Position = new(bone + 10, 20, 30) };
        var original = (AlsLocalPose[])pose.Clone(); model.Apply(pose);
        Assert.Equal(reference[2].Position, pose[2].Position);
        Assert.Equal(original[2].Rotation, pose[2].Rotation); Assert.Equal(original[2].Scale, pose[2].Scale);
        // Physical0 is Animation, physical2 has no track, logical1/4 are virtual.
        foreach (var bone in new[] { 0, 1, 3, 4 }) Assert.Equal(original[bone], pose[bone]);
    }

    [Fact]
    public void DefinitionAndModelOwnTheirSourceArrays()
    {
        int[] mapping = [0, 2, 3];
        AlsBasePoseTranslationRetargetMode[] modes = [0, AlsBasePoseTranslationRetargetMode.Skeleton, 0];
        bool[] tracked = [true, true, true];
        var definition = new AlsBasePoseRetargetDefinition(Evaluator(), 5, mapping, modes, tracked);
        var reference = Poses(); var expected = reference[2].Position;
        var model = new AlsBasePoseRetargetModel(definition, reference);
        mapping[1] = 1; modes[1] = 0; tracked[1] = false; reference[2] = AlsLocalPose.Identity;
        var pose = Poses(); pose[2] = pose[2] with { Position = new(9) }; model.Apply(pose);
        Assert.Equal(expected, pose[2].Position); Assert.Equal(2, definition.PhysicalToLogical[1]);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void RejectsInvalidAtomsBeforeAnyCandidateMutation(int bone)
    {
        var model = new AlsBasePoseRetargetModel(Definition(), Poses()); var pose = Poses();
        pose[2] = pose[2] with { Position = new(9) };
        pose[bone] = pose[bone] with { Scale = new(float.NaN, 1, 1) };
        var original = (AlsLocalPose[])pose.Clone();
        Assert.Throws<ArgumentException>(() => model.Apply(pose)); Assert.Equal(original, pose);
    }

    [Fact]
    public void ReplacedNonfiniteTranslationIsStillInvalidAndRetryUsesSameStatelessModel()
    {
        var reference = Poses(); var model = new AlsBasePoseRetargetModel(Definition(), reference); var pose = Poses();
        pose[2] = pose[2] with { Position = new(float.PositiveInfinity) };
        Assert.Throws<ArgumentException>(() => model.Apply(pose));
        pose[2] = pose[2] with { Position = new(900) }; model.Apply(pose);
        Assert.Equal(reference[2].Position, pose[2].Position);
    }

    [Fact]
    public void RejectsLayoutUnsupportedModeAndInvalidReference()
    {
        Assert.Throws<ArgumentException>(() => new AlsBasePoseRetargetDefinition(Evaluator(), 5, [0, 0], [0, 0], [true, true]));
        Assert.Throws<ArgumentException>(() => new AlsBasePoseRetargetDefinition(Evaluator(), 5, [0], [(AlsBasePoseTranslationRetargetMode)2], [true]));
        Assert.Throws<ArgumentException>(() => new AlsBasePoseRetargetModel(Definition(), []));
        var reference = Poses(); reference[4] = reference[4] with { Rotation = default };
        Assert.Throws<ArgumentException>(() => new AlsBasePoseRetargetModel(Definition(), reference));
        var model = new AlsBasePoseRetargetModel(Definition(), Poses());
        Assert.Throws<ArgumentException>(() => model.Apply(new AlsLocalPose[4]));
    }

    [Fact]
    public void RequiresVirtualGenerationBeforeRetargetingRatherThanRebuildingVirtualsAfterIt()
    {
        AlsLocalPose[] reference = [AlsLocalPose.Identity, AlsLocalPose.Identity with { Position = new(1, 0, 0) }, AlsLocalPose.Identity];
        var expansion = new AlsLogicalPoseExpansion([-1, 0, 0], [0, 1, -1], reference, [new(2, 0, 1)]);
        AlsLocalPose[] raw = [reference[0], reference[1] with { Position = new(2, 0, 0) }];
        var logical = new AlsLocalPose[3]; expansion.Expand(raw, logical, new AlsLocalPose[3]);
        var model = new AlsBasePoseRetargetModel(new(Evaluator(), 3, [0, 1], [0, AlsBasePoseTranslationRetargetMode.Skeleton], [true, true]), reference);
        model.Apply(logical);
        Assert.Equal(new Vector3(1, 0, 0), logical[1].Position);
        Assert.Equal(new Vector3(2, 0, 0), logical[2].Position);
    }

    private static AlsBasePoseRetargetDefinition Definition() => new(Evaluator(), 5, [0, 2, 3],
        [0, AlsBasePoseTranslationRetargetMode.Skeleton, AlsBasePoseTranslationRetargetMode.Skeleton], [true, true, false]);
    private static AlsBasePoseEvaluatorDefinition Evaluator() => new(5, 7, "asset", "/asset", 1, 0, true, true, "ExplicitTime", "None", "CanBeLeader", "DoNotSync");
    private static AlsLocalPose[] Poses() => Enumerable.Range(0, 5).Select(i => new AlsLocalPose(new(i, i * 2, i * 3),
        Quaternion.CreateFromYawPitchRoll(i * .2f, .1f, .3f), new(1, 2, 3))).ToArray();
}
