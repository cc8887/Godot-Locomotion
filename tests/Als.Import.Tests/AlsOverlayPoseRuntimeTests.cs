using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Tests;

public sealed class AlsOverlayPoseRuntimeTests
{
    private static readonly string[] Curves = ["Enable_Transition", "RotationAmount", "Weight_Gait", "Weight_InAir",
        "Layering_Arm_L_Add", "Layering_Arm_R_Add", "Layering_Spine_Add", "Layering_Head_Add"];
    private sealed class Sink : IAlsOverlayPoseSink
    {
        public bool Fail;
        public int Initializations;
        public readonly bool[] Seen = new bool[148];
        public void InitializeSource(int source, int initialization) { Initializations++; }
        public void UpdateSource(in AlsOverlaySourceUpdate update)
        {
            if (update.Initialization <= 0 || !float.IsFinite(update.Weight)) throw new InvalidOperationException();
        }
        public void EvaluateSource(int source, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
        {
            if (Fail) throw new InvalidOperationException("Injected source failure");
            Seen[source] = true;
            pose.Fill(new(new Vector3(source * .001f), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, source * .001f), Vector3.One));
            curves.Clear(); curves[0] = new(1); curves[4] = new(.1f);
        }
        public void RequestInertialization(in AlsOverlayInertialRequest request) { }
        public void QueueTransitionNotify(AlsOverlayMachineKind machine, in AlsOverlayTransitionNotify notify) { }
    }
    private static AlsOverlayPoseRuntime New() => new(AlsOverlayPoseCompilerTests.Definition.Value, Curves,
        Enumerable.Repeat(AlsLocalPose.Identity, 79).ToArray(), 7, 2);
    private sealed class PreciseSink : IAlsPreciseOverlayPoseSink
    {
        public bool Invalid;
        public void InitializeSource(int source, int initialization) { }
        public void UpdateSource(in AlsOverlaySourceUpdate update) { }
        public void EvaluateSource(int source, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves) =>
            throw new InvalidOperationException("Precise source must not use the single projection callback.");
        public void EvaluatePreciseSource(int source, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
        {
            pose.Fill(new(new(source * .001, 0, 0), AlsQuaternion.FromAxisAngle(Vector3.UnitZ, source * .001f), AlsDoubleVector.One));
            if (Invalid) pose[^1] = pose[^1] with { Rotation = new(0, 0, 0, double.NaN) };
            curves.Clear(); curves[0] = new(1); curves[4] = new(.1f);
        }
        public void RequestInertialization(in AlsOverlayInertialRequest request) { }
        public void QueueTransitionNotify(AlsOverlayMachineKind machine, in AlsOverlayTransitionNotify notify) { }
    }

    [Fact]
    public void InvalidPreciseSourceAbortsCandidateWithoutPublishingAndSameFrameCanRetry()
    {
        var runtime = New(); var sink = new PreciseSink(); var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[Curves.Length];
        runtime.Prepare(Context(1), State(0), Input(0), curves, sink); runtime.Evaluate(pose, curves); runtime.Commit();
        var savedPose = pose.ToArray(); var savedCurves = curves.ToArray(); sink.Invalid = true;
        runtime.Prepare(Context(2), State(3), Input(1), savedCurves, sink);
        Assert.Throws<ArgumentException>(() => runtime.Evaluate(pose, curves));
        Assert.False(runtime.HasCandidate); Assert.Equal(savedPose, pose); Assert.Equal(savedCurves, curves);
        Assert.Throws<InvalidOperationException>(runtime.Commit);
        sink.Invalid = false; runtime.Prepare(Context(2), State(3), Input(1), savedCurves, sink); runtime.Evaluate(pose, curves); runtime.Commit();
    }
    private static AlsOverlayStateInput State(int overlay, bool aim = false) => new((AlsOverlayKind)overlay,
        aim ? AlsRotationMode.Aiming : AlsRotationMode.LookingDirection, AlsGait.Running, AlsMovementStateInput.Grounded, false, 1);
    private static AlsOverlayPoseInput Input(int value, bool aim = false) => new(.6f, .4f, new(.1f, .2f, .3f, .4f), .13, -.2, .1, .7, value, aim);
    private static AlsOverlayPoseContext Context(long frame) => new(new(frame, 7, 2), frame, 1f / 60, 1, AlsLocalPose.Identity, 0, 0);

