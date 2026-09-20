using System.Text;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Tests;

public sealed class AlsAimFrameRuntimeTests
{
    private static readonly Lazy<AlsAimPoseDefinition> Shared = new(AlsAimPoseCompilerTests.Model);
    private static AlsAimPoseDefinition Model => Shared.Value;
    private static AlsAimingInputState Input(long frame, double yaw = 0, uint character = 1, uint generation = 1) =>
        new(new(frame, character, generation), default, new(-yaw, -30), new(yaw, 35), default, .2,
            .3 + frame % 10 * .01, .5 - Math.Abs(yaw) / 360, .5 + Math.Abs(yaw) / 360, (yaw + 180) / 360);
    private static float[] Weights(AlsAimMachineState state, int count) =>
        Enumerable.Range(0, count).Select(i => AlsTransitionStack.Weight(state.Transitions, i)).ToArray();

    [Fact]
    public void ExplicitTraversalSeparatesFrameGapHiddenUpdateAndInitialization()
    {
        var runtime=new AlsAimFrameRuntime(Model,1,1);
        var graph=default(AlsAnimationGraphFrame).Next(Input(1).Identity,1);
        graph=graph with {Update=new(short.MaxValue,1)};
        Prepare(1,true); runtime.Commit(Input(1).Identity);
        graph=graph.Next(Input(1001).Identity,2);
        Prepare(1001,true);
        Assert.Equal(graph.Update,runtime.Candidate.GetMachine(AlsAimMachineKind.Behavior).LastUpdateCounter);
        Assert.DoesNotContain(Operations(runtime),o=>o.Kind==AlsAimEvaluatorOperationKind.Initialize);
        runtime.Commit(Input(1001).Identity);
        graph=graph.Next(Input(1002).Identity,3);
        var prior=Fingerprint(runtime.Committed);
        Prepare(1002,false);
        Assert.Equal(0,runtime.OperationCount);
        Assert.Equal(1001,runtime.Candidate.GetMachine(AlsAimMachineKind.Behavior).LastUpdateSerial);
        runtime.Cancel(); Assert.Equal(prior,Fingerprint(runtime.Committed));
        Prepare(1002,false); runtime.Commit(Input(1002).Identity);
        graph=graph.Next(Input(1003).Identity,4);
        Prepare(1003,true);
        Assert.Contains(Operations(runtime),o=>o.Kind==AlsAimEvaluatorOperationKind.Initialize);
        runtime.Commit(Input(1003).Identity);
        graph=graph.Next(Input(1004).Identity,5);
        graph=graph with {Initialization=graph.Initialization.Next(5)};
        Prepare(1004,false); var operations=Operations(runtime);
        Assert.Contains(operations,o=>o.Kind==AlsAimEvaluatorOperationKind.Initialize);
        Assert.DoesNotContain(operations,o=>o.Kind==AlsAimEvaluatorOperationKind.Update);
        Assert.False(runtime.Candidate.GetMachine(AlsAimMachineKind.Behavior).Updated);
        runtime.Cancel(); Prepare(1004,false); Assert.Equal(operations,Operations(runtime)); runtime.Commit(Input(1004).Identity);
        graph=graph.Next(Input(1005).Identity,6);
        graph=graph with {Initialization=new(graph.Initialization.Counter,6)};
        Prepare(1005,false); Assert.Equal(0,runtime.OperationCount);
        void Prepare(long frame,bool visited)=>runtime.Prepare(Input(frame),AlsRotationMode.VelocityDirection,true,.01f,frame,visited,traversal:graph);
    }

    [Fact]
    public void RecordedCameraWeightsFollowAnimationTraversalAcrossFrameIdentityGap()
    {
        var runtime=new AlsAimFrameRuntime(Model,1,1);
        var graph=default(AlsAnimationGraphFrame).Next(Input(1).Identity,1);
        runtime.Prepare(Input(1),AlsRotationMode.Aiming,false,0,1,true,traversal:graph);
        runtime.Commit(Input(1).Identity);
        graph=graph.Next(Input(1001).Identity,2);
        runtime.Prepare(Input(1001,160),AlsRotationMode.Aiming,false,.01f,1001,true,traversal:graph);
        var camera=runtime.Candidate.GetMachine(AlsAimMachineKind.Camera);
        Assert.Equal(2,camera.CurrentState);
        Assert.Equal(0,camera.Transitions.Count);
        Assert.Equal(graph.Update,camera.LastUpdateCounter);
    }

