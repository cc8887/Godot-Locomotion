using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsLocomotionSourceRuntimeTests
{
    [Fact]
    public void ResolvesIdentityRatesAndSampleOrderFromOneView()
    {
        var f = new Fixture();
        Assert.True(f.Run(out var failure)); Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(new[] { 1, 0 }, f.Ticks.Select(t => t.PlayerId));
        Assert.Equal(new[] { -1, 2 }, f.Groups);
        Assert.Equal(new[] { 2, 1, 0 }, f.SampleTicks.Select(s => s.SampleId));
        Assert.Equal(1.25f, f.Ticks[0].PlayRate);
        Assert.Equal(1.5f, f.Ticks[1].PlayRate);
        Assert.Equal(101, f.Ticks[0].AssetId); Assert.Equal(100, f.Ticks[1].AssetId);
        Assert.True(f.Ticks[0].RequestedInertialization);
        Assert.Equal(new AlsAssetSyncSample(2, 2, .75f, 2, 3), f.SampleTicks[0]);
        Assert.Equal(new AlsAssetSyncSample(1, 1, .25f, 1, .5f), f.SampleTicks[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RejectsEveryForeignStampPartWithoutPublishing(int part)
    {
        var f = new Fixture();
        f.Expected = part switch
        {
            0 => f.Expected with { Version = AlsLocomotionSourceView.CurrentVersion + 1 }, 1 => f.Expected with { SkeletonId = 1 },
            2 => f.Expected with { Digest0 = 100 }, 3 => f.Expected with { Digest1 = 100 },
            4 => f.Expected with { Digest2 = 100 }, _ => f.Expected with { Digest3 = 100 }
        };
        Assert.False(f.Run(out var failure)); Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
        Assert.All(f.Ticks, t => Assert.Equal(default, t));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void RejectsInvalidContributionsOrBindingsAtomically(int mutation)
    {
        var f = new Fixture(); Assert.True(f.Run(out _));
        var ticks = f.Ticks.ToArray(); var samples = f.SampleTicks.ToArray(); var groups = f.Groups.ToArray();
        switch (mutation)
        {
            case 0: f.Updates[1] = f.Updates[1] with { PlayerId = 999 }; break;
            case 1: f.Updates[1] = f.Updates[1] with { Epoch = 0 }; break;
            case 2: f.Updates[1] = f.Updates[1] with { Time = float.NaN }; break;
            case 3: f.Updates[1] = f.Updates[1] with { Weight = -1 }; break;
            case 4: f.Updates[1] = f.Updates[1] with { SampleStart = 1 }; break;
            case 5: f.Samples[0] = f.Samples[0] with { SampleId = 0 }; break;
            case 6: f.Samples[0] = f.Samples[0] with { SampleId = f.Samples[1].SampleId }; break;
            case 7: f.Samples[2] = f.Samples[2] with { CachedPlayRate = float.PositiveInfinity }; break;
            case 8: f.Players[0] = f.Players[0] with { Kind = AlsLocomotionSourceKind.TeleportEvaluator }; break;
            case 9: f.Players[0] = f.Players[0] with { SyncGroupId = 77 }; break;
            case 10: f.SourceSamples[0] = f.SourceSamples[0] with { PlayerId = 1 }; break;
            case 11: f.SourceSamples[0] = f.SourceSamples[0] with { AssetRateScale = 2 }; break;
            case 12: f.SyncPlayers[0] = f.SyncPlayers[0] with { PlayerId = 1 }; break;
            case 13: f.Players[0] = f.Players[0] with { PlayRateBasis = 0 }; break;
        }
        Assert.False(f.Run(out var failure)); Assert.NotEqual(AlsP5FailureCode.None, failure);
        Assert.Equal(ticks, f.Ticks); Assert.Equal(samples, f.SampleTicks); Assert.Equal(groups, f.Groups);
    }

    [Fact]
    public void AbsoluteNodeWeightAboveOneIsPreservedInNativeTickRecord()
    {
        var f = new Fixture();
        f.Updates[0] = f.Updates[0] with { Weight = MathF.BitIncrement(1) };
        f.Updates[1] = f.Updates[1] with { Weight = 2 };
        Assert.True(f.Run(out _));
        Assert.Equal(MathF.BitIncrement(1), f.Ticks[0].Weight); Assert.Equal(2, f.Ticks[1].Weight);
        f.Samples[0] = f.Samples[0] with { Weight = 2 };
        Assert.False(f.Run(out _)); // normalized blend-space sample has a different contract
    }

    [Fact]
    public void NearZeroPositiveSequenceBasisProducesZeroRateLikeNativeUpdate()
    {
        var f = new Fixture();
        f.Players[0] = f.Players[0] with { PlayRateBasis = 1e-9f };
        Assert.True(f.Run(out _)); Assert.Equal(0, f.Ticks[1].PlayRate);
    }

    [Fact]
    public void RejectsDuplicatePlayerContributionsEvenWithDifferentOwnedSamples()
    {
        var f = new Fixture(); var view = f.View();
        Assert.False(AlsLocomotionSourceRuntime.TryBuildTicks(view, f.Expected,
            [new(1, 1, .2f, .5f, 0, 1), new(1, 1, .2f, .5f, 1, 1)],
            [new(1, 1, 1), new(2, 1, 1)], 1, f.Ticks, f.SampleTicks, f.Groups, out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
    }

    [Fact]
    public void RejectsOverflowAndShortOutputsWithoutPartialWrites()
    {
        var f = new Fixture(); var view = f.View();
        Assert.False(AlsLocomotionSourceRuntime.TryBuildTicks(view, f.Expected, f.Updates, f.Samples, 3,
            f.Ticks.AsSpan(0, 1), f.SampleTicks, f.Groups, out _));
        f.Players[0] = f.Players[0] with { DefaultPlayRate = float.MaxValue };
        Assert.False(f.Run(out var failure)); Assert.Equal(AlsP5FailureCode.NonFiniteOutput, failure);
        Assert.All(f.SampleTicks, s => Assert.Equal(default, s));
        Assert.All(f.Ticks, t => Assert.Equal(default, t));
    }

    [Fact]
    public void EmptyContributionFrameAndWarmUpdatesAllocateNothing()
    {
        var f = new Fixture(); var view = f.View();
        Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(view, f.Expected, [], [], 1, [], [], [], out _));
        for (var i = 0; i < 100; i++) Assert.True(f.Run(out _));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) if (!f.Run(out _)) throw new InvalidOperationException();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class Fixture
    {
        private readonly AlsLocomotionSourceStamp _stamp = new(AlsLocomotionSourceView.CurrentVersion, 0, 1, 2, 3, 4);
        public AlsLocomotionSourceStamp Expected = new(AlsLocomotionSourceView.CurrentVersion, 0, 1, 2, 3, 4);
        public readonly AlsLocomotionSourcePlayerBinding[] Players =
        [
            new(0, 20, AlsLocomotionSourceKind.Sequence, AlsLocomotionSourceDomain.Cycle, 2, 0, 2, 4,
                AlsSourceRateInput.StandingPlayRate, true, 0, 1, -1, AlsSourceAxisInput.None, AlsSourceAxisInput.None),
            new(1, 21, AlsLocomotionSourceKind.BlendSpace, AlsLocomotionSourceDomain.Cycle, -1, 0, 1.25f, 1,
                AlsSourceRateInput.Constant, true, 1, 2, -1, AlsSourceAxisInput.LeanLeftRight, AlsSourceAxisInput.LeanForwardBack)
        ];
        public readonly AlsLocomotionSourceSampleBinding[] SourceSamples =
        [
            new(0, 0, 0, 10, 0, 0, 0, 1, .5f, 2, -1, 0),
            new(1, 1, 0, 11, 0, 0, 0, 1, 1, 3, -1, 1),
            new(2, 1, 1, 12, 1, 0, 0, 2, 1, 4, -1, 2)
        ];
        public readonly AlsLocomotionSourceSyncBinding[] SyncPlayers =
            [new(0, 100, 0, false, true, false, AlsBlendSpaceNotifyMode.None), new(1, 101, 0, true, true, false, AlsBlendSpaceNotifyMode.HighestWeightedAnimation)];
        private readonly AlsAssetSyncSequence[] _sequences = [new(10, 2, .5f, 0, 0), new(11, 3, 1, 0, 0), new(12, 4, 1, 0, 0)];
        private readonly int[] _groupIds = [2];
        public readonly AlsLocomotionSourceUpdate[] Updates = [new(1, 1, .2f, .8f, 0, 2, true), new(0, 2, .5f, .2f, 2, 1)];
        public readonly AlsLocomotionSampleUpdate[] Samples = [new(2, .75f, 3), new(1, .25f, .5f), new(0, 1, 1)];
        public readonly AlsAssetSyncPlayer[] Ticks = new AlsAssetSyncPlayer[2];
        public readonly AlsAssetSyncSample[] SampleTicks = new AlsAssetSyncSample[3];
        public readonly int[] Groups = new int[2];
        public AlsLocomotionSourceView View() => new(_stamp, Players, SourceSamples, SyncPlayers, _groupIds, _sequences, []);
        public bool Run(out AlsP5FailureCode failure) => AlsLocomotionSourceRuntime.TryBuildTicks(View(), Expected, Updates, Samples, 3,
            Ticks, SampleTicks, Groups, out failure);
    }
}