    [Fact]
    public void HiddenLifecyclePreservesHistoryAndExplicitInitializationCommitsAtomically()
    {
        var runtime = New(); var sink = new Sink();
        var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[Curves.Length];
        var traversal = default(AlsAnimationGraphFrame);
        for (var frame = 1; frame <= 8; frame++)
        {
            var context = Context(frame); var next = traversal.Next(context.Identity, (ulong)frame);
            var hidden = frame is 1 or 4 or 5 or 6 or 7;
            if (frame == 5) next = next with { Bones = next.Bones.Next(5) };
            if (frame == 6) next = next with { Initialization = next.Initialization.Next(6) };
            if (frame == 7) next = next with { Initialization = new(next.Initialization.Counter, 7) };
            var oldId = runtime.CommittedIdentity; var oldInertia = runtime.CommittedInertiaHistoryCount;
            var oldMachine = runtime.Machine(AlsOverlayMachineKind.Overlay);
            var nodes = runtime.Definition.Nodes.ToArray().Where(n => n.Kind == AlsOverlayPoseKind.Source).Select(n => n.Index).ToArray();
            var epochs = nodes.Select(runtime.SourceInitialization).ToArray();
            Prepare();
            var initialized = sink.Initializations;
            var expectedEpochs = nodes.Select(runtime.SourceInitialization).ToArray();
            var expectedMachine = runtime.Machine(AlsOverlayMachineKind.Overlay);
            if (hidden)
            {
                Assert.Equal(0, runtime.SourceUpdates); Assert.Equal(0, runtime.SourceEvaluations);
                Assert.Equal(0, runtime.NotifyCount); Assert.Equal(0, runtime.InertialRequestCount);
                Assert.Throws<InvalidOperationException>(() => runtime.Evaluate(pose, curves));
                if (frame is 1 or 6) { Assert.True(initialized > 0); Assert.False(expectedMachine.Updated); }
                else
                {
                    Assert.Equal(0, initialized); Assert.Equal(epochs, expectedEpochs);
                    Assert.Equal(oldMachine.LastUpdateCounter, expectedMachine.LastUpdateCounter);
                    Assert.Equal(oldMachine.ElapsedSeconds, expectedMachine.ElapsedSeconds);
                }
            }
            else Assert.True(runtime.SourceUpdates > 0 && runtime.SourceEvaluations > 0);
            var expectedPose = pose.ToArray(); var expectedCurves = curves.ToArray();
            runtime.Cancel();
            Assert.Equal(oldId, runtime.CommittedIdentity); Assert.Equal(oldInertia, runtime.CommittedInertiaHistoryCount);
            Assert.Equal(epochs, nodes.Select(runtime.SourceInitialization));
            Assert.Equal(oldMachine.LastUpdateCounter, runtime.Machine(AlsOverlayMachineKind.Overlay).LastUpdateCounter);
            Prepare();
            Assert.Equal(initialized, sink.Initializations); Assert.Equal(expectedEpochs, nodes.Select(runtime.SourceInitialization));
            Assert.Equal(expectedPose, pose); Assert.Equal(expectedCurves, curves);
            runtime.ValidateCommit(); runtime.Commit();
            Assert.Equal(context.Identity, runtime.CommittedIdentity); Assert.Equal(next, runtime.CommittedTraversal);
            if (hidden) Assert.Equal(frame is 1 or 6 ? 0 : oldInertia, runtime.CommittedInertiaHistoryCount);
            else Assert.True(runtime.CommittedInertiaHistoryCount > 0);
            traversal = next;
            void Prepare()
            {
                sink.Initializations = 0;
                runtime.Prepare(context, State(0), Input(0), new AlsInertialCurve[Curves.Length], sink, next, !hidden);
                if (!hidden) runtime.Evaluate(pose, curves);
            }
        }
        Assert.Throws<ArgumentException>(() => runtime.Prepare(Context(9), State(0), Input(0), curves, sink));
        Assert.Throws<ArgumentException>(() => New().Prepare(Context(1), State(0), Input(0), curves, sink, updateSource: false));
    }

