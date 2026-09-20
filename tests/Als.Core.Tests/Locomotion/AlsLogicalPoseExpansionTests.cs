using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsLogicalPoseExpansionTests
{
    [Fact]
    public void TransformMathMatchesNativePositiveNonUniformNegativeAndNearZeroScaleCases()
    {
        using var fixture = ReadFixture(); var cases = fixture.RootElement.GetProperty("transformCases");
        Assert.Equal(15, cases.GetArrayLength());
        foreach (var row in cases.EnumerateArray())
        {
            var a = ReadPose(row.GetProperty("a")); var b = ReadPose(row.GetProperty("b"));
            var index = row.GetProperty("index").GetInt32();
            AssertPose(ReadPose(row.GetProperty("compose")), AlsLogicalPoseExpansion.Compose(a, b), $"compose {index}");
            AssertPose(ReadPose(row.GetProperty("relative")), AlsLogicalPoseExpansion.Relative(a, b), $"relative {index}");
        }
    }

    [Theory]
    [InlineData(0, "ALS_N_Pose")]
    [InlineData(1, "ALS_CLF_Pose")]
    public void AllLogicalBonesMatchNativeStandingAndCrouchingBasePoseExtraction(int assetIndex, string assetName)
    {
        using var fixture = ReadFixture(); var root = fixture.RootElement;
        Assert.Equal(2, root.GetProperty("assets").GetArrayLength());
        Assert.Equal("Raw", root.GetProperty("evaluation").GetString());
        var asset = root.GetProperty("assets")[assetIndex];
        Assert.EndsWith("/" + assetName + "." + assetName, asset.GetProperty("source").GetString());
        Assert.Equal(0, asset.GetProperty("timeSeconds").GetSingle());
        var names = asset.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToArray();
        var parents = asset.GetProperty("logicalParents").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        var mapping = asset.GetProperty("logicalToPhysical").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        var virtualBones = asset.GetProperty("virtualBones").EnumerateArray().Select(vb => new AlsLogicalVirtualBone(
            vb.GetProperty("bone").GetInt32(), vb.GetProperty("source").GetInt32(), vb.GetProperty("target").GetInt32())).ToArray();
        var expected = asset.GetProperty("pose").EnumerateArray().Select(ReadPose).ToArray();
        var rawExpected = asset.GetProperty("rawPoseWithoutRetarget").EnumerateArray().Select(ReadPose).ToArray();
        var reference = asset.GetProperty("reference").EnumerateArray().Select(ReadPose).ToArray();
        var expansion = new AlsLogicalPoseExpansion(parents, mapping, reference, virtualBones);
        Assert.Equal(79, names.Length); Assert.Equal(79, expected.Length); Assert.Equal(79, rawExpected.Length);
        Assert.Equal(79, expansion.LogicalCount); Assert.Equal(68, expansion.PhysicalCount); Assert.Equal(11, expansion.VirtualCount);
        var tracks = asset.GetProperty("tracks").EnumerateArray().Select(n => n.GetString()!).ToArray();
        Assert.Equal(68, tracks.Length);
        Assert.Equal(names.Where((_, bone) => mapping[bone] >= 0).Order(StringComparer.OrdinalIgnoreCase), tracks.Order(StringComparer.OrdinalIgnoreCase));
        var physical = new AlsLocalPose[expansion.PhysicalCount];
        var rawPhysical = new AlsLocalPose[expansion.PhysicalCount];
        for (var bone = 0; bone < names.Length; bone++)
            if (mapping[bone] >= 0)
            {
                physical[mapping[bone]] = expected[bone];
                rawPhysical[mapping[bone]] = rawExpected[bone];
            }
        var output = new AlsLocalPose[expansion.LogicalCount];
        expansion.Expand(rawPhysical, output, new AlsLocalPose[expansion.LogicalCount]);
        for (var bone = 0; bone < output.Length; bone++)
            AssertPose(rawExpected[bone], output[bone], assetName + " raw " + names[bone]);
        Assert.True(physical.Where((pose, index) => Vector3.Distance(pose.Position, rawPhysical[index].Position) > .001f).Any(),
            "These native cases must exercise physical retargeting after virtual generation.");
        foreach (var vb in virtualBones)
            AssertPose(rawExpected[vb.Bone], expected[vb.Bone], assetName + " native virtual unchanged by retargeting " + names[vb.Bone]);
        expansion.Expand(physical, output, new AlsLocalPose[expansion.LogicalCount], virtualGenerationPhysicalPose: rawPhysical);
        for (var bone = 0; bone < output.Length; bone++)
            AssertPose(expected[bone], output[bone], assetName + " retargeted " + names[bone]);
    }

    [Fact]
    public void PositiveNonUniformCompositionUsesTrsInsteadOfMatrixShearDecomposition()
    {
        var local = new AlsLocalPose(new(3, 4, 5), Quaternion.CreateFromYawPitchRoll(.7f, .2f, .4f), new(2, 3, 4));
        var parent = new AlsLocalPose(new(7, 8, 9), Quaternion.CreateFromYawPitchRoll(-.6f, -.3f, .8f), new(.5f, 2, 3));
        var combined = AlsLogicalPoseExpansion.Compose(local, parent);
        Assert.Equal(new Vector3(1, 6, 12), combined.Scale);
        Assert.InRange(MathF.Abs(Quaternion.Dot(parent.Rotation * local.Rotation, combined.Rotation) - 1), 0, .000002f);
        var relative = AlsLogicalPoseExpansion.Relative(combined, parent);
        AssertPose(local, relative, "positive non-uniform round trip");
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1e-9f, false)] [InlineData(1e-8f, false)] [InlineData(1e-7f, true)]
    public void ReciprocalUsesNativeSmallNumberBoundary(float sourceScale, bool hasReciprocal)
    {
        var source = AlsLocalPose.Identity with { Scale = new(sourceScale, 2, 3) };
        var target = AlsLocalPose.Identity with { Position = new(2, 4, 9), Scale = new(4, 6, 12) };
        var relative = AlsLogicalPoseExpansion.Relative(target, source);
        Assert.Equal(hasReciprocal ? 4 / sourceScale : 0, relative.Scale.X);
        Assert.Equal(hasReciprocal ? 2 / sourceScale : 0, relative.Position.X);
        Assert.Equal(new Vector2(3, 4), new(relative.Scale.Y, relative.Scale.Z));
    }

    [Fact]
    public void MissingVirtualTracksAreBuiltPerSequenceAndPhysicalAtomsArePreserved()
    {
        var expansion = Skeleton(); var physical = PhysicalPose(); var output = new AlsLocalPose[5]; var components = new AlsLocalPose[5];
        expansion.Expand(physical, output, components);
        Assert.Equal(physical, output[..3]);
        AssertPose(AlsLogicalPoseExpansion.Relative(components[2], components[1]), output[3], "first virtual");
        AssertPose(AlsLogicalPoseExpansion.Relative(components[1], components[2]), output[4], "virtual source resolves to raw target");
        physical[2] = physical[2] with { Position = new(-7, 9, 4) };
        var previous = output[3]; expansion.Expand(physical, output, components);
        Assert.True(Vector3.Distance(previous.Position, output[3].Position) > 1);
    }

    [Fact]
    public void ExplicitVirtualTrackIsPreservedAndItsChildStillUsesRawSourceAlias()
    {
        var expansion = Skeleton(); var physical = PhysicalPose(); var output = new AlsLocalPose[5]; var components = new AlsLocalPose[5];
        expansion.Expand(physical, output, components); var expectedChild = output[4];
        var explicitPose = new AlsLocalPose(new(99, -55, 32), Quaternion.CreateFromYawPitchRoll(.6f, .9f, -.2f), new(2, 1, .5f));
        expansion.Expand(physical, output, components, [explicitPose, default], [true, false]);
        Assert.Equal(explicitPose, output[3]); Assert.Equal(expectedChild, output[4]);
        Assert.True(Vector3.Distance(components[3].Position, components[2].Position) > 10);
        expansion.Expand(physical, output, components, [explicitPose, default], [false, false]);
        Assert.NotEqual(explicitPose, output[3]); Assert.Equal(expectedChild, output[4]);
    }

    [Fact]
    public void RetargetedPhysicalAtomsDoNotChangeTheEarlierVirtualGenerationSnapshot()
    {
        var expansion = Skeleton(); var raw = PhysicalPose(); var rawOutput = new AlsLocalPose[5];
        expansion.Expand(raw, rawOutput, new AlsLocalPose[5]);
        var retargeted = raw.ToArray();
        retargeted[1] = retargeted[1] with { Position = new(9, 2, 4) };
        retargeted[2] = retargeted[2] with { Position = new(-4, 3, 8) };
        var output = new AlsLocalPose[5]; var snapshot = new AlsLocalPose[5];
        expansion.Expand(retargeted, output, snapshot, virtualGenerationPhysicalPose: raw);
        Assert.Equal(retargeted, output[..3]); Assert.Equal(rawOutput[3..], output[3..]);
        var incorrectOrder = new AlsLocalPose[5];
        expansion.Expand(retargeted, incorrectOrder, snapshot);
        Assert.True(Vector3.Distance(incorrectOrder[3].Position, output[3].Position) > 1);
    }

    [Fact]
    public void VirtualTargetReadsFrozenTrackOrReferencePoseRatherThanEarlierGeneratedVirtual()
    {
        AlsLocalPose[] rest = [AlsLocalPose.Identity, AlsLocalPose.Identity, AlsLocalPose.Identity,
            AlsLocalPose.Identity with { Position = new(12, 0, 0) }, AlsLocalPose.Identity];
        var expansion = new AlsLogicalPoseExpansion([-1, 0, 0, 1, 1], [0, 1, 2, -1, -1], rest,
            [new(3, 1, 2), new(4, 1, 3)]);
        var output = new AlsLocalPose[5]; var components = new AlsLocalPose[5];
        expansion.Expand(PhysicalPose(), output, components);
        AssertPose(rest[3], output[4], "frozen reference target");
        Assert.True(Vector3.Distance(output[3].Position, output[4].Position) > 1);
        var explicitTarget = rest[3] with { Position = new(-8, 2, 4) };
        expansion.Expand(PhysicalPose(), output, components, [explicitTarget, default], [true, false]);
        AssertPose(explicitTarget, output[4], "frozen explicit target");
    }

    [Fact]
    public void ConstructorCopiesItsInputsAndRejectsInvalidMappings()
    {
        int[] parents = [-1, 0]; int[] mapping = [0, -1];
        AlsLocalPose[] rest = [AlsLocalPose.Identity, AlsLocalPose.Identity]; AlsLogicalVirtualBone[] virtualBones = [new(1, 0, 0)];
        var expansion = new AlsLogicalPoseExpansion(parents, mapping, rest, virtualBones);
        parents[1] = 1; mapping[0] = 7; rest[0] = default; virtualBones[0] = default;
        var output = new AlsLocalPose[2]; expansion.Expand([AlsLocalPose.Identity], output, new AlsLocalPose[2]);
        Assert.Equal(AlsLocalPose.Identity, output[1]);
        Assert.Throws<ArgumentException>(() => new AlsLogicalPoseExpansion([-1, 0], [0, 0],
            [AlsLocalPose.Identity, AlsLocalPose.Identity], []));
        Assert.Throws<ArgumentException>(() => new AlsLogicalPoseExpansion([-1, 0], [0, -1],
            [AlsLocalPose.Identity, AlsLocalPose.Identity], []));
        Assert.Throws<ArgumentException>(() => new AlsLogicalPoseExpansion([-1, 0, 1], [0, 1, -1],
            [AlsLocalPose.Identity, AlsLocalPose.Identity, AlsLocalPose.Identity], [new(2, 0, 1)]));
    }

    [Fact]
    public void InvalidBuffersOrInputsAreRejectedBeforeWritingOutput()
    {
        var expansion = Skeleton(); var output = Enumerable.Repeat(AlsLocalPose.Identity, 5).ToArray();
        var physical = PhysicalPose(); physical[2] = physical[2] with { Position = new(float.NaN, 0, 0) };
        Assert.Throws<ArgumentException>(() => expansion.Expand(physical, output, new AlsLocalPose[5]));
        Assert.Throws<ArgumentException>(() => expansion.Expand(PhysicalPose(), output, output));
        Assert.Throws<ArgumentException>(() => expansion.Expand(PhysicalPose(), output, new AlsLocalPose[5], [AlsLocalPose.Identity], [true]));
        Assert.All(output, pose => Assert.Equal(AlsLocalPose.Identity, pose));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExpansionDoesNotAllocateIncludingNegativeScalePath(bool negative)
    {
        var expansion = Skeleton(); var physical = PhysicalPose(); var output = new AlsLocalPose[5]; var scratch = new AlsLocalPose[5];
        if (negative) physical[0] = physical[0] with { Scale = new(-2, 3, 4) };
        for (var i = 0; i < 64; i++) expansion.Expand(physical, output, scratch);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) expansion.Expand(physical, output, scratch);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static AlsLogicalPoseExpansion Skeleton() => new([-1, 0, 0, 1, 3], [0, 1, 2, -1, -1],
        Enumerable.Repeat(AlsLocalPose.Identity, 5).ToArray(), [new(3, 1, 2), new(4, 3, 1)]);
    private static AlsLocalPose[] PhysicalPose() =>
    [
        new(new(7, 8, 9), Quaternion.CreateFromYawPitchRoll(.2f, .1f, -.3f), Vector3.One),
        new(new(2, -4, 3), Quaternion.CreateFromYawPitchRoll(.4f, .2f, .1f), Vector3.One),
        new(new(-3, 5, 1), Quaternion.CreateFromYawPitchRoll(-.3f, -.1f, .7f), Vector3.One),
    ];
    private static JsonDocument ReadFixture() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "P3", "v4_logical_pose_native.json")));
    private static AlsLocalPose ReadPose(JsonElement row)
    {
        var p = row.GetProperty("position"); var r = row.GetProperty("rotation"); var s = row.GetProperty("scale");
        return new(new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
            new(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()),
            new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
    private static void AssertPose(AlsLocalPose expected, AlsLocalPose actual, string context)
    {
        for (var axis = 0; axis < 3; axis++)
        {
            Assert.True(MathF.Abs(expected.Position[axis] - actual.Position[axis]) <= .0001f + MathF.Abs(expected.Position[axis]) * .000003f,
                $"{context} position {axis}: expected={expected.Position}, actual={actual.Position}");
            Assert.True(MathF.Abs(expected.Scale[axis] - actual.Scale[axis]) <= .0001f + MathF.Abs(expected.Scale[axis]) * .000003f,
                $"{context} scale {axis}: expected={expected.Scale}, actual={actual.Scale}");
        }
        Assert.True(MathF.Min((expected.Rotation - actual.Rotation).Length(), (expected.Rotation + actual.Rotation).Length()) <= .00001f,
            $"{context} rotation: expected={expected.Rotation}, actual={actual.Rotation}");
    }
}