    [Fact]
    public void ColdHiddenInitializationPrecedesFirstRelevantStateSelection()
    {
        var runtime = new AlsAimFrameRuntime(Model, 1, 1);
        runtime.Prepare(Input(1), AlsRotationMode.Aiming, true, .1f, 1, false);
        Assert.True(runtime.Candidate.GetMachine(AlsAimMachineKind.Behavior).Initialized);
        Assert.True(runtime.Candidate.GetMachine(AlsAimMachineKind.Input).Initialized);
        Assert.False(runtime.Candidate.GetMachine(AlsAimMachineKind.Camera).Initialized);
        Assert.Equal(1, runtime.Candidate.GetEvaluator(0).Initialization);
        Assert.False(runtime.Candidate.GetEvaluator(0).Updated);
        Assert.DoesNotContain(Operations(runtime), o => o.Kind == AlsAimEvaluatorOperationKind.Update);
        runtime.Commit(Input(1).Identity);
        runtime.Prepare(Input(2), AlsRotationMode.Aiming, true, .1f, 2, true);
        var parent = runtime.Candidate.GetMachine(AlsAimMachineKind.Behavior);
        var camera = runtime.Candidate.GetMachine(AlsAimMachineKind.Camera);
        Assert.Equal(1, parent.CurrentState); Assert.Equal(0, parent.Transitions.Count);
        Assert.Equal(4, camera.CurrentState); Assert.Equal(0, camera.Transitions.Count);
        var update = Assert.Single(Operations(runtime), o => o.Kind == AlsAimEvaluatorOperationKind.Update);
        Assert.Equal(Model.Machines[2].States[4].Evaluator, update.Evaluator);
        Assert.Equal(new AlsAimEvaluatorInput(35, .5f, true), update.State.Input);
        Assert.Equal(1, update.State.CachedWeight); Assert.False(update.State.Inactive);
    }

    [Fact]
    public void AlreadyBlendingInputReentryKeepsIndependentInitializationAndCurves()
    {
        var runtime = new AlsAimFrameRuntime(Model, 1, 1);
        runtime.Prepare(Input(1), AlsRotationMode.VelocityDirection, true, .1f, 1, true);
        runtime.Commit(Input(1).Identity);
        runtime.Prepare(Input(2), AlsRotationMode.VelocityDirection, false, .25f, 2, true);
        var noInput = runtime.Candidate.GetEvaluator(0); var hasInput = runtime.Candidate.GetEvaluator(1);
        Assert.Equal(.5f, noInput.Input.Time); Assert.Equal(.32f, hasInput.Input.Time);
        Assert.True(hasInput.Inactive); Assert.False(noInput.Inactive);
        runtime.Commit(Input(2).Identity);
        runtime.Prepare(Input(3), AlsRotationMode.VelocityDirection, true, .001f, 3, true);
        var state = runtime.Candidate.GetMachine(AlsAimMachineKind.Input);
        Assert.Equal(2, state.Transitions.Count);
        Assert.NotEqual(Model.Machines[1].Edges[state.GetActiveEdge(0)].Curve, Model.Machines[1].Edges[state.GetActiveEdge(1)].Curve);
        Assert.Equal(hasInput.Initialization, runtime.Candidate.GetEvaluator(1).Initialization);
        Assert.DoesNotContain(Operations(runtime), o => o.Evaluator == 1 && o.Kind == AlsAimEvaluatorOperationKind.Initialize);
        Assert.Contains(Operations(runtime), o => o.Evaluator == 1 && o.Kind == AlsAimEvaluatorOperationKind.ClearWeight);
        Assert.Equal(.33f, runtime.Candidate.GetEvaluator(1).Input.Time);
    }

