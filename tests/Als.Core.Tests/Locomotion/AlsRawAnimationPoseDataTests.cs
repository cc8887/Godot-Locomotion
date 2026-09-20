using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRawAnimationPoseDataTests
{
    [Fact]
    public void SourceIdentityPresenceMappingAndKeysRemainImmutable()
    {
        var identity = new AlsRawAnimationAssetIdentity(27, "original-resource", "/Game/Original.Original", 4);
        int[] mapping = [0, 1, -1]; int[] virtuals = [2]; bool[] presence = [true, false, true];
        var raw = AlsLocalPose.Identity with { Rotation = new(0, 0, 0, 1.000001f), Position = new(1, 2, 3) };
        AlsLocalPose[] physical = [raw, default, raw with { Position = new(4, 5, 6) }, default];
        AlsLocalPose[] virtualKeys = [AlsLocalPose.Identity, AlsLocalPose.Identity with { Position = new(7, 8, 9) }];
        var data = new AlsRawAnimationPoseData(identity, 30, 1, 2, 1.0 / 30,
            AlsRawAnimationInterpolation.Linear, mapping, virtuals, presence, physical, virtualKeys);
        Array.Fill(mapping, -1); Array.Fill(virtuals, 0); Array.Fill(presence, false);
        Array.Fill(physical, default); Array.Fill(virtualKeys, default);
        Assert.Equal(identity, data.Identity);
        Assert.Equal(3, data.LogicalBoneCount); Assert.Equal(2, data.PhysicalBoneCount); Assert.Equal(1, data.VirtualBoneCount);
        Assert.Equal(new[] { 0, 1, -1 }, data.LogicalToPhysical.ToArray());
        Assert.Equal(new[] { 0, 1 }, data.PhysicalToLogical.ToArray());
        Assert.Equal(new[] { 2 }, data.VirtualLogicalIndices.ToArray());
        Assert.Equal(new[] { true, false, true }, data.LogicalTrackPresence.ToArray());
        Assert.True(data.VirtualTrackPresence[0]);
        Assert.Equal(raw, data.GetPhysicalKey(0)[0]);
        Assert.Equal(BitConverter.SingleToInt32Bits(1.000001f), BitConverter.SingleToInt32Bits(data.GetPhysicalKey(0)[0].Rotation.W));
        Assert.Equal(new Vector3(4, 5, 6), data.GetPhysicalKey(1)[0].Position);
        Assert.Equal(new Vector3(7, 8, 9), data.GetVirtualKey(1)[0].Position);
    }

    [Fact]
    public void TrackPresenceDistinguishesMissingSlotsFromInvalidAuthoredAtoms()
    {
        var invalid = default(AlsLocalPose);
        var absent = Create([0, -1], [1], [false, false], [invalid], [invalid]);
        Assert.False(absent.LogicalTrackPresence[0]); Assert.False(absent.VirtualTrackPresence[0]);
        Assert.Throws<ArgumentException>(() => Create([0, -1], [1], [true, false], [invalid], [invalid]));
        Assert.Throws<ArgumentException>(() => Create([0, -1], [1], [false, true], [invalid], [invalid]));
        foreach (var atom in new[]
        {
            AlsLocalPose.Identity with { Position = new(float.NaN, 0, 0) },
            AlsLocalPose.Identity with { Scale = new(1, float.PositiveInfinity, 1) },
            AlsLocalPose.Identity with { Rotation = new(0, 0, float.NegativeInfinity, 1) },
            AlsLocalPose.Identity with { Rotation = new(0, 0, 0, .5f) },
        })
            Assert.Throws<ArgumentException>(() => Create([0], [], [true], [atom], []));
    }

    [Fact]
    public void RejectsAmbiguousOrIncompleteBoneAndKeyMappings()
    {
        Assert.Throws<ArgumentException>(() => Create([0, 0], [], [true, true], [AlsLocalPose.Identity, AlsLocalPose.Identity], []));
        Assert.Throws<ArgumentException>(() => Create([1], [], [true], [AlsLocalPose.Identity], []));
        Assert.Throws<ArgumentException>(() => Create([0, -1], [], [true, false], [AlsLocalPose.Identity], []));
        Assert.Throws<ArgumentException>(() => Create([0, -1, -1], [1, 1], [true, false, false], [AlsLocalPose.Identity], [default, default]));
        Assert.Throws<ArgumentException>(() => Create([0, -1], [0], [true, false], [AlsLocalPose.Identity], [default]));
        Assert.Throws<ArgumentException>(() => Create([0], [], [], [AlsLocalPose.Identity], []));
        Assert.Throws<ArgumentException>(() => Create([0], [], [true], [], []));
        Assert.Throws<ArgumentException>(() => Create([0, -1], [1], [true, false], [AlsLocalPose.Identity], []));
        var data = Create([0], [], [true], [AlsLocalPose.Identity], []);
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = data.GetPhysicalKey(-1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = data.GetVirtualKey(1); });
    }

    [Fact]
    public void RejectsForeignResourceIdentityTimingAndUnknownInterpolation()
    {
        var identity = new AlsRawAnimationAssetIdentity(1, "resource", "/Game/Resource.Resource", 0);
        foreach (var invalid in new[] { default(AlsRawAnimationAssetIdentity), identity with { AnimationId = -1 },
            identity with { SkeletonId = -1 }, identity with { AssetId = " " }, identity with { AssetPath = "" } })
            Assert.Throws<ArgumentException>(() => new AlsRawAnimationPoseData(invalid, 30, 1, 1, 0,
                AlsRawAnimationInterpolation.Linear, [0], [], [true], [AlsLocalPose.Identity], []));
        foreach (var rate in new[] { (0, 1), (30, 0), (-1, 1), (30, -1) })
            Assert.Throws<ArgumentOutOfRangeException>(() => new AlsRawAnimationPoseData(identity, rate.Item1, rate.Item2, 1, 0,
                AlsRawAnimationInterpolation.Linear, [0], [], [true], [AlsLocalPose.Identity], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsRawAnimationPoseData(identity, 30, 1, 0, 0,
            AlsRawAnimationInterpolation.Linear, [0], [], [true], [], []));
        foreach (var duration in new[] { double.NaN, double.PositiveInfinity, -1.0 })
            Assert.Throws<ArgumentOutOfRangeException>(() => new AlsRawAnimationPoseData(identity, 30, 1, 1, duration,
                AlsRawAnimationInterpolation.Linear, [0], [], [true], [AlsLocalPose.Identity], []));
        Assert.Throws<ArgumentException>(() => new AlsRawAnimationPoseData(identity, 30, 1, 2, 0,
            AlsRawAnimationInterpolation.Linear, [0], [], [true], [AlsLocalPose.Identity, AlsLocalPose.Identity], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsRawAnimationPoseData(identity, 30, 1, 1, 0,
            (AlsRawAnimationInterpolation)17, [0], [], [true], [AlsLocalPose.Identity], []));
    }

    private static AlsRawAnimationPoseData Create(int[] mapping, int[] virtuals, bool[] presence,
        AlsLocalPose[] physical, AlsLocalPose[] virtualKeys) => new(new(1, "resource", "/Game/Resource.Resource", 0),
        30, 1, 1, 0, AlsRawAnimationInterpolation.Linear, mapping, virtuals, presence, physical, virtualKeys);
}
