using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMainGroundedBoneCacheTests
{
    private static readonly Lazy<AlsMainGroundedCachedGraphDefinition> Compiled = new(() =>
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var skeleton = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set).SkeletonId;
        var sources = AlsLocomotionSourceCompiler.Compile(Read("v4_locomotion_source_graph.json"), set, skeleton);
        return AlsMainGroundedCachedGraphCompiler.Compile(Read("v4_locomotion_source_graph.json"), Read("v4_pose_cache_graph.json"),
            Read("v4_locomotion_detail_graph.json"), sources, set).Runtime;
    });

    [Fact]
    public void ExplicitStandingAndDetailInitializationCacheEntryWithoutAnUpdate()
    {
        var f = new Fixture(); f.Begin(1, new(0, 0));
        var standing = AlsGroundedStateMachine.Initialize(Compiled.Value.Standing.Standing);
        f.Bones.ObserveInitialization(AlsMainBoneMachine.Standing, standing);
        Assert.Equal(1, f.Bones.StateRefreshes);
        f.Bones.CacheState(AlsMainBoneMachine.Standing, 0);
        Assert.Equal(1, f.Bones.StateRefreshes);
        var detail = AlsLocomotionDetailMachine.Initialize();
        f.Bones.ObserveInitialization(detail);
        Assert.Equal(new[] { Compiled.Value.Standing.CycleCacheIndex }, f.Sink.Calls);
        Assert.False(detail.State.HasUpdated);
        var before = f.Bones.StateRefreshes;
        f.Bones.ObserveInitialization(detail);
        Assert.Equal(before + 1, f.Bones.StateRefreshes);
        Assert.Single(f.Sink.Calls); // Actual machine initialization does not invalidate Save's independent bone counter.
    }

    [Fact]
    public void ExplicitStopInitializationIsNotResetAgainByItsParentUpdate()
    {
        var f = new Fixture(); f.Begin(2, new(0, 0));
        var rules = new AlsGroundedRuleInput(true, false, false, AlsStance.Standing, true, false, 0, .2f);
        var moving = AlsGroundedStateMachine.Update(Compiled.Value.Standing.Standing, default, rules,
            new AlsGroundedAutomaticTime[5], 1, .02f, 1);
        var stopping = AlsGroundedStateMachine.Update(Compiled.Value.Standing.Standing, moving.State, rules with { ShouldMove = false },
            new AlsGroundedAutomaticTime[5], 1, .02f, 2);
        var stop = AlsGroundedStateMachine.Initialize(Compiled.Value.Standing.Stop);
        f.Bones.ObserveInitialization(AlsMainBoneMachine.Stop, stop);
        f.Bones.Observe(AlsMainBoneMachine.Standing, stopping);
        var before = f.Bones.StateRefreshes;
        f.Bones.CacheState(AlsMainBoneMachine.Stop, stop.State.CurrentState);
        Assert.Equal(before, f.Bones.StateRefreshes);
    }

    [Fact]
    public void InitializationObserverRejectsUpdatedOrWrongMachineSnapshots()
    {
        var f = new Fixture(); f.Begin(1, new(0, 0));
        var standing = AlsGroundedStateMachine.Initialize(Compiled.Value.Standing.Standing);
        Assert.Throws<ArgumentException>(() => f.Bones.ObserveInitialization(AlsMainBoneMachine.Stop, standing));
        var updated = AlsGroundedStateMachine.Update(Compiled.Value.Standing.Standing, standing.State,
            new(false, false, false, AlsStance.Standing, true, false, 0, 0), new AlsGroundedAutomaticTime[5], 1, .01f, 1);
        Assert.Throws<ArgumentException>(() => f.Bones.ObserveInitialization(AlsMainBoneMachine.Standing, updated));
        Assert.Throws<ArgumentException>(() => f.Bones.ObserveInitialization(default(AlsDetailMachineUpdate)));
        Assert.Equal(0, f.Bones.StateRefreshes);
    }

    [Fact]
    public void RelevanceGapClearsMachineCountersEvenWhenTheBoneCounterIsUnchanged()
    {
        var f = new Fixture(); f.Begin(1, new(0, 0));
        var rules = new AlsGroundedRuleInput(false, false, false, AlsStance.Standing, true, false, 0, 0);
        var first = AlsGroundedStateMachine.Update(Compiled.Value.Standing.Standing, default, rules,
            new AlsGroundedAutomaticTime[5], 1, .01f, 1);
        f.Bones.Observe(AlsMainBoneMachine.Standing, first);
        Assert.Equal(1, f.Bones.StateRefreshes);
        f.Commit(new(f.Bones.Identity, true, default, new(f.Bones.Identity, true, first.State, default, default, 0), default, default));
        f.Begin(3, new(0, 0));
        var gap = AlsGroundedStateMachine.Update(Compiled.Value.Standing.Standing, first.State, rules,
            new AlsGroundedAutomaticTime[5], 1, .01f, 3);
        Assert.True(gap.Reinitialized);
        f.Bones.Observe(AlsMainBoneMachine.Standing, gap);
        Assert.Equal(1, f.Bones.StateRefreshes);
    }

    [Fact]
    public void MatchingFrameIdentityDoesNotGrantOwnershipOfAnotherPoseCandidate()
    {
        var f = new Fixture(); f.Begin(1, new(0, 0));
        var other = new AlsPoseCacheEvaluation(Compiled.Value.Caches, 1, 0);
        other.BeginCandidate(new(1, 2, 3), f.CommittedPoses);
        Assert.True(f.Bones.Owns(f.Poses)); Assert.False(f.Bones.Owns(other));
    }

    [Fact]
    public void EntryAliasesCacheOnceAndFirstUpdateDoesNotResetAlreadyCachedEntry()
    {
        var f = new Fixture(); f.Begin(1, new(0, 0));
        f.Bones.CacheEntry(248); f.Bones.CacheEntry(302);
        Assert.Equal(new[] { 100 }, f.Sink.Calls.ToArray()); Assert.Equal(1, f.Bones.StateRefreshes);
        var update = AlsGroundedStateMachine.Update(Compiled.Value.Main, default,
            new(false, false, false, AlsStance.Standing, true, false, 0, 0), new AlsGroundedAutomaticTime[8], 1, .01f, 1);
        f.Bones.Observe(AlsMainBoneMachine.Main, update);
        Assert.Equal(new[] { 100, 99 }, f.Sink.Calls.ToArray());
        Assert.Equal(3, f.Bones.StateRefreshes); // Main Entry, Main Standing, Standing Idle.
    }

    [Fact]
    public void ActualHierarchySharesAllSixSaveBoneCaches()
    {
        var f = new Fixture(); f.Begin(1, new(0, 0)); f.Bones.CacheEntry(248);
        f.Bones.CacheState(AlsMainBoneMachine.Main, 1);
        f.Bones.CacheState(AlsMainBoneMachine.Standing, 1);
        f.Bones.CacheState(AlsMainBoneMachine.Main, 2);
        f.Bones.CacheState(AlsMainBoneMachine.Crouching, 1);
        Assert.Equal(new[] { 100, 99, 33, 32, 30, 31 }, f.Sink.Calls.ToArray());
        f.Bones.CacheState(AlsMainBoneMachine.Detail, 3);
        f.Bones.CacheState(AlsMainBoneMachine.Crouching, 4);
        f.Bones.CacheState(AlsMainBoneMachine.Standing, 2);
        Assert.Equal(6, f.Bones.SourceRefreshes);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(-2, 0, 1)]
    public void CounterAndGlobalFrameBothParticipateIncludingBackwardCounters(short count, ulong frame, int refreshes)
    {
        var f = new Fixture(); f.Begin(1, new(0, 0)); f.Bones.CacheEntry(248); f.Commit();
        f.Begin(2, new(count, frame)); f.Bones.CacheEntry(302);
        Assert.Equal(refreshes, f.Bones.SourceRefreshes); Assert.Equal(refreshes, f.Bones.StateRefreshes);
    }

    [Fact]
    public void SaveInitializationResetsStateCountersWithoutInventingSaveBoneInvalidation()
    {
        var f = new Fixture(); f.Begin(1, new(0, 0)); f.Bones.CacheState(AlsMainBoneMachine.Main, 1);
        var before = f.Bones.StateRefreshes;
        f.Bones.InitializeSavedSource(99);
        f.Bones.CacheState(AlsMainBoneMachine.Standing, 0);
        Assert.Equal(before + 1, f.Bones.StateRefreshes); Assert.Equal(1, f.Bones.SourceRefreshes);
    }

    [Fact]
    public void SourceFailurePoisonsCandidateAndRetryRestoresCounters()
    {
        var f = new Fixture(); f.Begin(1, new(0, 0)); f.Sink.FailNode = 99;
        Assert.Throws<InvalidOperationException>(() => f.Bones.CacheState(AlsMainBoneMachine.Main, 1));
        Assert.True(f.Bones.IsFaulted); Assert.True(f.Poses.IsFaulted);
        Assert.Throws<InvalidOperationException>(() => f.Bones.CacheEntry(248));
        f.Begin(1, new(0, 0)); f.Sink.FailNode = -1;
        f.Bones.CacheState(AlsMainBoneMachine.Main, 1);
        Assert.False(f.Bones.IsFaulted); Assert.Equal(1, f.Bones.SourceRefreshes); Assert.Equal(2, f.Bones.StateRefreshes);
    }

    [Theory]
    [InlineData(AlsMainBoneMachine.Main, 5)]
    [InlineData(AlsMainBoneMachine.Stop, 1)]
    [InlineData(AlsMainBoneMachine.Standing, -1)]
    [InlineData(AlsMainBoneMachine.Detail, 6)]
    public void RejectsConduitsAndOutOfRangeStates(AlsMainBoneMachine machine, int state)
    {
        var f = new Fixture(); f.Begin(1, new(0, 0));
        Assert.Throws<ArgumentException>(() => f.Bones.CacheState(machine, state)); Assert.Empty(f.Sink.Calls);
    }

    [Fact]
    public void UnbegunAndStaleOwnersCannotRefreshBones()
    {
        var f = new Fixture(); Assert.Throws<InvalidOperationException>(() => f.Bones.CacheEntry(248));
        f.Begin(1, new(0, 0)); f.Poses.BeginCandidate(new(2, 2, 3), f.CommittedPoses);
        Assert.Throws<InvalidOperationException>(() => f.Bones.CacheEntry(248));
        Assert.Throws<ArgumentException>(() => f.Bones.Begin(f.Poses, f.CommittedBones, default, default, f.Sink));
    }

    private sealed class Fixture
    {
        public AlsMainGroundedBoneCache Bones = new(Compiled.Value), CommittedBones = new(Compiled.Value);
        public AlsPoseCacheEvaluation Poses = new(Compiled.Value.Caches, 1, 0), CommittedPoses = new(Compiled.Value.Caches, 1, 0);
        public readonly Sink Sink = new();
        private AlsMainGroundedCachedState _previous;
        public void Begin(long frame, AlsGraphTraversalCounter counter)
        {
            Sink.Calls.Clear(); Poses.BeginCandidate(new(frame, 2, 3), CommittedPoses);
            Bones.Begin(Poses, CommittedBones, counter, _previous, Sink);
        }
        public void Commit(AlsMainGroundedCachedState? state = null)
        {
            _previous = state ?? new(Bones.Identity, true, default, default, default, default);
            (Bones, CommittedBones) = (CommittedBones, Bones); (Poses, CommittedPoses) = (CommittedPoses, Poses);
        }
    }
    private sealed class Sink : IAlsMainGroundedBoneCacheSink
    {
        public readonly List<int> Calls = [];
        public int FailNode = -1;
        public void RefreshSourceBones(int node)
        { if (node == FailNode) throw new InvalidOperationException("Injected bone refresh failure."); Calls.Add(node); }
    }
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
}