    [Fact]
    public void ExplicitTraversalLateSourceFailureRetainsCommittedEpochsAndAllowsSameFrameRetry()
    {
        var runtime = New(); var sink = new Sink(); var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[Curves.Length];
        var first = default(AlsAnimationGraphFrame).Next(Context(1).Identity, 1);
        runtime.Prepare(Context(1), State(0), Input(0), curves, sink, first); runtime.Evaluate(pose, curves); runtime.Commit();
        var inertia = runtime.CommittedInertiaHistoryCount;
        var next = first.Next(Context(2).Identity, 2) with { Initialization = first.Initialization.Next(2) };
        runtime.Prepare(Context(2), State(3), Input(1), curves, sink, next); sink.Fail = true;
        Assert.Throws<InvalidOperationException>(() => runtime.Evaluate(pose, curves));
        Assert.False(runtime.HasCandidate); Assert.Equal(first, runtime.CommittedTraversal);
        Assert.Equal(inertia, runtime.CommittedInertiaHistoryCount);
        sink.Fail = false; runtime.Prepare(Context(2), State(3), Input(1), curves, sink, next);
        runtime.Evaluate(pose, curves); runtime.Commit(); Assert.Equal(next, runtime.CommittedTraversal);
    }

    [Fact]
    public void AllOverlayBranchesSupportCancellationNestedReentryAndLatePoseFailure()
    {
        var runtime = New(); var sink = new Sink();
        var pose = new AlsLocalPose[79]; var expected = new AlsLocalPose[79];
        var curves = new AlsInertialCurve[Curves.Length]; var expectedCurves = new AlsInertialCurve[Curves.Length];
        var feedback = new AlsInertialCurve[Curves.Length]; feedback[0] = new(1); feedback[2] = new(2.5f); feedback[3] = new(.6f);
        for (var frame = 0; frame < 156; frame++)
        {
            var context = Context(frame + 1); var aim = frame % 3 == 0; var state = State(frame / 12, aim); var input = Input(frame % 4, aim);
            runtime.Prepare(context, state, input, feedback, sink); runtime.Evaluate(expected, expectedCurves);
            var machine = runtime.Machine(AlsOverlayMachineKind.Overlay); runtime.Cancel();
            if (frame % 5 == 0)
            {
                runtime.Prepare(context, state, input, feedback, sink); pose.AsSpan().Fill(AlsLocalPose.Identity); curves.AsSpan().Fill(new(123)); sink.Fail = true;
                Assert.Throws<InvalidOperationException>(() => runtime.Evaluate(pose, curves)); sink.Fail = false;
                Assert.False(runtime.HasCandidate); Assert.All(pose, p => Assert.Equal(AlsLocalPose.Identity, p)); Assert.All(curves, c => Assert.Equal(123, c.Value));
            }
            runtime.Prepare(context, state, input, feedback, sink); runtime.Evaluate(pose, curves);
            Assert.Equal(expected, pose); Assert.Equal(expectedCurves, curves);
            Assert.Equal(machine.CurrentState, runtime.Machine(AlsOverlayMachineKind.Overlay).CurrentState);
            runtime.Commit(); Assert.False(runtime.HasCandidate);
        }
        Assert.True(sink.Seen.Count(s => s) > 100);
    }

    [Fact]
    public void RejectsForeignHistoryAndMissingCurveWritesBeforePublication()
    {
        var runtime = New(); var sink = new Sink(); var feedback = new AlsInertialCurve[Curves.Length];
        Assert.Throws<ArgumentException>(() => runtime.Prepare(Context(1) with { Identity = new(1, 8, 2) }, State(0), Input(0), feedback, sink));
        Assert.Throws<ArgumentException>(() => runtime.Prepare(Context(1), State(0), Input(0, true), feedback, sink));
        Assert.Throws<ArgumentException>(() => new AlsOverlayPoseRuntime(AlsOverlayPoseCompilerTests.Definition.Value, [], Enumerable.Repeat(AlsLocalPose.Identity, 79).ToArray(), 7, 2));
        runtime.Prepare(Context(1), State(0), Input(0), feedback, sink);
        Assert.Throws<InvalidOperationException>(() => runtime.Commit());
        runtime.Evaluate(new AlsLocalPose[79], new AlsInertialCurve[Curves.Length]); runtime.Commit();
        Assert.Throws<ArgumentException>(() => runtime.Prepare(Context(1), State(0), Input(0), feedback, sink));
    }

