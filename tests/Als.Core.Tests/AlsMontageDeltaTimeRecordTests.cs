using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageDeltaTimeRecordTests
{
    private static AlsFrameIdentity Id(int frame) => new(frame,81,3);
    private static AlsAuthoredMontageAsset Asset() => new(0,1,new(2),0,2,0,1,
        new(AlsActionLifecycleMode.MontageHoldAtEnd,.1f,AlsActionBlendOption.Linear,.4f,AlsActionBlendOption.Linear,-1))
        { AdditionalTracks=[new(2,new(3),.2f,.5f,1)] };

    [Fact]
    public void SectionEndKeepsMovementBeforePoseOffsetAndRetainsItWhileHeld()
    {
        var r=new AlsMontageRuntime([],[Asset()]);
        r.Begin(Id(0),0);r.PlayAction(0,1,.25f);r.Commit(Id(0));
        r.Begin(Id(1),3);var e=r.Evaluation[0];
        Assert.Equal(2-.00005f,e.MontagePosition);
        Assert.Equal(new AlsMontageDeltaTimeRecord(.25f,1.75f),e.DeltaTimeRecord);
        Assert.NotEqual(e.MontagePosition-.25f,e.DeltaTimeRecord.Delta);
        Assert.Equal(e.DeltaTimeRecord,r.Evaluation[1].DeltaTimeRecord);
        Assert.False(r.Candidate[0].Playing);r.Commit(Id(1));
        r.Begin(Id(2),0);Assert.Equal(e.DeltaTimeRecord,r.Evaluation[0].DeltaTimeRecord);r.Commit(Id(2));
        r.Begin(Id(3),.2f);Assert.Equal(e.DeltaTimeRecord,r.Evaluation[0].DeltaTimeRecord);
    }

    [Fact]
    public void PlayingZeroTickResetsIntervalAndReverseClampPreservesNegativeMovement()
    {
        var r=new AlsMontageRuntime([],[Asset()]);
        r.Begin(Id(0),0);r.PlayAction(0,-1,.25f);r.Commit(Id(0));
        r.Begin(Id(1),.1f);r.Commit(Id(1));
        r.Begin(Id(2),0);Assert.Equal(new AlsMontageDeltaTimeRecord(.15f,0),r.Evaluation[0].DeltaTimeRecord);r.Commit(Id(2));
        r.Begin(Id(3),1);Assert.Equal(new AlsMontageDeltaTimeRecord(.15f,-.15f),r.Evaluation[0].DeltaTimeRecord);
        Assert.Equal(0,r.Evaluation[0].MontagePosition);Assert.False(r.Candidate[0].Playing);
    }

    [Fact]
    public void NativeClippedMoveRoundingKeepsOneUlpFadeAliveAtSectionEnd()
    {
        // Original AM_MM_HitReact_Left_Lgt_01, captured native boundary tick.
        var a=Asset() with {Duration=1.0666667222976685f,Lifecycle=new(
            AlsActionLifecycleMode.MontageAutoBlendOut,.06f,AlsActionBlendOption.Linear,.5f,AlsActionBlendOption.Cubic,-1)};
        var r=new AlsMontageRuntime([],[a]);r.Begin(Id(0),0);r.PlayAction(0,1);r.Commit(Id(0));
        r.Begin(Id(1),.037f);r.Commit(Id(1));r.Begin(Id(2),2.133333444595337f);
        Assert.Equal(1,r.Evaluation[0].Weight);Assert.False(r.Candidate[0].Playing);
        Assert.Equal(1.0296666622161865f,r.Evaluation[0].DeltaTimeRecord.Delta);
        Assert.Equal(1.1920928955078125e-7f,r.Candidate[0].BlendTime);
    }

    [Fact]
    public void StopCommandsAndCancelledCandidatesCannotMutateFrozenPhysicalIntervals()
    {
        var r=new AlsMontageRuntime([],[Asset()]);
        r.Begin(Id(0),0);r.PlayAction(0,1,.1f);r.Commit(Id(0));
        r.Begin(Id(1),.037f);var entries=r.Evaluation.ToArray();
        r.StopSlots([new(3)],.2f);Assert.Equal(entries,r.Evaluation.ToArray());
        var candidate=r.Candidate.ToArray();r.Discard();
        Assert.NotEmpty(System.Text.Json.JsonSerializer.Serialize(candidate));
        r.Begin(Id(1),.037f);r.StopSlots([new(3)],.2f);
        Assert.Equal(entries,r.Evaluation.ToArray());Assert.Equal(candidate,r.Candidate.ToArray());
        var committed=r.Frame;var saved=committed.Evaluations.ToArray();r.Commit(Id(1));
        r.Begin(Id(2),.01f);Assert.Equal(saved,committed.Evaluations.ToArray());r.Discard();
    }
}
