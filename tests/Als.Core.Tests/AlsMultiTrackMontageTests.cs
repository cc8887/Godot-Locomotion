using System.Collections.Immutable;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMultiTrackMontageTests
{
    private static AlsFrameIdentity Id(int frame) => new(frame, 7, 3);
    private static AlsAuthoredMontageAsset Asset(int id = 0) => new(id, 1, new(2), 0, 3, .1f, 1,
        new(AlsActionLifecycleMode.MontageAutoBlendOut, .2f, AlsActionBlendOption.HermiteCubic, .3f, AlsActionBlendOption.Cubic, -1))
    { AdditionalTracks = [new(2, new(3), .25f, .5f, 1)] };

    [Fact]
    public void SecondarySlotStopCancelsOnePhysicalInstanceAndBothTracksTogether()
    {
        var runtime = new AlsMontageRuntime([], [Asset()]);
        runtime.Begin(Id(0), .1f); runtime.PlayAction(0, 1); runtime.Commit(Id(0));
        runtime.Begin(Id(1), .1f);
        Assert.Single(runtime.Candidate.ToArray()); Assert.Equal(2, runtime.Evaluation.Length);
        var frozen = runtime.Evaluation.ToArray();
        runtime.StopSlots([new(3)], .1f);
        Assert.True(runtime.Candidate[0].Interrupted); Assert.False(runtime.IsActionPlaying(0));
        Assert.Equal(frozen, runtime.Evaluation.ToArray());
        var stopped = runtime.Candidate.ToArray(); runtime.Discard();
        runtime.Begin(Id(1), .1f); runtime.StopSlots([new(3)], .1f);
        Assert.Equal(stopped, runtime.Candidate.ToArray()); runtime.Commit(Id(1));
        runtime.Begin(Id(2), .1f); Assert.Empty(runtime.Candidate.ToArray()); Assert.Empty(runtime.Evaluation.ToArray());
    }

    [Fact]
    public void OverlapGrowthRetainsEveryTrackWithoutCreatingAdditionalClocks()
    {
        var runtime = new AlsMontageRuntime([], [Asset()]);
        runtime.Begin(Id(0), .1f);
        for (var i = 0; i < 20; i++) runtime.PlayAction(0, 1, stopGroup: false);
        runtime.Commit(Id(0)); runtime.Begin(Id(1), .1f);
        Assert.Equal(20, runtime.Candidate.Length); Assert.Equal(40, runtime.Evaluation.Length);
        Assert.Equal(20, runtime.Evaluation.ToArray().Select(e => e.InstanceId).Distinct().Count());
        foreach (var tracks in runtime.Evaluation.ToArray().GroupBy(e => e.InstanceId))
        {
            var pair = tracks.ToArray(); Assert.Equal(pair[0].Weight, pair[1].Weight);
            Assert.Equal(.2f, pair[0].Position); Assert.Equal(.3f, pair[1].Position);
        }
    }

    [Fact]
    public void AliasesCompareTrackContentAndRejectContradictorySecondaryBindings()
    {
        var first = Asset() with { MontageId = 9 };
        var alias = first with { ActionDefinitionId = 1, AdditionalTracks = ImmutableArray.Create(first.AdditionalTracks[0]) };
        var runtime = new AlsMontageRuntime([], [first, alias]);
        runtime.Begin(Id(0), .1f); runtime.PlayAction(0, 1); runtime.PlayAction(1, 1);
        Assert.Equal(runtime.Candidate[0].MontageId, runtime.Candidate[1].MontageId);
        Assert.Throws<ArgumentException>(() => new AlsMontageRuntime([], [first, alias with { AdditionalTracks = [new(3, new(3), .25f, .5f, 1)] }]));
        Assert.Throws<ArgumentException>(() => new AlsMontageRuntime([], [first with { AdditionalTracks = [new(2, first.Slot, .25f, .5f, 1)] }]));
    }
}
