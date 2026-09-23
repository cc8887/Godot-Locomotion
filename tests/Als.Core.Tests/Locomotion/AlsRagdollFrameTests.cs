using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsRagdollFrameTests
{
    private static readonly AlsLocalPose[] Reference = [AlsLocalPose.Identity];
    private static AlsRagdollFrameRuntime Create(AlsRagdollSharedSourceBinding? shared = null)
    {
        var machine = new AlsGroundedMachineDefinition(AlsGroundedMachineKind.Ragdoll, 0, 3, true,
            [new(false, AlsGroundedCondition.Always, 0, 1, -1, -1, -1), new(false, AlsGroundedCondition.Always, 1, 1, -1, -1, -1)],
            [new(0, 1, AlsGroundedCondition.MovementNotRagdoll, 0, 0, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1),
                new(1, 0, AlsGroundedCondition.MovementRagdoll, 0, 0, AlsTransitionBlend.HermiteCubic, false, -1, -1, -1)]);
        return new(new(14, 16, machine, new("root", 0, 1000, 0, 1), new(5, 2, 1, 0, 0), 0),
            new("RagdollPose", "Mesh", 3, 1, ["root"], [0], Reference), 3, 1, 1, 1, shared);
    }
    private sealed class Source : IAlsRagdollPoseSource
    {
        public int Calls;
        public bool Fail;
        public void Sample(float seconds, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
        {
            Calls++; pose[0] = AlsLocalPose.Identity with { Position = new(seconds, 0, 0) }; curves[0] = new(.8f);
            if (Fail) throw new InvalidOperationException("Injected source failure");
        }
    }
    private static AlsAnimationGraphFrame Next(AlsAnimationGraphFrame frame, int id) => frame.Next(new(id, 3, 1), (ulong)id);
    private static void Prepare(AlsRagdollFrameRuntime runtime, AlsAnimationGraphFrame frame, AlsMovementStateInput movement,
        bool visit = true, double speed = 500, float delta = .1f, bool inactive = false)
    {
        var context = new AlsPoseUpdateContext(frame.Identity, .3f, delta, .7f).WithState(90, 4);
        runtime.Prepare(movement, new(0, 0, speed), visit, inactive ? context.AsInactive() : context, frame);
    }

    [Theory] [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void IndependentClockAdvancesOncePerVisitedFrameIncludingInactive(int hz)
    {
        var runtime = Create(); var source = new Source(); var frame = default(AlsAnimationGraphFrame);
        for (var i = 1; i <= hz; i++)
        {
            frame = Next(frame, i); Prepare(runtime, frame, AlsMovementStateInput.Ragdoll, delta: 1f / hz, inactive: true);
            Assert.Equal(.5, runtime.Candidate.FlailRate); Assert.Equal(1, runtime.Candidate.PlayerEpoch);
            Assert.False(runtime.SourceContext.IsActive); Assert.Equal(.7f, runtime.SourceContext.RootMotionWeight);
            Assert.Equal(new AlsActiveAnimationState(14, 0), runtime.SourceContext.GetState(1));
            runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        }
        Assert.InRange(MathF.Abs(runtime.Committed.Time - .5f), 0, 1e-6f); Assert.Equal(hz, source.Calls);
    }

    [Fact]
    public void FlailHandoffSurvivesFailedCandidateAndRejectsSnapshotHiddenAndForeignFrames()
    {
        var runtime=Create(); var source=new Source(); var frame=Next(default,1);
        var output=new AlsLocalPose[1];
        Assert.False(runtime.TryCopyCommittedFlail(frame.Identity,output));
        Prepare(runtime,frame,AlsMovementStateInput.Ragdoll); runtime.Evaluate(source,null);
        var expected=runtime.Pose.ToArray();
        Assert.False(runtime.TryCopyCommittedFlail(frame.Identity,output));
        runtime.Commit(frame.Identity);
        Assert.True(runtime.TryCopyCommittedFlail(frame.Identity,output)); Assert.Equal(expected,output);
        output[0]=default;
        Assert.True(runtime.TryCopyCommittedFlail(frame.Identity,output)); Assert.Equal(expected,output);
        var next=Next(frame,2); source.Fail=true;
        Prepare(runtime,next,AlsMovementStateInput.Ragdoll);
        Assert.Throws<InvalidOperationException>(()=>runtime.Evaluate(source,null));
        Assert.True(runtime.TryCopyCommittedFlail(frame.Identity,output)); Assert.Equal(expected,output);
        Assert.False(runtime.TryCopyCommittedFlail(new(1,4,1),output));
        Assert.False(runtime.TryCopyCommittedFlail(new(1,3,2),output));
        source.Fail=false;
        Prepare(runtime,next,AlsMovementStateInput.Grounded); runtime.Evaluate(source,null); runtime.Commit(next.Identity);
        Assert.Equal(default,runtime.CommittedFlailIdentity);
        Assert.False(runtime.TryCopyCommittedFlail(frame.Identity,output));
        Assert.False(runtime.TryCopyCommittedFlail(next.Identity,output));
        frame=Next(next,3); Prepare(runtime,frame,AlsMovementStateInput.Ragdoll);
        runtime.Evaluate(source,null); runtime.Commit(frame.Identity);
        Assert.True(runtime.TryCopyCommittedFlail(frame.Identity,output));
        next=Next(frame,4); Prepare(runtime,next,AlsMovementStateInput.Ragdoll,visit:false); runtime.Commit(next.Identity);
        Assert.False(runtime.TryCopyCommittedFlail(frame.Identity,output));
    }

    [Fact]
    public void SameInitializationCounterWithDifferentGlobalStampPreservesFlailEpochAndTime()
    {
        var runtime=Create(); var source=new Source(); var frame=Next(default,1);
        Prepare(runtime,frame,AlsMovementStateInput.Ragdoll); runtime.Evaluate(source,null); runtime.Commit(frame.Identity);
        var prior=runtime.Committed;
        frame=Next(frame,2) with {Initialization=new(frame.Initialization.Counter,2)};
        Prepare(runtime,frame,AlsMovementStateInput.Ragdoll);
        Assert.Equal(prior.PlayerEpoch,runtime.Candidate.PlayerEpoch); Assert.True(runtime.Candidate.Time>prior.Time);
        runtime.Evaluate(source,null); var expected=runtime.Candidate.Diagnostics; runtime.Cancel();
        Assert.Equal(prior.Diagnostics,runtime.Committed.Diagnostics);
        Prepare(runtime,frame,AlsMovementStateInput.Ragdoll); runtime.Evaluate(source,null);
        Assert.Equal(expected,runtime.Candidate.Diagnostics); runtime.Commit(frame.Identity);
    }

    [Fact]
    public void HiddenColdBranchInitializesButDoesNotTickAndFirstUpdateCanSelectSnapshot()
    {
        var runtime = Create(); var source = new Source(); var frame = Next(default, 1);
        Prepare(runtime, frame, AlsMovementStateInput.Grounded, visit: false, speed: double.NaN);
        Assert.Equal(1, runtime.Candidate.PlayerEpoch); Assert.False(runtime.Candidate.Machine.HasUpdated);
        runtime.Commit(frame.Identity);
        frame = Next(frame, 2); Prepare(runtime, frame, AlsMovementStateInput.Grounded);
        Assert.Equal(1, runtime.Candidate.Machine.CurrentState); Assert.False(runtime.Candidate.PlayerTicked);
        runtime.Evaluate(source, null); Assert.Equal(Reference, runtime.Pose.ToArray()); Assert.False(runtime.Curves[0].Present);
        runtime.Commit(frame.Identity); Assert.Equal(0, source.Calls);
    }

    [Fact]
    public void ExitImmediatelyUsesSavedPoseWhileRootCanStillBeBlendingAndRetainsRate()
    {
        var runtime = Create(); var source = new Source(); var frame = Next(default, 1);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll, speed: 800); runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        var time = runtime.Committed.Time; frame = Next(frame, 2);
        var saved = new AlsNamedPoseSnapshot(frame.Identity, "RagdollPose", "Mesh", ["root"],
            [AlsLocalPose.Identity with { Position = new(7, 8, 9) }]);
        Prepare(runtime, frame, AlsMovementStateInput.Grounded, speed: double.NaN, inactive: true);
        Assert.Equal(.8, runtime.Candidate.FlailRate); Assert.Equal(time, runtime.Candidate.Time);
        Assert.Equal(1, runtime.Candidate.Machine.CurrentState); Assert.False(runtime.Candidate.PlayerTicked);
        runtime.Evaluate(source, saved); Assert.Equal(saved.LocalPoses.ToArray(), runtime.Pose.ToArray());
        Assert.False(runtime.Curves[0].Present); runtime.Commit(frame.Identity);
        frame = Next(frame, 3); Prepare(runtime, frame, AlsMovementStateInput.Ragdoll);
        Assert.Equal(2, runtime.Candidate.PlayerEpoch); Assert.Equal(.05f, runtime.Candidate.Time);
    }

    [Fact]
    public void HiddenFramesResetOnRelevanceButFrameIdGapsAloneDoNot()
    {
        var runtime = Create(); var source = new Source(); var frame = Next(default, 1);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll); runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        frame = Next(frame, 100); Prepare(runtime, frame, AlsMovementStateInput.Ragdoll);
        Assert.Equal(1, runtime.Candidate.PlayerEpoch); Assert.Equal(.1f, runtime.Candidate.Time);
        runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        frame = Next(frame, 101); Prepare(runtime, frame, AlsMovementStateInput.Ragdoll, visit: false, speed: 300);
        Assert.Equal(.3, runtime.Candidate.FlailRate); Assert.Equal(.1f, runtime.Candidate.Time); runtime.Commit(frame.Identity);
        frame = Next(frame, 102); Prepare(runtime, frame, AlsMovementStateInput.Ragdoll);
        Assert.Equal(2, runtime.Candidate.PlayerEpoch); Assert.Equal(.05f, runtime.Candidate.Time);
    }

    [Fact]
    public void TraversalCounterWrapIsContinuousButMissingAnUpdateResets()
    {
        var runtime = Create(); var source = new Source(); var frame = Next(default, 1);
        frame = frame with { Update = new(short.MaxValue, 1) };
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll); runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        frame = Next(frame, 2); Assert.Equal(short.MinValue, frame.Update.Counter);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll); Assert.Equal(1, runtime.Candidate.PlayerEpoch);
        runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        frame = Next(frame, 3); frame = frame with { Update = frame.Update.Next(3) };
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll); Assert.Equal(2, runtime.Candidate.PlayerEpoch);
    }

    [Fact]
    public void ZeroRateAndZeroDeltaDoNotSuppressTheStateTransition()
    {
        var runtime = Create(); var source = new Source(); var frame = Next(default, 1);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll, speed: 0, delta: 0);
        Assert.True(runtime.Candidate.PlayerTicked); Assert.Equal(0, runtime.Candidate.Time);
        runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        frame = Next(frame, 2); Prepare(runtime, frame, AlsMovementStateInput.Grounded, delta: 0);
        Assert.Equal(1, runtime.Candidate.Machine.CurrentState); Assert.Equal(0, runtime.Candidate.Machine.Transitions.Count);
        runtime.Evaluate(source, null); Assert.False(runtime.Curves[0].Present);
    }

    [Fact]
    public void InitializationCounterResetsClockButBoneCounterDoesNot()
    {
        var runtime = Create(); var source = new Source(); var frame = Next(default, 1);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll); runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        frame = Next(frame, 2); frame = frame with { Bones = frame.Bones.Next(2) };
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll); Assert.Equal(1, runtime.Candidate.PlayerEpoch);
        runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        frame = Next(frame, 3); frame = frame with { Initialization = frame.Initialization.Next(3) };
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll); Assert.Equal(2, runtime.Candidate.PlayerEpoch); Assert.Equal(.05f, runtime.Candidate.Time);
    }

    [Fact]
    public void DownstreamFailureOrCancelCannotAdvancePublishedMachineRateOrClock()
    {
        var runtime = Create(); var source = new Source(); var frame = Next(default, 1);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll); runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        var committed = runtime.Committed; frame = Next(frame, 2);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll, speed: 900); var candidate = runtime.Candidate;
        source.Fail = true; Assert.Throws<InvalidOperationException>(() => runtime.Evaluate(source, null));
        Assert.Equal(committed.Diagnostics, runtime.Committed.Diagnostics); source.Fail = false;
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll, speed: 900); Assert.Equal(candidate.Diagnostics, runtime.Candidate.Diagnostics);
        runtime.Evaluate(source, null); runtime.Cancel(); Assert.Equal(committed.Diagnostics, runtime.Committed.Diagnostics);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll, speed: 900); runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        Assert.Equal(candidate.Diagnostics, runtime.Committed.Diagnostics);
    }

    [Theory] [InlineData(4, 1)] [InlineData(3, 2)]
    public void RejectsForeignOwnership(uint character, uint generation)
    {
        var runtime = Create(); var frame = default(AlsAnimationGraphFrame).Next(new(1, character, generation), 1);
        Assert.Throws<ArgumentException>(() => Prepare(runtime, frame, AlsMovementStateInput.Ragdoll));
        Assert.Equal(default, runtime.Committed.Traversal.Identity);
    }

    [Fact]
    public void SharedClockWaitsForTheMatchingBatchAndNeverTicksPrivately()
    {
        var stamp = new AlsLocomotionSourceStamp(AlsLocomotionSourceView.CurrentVersion, 0, 1, 2, 3, 4);
        var runtime = Create(new(stamp, 223, 257)); var source = new Source(); var frame = Next(default, 1);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll);
        Assert.False(runtime.Candidate.PlayerTicked); Assert.Equal(0, runtime.Candidate.Time);
        Assert.True(runtime.SourceRequest.Updated); Assert.Equal(1, runtime.SourceRequest.Initializations);
        Assert.Throws<InvalidOperationException>(() => runtime.Evaluate(source, null));
        Assert.Throws<InvalidOperationException>(() => runtime.Commit(frame.Identity));
        var player = new AlsAssetPlayerHistory(223, 5, 1, .05f, 0, .05f, AlsAssetMarkerRecord.Invalid, 3, 1);
        var sample = new AlsAssetSampleHistory(257, 5, .05f, 0, AlsAssetMarkerRecord.Invalid, 0, .05f);
        Assert.Throws<ArgumentException>(() => runtime.CompleteShared(frame.Identity, stamp with { Digest0 = 99 }, player, sample));
        Assert.Throws<ArgumentException>(() => runtime.CompleteShared(frame.Identity, stamp, player with { PlayerId = 16 }, sample));
        Assert.Throws<ArgumentException>(() => runtime.CompleteShared(frame.Identity, stamp, player with { Epoch = 2 }, sample));
        Assert.Throws<ArgumentException>(() => runtime.CompleteShared(frame.Identity, stamp, player, sample with { SampleId = 0 }));
        Assert.Equal(0, runtime.Candidate.Time);
        runtime.CompleteShared(frame.Identity, stamp, player, sample);
        Assert.Throws<ArgumentException>(() => runtime.CompleteShared(frame.Identity, stamp, player, sample));
        runtime.Evaluate(source, null); Assert.Equal(.05f, runtime.Pose[0].Position.X);
        runtime.Cancel(); Assert.Equal(default, runtime.Committed.Traversal.Identity);
        Prepare(runtime, frame, AlsMovementStateInput.Ragdoll); Assert.Equal(1, runtime.SourceRequest.Epoch);
        Assert.Equal(0, runtime.SourceRequest.Time); runtime.CompleteShared(frame.Identity, stamp, player, sample);
        runtime.Evaluate(source, null); runtime.Commit(frame.Identity); Assert.Equal(player, runtime.Committed.Player);
    }

    [Fact]
    public void HiddenSharedInitializationStillRequiresBatchCompletion()
    {
        var stamp = new AlsLocomotionSourceStamp(AlsLocomotionSourceView.CurrentVersion, 0, 1, 2, 3, 4);
        var runtime = Create(new(stamp, 223, 257)); var frame = Next(default, 1);
        Prepare(runtime, frame, AlsMovementStateInput.Grounded, visit: false);
        Assert.Equal(1, runtime.SourceRequest.Initializations); Assert.False(runtime.SourceRequest.Updated);
        Assert.Throws<InvalidOperationException>(() => runtime.Commit(frame.Identity));
        runtime.CompleteShared(frame.Identity, stamp); runtime.Commit(frame.Identity);
        Assert.Equal(1, runtime.Committed.PlayerEpoch); Assert.False(runtime.Committed.PlayerTicked);
        frame = Next(frame, 2); Prepare(runtime, frame, AlsMovementStateInput.Grounded, visit: false);
        Assert.Equal(0, runtime.SourceRequest.Initializations); runtime.CompleteShared(frame.Identity, stamp); runtime.Commit(frame.Identity);
    }

    [Fact]
    public void WarmFrameStateClockAndPoseAllocateNothing()
    {
        var runtime = Create(); var source = new Source(); var frame = default(AlsAnimationGraphFrame);
        for (var i = 1; i <= 1000; i++) Step(i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1001; i <= 3000; i++) Step(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Step(int id)
        {
            frame = Next(frame, id); Prepare(runtime, frame, AlsMovementStateInput.Ragdoll);
            runtime.Evaluate(source, null); runtime.Commit(frame.Identity);
        }
    }
}
