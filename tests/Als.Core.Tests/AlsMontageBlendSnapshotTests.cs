using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageBlendSnapshotTests
{
    private static AlsFrameIdentity Id(int f) => new(f, 61, 3);
    private static AlsAuthoredMontageAsset Asset() => new(0, 1, new(2), 0, 2, 0, 1,
        new(AlsActionLifecycleMode.MontageAutoBlendOut, 1, AlsActionBlendOption.Cubic,
            .4f, AlsActionBlendOption.HermiteCubic, -1))
        { BlendInProfileId = 4, BlendOutProfileId = 7, AdditionalTracks = [new(2, new(3), .2f, .5f, 1)] };

    [Fact]
    public void EarlyStopUsesLinearStartAlphaAndBothTracksRetainFrozenBlendUntilNextFrame()
    {
        var r = new AlsMontageRuntime([], [Asset()]);
        r.Begin(Id(0), .1f); r.PlayAction(0, 1); r.Commit(Id(0));
        r.Begin(Id(1), .2f); var frozen = r.Evaluation.ToArray();
        Assert.Equal(4, frozen[0].BlendSnapshot.ProfileId);
        Assert.NotEqual(r.Candidate[0].Blend.CurrentWeight, r.Candidate[0].Blend.Alpha);
        r.StopSlots([new(3)], .4f);
        Assert.Equal(frozen, r.Evaluation.ToArray());
        Assert.Equal(.2f, r.Candidate[0].BlendStartAlpha); Assert.Equal(7, r.Candidate[0].ActiveBlendProfileId);
        var stopped = r.Candidate.ToArray(); r.Discard();
        r.Begin(Id(1), .2f); r.StopSlots([new(3)], .4f); Assert.Equal(stopped, r.Candidate.ToArray()); r.Commit(Id(1));
        r.Begin(Id(2), .1f);
        Assert.Equal(7, r.Evaluation[0].BlendSnapshot.ProfileId);
        Assert.Equal(.2f, r.Evaluation[0].BlendSnapshot.StartAlpha);
        Assert.Equal(r.Evaluation[0].BlendSnapshot, r.Evaluation[1].BlendSnapshot);
        var start = r.Candidate[0].Blend.Alpha;
        r.StopInstance(r.Candidate[0].InstanceId, .04f, AlsActionBlendOption.Linear);
        Assert.Equal(start, r.Candidate[0].BlendStartAlpha);
        Assert.Equal(7, r.Candidate[0].ActiveBlendProfileId);
        Assert.Equal(AlsActionBlendOption.HermiteCubic, r.Candidate[0].Settings.BlendOutOption);
        Assert.Equal(.2f, r.Evaluation[0].BlendSnapshot.StartAlpha);
    }

    [Fact]
    public void AutomaticFadeAndRagdollSelectOutgoingProfileWithoutMutatingCommittedFrame()
    {
        var r = new AlsMontageRuntime([], [Asset()]);
        r.Begin(Id(0), 0); r.PlayAction(0, 1, 1.55f); r.Commit(Id(0));
        r.Begin(Id(1), .1f);
        Assert.Equal(7, r.Evaluation[0].BlendSnapshot.ProfileId);
        Assert.Equal(.1f, r.Evaluation[0].BlendSnapshot.StartAlpha);
        var frozen = r.Frame; var entries = frozen.Evaluations.ToArray(); r.Commit(Id(1));
        r.Begin(Id(2), .01f, ragdoll: true); Assert.Equal(entries, frozen.Evaluations.ToArray()); r.Discard();
        var other = new AlsMontageRuntime([], [Asset()]);
        other.Begin(Id(0), 0); other.PlayAction(0, 1); other.Commit(Id(0));
        other.Begin(Id(1), .2f); other.Commit(Id(1)); other.Begin(Id(2), .01f, ragdoll: true);
        Assert.Equal(7, other.Evaluation[0].BlendSnapshot.ProfileId);
        Assert.Equal(.2f, other.Evaluation[0].BlendSnapshot.StartAlpha);
    }

    [Fact]
    public void AliasesCannotDisagreeAboutProfilesAndInvalidIdsAreRejected()
    {
        var a = Asset() with { MontageId = 9 };
        Assert.Throws<ArgumentException>(() => new AlsMontageRuntime([], [a, a with { ActionDefinitionId = 1, BlendOutProfileId = 8 }]));
        Assert.Throws<ArgumentException>(() => new AlsMontageRuntime([], [a with { BlendInProfileId = -2 }]));
    }
}
