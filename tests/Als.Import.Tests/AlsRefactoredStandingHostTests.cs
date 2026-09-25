using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredStandingHostTests
{
    internal static readonly Lazy<AlsRefactoredStandingHostProfile> Data = new(() =>
    {
        var index = MantlingHostFixture.Read("refactored_animation_sources");
        byte[] Read(string p) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p));
        var catalog = new AlsRefactoredAnimationCatalog(index, Read);
        var triangles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), index, Read);
        return new(catalog, MantlingHostFixture.Read("refactored_stance_machines"),
            new(MantlingHostFixture.Read("refactored_sync_inputs"), catalog), triangles,
            new(MantlingHostFixture.Read("refactored_movement_settings"), catalog),
            new(MantlingHostFixture.Read("refactored_rest_settings"), catalog),
            new(MantlingHostFixture.Read("refactored_skeleton_curves"), catalog),
            MantlingHostFixture.Read("refactored_slot_inventory"), MantlingHostFixture.Read("refactored_quick_stop_settings"));
    });

    private static AlsRefactoredStandingHostInput Input(int frame, int hz)
    {
        var phase = frame / hz; var local = (float)(frame % hz) / hz;
        var moving = phase is >= 2 and <= 5 && (local < .08f || local >= .3f && local < .82f);
        var rotation = phase is 7 or 8 ? AlsRefactoredRestRotation.Aiming : AlsRefactoredRestRotation.ViewDirection;
        var yaw = phase switch { 0 => 0f, 1 => -110f, 6 => 110f, 7 => -85f, 8 => 85f, _ => 0f };
        var delta = 1f / hz; var angle = phase % 2 == 0 ? 55f : -55f;
        var movement = new AlsRefactoredMovementInput(moving ? new(170, phase % 2 == 0 ? 100 : -100, 0) : AlsDoubleVector.Zero,
            new(200, 100, 0), AlsQuaternion.Identity, moving ? 200 : 0, 1, angle, 0, 1000, 800,
            "Als.Gait.Running", false, frame == 0, delta, 1, 0, 0, .2f);
        var rest = new AlsRefactoredRestInput(delta, yaw, 0, moving, false, rotation,
            AlsRefactoredRestStance.Standing, true, frame == 0, 1, phase == 9 ? 1 : 0, 0,
            new(20, 0, 0), default, default, default);
        return new(movement, rest, new(1, 1, 1, 0), new(false, false, moving, 90, 0, 0),
            (phase % 4) switch { 0 => -.75f, 1 => -.25f, 2 => .25f, _ => .75f }, moving, phase == 4 && local < .01f);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void WholeStandingFrameRollsBackIncludingActionsAndSupportsSkippedEvaluation(int hz)
    {
        var profile = Data.Value; var host = profile.CreateRuntime(19, 1); var clean = profile.CreateRuntime(19, 1);
        var full = profile.CreateRuntime(19, 1);
        var init = new AlsGraphTraversalCounter(0, 0); var counter = init;
        var states = new HashSet<int>(); var slots = new HashSet<AlsMontageSlot>(); var quick = 0; var skipped = 0;
        var evaluated = 0; var movement = 0; var nativeBones = 0;
        for (var frame = 0; frame < hz * 11; frame++)
        {
            if (frame == hz * 10) counter = counter.Next((ulong)frame).Next((ulong)frame);
            var initialize = frame == 0 || frame == hz * 10 + 2;
            if (initialize && frame > 0) init = init.Next((ulong)frame);
            var id = new AlsFrameIdentity(frame, 19, 1);
            var context = new AlsPoseUpdateContext(id, 1, 1f / hz).WithUpdateCounter(counter);
            var input = Input(frame, hz); var evaluate = frame % 13 >= 3;
            void Prepare(AlsRefactoredStandingHost runtime)
            {
                runtime.Prepare(context, input, init, initialize);
                if (evaluate) runtime.Evaluate(AlsPrecisePose.Identity);
            }
            Prepare(host); var state = host.State; var parent = host.MovementState; var rest = host.RestState;
            var cache = host.CacheUpdates.ToArray(); var snapshot = host.MontageFrame.Evaluations.ToArray();
            var pose = evaluate ? host.Pose.ToArray() : []; var curves = evaluate ? host.Curves.ToArray() : [];
            Assert.Throws<ArgumentException>(() => host.Commit(id)); // Main-thread phase cannot be omitted.
            if (evaluate)
            {
                host.Evaluate(AlsPrecisePose.Identity);
                Assert.Equal(pose, host.Pose.ToArray()); Assert.Equal(curves, host.Curves.ToArray());
                foreach (var bone in pose) bone.Validate();
                Assert.Equal(new AlsInertialCurve(1), curves[Array.IndexOf(host.CurveNames.ToArray(), "PoseStanding")]);
                evaluated++; nativeBones += pose.Length;
            }
            else { skipped++; Assert.Throws<InvalidOperationException>(() => host.Pose.ToArray()); }
            host.PostUpdateActions(); var instances = host.CandidateMontages.ToArray(); var count = host.QuickStopDispatchCount;
            Assert.Equal(snapshot, host.MontageFrame.Evaluations.ToArray()); // Newly played instances are for next frame.
            Assert.Throws<InvalidOperationException>(() => host.PostUpdateActions());
            host.Cancel();
            Assert.Throws<InvalidOperationException>(() => host.Pose.ToArray());
            Prepare(host); Prepare(clean);
            Assert.Equal(state, host.State); Assert.Equal(parent, host.MovementState); Assert.Equal(rest, host.RestState);
            Assert.Equal(cache, host.CacheUpdates.ToArray());
            if (evaluate)
            {
                Assert.Equal(pose, host.Pose.ToArray()); Assert.Equal(curves, host.Curves.ToArray());
                Assert.True(host.Pose.SequenceEqual(clean.Pose)); Assert.True(host.Curves.SequenceEqual(clean.Curves));
            }
            host.PostUpdateActions(); clean.PostUpdateActions();
            Assert.Equal(count, host.QuickStopDispatchCount); Assert.Equal(instances, host.CandidateMontages.ToArray());
            Assert.True(host.CandidateMontages.SequenceEqual(clean.CandidateMontages));
            full.Prepare(context, input, init, initialize); full.Evaluate(AlsPrecisePose.Identity); full.PostUpdateActions();
            Assert.Equal(host.State, full.State); Assert.Equal(host.RestState, full.RestState); Assert.Equal(host.MovementState, full.MovementState);
            Assert.True(host.CandidateMontages.SequenceEqual(full.CandidateMontages));
            states.Add(state); quick += count;
            foreach (var entry in host.CandidateMontages) slots.Add(entry.Slot);
            if (cache.Length > 0) movement++;
            Assert.Equal(cache.Length, cache.Select(c => c.PropertyIndex).Distinct().Count());
            host.Commit(id); clean.Commit(id); full.Commit(id);
            Assert.Equal(id, host.CommittedIdentity); Assert.Equal(state, host.CommittedState);
            Assert.True(host.CommittedMontages.SequenceEqual(clean.CommittedMontages));
            Assert.Throws<ArgumentException>(() => host.Prepare(context, input, init));
            counter = counter.Next((ulong)frame + 1);
        }
        Assert.Equal(5, states.Count); Assert.True(quick > 0); Assert.True(movement > 0);
        Assert.Contains((AlsMontageSlot)AlsTurnSlot.Standing, slots); Assert.Contains(AlsMontageSlot.Transition, slots);
        Assert.True(skipped > 0); Assert.Equal(evaluated * 79, nativeBones);
    }

    [Fact]
    public void MovingSmoothDrivesStandingIndependentlyOfRawMoving()
    {
        var host = Data.Value.CreateRuntime(19, 1); var counter = new AlsGraphTraversalCounter(0, 0);
        for (var frame = 0; frame < 2; frame++)
        {
            var id = new AlsFrameIdentity(frame, 19, 1);
            var context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(counter);
            var input = Input(0, 60) with { MovingSmooth = frame == 1 };
            Assert.False(input.Rest.Moving);
            host.Prepare(context, input, new(0, 0), frame == 0);
            Assert.Equal(frame, host.State);
            host.PostUpdateActions(); host.Commit(id); counter = counter.Next((ulong)frame + 1);
        }
    }

    [Fact]
    public void HiddenMovementFramesRetireSyncGroupBeforeRunStartReentry()
    {
        var profile = Data.Value; var host = profile.CreateRuntime(19, 1);
        var counter = new AlsGraphTraversalCounter(0, 0); var first = 0f;
        var playerId = Array.FindIndex(profile.Details.Players.Players.ToArray(), p => p.PropertyIndex == 114);
        Assert.True(playerId >= 0);
        for (var frame = 0; frame <= 69; frame++)
        {
            var id = new AlsFrameIdentity(frame, 19, 1);
            var context = new AlsPoseUpdateContext(id, 1, 1f / 30).WithUpdateCounter(counter);
            host.Prepare(context, Input(frame, 30), new(0, 0), frame == 0);
            if (frame is >= 65 and <= 68) Assert.Null(host.MovementPlayers);
            if (frame is 60 or 69)
            {
                var time = host.MovementPlayers!.Players.ToArray().Single(p => p.PlayerId == playerId).Time;
                if (frame == 60) first = time;
                else Assert.Equal(first, time);
                Assert.Equal(.1f + 1.25f * (1f / 30), time);
            }
            host.PostUpdateActions(); host.Commit(id); counter = counter.Next((ulong)frame + 1);
        }
    }

    [Fact]
    public void QuickStopFailureAfterPostUpdateRollsBackBankAndNotificationTogether()
    {
        var host = Data.Value.CreateRuntime(19, 1); var clean = Data.Value.CreateRuntime(19, 1);
        var init = new AlsGraphTraversalCounter(0, 0); var counter = init;
        for (var frame = 0; frame < 4; frame++)
        {
            var id = new AlsFrameIdentity(frame, 19, 1);
            var context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(counter);
            var input = frame == 1 ? Input(120, 60) : Input(0, 60);
            if (frame == 2)
            {
                var committed = host.CommittedMontages.ToArray(); var state = host.CommittedState;
                host.Prepare(context, input with { QuickStop = input.QuickStop with { InputYaw = double.NaN } }, init);
                host.Evaluate(AlsPrecisePose.Identity);
                Assert.Throws<ArgumentException>(() => host.PostUpdateActions());
                Assert.Equal(committed, host.CommittedMontages.ToArray()); Assert.Equal(state, host.CommittedState);
                Assert.Throws<InvalidOperationException>(() => host.Commit(id));
            }
            host.Prepare(context, input, init); clean.Prepare(context, input, init);
            host.Evaluate(AlsPrecisePose.Identity); clean.Evaluate(AlsPrecisePose.Identity);
            Assert.True(host.Pose.SequenceEqual(clean.Pose));
            host.PostUpdateActions(); clean.PostUpdateActions();
            if (frame == 2) Assert.Equal(1, host.QuickStopDispatchCount);
            Assert.True(host.CandidateMontages.SequenceEqual(clean.CandidateMontages));
            host.Commit(id); clean.Commit(id); counter = counter.Next((ulong)frame + 1);
        }
    }

    [Fact]
    public void FailedFrameCancelsEveryParticipantAndCannotLeakToAnotherCharacter()
    {
        var host = Data.Value.CreateRuntime(19, 1); var clean = Data.Value.CreateRuntime(19, 1);
        var init = new AlsGraphTraversalCounter(0, 0); var input = Input(0, 60);
        var id = new AlsFrameIdentity(0, 19, 1); var context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(init);
        Assert.Throws<ArgumentException>(() => host.Prepare(new AlsPoseUpdateContext(new(0, 20, 1), 1, 1f / 60).WithUpdateCounter(init), input, init));
        // Failure after bank, queue and Movement Parent have all prepared.
        Assert.Throws<ArgumentException>(() => host.Prepare(context, input with { Rest = input.Rest with { Scale = 0 } }, init));
        Assert.Throws<InvalidOperationException>(() => host.Commit(id));
        host.Prepare(context, input, init); Assert.Throws<ArgumentException>(() => host.Evaluate(default));
        Assert.Throws<InvalidOperationException>(() => host.PostUpdateActions());
        host.Prepare(context, input, init); clean.Prepare(context, input, init);
        host.Evaluate(AlsPrecisePose.Identity); clean.Evaluate(AlsPrecisePose.Identity);
        Assert.True(host.Pose.SequenceEqual(clean.Pose)); Assert.True(host.Curves.SequenceEqual(clean.Curves));
        host.PostUpdateActions(); clean.PostUpdateActions(); host.Commit(id); clean.Commit(id);
        Assert.Equal(clean.CommittedState, host.CommittedState);
        Assert.True(host.CommittedMontages.SequenceEqual(clean.CommittedMontages));
    }
}