    [Fact]
    public void FourChildInterruptionsRetainWeightsAndInstantSwitchUpdatesThePreviousChild()
    {
        float[] times = [.5f, .2f, .3f, 0];
        var first = AlsOverlayPoseWeights.BlendList(default, 0, times, AlsTransitionBlend.HermiteCubic, false, false, 0);
        var next = AlsOverlayPoseWeights.BlendList(first.State, 1, times, AlsTransitionBlend.HermiteCubic, false, false, .1f);
        Assert.Equal(new Vector4(.5f, .5f, 0, 0), next.State.Weights);
        var interrupted = AlsOverlayPoseWeights.BlendList(next.State, 2, times, AlsTransitionBlend.HermiteCubic, false, false, .1f);
        Assert.True(interrupted.State.Weights.X > 0 && interrupted.State.Weights.Y > 0 && interrupted.State.Weights.Z > 0);
        var instant = AlsOverlayPoseWeights.BlendList(interrupted.State, 3, times, AlsTransitionBlend.HermiteCubic, false, true, .1f);
        Assert.Equal(2, instant.ZeroWeightPreviousChild); Assert.Equal(3, instant.InitializeChild); Assert.Equal(new Vector4(0, 0, 0, 1), instant.State.Weights);
        var inertial = AlsOverlayPoseWeights.BlendList(instant.State, -1, times, AlsTransitionBlend.Linear, true, true, 0);
        Assert.Equal(.5f, inertial.InertialSeconds); Assert.Equal(3, inertial.ZeroWeightPreviousChild); Assert.Equal(0, inertial.InitializeChild);
    }

    [Fact]
    public void AlphaKeepsIndependentUnclampedHistoryAndAsymmetricSpeeds()
    {
        var initialized = false; var history = 0f;
        var policy = new AlsOverlayAlphaPolicy(1, 0, false, 0, 1, true, 0, 5);
        Assert.Equal(1, AlsOverlayPoseWeights.Alpha(2, policy, .1f, ref initialized, ref history)); Assert.Equal(2, history);
        Assert.Equal(1, AlsOverlayPoseWeights.Alpha(0, policy, .1f, ref initialized, ref history)); Assert.Equal(1, history);
        Assert.Equal(.5f, AlsOverlayPoseWeights.Alpha(0, policy, .1f, ref initialized, ref history));
        Assert.Equal(1, AlsOverlayPoseWeights.Alpha(3, policy, 0, ref initialized, ref history)); Assert.Equal(3, history);
        initialized = false;
        policy = new(1, 0, true, 0, 1, true, 20, .5f, true, 0, .25f, 0, 1);
        Assert.Equal(.5f, AlsOverlayPoseWeights.Alpha(.125f, policy, 0, ref initialized, ref history));
        Assert.Equal(new Vector4(0, 1, 0, 0), AlsOverlayPoseWeights.MultiWay(new(-.1f, .6f, 0, 0), 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExclusiveOwnersMatchAndTenThousandPreparedEvaluatedCommitsAllocateNothing(bool precise)
    {
        var runtimes = Enumerable.Range(0, 4).Select(_ => New()).ToArray();
        var sinks = Enumerable.Range(0, 4).Select(_ => precise ? (IAlsOverlayPoseSink)new PreciseSink() : new Sink()).ToArray();
        var poses = Enumerable.Range(0, 4).Select(_ => new AlsLocalPose[79]).ToArray();
        var outputs = Enumerable.Range(0, 4).Select(_ => new AlsInertialCurve[Curves.Length]).ToArray();
        var feedback = new AlsInertialCurve[Curves.Length]; feedback[0] = new(1); feedback[2] = new(2.5f);
        Parallel.For(0, 4, owner => Run(runtimes[owner], sinks[owner], poses[owner], outputs[owner], feedback, 1, 300));
        for (var i = 1; i < 4; i++) { Assert.Equal(poses[0], poses[i]); Assert.Equal(outputs[0], outputs[i]); }
        Run(runtimes[0], sinks[0], poses[0], outputs[0], feedback, 301, 500);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(runtimes[0], sinks[0], poses[0], outputs[0], feedback, 801, 10000);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        static void Run(AlsOverlayPoseRuntime runtime, IAlsOverlayPoseSink sink, AlsLocalPose[] pose, AlsInertialCurve[] output,
            AlsInertialCurve[] feedback, int start, int count)
        {
            for (var frame = start; frame < start + count; frame++)
            {
                var aim = frame % 9 < 4;
                runtime.Prepare(Context(frame), State(frame / 13 % 13, aim), Input(frame % 4, aim), feedback, sink);
                runtime.Evaluate(pose, output); runtime.Commit();
            }
        }
    }
}
