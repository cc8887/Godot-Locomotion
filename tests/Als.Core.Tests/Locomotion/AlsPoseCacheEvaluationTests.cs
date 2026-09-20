using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsPoseCacheEvaluationTests
{
    private static readonly AlsFrameIdentity Identity = new(10, 2, 1);
    private static readonly AlsGraphTraversalCounter Counter = new(0, 100);
    private static AlsPoseCacheDefinition Definition() => new(16, [8, 9], [new(1, 8), new(2, 8), new(3, 9)]);

    [Fact]
    public void CounterUsesSigned16BitWrapAndSkipsUnsetSentinel()
    {
        Assert.False(default(AlsGraphTraversalCounter).MatchesAll(default));
        Assert.Equal(new(0, 100), default(AlsGraphTraversalCounter).Next(100));
        Assert.Equal(short.MinValue, new AlsGraphTraversalCounter(short.MaxValue, 100).Next(101).Counter);
        Assert.Equal(0, new AlsGraphTraversalCounter(-2, 100).Next(101).Counter);
        Assert.True(Counter.MatchesCounter(new(0, 101)));
        Assert.False(Counter.MatchesAll(new(0, 101)));
        Assert.False(Counter.MatchesAll(new(1, 100)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsGraphTraversalCounter(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlsGraphTraversalCounter(0, ulong.MaxValue));
    }

    [Fact]
    public void SharedReadsCopyBonesAndCurvePresenceWithoutRepeatingSource()
    {
        var f = new Fixture();
        var scope = f.Begin();
        f.Read(1, scope);
        var pose = f.Bones[0]; var curve = f.Curves[0];
        f.Bones[0] = AlsLocalPose.Identity; f.Curves[0] = new(99, false);
        f.Read(2, scope);
        Assert.Equal(pose, f.Bones[0]); Assert.Equal(curve, f.Curves[0]);
        Assert.Equal(1, f.Sink.Evaluations);
        Assert.Equal(1, f.Candidate.SourceEvaluations);
        f.Read(3, scope);
        Assert.Equal(2, f.Sink.Evaluations);
        Assert.Equal(9, f.Bones[0].Position.X);
        f.Candidate.PopScope(scope);
    }

    [Fact]
    public void NestedScopeDoesNotReadOuterEntryButOuterEntrySurvives()
    {
        var f = new Fixture(); var outer = f.Begin();
        f.Read(1, outer); var first = f.Bones[0];
        var inner = f.Candidate.PushScope();
        f.Read(2, inner); Assert.Equal(2, f.Sink.Evaluations);
        f.Candidate.PopScope(inner);
        f.Read(1, outer);
        Assert.Equal(first, f.Bones[0]); Assert.Equal(2, f.Sink.Evaluations);
        f.Candidate.PopScope(outer);
        var next = f.Candidate.PushScope();
        f.Read(1, next); Assert.Equal(3, f.Sink.Evaluations);
        f.Candidate.PopScope(next);
    }

    [Theory]
    [InlineData(0, 101)]
    [InlineData(1, 100)]
    public void EitherCounterOrGlobalFrameChangeInvalidatesPose(int count, int frame)
    {
        var f = new Fixture(); var scope = f.Begin();
        f.Read(1, scope);
        f.Candidate.Evaluate(2, new((short)count, (ulong)frame), scope, f.Sink, f.Bones, f.Curves);
        Assert.Equal(2, f.Sink.Evaluations);
        f.Candidate.PopScope(scope);
    }

    [Fact]
    public void InnerCounterChangeInvalidatesOuterReuse()
    {
        var f = new Fixture(); var outer = f.Begin(); f.Read(1, outer);
        var inner = f.Candidate.PushScope();
        f.Candidate.Evaluate(1, Counter.Next(100), inner, f.Sink, f.Bones, f.Curves);
        f.Candidate.PopScope(inner);
        f.Read(1, outer);
        Assert.Equal(3, f.Sink.Evaluations);
        f.Candidate.PopScope(outer);
    }

    [Fact]
    public void InitializationUsesCounterOnlyAndDoesNotItselfInvalidatePose()
    {
        var f = new Fixture(); var scope = f.Begin();
        Assert.True(f.Candidate.Initialize(1, Counter, f.Sink));
        Assert.False(f.Candidate.Initialize(2, new(0, 200), f.Sink));
        f.Read(1, scope);
        Assert.True(f.Candidate.Initialize(2, Counter.Next(100), f.Sink));
        f.Read(1, scope);
        Assert.Equal(2, f.Sink.Initializations); Assert.Equal(1, f.Sink.Evaluations);
        f.Candidate.PopScope(scope);
    }

    [Fact]
    public void BoneCacheUsesBothCountersAndInvalidatesEvaluation()
    {
        var f = new Fixture(); var scope = f.Begin(); f.Read(1, scope);
        Assert.True(f.Candidate.CacheBones(1, Counter, f.Sink));
        f.Read(2, scope);
        Assert.False(f.Candidate.CacheBones(2, Counter, f.Sink));
        f.Read(1, scope);
        Assert.Equal(2, f.Sink.Evaluations);
        Assert.True(f.Candidate.CacheBones(1, new(0, 101), f.Sink));
        f.Read(1, scope);
        Assert.Equal(3, f.Sink.Evaluations); Assert.Equal(2, f.Sink.BoneCaches);
        f.Candidate.PopScope(scope);
    }

    [Fact]
    public void CandidateCopiesLifecycleButNeverScopedPoseMemory()
    {
        var f = new Fixture(); var scope = f.Begin();
        f.Candidate.Initialize(1, Counter, f.Sink);
        f.Candidate.CacheBones(1, Counter, f.Sink);
        f.Read(1, scope); f.Candidate.PopScope(scope);
        (f.Committed, f.Candidate) = (f.Candidate, f.Committed);
        f.Candidate.BeginCandidate(new(20, 2, 1), f.Committed);
        Assert.False(f.Candidate.Initialize(2, new(0, 500), f.Sink));
        Assert.False(f.Candidate.CacheBones(2, Counter, f.Sink));
        scope = f.Candidate.PushScope(); f.Read(2, scope);
        Assert.Equal(2, f.Sink.Evaluations);
        f.Candidate.PopScope(scope);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CallbackFailurePoisonsCandidateButRetryStartsFromCommitted(int operation)
    {
        var f = new Fixture(); var scope = f.Begin();
        f.Sink.FailOperation = operation;
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (operation == 0) f.Candidate.Initialize(1, Counter, f.Sink);
            else if (operation == 1) f.Candidate.CacheBones(1, Counter, f.Sink);
            else f.Read(1, scope);
        });
        Assert.True(f.Candidate.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => f.Read(2, scope));
        f.Candidate.PopScope(scope);
        Assert.Throws<InvalidOperationException>(() => f.Committed.BeginCandidate(new(11, 2, 1), f.Candidate));
        f.Sink.FailOperation = -1;
        scope = f.Begin();
        Assert.True(f.Candidate.Initialize(1, Counter, f.Sink));
        Assert.True(f.Candidate.CacheBones(1, Counter, f.Sink));
        f.Read(2, scope);
        Assert.Equal(8, f.Bones[0].Position.X);
        f.Candidate.PopScope(scope);
    }

    [Fact]
    public void ScopeTokensEnforceOwnerLifetimeAndLifoOrder()
    {
        var f = new Fixture(); var outer = f.Begin();
        var other = new Fixture(); var foreign = other.Begin();
        Assert.Throws<InvalidOperationException>(() => f.Read(1, foreign));
        var inner = f.Candidate.PushScope();
        Assert.Throws<InvalidOperationException>(() => f.Read(1, outer));
        Assert.Throws<InvalidOperationException>(() => f.Candidate.PopScope(outer));
        Assert.Throws<InvalidOperationException>(() => f.Candidate.PushScope());
        Assert.Throws<InvalidOperationException>(() => f.Candidate.BeginCandidate(Identity, f.Committed));
        f.Candidate.PopScope(inner); f.Candidate.PopScope(outer);
        var fresh = f.Candidate.PushScope();
        Assert.Throws<InvalidOperationException>(() => f.Read(1, outer));
        f.Candidate.PopScope(fresh); other.Candidate.PopScope(foreign);
        var retry = f.Begin();
        Assert.Throws<InvalidOperationException>(() => f.Read(1, fresh));
        f.Candidate.PopScope(retry);
    }

    [Fact]
    public void RejectsDifferentOwnerLayoutInvalidCounterAndUnboundRead()
    {
        var f = new Fixture(); var scope = f.Begin();
        Assert.Throws<ArgumentException>(() => f.Read(0, scope));
        Assert.Throws<ArgumentException>(() => f.Candidate.Evaluate(1, default, scope, f.Sink, f.Bones, f.Curves));
        Assert.Throws<ArgumentException>(() => f.Candidate.Evaluate(1, Counter, scope, f.Sink, [], f.Curves));
        f.Candidate.PopScope(scope);
        Assert.Throws<InvalidOperationException>(() => f.Candidate.BeginCandidate(Identity, f.Candidate));
        Assert.Throws<InvalidOperationException>(() => f.Committed.BeginCandidate(Identity, f.Candidate));
        Assert.Throws<InvalidOperationException>(() => f.Committed.BeginCandidate(new(11, 3, 1), f.Candidate));
        Assert.Throws<InvalidOperationException>(() => f.Committed.BeginCandidate(new(11, 2, 2), f.Candidate));
        Assert.Throws<InvalidOperationException>(() => new AlsPoseCacheEvaluation(Definition(), 2, 1).BeginCandidate(Identity, f.Committed));
        Assert.Throws<InvalidOperationException>(() => f.Candidate.BeginCandidate(default, f.Committed));
    }

    [Fact]
    public void NestedSourceReadUsesDistinctBuffersAndDetectsCycles()
    {
        var f = new Fixture(); var scope = f.Begin();
        f.Sink.NestedRead = () => f.Read(3, scope);
        f.Read(1, scope);
        Assert.Equal(2, f.Sink.Evaluations);
        Assert.Equal(8, f.Bones[0].Position.X);
        f.Candidate.PopScope(scope);
        scope = f.Begin();
        f.Sink.NestedRead = () => f.Read(2, scope);
        Assert.Throws<InvalidOperationException>(() => f.Read(1, scope));
        Assert.True(f.Candidate.IsFaulted);
        f.Candidate.PopScope(scope);
    }

    [Fact]
    public void CandidateCopyLifecycleAndRepeatedReadsAllocateNothing()
    {
        var f = new Fixture();
        for (var i = 0; i < 100; i++) Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Run()
        {
            var scope = f.Begin();
            f.Candidate.Initialize(1, Counter, f.Sink); f.Candidate.CacheBones(1, Counter, f.Sink);
            f.Read(1, scope); f.Read(2, scope);
            var inner = f.Candidate.PushScope(); f.Read(2, inner); f.Candidate.PopScope(inner);
            f.Read(1, scope); f.Candidate.PopScope(scope);
        }
    }

    private sealed class Fixture
    {
        public AlsPoseCacheEvaluation Committed, Candidate;
        public readonly Sink Sink = new();
        public readonly AlsLocalPose[] Bones = new AlsLocalPose[2];
        public readonly AlsInertialCurve[] Curves = new AlsInertialCurve[1];
        public Fixture()
        {
            var definition = Definition();
            Committed = new(definition, 2, 1); Candidate = new(definition, 2, 1);
        }
        public AlsPoseCacheScope Begin() { Candidate.BeginCandidate(Identity, Committed); return Candidate.PushScope(); }
        public void Read(int read, AlsPoseCacheScope scope) => Candidate.Evaluate(read, Counter, scope, Sink, Bones, Curves);
    }

    private sealed class Sink : IAlsPoseCachePoseSink
    {
        public int Initializations, BoneCaches, Evaluations;
        public int FailOperation = -1;
        public Action? NestedRead;
        public void InitializeSource(int node) { Initializations++; Fail(0); }
        public void CacheSourceBones(int node) { BoneCaches++; Fail(1); }
        public void EvaluateSource(int node, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
        {
            Evaluations++;
            bones[0] = new(new Vector3(node, Evaluations, 0), Quaternion.Identity, Vector3.One);
            curves[0] = new(Evaluations, node == 8);
            if (node == 8) NestedRead?.Invoke();
            Fail(2);
        }
        private void Fail(int operation) { if (FailOperation == operation) throw new InvalidOperationException("Injected candidate failure."); }
    }
}
