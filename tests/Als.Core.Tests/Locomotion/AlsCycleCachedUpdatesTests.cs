using System.Numerics;
using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsCycleCachedUpdatesTests
{
    [Theory]
    [InlineData(0f, false)]
    [InlineData(.000001f, false)]
    [InlineData(.00001f, false)]
    [InlineData(.000011f, true)]
    public void ZeroMultiWayAlphaDoesNotInventAForwardUpdate(float total, bool relevant)
    {
        var updates = AlsCycleCacheWeights.Resolve(AlsTransitionStack.Initialize(0), new(total,0,0,0), 1);
        Assert.Equal(relevant, updates[0].Present);
        Assert.Equal(relevant ? 1 : 0, updates[0].Weight);
        for (var i = 1; i < 6; i++) Assert.False(updates[i].Present);
    }

    [Fact]
    public void SharedSourceUsesMaximumNotSummedPoseWeight()
    {
        var stack = Transition((int)AlsCycleDirection.Forward, (int)AlsCycleDirection.RightForward, .25f);
        var updates = AlsCycleCacheWeights.Resolve(stack, new(.6f,0,.2f,.2f), .8f);
        Assert.Equal(.8f * .75f * .6f, updates[0].Weight);
        Assert.NotEqual(.8f * .6f, updates[0].Weight);
        Assert.Equal(0, updates[0].State); Assert.False(updates[0].Active);
        Assert.Equal(.8f * .75f * .2f, updates[2].Weight);
        Assert.Equal(.8f * .25f * .2f, updates[3].Weight);
        Assert.True(updates[3].Active);
        Assert.False(updates[1].Present);
    }

    [Fact]
    public void EqualWeightRetainsFirstTraversalStateNotLowestStateNumber()
    {
        var stack = Transition(5, 0, .5f);
        var updates = AlsCycleCacheWeights.Resolve(stack, Vector4.UnitX, 1);
        Assert.Equal(new(true,.5f,5,false), updates[0]);
        stack = AlsTransitionStack.Advance(stack, .01f);
        updates = AlsCycleCacheWeights.Resolve(stack, Vector4.UnitX, 1);
        Assert.Equal(0, updates[0].State); Assert.True(updates[0].Active);
    }

    [Fact]
    public void RelevantLocalPinIsUpdatedEvenAtZeroOrTinyGlobalWeight()
    {
        var stack = Transition((int)AlsCycleDirection.LeftBackward, (int)AlsCycleDirection.Forward, 0);
        var updates = AlsCycleCacheWeights.Resolve(stack, Vector4.UnitZ, .000001f);
        Assert.Equal(new(true,0,0,true), updates[2]);
        Assert.Equal(new(true,.000001f,(int)AlsCycleDirection.LeftBackward,false), updates[3]);
        updates = AlsCycleCacheWeights.Resolve(stack, Vector4.UnitZ, 0);
        Assert.Equal(new(true, 0, 0, true), updates[2]);
        Assert.Equal(new(true, 0, (int)AlsCycleDirection.LeftBackward, false), updates[3]);
        for (var i = 0; i < 6; i++) if (i is not 2 and not 3) Assert.False(updates[i].Present);
    }

    [Fact]
    public void InterruptedReentryMatchesDeferredWholeContextSelection()
    {
        var reads = Enumerable.Range(0, 36).Select(i => new AlsPoseCacheReadBinding(i, 36 + i % 6)).ToArray();
        var traversal = new AlsPoseCacheTraversal(new(42, [36,37,38,39,40,41], reads), 36);
        var sink = new Sink();
        var stack = AlsTransitionStack.Initialize(5);
        int[] targets = [0, 2, 4, 5, 3, 1, 0];
        var velocities = new[] { Vector4.UnitX, Vector4.UnitY, Vector4.UnitZ, Vector4.UnitW, new Vector4(.3f,.1f,.4f,.2f) };
        var checks = 0;
        foreach (var target in targets)
        {
            stack = AlsTransitionStack.Start(stack, target, .7f, AlsTransitionBlend.Cubic);
            for (var frame = 0; frame < 4; frame++)
            {
                stack = AlsTransitionStack.Advance(stack, .025f);
                foreach (var velocity in velocities)
                {
                    var id = new AlsFrameIdentity(++checks,0,1);
                    traversal.Begin(id); sink.Values = default;
                    var visited = new HashSet<int>();
                    for (var i = 0; i < stack.Count; i++)
                    {
                        Register(stack.GetTransition(i).From); Register(stack.GetTransition(i).To);
                    }
                    if (stack.Count == 0) Register(stack.CurrentState);
                    traversal.Drain(sink);
                    var result = AlsCycleCacheWeights.Resolve(stack, velocity, .75f);
                    for (var i = 0; i < 6; i++)
                    {
                        Assert.Equal(result[i].Present, sink.Values[i].Present);
                        if (result[i].Present)
                        {
                            Assert.Equal(sink.Values[i].Weight, result[i].Weight);
                            Assert.Equal(sink.Values[i].State, result[i].State);
                            Assert.Equal(result[i].State == stack.CurrentState, result[i].Active);
                        }
                    }
                    void Register(int state)
                    {
                        if (!visited.Add(state)) return;
                        var v = AlsStandingCyclePose.NormalizeVelocityWeights(velocity);
                        for (var direction = 0; direction < 6; direction++)
                        {
                            var local = AlsStandingCycle.DirectionWeight((AlsCycleDirection)state, direction, v);
                            if (local > AlsPoseBlender.WeightThreshold) traversal.Use(state * 6 + direction,
                                new AlsPoseUpdateContext(id, .75f * AlsTransitionStack.Weight(stack, state) * local, .025f).WithState(100, state));
                        }
                    }
                }
            }
        }
        Assert.Equal(140, checks);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-.1f)]
    [InlineData(1.1f)]
    public void RejectsInvalidGraphWeight(float value) => Assert.Throws<ArgumentException>(() =>
        AlsCycleCacheWeights.Resolve(AlsTransitionStack.Initialize(0), Vector4.UnitX, value));

    [Fact]
    public void CleanupCannotEraseAnUnfinishedOlderStatesSourceVisit()
    {
        var stack = AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 1, 2, AlsTransitionBlend.Linear);
        stack = AlsTransitionStack.Advance(stack, .1f);
        stack = AlsTransitionStack.Start(stack, 2, .1f, AlsTransitionBlend.Linear);
        var evaluated = AlsTransitionStack.Advance(stack, .2f, out var before);
        Assert.Equal(0, evaluated.Count);
        Assert.False(before.GetTransition(0).Complete); Assert.True(before.GetTransition(1).Complete);
        var updates = AlsCycleCacheWeights.Resolve(before, evaluated, Vector4.UnitZ, .8f);
        // B's left input maps to LB, which remains visited at zero global
        // weight even though the evaluated machine now contains only LF.
        Assert.Equal(new(true,0,1,false), updates[3]);
        Assert.Equal(new(true,.8f,2,true), updates[2]);
    }

    [Fact]
    public void ReenteredCurrentStateKeepsItsFirstUpdateWeightAcrossCleanup()
    {
        var stack = AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 1, 2, AlsTransitionBlend.Linear);
        stack = AlsTransitionStack.Advance(stack, .1f);
        stack = AlsTransitionStack.Start(stack, 0, 1, AlsTransitionBlend.Custom);
        // A custom curve may reach its endpoint value before alpha reaches 1.
        // Completion and pose alpha are separate native state-machine values.
        var evaluated = AlsTransitionStack.Advance(stack, .1f, out var before, _ => .75f);
        Assert.Equal(0, evaluated.Count); Assert.True(before.GetTransition(1).Complete);
        var updates = AlsCycleCacheWeights.Resolve(before, evaluated, Vector4.UnitZ, 1);
        Assert.Equal(new(true,.975f,0,true), updates[2]);
        Assert.Equal(new(true,.025f,1,false), updates[3]);
        Assert.Equal(1, AlsCycleCacheWeights.Resolve(evaluated, Vector4.UnitZ, 1)[2].Weight);
    }

    [Fact]
    public void ResolutionIsUnmanagedAllocationFreeAndRetryable()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsCycleCachedUpdates>());
        var stack = Transition(5, 0, .5f);
        for (var i = 0; i < 1000; i++) Run(stack);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Run(stack);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void Run(in AlsTransitionStackState stack)
    {
        var result = AlsCycleCacheWeights.Resolve(stack, Vector4.UnitX, 1);
        var retry = AlsCycleCacheWeights.Resolve(stack, Vector4.UnitX, 1);
        if (result[0] != new AlsCachedDirectionUpdate(true,.5f,5,false) || result[0] != retry[0]) throw new InvalidOperationException();
    }
    private static AlsTransitionStackState Transition(int from, int to, float elapsed) =>
        AlsTransitionStack.Advance(AlsTransitionStack.Start(AlsTransitionStack.Initialize(from), to, 1, AlsTransitionBlend.Linear), elapsed);

    private sealed class Sink : IAlsPoseCacheUpdateSink
    {
        public AlsCycleCachedUpdates Values;
        public void UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context) =>
            Values[cacheNodeIndex - 36] = new(true, context.Weight, context.GetState(0).StateIndex, false);
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
    }
}