    [Fact]
    public void CameraSwitchRetiresOutgoingCurveAtItsEndpointBeforeDurationExpires()
    {
        var runtime = new AlsAimFrameRuntime(Model, 1, 1);
        runtime.Prepare(Input(1), AlsRotationMode.Aiming, false, 0, 1, true); runtime.Commit(Input(1).Identity);
        runtime.Prepare(Input(2, -160), AlsRotationMode.Aiming, false, 0, 2, true); runtime.Commit(Input(2).Identity);
        Assert.Equal(1, runtime.Committed.GetMachine(AlsAimMachineKind.Camera).CurrentState);
        runtime.Prepare(Input(3, 160), AlsRotationMode.Aiming, false, .5f, 3, true);
        var state = runtime.Candidate.GetMachine(AlsAimMachineKind.Camera);
        Assert.Equal(2, state.CurrentState); Assert.Equal(1, state.Transitions.Count);
        Assert.Equal(.5f, state.Transitions.GetTransition(0).Alpha, 6);
        var updates = Operations(runtime).Where(o => o.Kind == AlsAimEvaluatorOperationKind.Update).ToArray();
        Assert.Equal(new[] { 3, 2 }.Select(s => Model.Machines[2].States[s].Evaluator), updates.Select(o => o.Evaluator));
        Assert.True(updates[0].State.Inactive); Assert.False(updates[1].State.Inactive);
        Assert.Equal(2, runtime.Candidate.GetEvaluator(Model.Machines[2].States[1].Evaluator).LastUpdateSerial);
        runtime.Commit(Input(3).Identity);
        runtime.Prepare(Input(4, 160), AlsRotationMode.Aiming, false, .5f, 4, true);
        Assert.Equal(0, runtime.Candidate.GetMachine(AlsAimMachineKind.Camera).Transitions.Count);
        Assert.Single(Operations(runtime), o => o.Kind == AlsAimEvaluatorOperationKind.Update);
    }

    [Fact]
    public void ElapsedGetterResetsWithinTheFrameAndTwoSecondRuleIsStrict()
    {
        var machine = new AlsAimStateMachine(Model, AlsAimMachineKind.Camera);
        var state = machine.Update(default, AlsRotationMode.Aiming, false, 0, new float[5], 1, 3, 1).State;
        Assert.Equal(4, state.CurrentState); Assert.Equal(3, state.ElapsedSeconds);
        state = machine.Update(state, AlsRotationMode.Aiming, false, -160, Weights(state, 5), 1, 0, 2).State;
        Assert.Equal(1, state.CurrentState); Assert.Equal(0, state.ElapsedSeconds);
        state = machine.Update(state, AlsRotationMode.Aiming, false, -160, Weights(state, 5), 1, 2, 3).State;
        state = machine.Update(state, AlsRotationMode.Aiming, false, -160, Weights(state, 5), 1, 0, 4).State;
        Assert.Equal(1, state.CurrentState); Assert.Equal(2, state.ElapsedSeconds);
        state = machine.Update(state, AlsRotationMode.Aiming, false, -160, Weights(state, 5), 1, .01f, 5).State;
        Assert.Equal(1, state.CurrentState);
        state = machine.Update(state, AlsRotationMode.Aiming, false, -160, Weights(state, 5), 1, 0, 6).State;
        Assert.Equal(0, state.CurrentState); Assert.Equal(0, state.ElapsedSeconds);
    }

    [Fact]
    public void GatesUseRecordedWeightsInsteadOfTheLiveTransitionStack()
    {
        var machine = new AlsAimStateMachine(Model, AlsAimMachineKind.Camera);
        var state = machine.Update(default, AlsRotationMode.Aiming, false, 0, new float[5], 1, 0, 1).State;
        var complete = Weights(state, 5);
        var snap = machine.Update(state, AlsRotationMode.Aiming, false, 160, complete, 1, .01f, 2);
        var delayed = machine.Update(state, AlsRotationMode.Aiming, false, 160, new float[5], 1, .01f, 2);
        Assert.Equal(2, snap.State.CurrentState); Assert.Equal(0, snap.State.Transitions.Count);
        Assert.Equal(2, delayed.State.CurrentState); Assert.Equal(1, delayed.State.Transitions.Count);
        Assert.Equal(.2f, delayed.State.Transitions.Latest.Duration);
    }

    [Fact]
    public void EachTransitionChoosesItsOwnProfileWithoutChangingScalarUpdateWeights()
    {
        var machine = new AlsAimStateMachine(Model, AlsAimMachineKind.Behavior);
        var state = machine.Update(default, AlsRotationMode.VelocityDirection, false, 0, new float[2], 1, 0, 1).State;
        var update = machine.Update(state, AlsRotationMode.Aiming, false, 0, Weights(state, 2), .4f, .5f, 2);
        Assert.Equal(.2f, update.GetUpdate(0).Weight); Assert.Equal(.2f, update.GetUpdate(1).Weight);
        var head = Model.Head.BoneNames.ToArray().ToList().IndexOf("head");
        Assert.Equal(.8f, machine.BoneStateWeight(update.State, 1, head), 6);
        Assert.Equal(.5f, machine.BoneStateWeight(update.State, 1, 0), 6);
        state = machine.Update(update.State, AlsRotationMode.VelocityDirection, false, 0, Weights(update.State, 2), 1, .1f, 3).State;
        Assert.Equal(2, state.Transitions.Count);
        Assert.Equal(1, machine.BoneStateWeight(state, 0, head) + machine.BoneStateWeight(state, 1, head), 6);
    }

