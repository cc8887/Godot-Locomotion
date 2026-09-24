using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsMontageActionPlaybackTests
{
    [Fact]
    public void AuthoredRateScaleChangesTraversalButNotElapsedSecondsOrBlendClock()
    {
        var (bank, actions, reader) = Create(Asset() with { RateScale = 1.2f, RootMotionEnabled = true });
        Begin(actions, 1, Start(1)); actions.Commit(Id(1));
        Begin(actions, 2, AlsActionRequest.None);
        var playback = Read(reader, actions, 2);
        Assert.Equal(1.25f, bank.Candidate[0].PlayRate);
        Assert.Equal(1.2f, bank.Candidate[0].RateScale);
        Assert.Equal(.075f, playback.CurrentTime, 6);
        Assert.Equal(.4f, playback.CurrentClipTime, 6);
        Assert.Equal(.05f, playback.FinalSegmentDeltaSeconds, 6);
        Assert.Equal(3, playback.PlayRate);
        Assert.Equal(.25f, playback.EffectiveWeight);
        Assert.Equal(playback.CurrentClipTime, bank.RootMotionRange.EndSeconds);
        var traversal = bank.Traversal.ToArray(); actions.Discard();
        Begin(actions, 2, AlsActionRequest.None);
        Assert.Equal(playback, Read(reader, actions, 2));
        Assert.Equal(traversal, bank.Traversal.ToArray());
    }
    [Fact]
    public void AcceptedFrameIsStationaryAndNextFrameUsesPhysicalClockAndClipMapping()
    {
        var (bank, actions, reader) = Create();
        Begin(actions, 1, Start(1)); var first = Read(reader, actions, 1);
        Assert.Equal(37, first.OccurrenceHandleId); Assert.Equal(7, first.SectionId); Assert.Equal(11, first.SegmentId);
        Assert.Equal(1, first.PlaybackEpoch); Assert.Equal(1, first.Active);
        Assert.Equal(0, first.CurrentTime); Assert.Equal(.25f, first.CurrentClipTime);
        Assert.Equal(0, first.FinalSegmentDeltaSeconds); Assert.Equal(0, first.EffectiveWeight);
        actions.Commit(Id(1)); Begin(actions, 2, AlsActionRequest.None);
        var next = Read(reader, actions, 2);
        Assert.Equal(.0625f, next.CurrentTime); Assert.Equal(.375f, next.CurrentClipTime);
        Assert.Equal(0, next.PreviousTime); Assert.Equal(.25f, next.PreviousClipTime);
        Assert.Equal(.05f, next.FinalSegmentDeltaSeconds); Assert.Equal(2.5f, next.PlayRate);
        Assert.Equal(.2f, next.BlendSeconds); Assert.Equal(.25f, next.EffectiveWeight);
        Assert.Equal(next.CurrentClipTime, bank.Evaluation[0].Position);
        Assert.Equal(0, reader.ReadOwned(actions, Id(2), AlsMontageSlot.BaseLayer, false).EffectiveWeight);
    }

    [Fact]
    public void ReplacementAndCancelNeverResurrectAFadingOwnerAndQueriesDoNotMutateTheBank()
    {
        var (bank, actions, reader) = Create();
        Begin(actions, 1, Start(1)); actions.Commit(Id(1));
        Begin(actions, 2, Start(2)); var frozen = bank.Evaluation.ToArray(); var states = bank.Candidate.ToArray();
        Assert.Equal(2, Read(reader, actions, 2).PlaybackEpoch);
        Assert.Equal(0, Read(reader, actions, 2).EffectiveWeight);
        var outgoing = reader.ReadInstance(bank, Id(2), 1);
        Assert.Equal(1, outgoing.PlaybackEpoch); Assert.True(outgoing.EffectiveWeight > 0);
        Assert.Equal(frozen, bank.Evaluation.ToArray()); Assert.Equal(states, bank.Candidate.ToArray());
        actions.Commit(Id(2)); Begin(actions, 3, new(2, AlsActionCommand.Cancel, 0, -1, 0, 1));
        Assert.Equal(AlsActionPlayback.CreateDefault(), Read(reader, actions, 3));
        Assert.Equal(1, reader.ReadInstance(bank, Id(3), 2).Active);
        Assert.True(bank.Candidate.ToArray().All(i => i.Interrupted));
    }

    [Fact]
    public void FrozenWeightsNormalizeAllOverlapsWithoutCombiningTheirIdentities()
    {
        var (bank, actions, reader) = Create();
        actions.Begin(Id(1), .05f); actions.ApplyRequest(Start(1));
        for (var i = 0; i < 8; i++) bank.PlayAction(0, 1.25f, stopGroup: false);
        actions.Complete(); actions.Commit(Id(1)); Begin(actions, 2, AlsActionRequest.None);
        var sum = 0f;
        foreach (var instance in bank.Candidate)
        {
            var playback = reader.ReadInstance(bank, Id(2), instance.InstanceId);
            Assert.Equal(instance.InstanceId, playback.PlaybackEpoch); sum += playback.EffectiveWeight;
        }
        Assert.Equal(1f, sum, 6);
        Assert.Equal(1, Read(reader, actions, 2).PlaybackEpoch); // Request ownership is not ActiveMontagesMap.
    }

    [Fact]
    public void NaturalFadeKeepsOwnerUntilTerminationAndReportsEndpointTraversalSeparatelyFromPoseClamp()
    {
        var asset = Asset() with { Lifecycle = Asset().Lifecycle with { BlendOutTriggerSeconds = 0 } };
        var (bank, actions, reader) = Create(asset); var completions = 0; var clamped = 0; var fading = 0;
        for (var frame = 1; frame <= 45; frame++)
        {
            Begin(actions, frame, frame == 1 ? Start(1) : AlsActionRequest.None);
            var playback = Read(reader, actions, frame);
            if (bank.Candidate.Length > 0)
            {
                var instance = bank.Candidate[0]; Assert.Equal(1, playback.Active);
                if (instance.Blend.BlendingOut == 1) { fading++; Assert.Equal(0, bank.ActiveActionInstance(0)); }
                if (bank.Traversal.Length > 0 && bank.Traversal[0].CurrentPosition == asset.Duration && instance.Playing == false)
                {
                    Assert.Equal(asset.Duration - .00005f, playback.CurrentTime);
                    Assert.Equal(.05f, playback.FinalSegmentDeltaSeconds); clamped++;
                }
            }
            else Assert.Equal(AlsActionPlayback.CreateDefault(), playback);
            for (var i = 0; i < actions.Outcomes.Count; i++) if (actions.Outcomes[i].ResultCode == AlsActionResultCode.Completed) completions++;
            actions.Commit(Id(frame));
        }
        Assert.Equal(1, completions); Assert.Equal(1, clamped); Assert.True(fading > 0);
    }

    [Fact]
    public void DiscardAndRetryPreserveSummaryAndRejectStaleOrUnfinishedReads()
    {
        var (bank, actions, reader) = Create();
        actions.Begin(Id(1), .05f); actions.ApplyRequest(Start(1));
        Assert.Throws<InvalidOperationException>(() => Read(reader, actions, 1));
        actions.Complete(); actions.Commit(Id(1)); Begin(actions, 2, Start(2));
        var expected = Read(reader, actions, 2); var outgoing = reader.ReadInstance(bank, Id(2), 1);
        Assert.Throws<InvalidOperationException>(() => Read(reader, actions, 1));
        actions.Discard(); Assert.Equal(1, actions.CommittedOwners[0].InstanceId);
        Assert.Throws<InvalidOperationException>(() => Read(reader, actions, 2));
        Begin(actions, 2, Start(2)); Assert.Equal(expected, Read(reader, actions, 2));
        Assert.Equal(outgoing, reader.ReadInstance(bank, Id(2), 1));
        Assert.Equal(AlsActionPlayback.CreateDefault(), reader.ReadInstance(bank, Id(2), 999));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IndependentGroupsRequireExplicitSlotAndCannotBeCollapsed(bool sameSlot)
    {
        var a = Asset(); var b = a with { ActionDefinitionId = 1, MontageId = 56, GroupId = 4,
            Slot = sameSlot ? AlsMontageSlot.BaseLayer : AlsMontageSlot.Grounded };
        var bank = new AlsMontageRuntime([], [a, b]);
        var actions = new AlsMontageActionRuntime(bank, [Policy(), Policy() with { DefinitionId = 1 }]);
        var reader = new AlsMontageActionPlaybackReader([new(a, 7, 11, 37), new(b, 7, 12, 38)]);
        Begin(actions, 1, Start(1)); actions.Commit(Id(1));
        Begin(actions, 2, Start(2) with { ActionDefinitionId = 1 });
        if (sameSlot) Assert.Throws<InvalidOperationException>(() => Read(reader, actions, 2));
        else
        {
            Assert.Equal(1, Read(reader, actions, 2).PlaybackEpoch);
            Assert.Equal(2, reader.ReadOwned(actions, Id(2), AlsMontageSlot.Grounded).PlaybackEpoch);
        }
    }

    [Fact]
    public void StalePhysicalBindingsAndDuplicateOccurrenceHandlesAreRejected()
    {
        var (bank, actions, _) = Create(); Begin(actions, 1, Start(1));
        var reader = new AlsMontageActionPlaybackReader([new(Asset() with { ClipRate = 3 }, 7, 11, 37)]);
        Assert.Throws<InvalidOperationException>(() => reader.ReadInstance(bank, Id(1), 1));
        Assert.Throws<ArgumentException>(() => new AlsMontageActionPlaybackReader([
            new(Asset(), 7, 11, 37), new(Asset() with { ActionDefinitionId = 1 }, 7, 12, 37)]));
        Assert.Equal(1, actions.CandidateOwners[0].InstanceId);
        Assert.Single(bank.Candidate.ToArray());
    }

    [Fact]
    public void SummaryQueriesAllocateNothingAfterWarmup()
    {
        var (bank, actions, reader) = Create(); for (var f = 1; f <= 300; f++) Tick(f);
        var before = GC.GetAllocatedBytesForCurrentThread(); for (var f = 301; f <= 1300; f++) Tick(f);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Tick(int f)
        {
            Begin(actions, f, Start(f)); _ = Read(reader, actions, f);
            foreach (var instance in bank.Candidate) _ = reader.ReadInstance(bank, Id(f), instance.InstanceId);
            actions.Commit(Id(f));
        }
    }

    private static AlsAuthoredMontageAsset Asset() => new(0, 10, AlsMontageSlot.BaseLayer, 3, 1.5f, .25f, 2,
        new(AlsActionLifecycleMode.MontageAutoBlendOut, .2f, AlsActionBlendOption.Linear, .3f, AlsActionBlendOption.Linear, -1), MontageId: 55);
    private static AlsMontageActionPolicy Policy() => new(0, 7, 0, 1.25f, .2f, true);
    private static (AlsMontageRuntime, AlsMontageActionRuntime, AlsMontageActionPlaybackReader) Create(AlsAuthoredMontageAsset? asset = null)
    {
        var a = asset ?? Asset(); var bank = new AlsMontageRuntime([], [a]);
        return (bank, new(bank, [Policy()]), new([new(a, 7, 11, 37)]));
    }
    private static void Begin(AlsMontageActionRuntime owner, int frame, AlsActionRequest request)
    { owner.Begin(Id(frame), .05f); owner.ApplyRequest(request); owner.Complete(); }
    private static AlsActionPlayback Read(AlsMontageActionPlaybackReader reader, AlsMontageActionRuntime actions, int f) =>
        reader.ReadOwned(actions, Id(f), AlsMontageSlot.BaseLayer);
    private static AlsFrameIdentity Id(int f) => new(f, 1, 1);
    private static AlsActionRequest Start(int id) => new(id, AlsActionCommand.Start, 0, 7, 100, 1);
}
