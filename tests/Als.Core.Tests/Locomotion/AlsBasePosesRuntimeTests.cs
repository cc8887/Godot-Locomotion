using System.Numerics;
using System.Runtime.ExceptionServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsBasePosesRuntimeTests
{
    [Fact]
    public void ColdInitializationVisitsBothEvaluatorIdentitiesButReadsNoNewWeights()
    {
        var f = new Fixture();
        f.Runtime.BeginCandidate(Id(1), f.Sink);
        f.Runtime.Initialize(Counter(0)); f.Runtime.CacheBones(Counter(0));
        Assert.Equal(new[] { 40, 12 }, f.Sink.Initialized[..f.Sink.Initializations]);
        Assert.Equal(new[] { 40, 12 }, f.Sink.Cached[..f.Sink.Caches]);
        Assert.Equal(Vector2.Zero, f.Runtime.State.DesiredAlphas);
        Assert.Equal(Vector2.Zero, f.Runtime.State.CachedAlphas);
        f.Runtime.Evaluate(f.Output, f.Curves);
        Assert.Equal(f.Reference, f.Output); Assert.All(f.Curves, c => Assert.False(c.Present));
        Assert.Equal(0, f.Sink.Ticks); Assert.Equal(0, f.Sink.Evaluations);
        Assert.Equal(40, f.Runtime.State.Normal.NodeIndex); Assert.Equal(12, f.Runtime.State.Crouching.NodeIndex);
        Assert.Equal(101, f.Runtime.State.Normal.AnimationId); Assert.Equal(202, f.Runtime.State.Crouching.AnimationId);
        f.Runtime.Commit();
    }

    [Fact]
    public void EntirelyUnvisitedInputCanCommitBeforeItsActualFirstInitialization()
    {
        var f = new Fixture();
        f.Runtime.BeginCandidate(Id(1), f.Sink); f.Runtime.Commit();
        Assert.False(f.Runtime.State.Initialized); Assert.Equal(Id(1), f.Runtime.Identity);
        f.Begin(2, initialize: true); f.Update(2, 1, 0); f.Runtime.Evaluate(f.Output, f.Curves); f.Runtime.Commit();
        AssertPose(f.Normal, f.Output);
        Assert.Equal(1, f.Runtime.State.Normal.UpdateCount); Assert.Equal(0, f.Runtime.State.Crouching.UpdateCount);
        Assert.True(f.Runtime.State.Crouching.Reinitialized);
    }

    [Fact]
    public void ReinitializationUsesRetainedDesiredUntilTheNextActualUpdate()
    {
        var f = new Fixture(); f.Begin(1, initialize: true); f.Update(1, 3, 1);
        f.Runtime.Evaluate(f.Output, f.Curves); f.Runtime.Commit();
        var retained = (AlsLocalPose[])f.Output.Clone();
        f.Runtime.BeginCandidate(Id(2), f.Sink); f.Runtime.Initialize(Counter(1));
        Assert.Equal(new Vector2(3, 1), f.Runtime.State.DesiredAlphas);
        Assert.Equal(new Vector2(.75f, .25f), f.Runtime.State.CachedAlphas);
        Assert.Equal(2, f.Runtime.State.Normal.InitializationEpoch); Assert.Equal(2, f.Runtime.State.Crouching.InitializationEpoch);
        f.Runtime.Evaluate(f.Output, f.Curves); Assert.Equal(retained, f.Output);
        f.Sink.ResetFrame(); f.Update(2, 0, 1); f.Runtime.Evaluate(f.Output, f.Curves);
        AssertPose(f.Crouching, f.Output); Assert.Equal(1, f.Sink.Ticks);
        Assert.Equal(12, f.Sink.TickRecords[0].NodeIndex); Assert.True(f.Sink.TickRecords[0].Reinitialized);
        Assert.True(f.Runtime.State.Normal.Reinitialized); Assert.False(f.Runtime.State.Crouching.Reinitialized);
        f.Runtime.Commit();
        f.Begin(3); f.Update(3, 1, 0);
        Assert.True(f.Sink.TickRecords[0].Reinitialized); Assert.Equal(40, f.Sink.TickRecords[0].NodeIndex);
        f.Runtime.Cancel();
    }

    [Fact]
    public void ZeroGlobalWeightStillSubmitsIndependentFixedTimeTicksWithTheOriginalContext()
    {
        var f = new Fixture(); f.Begin(1, initialize: true);
        var context = new AlsPoseUpdateContext(Id(1), 0, .2f, .4f).WithState(7, 3).AsInactive();
        f.Runtime.Update(context, Input(1, 3, 1));
        Assert.Equal(2, f.Sink.Ticks);
        for (var i = 0; i < 2; i++)
        {
            var tick = f.Sink.TickRecords[i]; var child = f.Sink.Contexts[i];
            Assert.Equal(Id(1), tick.Identity); Assert.Equal(i == 0 ? 40 : 12, tick.NodeIndex);
            Assert.Equal(i == 0 ? 101 : 202, tick.AnimationId); Assert.Equal(1, tick.InitializationEpoch);
            Assert.True(tick.IsEvaluator); Assert.True(tick.Teleport); Assert.True(tick.Reinitialized);
            Assert.Equal(0, tick.PreviousTime); Assert.Equal(0, tick.CurrentTime);
            Assert.Equal(0, tick.PlayRate); Assert.Equal(0, tick.DeltaTime);
            Assert.Equal(0, child.Weight); Assert.Equal(.2f, child.Delta); Assert.Equal(.4f, child.RootMotionWeight);
            Assert.False(child.IsActive); Assert.Equal(new AlsActiveAnimationState(7, 3), child.GetState(0));
        }
        Assert.False(f.Runtime.State.Normal.HasBeenFullWeight); Assert.False(f.Runtime.State.Crouching.HasBeenFullWeight);
        f.Runtime.Cancel();
    }

    [Theory]
    [InlineData(2f, 6f, .25f, .75f)]
    [InlineData(-1f, 2f, 0f, 1f)]
    [InlineData(1f, -2f, 0f, 0f)]
    [InlineData(-1f, -1f, 0f, 0f)]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(.00001f, 0f, 0f, 0f)]
    [InlineData(.000011f, 0f, 1f, 0f)]
    [InlineData(float.MaxValue, float.MaxValue, 0f, 0f)]
    public void CachedAlphasFollowFloatTotalThresholdNormalizationAndScaleBias(float n, float clf, float expectedN, float expectedClf)
    {
        var f = new Fixture(); f.Begin(1, initialize: true); f.Update(1, n, clf);
        Assert.Equal(new Vector2(n, clf), f.Runtime.State.DesiredAlphas);
        Assert.Equal(new Vector2(expectedN, expectedClf), f.Runtime.State.CachedAlphas);
        Assert.Equal((expectedN > AlsPoseBlender.WeightThreshold ? 1 : 0) +
            (expectedClf > AlsPoseBlender.WeightThreshold ? 1 : 0), f.Sink.Ticks);
        f.Runtime.Evaluate(f.Output, f.Curves);
        if (expectedN == 0 && expectedClf == 0)
        { Assert.Equal(f.Reference, f.Output); Assert.All(f.Curves, c => Assert.False(c.Present)); }
        f.Runtime.Cancel();
    }

    [Fact]
    public void DroppingAnIrrelevantChildDoesNotRenormalizeTheRemainingPoseOrCurves()
    {
        var f = new Fixture(); f.Begin(1, initialize: true);
        var tiny = AlsPoseBlender.WeightThreshold * .5f;
        f.Update(1, tiny, 1); f.Runtime.Evaluate(f.Output, f.Curves);
        var weight = 1 / (tiny + 1);
        Assert.Equal(1, f.Sink.Ticks); Assert.Equal(12, f.Sink.TickRecords[0].NodeIndex);
        Assert.Equal(1, f.Sink.Evaluations); Assert.Equal(12, f.Sink.Evaluated[0]);
        Assert.True(f.Runtime.State.CachedAlphas.X > 0);
        Assert.Equal(f.Crouching[0].Position * weight, f.Output[0].Position);
        Assert.Equal(f.Crouching[0].Scale * weight, f.Output[0].Scale);
        Assert.NotEqual(f.Crouching[0].Scale, f.Output[0].Scale);
        Assert.False(f.Curves[1].Present); Assert.Equal(new AlsInertialCurve(6 * weight), f.Curves[2]);
        f.Runtime.Cancel();
    }

    [Fact]
    public void MultiWayUsesOrderedWeightedQuaternionAccumulationAndCurvePresence()
    {
        var f = new Fixture(); f.Begin(1, initialize: true); f.Update(1, 1, 3);
        f.Runtime.Evaluate(f.Output, f.Curves);
        Assert.Equal(new[] { 40, 12 }, f.Sink.Evaluated[..f.Sink.Evaluations]);
        var normal = f.Normal[0]; var crouching = f.Crouching[0];
        var sign = Quaternion.Dot(normal.Rotation, crouching.Rotation) < 0 ? -1f : 1f;
        var expected = Quaternion.Normalize(normal.Rotation * .25f + crouching.Rotation * (sign * .75f));
        Assert.InRange((expected - f.Output[0].Rotation).Length(), 0, .000001f);
        Assert.Equal(normal.Position * .25f + crouching.Position * .75f, f.Output[0].Position);
        Assert.Equal(normal.Scale * .25f + crouching.Scale * .75f, f.Output[0].Scale);
        Assert.Equal(new AlsInertialCurve(8), f.Curves[0]);
        Assert.Equal(new AlsInertialCurve(.75f), f.Curves[1]);
        Assert.Equal(new AlsInertialCurve(4.5f), f.Curves[2]);
        Assert.False(f.Curves[3].Present); Assert.Equal(new AlsInertialCurve(0), f.Curves[4]);
        f.Runtime.Cancel();
    }

    [Fact]
    public void IndependentEvaluationsResampleWithoutUpdatingOrAdvancingEitherNode()
    {
        var f = new Fixture(); f.Begin(1, initialize: true); f.Update(1, 1, 1);
        f.Runtime.Evaluate(f.Output, f.Curves);
        var first = (AlsLocalPose[])f.Output.Clone(); var firstCurves = (AlsInertialCurve[])f.Curves.Clone();
        var before = f.Runtime.State;
        f.Runtime.Evaluate(f.Output, f.Curves);
        Assert.Equal(first, f.Output); Assert.Equal(firstCurves, f.Curves);
        Assert.Equal(2, f.Sink.Ticks); Assert.Equal(4, f.Sink.Evaluations);
        Assert.Equal(before.Normal.UpdateCount, f.Runtime.State.Normal.UpdateCount);
        Assert.Equal(before.Crouching.UpdateCount, f.Runtime.State.Crouching.UpdateCount);
        Assert.Equal(0, f.Runtime.State.Normal.Time); Assert.Equal(0, f.Runtime.State.Crouching.Time);
        Assert.Equal(2, f.Runtime.State.Normal.EvaluationCount); Assert.Equal(2, f.Runtime.State.Crouching.EvaluationCount);
        f.Runtime.Commit();
    }

    [Fact]
    public void LifecycleVisitsAreNotSuppressedByInventedInnerCounters()
    {
        var f = new Fixture(); f.Runtime.BeginCandidate(Id(1), f.Sink);
        f.Runtime.Initialize(Counter(0)); f.Runtime.Initialize(Counter(0));
        f.Runtime.CacheBones(Counter(0)); f.Runtime.CacheBones(Counter(0));
        Assert.Equal(4, f.Sink.Initializations); Assert.Equal(4, f.Sink.Caches);
        Assert.Equal(2, f.Runtime.State.Normal.InitializationEpoch); Assert.Equal(2, f.Runtime.State.Crouching.InitializationEpoch);
        f.Runtime.Commit();
    }

    [Theory]
    [InlineData("initialize")] [InlineData("bones")] [InlineData("update")] [InlineData("evaluate")]
    public void PartialSecondSourceFailureKeepsTheCommittedHistoryAndAllowsSameFrameRetry(string stage)
    {
        var f = new Fixture(); f.Run(1, initialize: true); var committed = f.Runtime.State;
        f.Sink.Failure = stage;
        Assert.Throws<InvalidOperationException>(() => f.Run(2, initialize: true));
        Assert.False(f.Runtime.HasCandidate); Assert.Equal(committed, f.Runtime.State);
        f.Sink.Failure = ""; f.Run(2, initialize: true);
        var expected = new Fixture(); expected.Run(1, initialize: true); expected.Run(2, initialize: true);
        Assert.Equal(expected.Runtime.State, f.Runtime.State); Assert.Equal(expected.Output, f.Output); Assert.Equal(expected.Curves, f.Curves);
    }

    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.MaxValue)]
    public void InvalidConsumedPropertyCancelsBeforeSubmittingAnyTick(double value)
    {
        var f = new Fixture(); f.Begin(1, initialize: true);
        Assert.Throws<ArgumentException>(() => f.Runtime.Update(new(Id(1), 1, .1f), Input(1, value, 1)));
        Assert.False(f.Runtime.HasCandidate); Assert.Equal(0, f.Sink.Ticks);
        f.Run(1, initialize: true);
    }

    [Fact]
    public void CurrentInputIdentityIsMandatoryAndInvalidPayloadRetryDoesNotReuseScratch()
    {
        var f = new Fixture(); f.Begin(1, initialize: true);
        Assert.Throws<ArgumentException>(() => f.Runtime.Update(new(Id(1), 1, .1f), Input(2, 1, 1)));
        Assert.False(f.Runtime.HasCandidate);
        f.Begin(1, initialize: true); f.Update(1, 1, 1); f.Sink.PoisonCurves = true;
        Assert.Throws<ArgumentException>(() => f.Runtime.Evaluate(f.Output, f.Curves)); Assert.False(f.Runtime.HasCandidate);
        f.Sink.PoisonCurves = false; f.Run(1, initialize: true);
        Assert.All(f.Curves, c => Assert.True(float.IsFinite(c.Value)));
    }

    [Fact]
    public void CancelRestoresDesiredAndEvaluatorHistoriesAndCannotCommitTheDiscardedFrame()
    {
        var f = new Fixture(); f.Run(1, initialize: true); var committed = f.Runtime.State;
        f.Begin(2); f.Update(2, 0, 1); f.Runtime.Evaluate(f.Output, f.Curves); f.Runtime.Cancel();
        Assert.Equal(committed, f.Runtime.State); Assert.Throws<InvalidOperationException>(() => f.Runtime.Commit());
        f.Begin(2); f.Update(2, 1, 0); f.Runtime.Evaluate(f.Output, f.Curves); f.Runtime.Commit();
        Assert.Equal(Id(2), f.Runtime.Identity); Assert.Equal(new Vector2(1, 0), f.Runtime.State.DesiredAlphas);
        Assert.Throws<InvalidOperationException>(() => f.Runtime.BeginCandidate(Id(2), f.Sink));
        Assert.Throws<InvalidOperationException>(() => f.Runtime.BeginCandidate(new(3, 99, 4), f.Sink));
        Assert.Throws<InvalidOperationException>(() => f.Runtime.BeginCandidate(new(3, 7, 5), f.Sink));
    }

    [Fact]
    public void RepeatedFullCandidateLifecycleHasZeroSteadyStateAllocation()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var f = new Fixture(); f.Run(1, initialize: true);
                for (var frame = 2; frame <= 64; frame++) f.Run(frame);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var frame = 65; frame <= 164; frame++) f.Run(frame);
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(0, allocated);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static AlsFrameIdentity Id(int frame) => new(frame, 7, 4);
    private static AlsGraphTraversalCounter Counter(short counter) => new(counter, 0);
    private static AlsLayeringInput Input(int frame, double n, double clf) =>
        default(AlsLayeringInput) with { Identity = Id(frame), BasePoseNormal = n, BasePoseCrouching = clf };
    private static void AssertPose(AlsLocalPose[] expected, AlsLocalPose[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var bone = 0; bone < expected.Length; bone++)
        {
            Assert.Equal(expected[bone].Position, actual[bone].Position); Assert.Equal(expected[bone].Scale, actual[bone].Scale);
            Assert.InRange(MathF.Min((expected[bone].Rotation - actual[bone].Rotation).Length(),
                (expected[bone].Rotation + actual[bone].Rotation).Length()), 0, .000001f);
        }
    }

    private sealed class Fixture
    {
        public readonly AlsLocalPose[] Reference = [new(new(99, 98, 97), Quaternion.Identity, new(2, 2, 2))];
        public readonly AlsLocalPose[] Normal = [new(new(2, 4, 6), Quaternion.CreateFromAxisAngle(Vector3.UnitY, .5f), new(1, 2, 3))];
        public readonly AlsLocalPose[] Crouching = [new(new(10, 20, 30), -Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1.3f), new(3, 4, 5))];
        public readonly string[] Names = ["Shared", "NOnly", "ClfOnly", "Absent", "PresentZero"];
        public readonly AlsLocalPose[] Output = new AlsLocalPose[1];
        public readonly AlsInertialCurve[] Curves = new AlsInertialCurve[5];
        public readonly AlsBasePosesRuntime Runtime;
        public readonly Sink Sink;
        public Fixture()
        {
            var definition = new AlsBasePosesDefinition("native BasePoses fixture", 80, 60, 25, 0,
                [Evaluator(40, 101, "N"), Evaluator(12, 202, "CLF")], ["BasePose_N", "BasePose_CLF"], [0, 0]);
            Runtime = new(definition, Reference, Names); Sink = new(this);
        }
        private static AlsBasePoseEvaluatorDefinition Evaluator(int node, int animation, string asset) =>
            new(node, animation, asset, "/fixture/" + asset, 1, 0, true, true, "ExplicitTime", "None", "CanBeLeader", "DoNotSync");
        public void Begin(int frame, bool initialize = false)
        {
            Sink.ResetFrame(); Runtime.BeginCandidate(Id(frame), Sink);
            if (initialize) { Runtime.Initialize(Counter((short)frame)); Runtime.CacheBones(Counter((short)frame)); }
        }
        public void Update(int frame, double n, double clf) => Runtime.Update(new(Id(frame), 1, 1f / 60), Input(frame, n, clf));
        public void Run(int frame, bool initialize = false)
        { Begin(frame, initialize); Update(frame, 1, 1); Runtime.Evaluate(Output, Curves); Runtime.Commit(); }
    }

    private sealed class Sink(Fixture fixture) : IAlsBasePosesSink
    {
        public readonly int[] Initialized = new int[16], Cached = new int[16], Evaluated = new int[16];
        public readonly AlsBasePoseEvaluatorTick[] TickRecords = new AlsBasePoseEvaluatorTick[16];
        public readonly AlsPoseUpdateContext[] Contexts = new AlsPoseUpdateContext[16];
        public int Initializations, Caches, Ticks, Evaluations;
        public string Failure = "";
        public bool PoisonCurves;
        public void ResetFrame() { Initializations = Caches = Ticks = Evaluations = 0; }
        private void Check(string stage, int node)
        { if (Failure == stage && node == 12) throw new InvalidOperationException("Injected second evaluator failure."); }
        public void InitializeEvaluator(in AlsBasePoseEvaluatorDefinition evaluator)
        { Check("initialize", evaluator.NodeIndex); Initialized[Initializations++] = evaluator.NodeIndex; }
        public void CacheEvaluatorBones(in AlsBasePoseEvaluatorDefinition evaluator)
        { Check("bones", evaluator.NodeIndex); Cached[Caches++] = evaluator.NodeIndex; }
        public void UpdateEvaluator(in AlsBasePoseEvaluatorDefinition evaluator, in AlsBasePoseEvaluatorTick tick, in AlsPoseUpdateContext context)
        { Check("update", evaluator.NodeIndex); TickRecords[Ticks] = tick; Contexts[Ticks++] = context; }
        public void EvaluateEvaluator(in AlsBasePoseEvaluatorDefinition evaluator, float seconds, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
        {
            Check("evaluate", evaluator.NodeIndex); Evaluated[Evaluations++] = evaluator.NodeIndex;
            if (seconds != 0) throw new InvalidOperationException("Base evaluator advanced its explicit time.");
            var normal = evaluator.NodeIndex == 40;
            (normal ? fixture.Normal : fixture.Crouching).CopyTo(pose); curves.Clear();
            curves[0] = new(normal ? 2 : 10); curves[normal ? 1 : 2] = new(normal ? 3 : 6);
            if (normal) curves[4] = new(0);
            if (PoisonCurves) curves[3] = new(float.NaN, false);
        }
    }
}