    [Fact]
    public void HiddenFrameClearsProxyRecordsAndReentryReinitializesNestedOwners()
    {
        var runtime = new AlsAimFrameRuntime(Model, 1, 1);
        runtime.Prepare(Input(1), AlsRotationMode.Aiming, true, .1f, 1, true); runtime.Commit(Input(1).Identity);
        var source = Model.Machines[2].States[4].Evaluator; var old = runtime.Committed.GetEvaluator(source);
        runtime.Prepare(Input(2), AlsRotationMode.Aiming, true, .1f, 2, false);
        Assert.Equal(0, runtime.OperationCount);
        Assert.Equal(0, runtime.Candidate.GetRecordedWeight(AlsAimMachineKind.Camera, 4));
        Assert.Equal(old, runtime.Candidate.GetEvaluator(source)); runtime.Commit(Input(2).Identity);
        runtime.Prepare(Input(3), AlsRotationMode.Aiming, true, .1f, 3, true);
        Assert.Equal(old.Initialization + 1, runtime.Candidate.GetEvaluator(source).Initialization);
        Assert.Equal(.1f, runtime.Candidate.GetMachine(AlsAimMachineKind.Camera).ElapsedSeconds);
        Assert.Equal(0, runtime.Candidate.GetMachine(AlsAimMachineKind.Camera).Transitions.Count);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void FullGraphHistoryAndAllOperationsSurviveEveryFrameCancellation(int hz)
    {
        var runtime = new AlsAimFrameRuntime(Model, 1, 1);
        var updates = 0; var inits = 0; var hidden = 0; var maxTransitions = 0;
        for (var frame = 1; frame <= hz * 12; frame++)
        {
            var time = (float)frame / hz;
            var input = Input(frame, frame % 5 == 0 ? 127 : Math.Sin(time * 3) * 179);
            var mode = (AlsRotationMode)(frame / (hz * 2) % 3); var visible = frame % hz >= 3;
            var before = Fingerprint(runtime.Committed);
            runtime.Prepare(input, mode, frame % hz > hz / 2, 1f / hz, frame, visible, .7f);
            var expected = Fingerprint(runtime.Candidate); var operations = Operations(runtime);
            maxTransitions = Math.Max(maxTransitions, runtime.Candidate.GetMachine(AlsAimMachineKind.Camera).Transitions.Count);
            updates += operations.Count(o => o.Kind == AlsAimEvaluatorOperationKind.Update);
            inits += operations.Count(o => o.Kind == AlsAimEvaluatorOperationKind.Initialize); if (!visible) hidden++;
            runtime.Cancel(); Assert.Equal(before, Fingerprint(runtime.Committed));
            runtime.Prepare(input, mode, frame % hz > hz / 2, 1f / hz, frame, visible, .7f);
            Assert.Equal(expected, Fingerprint(runtime.Candidate)); Assert.Equal(operations, Operations(runtime));
            runtime.Commit(input.Identity); Assert.Equal(expected, Fingerprint(runtime.Committed));
        }
        Assert.True(updates > hz * 6); Assert.True(inits > 20); Assert.True(hidden >= 30); Assert.True(maxTransitions > 1);
    }

    [Fact]
    public void ZeroWeightInactiveTraversalStillUpdatesAndSkippedSerialResetsRelevance()
    {
        var runtime = new AlsAimFrameRuntime(Model, 1, 1);
        runtime.Prepare(Input(1), AlsRotationMode.Aiming, false, .1f, 1, true, 0, true);
        var update = Assert.Single(Operations(runtime), o => o.Kind == AlsAimEvaluatorOperationKind.Update);
        Assert.Equal(0, update.State.CachedWeight); Assert.True(update.State.Inactive);
        Assert.Equal(1, runtime.Candidate.GetRecordedWeight(AlsAimMachineKind.Camera, 4));
        runtime.Commit(Input(1).Identity);
        runtime.Prepare(Input(2), AlsRotationMode.Aiming, false, .1f, 4, true);
        var next = Assert.Single(Operations(runtime), o => o.Kind == AlsAimEvaluatorOperationKind.Update);
        Assert.Equal(update.State.Initialization + 1, next.State.Initialization);
        Assert.False(next.State.Inactive); Assert.Equal(1, next.State.CachedWeight);
        Assert.Equal(.1f, runtime.Candidate.GetMachine(AlsAimMachineKind.Camera).ElapsedSeconds);
    }

    [Fact]
    public void FrameOwnerRejectsForeignStalePendingAndFailedCandidates()
    {
        var runtime = new AlsAimFrameRuntime(Model, 1, 1);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(Input(1, character: 2), AlsRotationMode.Aiming, false, .1f, 1, true));
        Assert.Throws<ArgumentException>(() => runtime.Prepare(Input(1, generation: 2), AlsRotationMode.Aiming, false, .1f, 1, true));
        runtime.Prepare(Input(1), AlsRotationMode.Aiming, false, .1f, 1, true);
        Assert.Throws<InvalidOperationException>(() => runtime.Prepare(Input(2), AlsRotationMode.Aiming, false, .1f, 2, true));
        Assert.Throws<InvalidOperationException>(() => runtime.Commit(Input(2).Identity));
        runtime.Commit(Input(1).Identity);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(Input(1), AlsRotationMode.Aiming, false, .1f, 2, true));
        Assert.Throws<ArgumentException>(() => runtime.Prepare(Input(2), AlsRotationMode.Aiming, false, .1f, 1, true));
        var before = Fingerprint(runtime.Committed);
        var invalid = Input(2) with { SmoothedAngle = new(0, double.NaN) };
        Assert.Throws<ArgumentException>(() => runtime.Prepare(invalid, AlsRotationMode.Aiming, false, .1f, 2, true));
        Assert.Throws<InvalidOperationException>(() => runtime.Commit(invalid.Identity));
        Assert.Equal(before, Fingerprint(runtime.Committed)); runtime.Cancel();
        runtime.Prepare(Input(2), AlsRotationMode.Aiming, false, .1f, 2, true); runtime.Commit(Input(2).Identity);
    }

    [Fact]
    public void TenOwnersShareDefinitionsButNeverHistoryAndHotPrepareAllocatesNothing()
    {
        var serial = new string[10]; var parallel = new string[10];
        for (uint owner = 0; owner < 10; owner++) serial[owner] = Run(owner);
        Parallel.For(0, 10, owner => parallel[owner] = Run((uint)owner)); Assert.Equal(serial, parallel);
        var runtime = new AlsAimFrameRuntime(Model, 1, 1);
        for (var frame = 1; frame <= 1000; frame++) Step(runtime, frame, 1);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 1001; frame <= 11000; frame++) Step(runtime, frame, 1);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        string Run(uint owner)
        {
            var frameOwner = new AlsAimFrameRuntime(Model, owner, 1);
            for (var frame = 1; frame <= 600; frame++) Step(frameOwner, frame, owner);
            return Fingerprint(frameOwner.Committed);
        }
        static void Step(AlsAimFrameRuntime owner, int frame, uint character)
        {
            var input = Input(frame, Math.Sin((frame + character * 20) * .07) * 179, character);
            owner.Prepare(input, (AlsRotationMode)(frame / 60 % 3), frame % 20 < 10, 1f / 60, frame, frame % 50 >= 2);
            owner.Commit(input.Identity);
        }
    }

    private static AlsAimEvaluatorOperation[] Operations(AlsAimFrameRuntime runtime) =>
        Enumerable.Range(0, runtime.OperationCount).Select(runtime.GetOperation).ToArray();
    private static string Fingerprint(in AlsAimFrameState frame)
    {
        var text = new StringBuilder(); text.Append(frame.Identity).Append('|').Append(frame.Serial);
        for (var m = 0; m < 3; m++)
        {
            var kind = (AlsAimMachineKind)m; var machine = frame.GetMachine(kind);
            text.Append('|').Append(machine.Kind).Append(machine.Initialized).Append(machine.Updated).Append(machine.LastUpdateSerial)
                .Append(machine.CurrentState).Append(BitConverter.SingleToInt32Bits(machine.ElapsedSeconds)).Append(machine.Transitions.Count);
            for (var state = 0; state < (m == 2 ? 5 : 2); state++) text.Append('|').Append(BitConverter.SingleToInt32Bits(frame.GetRecordedWeight(kind, state)));
            for (var t = 0; t < machine.Transitions.Count; t++) text.Append('|').Append(machine.GetActiveEdge(t)).Append(machine.Transitions.GetTransition(t));
        }
        for (var evaluator = 0; evaluator < 7; evaluator++) text.Append('|').Append(frame.GetEvaluator(evaluator));
        return text.ToString();
    }
}
