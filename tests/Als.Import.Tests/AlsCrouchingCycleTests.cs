using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingCycleTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.Compile(Read("v4_grounded_dependencies.json"), Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));
    private static readonly Lazy<AlsCrouchingCycleProfile> Profile = new(() => Compile());
    private static AlsGroundedRuleInput Rules => new(true, false, false, AlsStance.Crouching, true, false, 1, 0) { FeetCrossing = 1 };
    private static AlsPoseUpdateContext Context(long frame, float delta = .01f, float weight = 1) =>
        new AlsPoseUpdateContext(new(frame, 0, 1), weight, delta).WithState(900, 1);

    [Fact]
    public void NativeUpdateTraversalPreservesCycleAndDirectionAcrossFrameGap()
    {
        var runtime=new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink=new Sink();
        var tick=new AlsGraphTraversalCounter(short.MaxValue,1);
        var first=runtime.Prepare(default,Rules,.4f,Vector4.One,Context(1).WithUpdateCounter(tick),sink);
        tick=tick.Next(2); sink.Reset();
        var next=runtime.Prepare(first.State,Rules,.4f,Vector4.One,Context(1001).WithUpdateCounter(tick),sink);
        Assert.Equal(first.State.InitializationEpoch,next.State.InitializationEpoch);
        Assert.Equal(tick,next.State.Machine.LastUpdateCounter); Assert.Equal(tick,next.State.Direction.LastUpdateCounter);
        Assert.Equal(0,sink.InitCount);
        for(var i=0;i<sink.Count;i++)Assert.Equal(tick,sink.Contexts[i].UpdateCounter);
        tick=tick.Next(3).Next(4); sink.Reset();
        var gap=runtime.Prepare(next.State,Rules,.4f,Vector4.One,Context(1002).WithUpdateCounter(tick),sink);
        Assert.Equal(next.State.InitializationEpoch+1,gap.State.InitializationEpoch);
        Assert.True(sink.InitCount>0); Assert.Equal(tick,gap.State.Direction.LastUpdateCounter);
    }

    [Fact]
    public void CompilesWholeCyclesOwnershipAndFixedAdditiveAlpha()
    {
        var profile = Profile.Value;
        Assert.Equal(AlsGroundedMachineKind.CrouchingCycles, profile.Runtime.Machine.Kind);
        Assert.Single(profile.Runtime.Machine.States.ToArray()); Assert.Empty(profile.Runtime.Machine.Edges.ToArray());
        Assert.Equal(54, profile.Runtime.WalkPosePlayerId); Assert.Equal(48, profile.Runtime.LeanPlayerId);
        Assert.Equal(new Vector3(1.4f, 1, 1.4f), profile.Diagonal.Scale);
        Assert.Equal(new AlsCrouchingStrideSettings(10, 10), profile.Runtime.Stride);
    }

    [Theory]
    [InlineData(0f)] [InlineData(.4f)] [InlineData(1f)]
    public void InitializesAllOuterInputsEvenWhenDirectionIsInactive(float weight)
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink();
        var update = runtime.Prepare(default, Rules, 0, Vector4.One, Context(1, weight: weight), sink);
        Assert.True(update.InitializedSources); Assert.False(update.DirectionUpdated);
        Assert.Equal(new[] { 54, 49, 50, 55, 51, 52, 53, 48 }, sink.Initialized.AsSpan(0, sink.InitCount).ToArray());
        Assert.Equal(new[] { 54, 48 }, sink.Players.AsSpan(0, sink.Count).ToArray());
        for (var i = 0; i < sink.Count; i++)
        { Assert.Equal(weight, sink.Contexts[i].Weight); Assert.Equal(2, sink.Contexts[i].StateCount); }
        Assert.False(update.State.Direction.HasUpdated);
    }

    [Fact]
    public void UpdateOrderIsWalkThenLinkedCacheThenLeanWithDistinctWeights()
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink();
        var update = runtime.Prepare(default, Rules, .4f, Vector4.One, Context(1, weight: .5f), sink);
        Assert.True(update.DirectionUpdated);
        Assert.Equal(new[] { 54, 55, 49, 50, 52, 48 }, sink.Players.AsSpan(0, sink.Count).ToArray());
        Assert.Equal(.3f, sink.Contexts[0].Weight); Assert.Equal(.5f, sink.Contexts[sink.Count - 1].Weight);
        for (var i = 1; i < sink.Count - 1; i++)
        {
            Assert.Equal(.05f, sink.Contexts[i].Weight); Assert.Equal(3, sink.Contexts[i].StateCount);
            Assert.Equal(new AlsActiveAnimationState(900, 1), sink.Contexts[i].GetState(0));
            Assert.Equal(new AlsActiveAnimationState(Profile.Value.Runtime.MachineNodeIndex, 0), sink.Contexts[i].GetState(1));
            Assert.Equal(new AlsActiveAnimationState(Profile.Value.Direction.UpdateGraph.MachineNodeIndex, 0), sink.Contexts[i].GetState(2));
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(100)]
    public void ExplicitInitializationOwnsBothPinsBeforeDelayedFirstUpdate(long frame)
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink();
        var initial = runtime.Initialize(default, Context(1).Identity, sink);
        Assert.True(initial.State.Machine.HasInitialized); Assert.False(initial.State.Machine.HasUpdated);
        Assert.True(initial.State.Direction.HasInitialized); Assert.False(initial.State.Direction.HasUpdated);
        Assert.Equal(default, initial.State.Stride); Assert.Equal(1, initial.State.InitializationEpoch);
        Assert.Equal(0, initial.State.DiagonalAlpha); Assert.Equal(0, sink.Count);
        Assert.Equal(new[] { 54, 49, 50, 55, 51, 52, 53, 48 }, sink.Initialized);
        sink.Reset();
        var first = runtime.Prepare(initial.State, Rules, 0, Vector4.One, Context(frame), sink, .6f);
        Assert.False(first.InitializedSources); Assert.False(first.DirectionUpdated);
        Assert.Equal(0, sink.InitCount); Assert.Equal(1, first.State.InitializationEpoch);
        sink.Reset();
        var direction = runtime.Prepare(first.State, Rules, 1, Vector4.One, Context(frame + 1, 1), sink, .2f);
        Assert.True(direction.DirectionUpdated); Assert.False(direction.DirectionUpdate.Reinitialized);
        Assert.Equal(0, direction.DirectionUpdate.InitializeStates); Assert.Equal(0, sink.InitCount);
        Assert.Equal(.2f, direction.State.DiagonalAlpha);
    }

    [Fact]
    public void WholeReinitializationResetsStrideButPreservesLastUpdatedControlAlpha()
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink();
        var previous = runtime.Prepare(default, Rules, .7f, Vector4.One, Context(1), sink, .6f).State;
        sink.Reset();
        var reset = runtime.Initialize(previous, Context(10).Identity, sink);
        Assert.Equal(2, reset.State.InitializationEpoch); Assert.Equal(.6f, reset.State.DiagonalAlpha);
        Assert.Equal(default, reset.State.Stride); Assert.False(reset.State.Direction.HasUpdated);
        Assert.False(reset.State.Machine.HasUpdated); Assert.Equal(8, sink.InitCount); Assert.Equal(0, sink.Count);
        sink.Reset();
        var first = runtime.Prepare(reset.State, Rules, .3f, Vector4.One, Context(100), sink, 2);
        Assert.Equal(.3f, first.State.Stride.Alpha); Assert.Equal(1, first.State.DiagonalAlpha);
        Assert.False(first.InitializedSources); Assert.Equal(2, first.State.InitializationEpoch);
        Assert.Equal(0, sink.InitCount);
    }

    [Fact]
    public void ExplicitInitializationFailureRetriesWithoutConsumingTheEpoch()
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink();
        var previous = runtime.Prepare(default, Rules, 1, Vector4.One, Context(1), sink).State;
        sink.Reset(); sink.FailAtInitialization = 4;
        Assert.Throws<InvalidOperationException>(() => runtime.Initialize(previous, Context(2).Identity, sink));
        Assert.Equal(1, previous.InitializationEpoch);
        sink.Reset(); sink.FailAtInitialization = -1;
        var retry = runtime.Initialize(previous, Context(2).Identity, sink);
        Assert.Equal(2, retry.State.InitializationEpoch); Assert.Equal(8, sink.InitCount);
        sink.Reset();
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Initialize(previous with { InitializationEpoch = long.MaxValue }, Context(2).Identity, sink));
        Assert.Equal(0, sink.InitCount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void InitializedButUnupdatedOwnerRejectsForeignOrEarlierFrames(int mutation)
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink();
        var previous = runtime.Initialize(default, Context(10).Identity, sink).State;
        var identity = mutation switch { 0 => new AlsFrameIdentity(11, 1, 1), 1 => new AlsFrameIdentity(11, 0, 2), _ => Context(9).Identity };
        sink.Reset();
        Assert.Throws<ArgumentException>(() => runtime.Initialize(previous, identity, sink));
        Assert.Throws<ArgumentException>(() => runtime.Prepare(previous, Rules, 1, Vector4.One, new(identity, 1, .01f), sink));
        Assert.Equal(0, sink.Count); Assert.Equal(0, sink.InitCount);
    }

    [Fact]
    public void OnlyWholeCyclesReentryResetsOuterSourceInitialization()
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink();
        var first = runtime.Prepare(default, Rules, 1, Vector4.One, Context(1), sink);
        sink.Reset();
        var stopped = runtime.Prepare(first.State, Rules, -1, Vector4.One, Context(2, 1), sink);
        Assert.False(stopped.DirectionUpdated); Assert.Equal(0, sink.InitCount);
        var held = runtime.Prepare(stopped.State, Rules, -1, Vector4.One, Context(3), sink);
        sink.Reset();
        var directionEntered = runtime.Prepare(held.State, Rules with { MovementDirection = AlsMovementDirection.Left }, 1, Vector4.One, Context(4, .06f), sink);
        Assert.True(directionEntered.DirectionUpdate.Reinitialized); Assert.False(directionEntered.InitializedSources);
        Assert.Equal(0, sink.InitCount); Assert.InRange(directionEntered.State.Stride.Alpha, .19f, .21f);
        sink.Reset();
        var cyclesEntered = runtime.Prepare(directionEntered.State, Rules, .8f, Vector4.One, Context(6), sink);
        Assert.True(cyclesEntered.InitializedSources); Assert.Equal(8, sink.InitCount);
        Assert.Equal(.8f, cyclesEntered.State.Stride.Alpha); Assert.Equal(0, cyclesEntered.State.Direction.Transitions.Count);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void CandidateRetryPreservesStatesWeightsAndInitialization(int hz)
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var firstSink = new Sink(); var retrySink = new Sink();
        var previous = default(AlsCrouchingCycleState);
        for (var frame = 1; frame <= hz * 2; frame++)
        {
            var rules = Rules with { MovementDirection = (frame % hz < hz / 2 ? AlsMovementDirection.Left : AlsMovementDirection.Right) };
            firstSink.Reset(); retrySink.Reset();
            var first = runtime.Prepare(previous, rules, .7f, Vector4.One, Context(frame, 1f / hz), firstSink);
            var retry = runtime.Prepare(previous, rules, .7f, Vector4.One, Context(frame, 1f / hz), retrySink);
            Assert.Equal(first.InitializedSources, retry.InitializedSources); Assert.Equal(first.State.Stride, retry.State.Stride);
            Assert.Equal(first.State.Direction.Transitions, retry.State.Direction.Transitions);
            Assert.Equal(firstSink.Players.AsSpan(0, firstSink.Count).ToArray(), retrySink.Players.AsSpan(0, retrySink.Count).ToArray());
            for (var i = 0; i < firstSink.Count; i++) Assert.Equal(firstSink.Contexts[i].Weight, retrySink.Contexts[i].Weight);
            previous = first.State;
        }
    }

    [Fact]
    public void FailedInitializationCanRetryFromTheUnchangedOwner()
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink { FailAtInitialization = 4 };
        Assert.Throws<InvalidOperationException>(() => runtime.Prepare(default, Rules, 1, Vector4.One, Context(1), sink));
        sink.Reset(); sink.FailAtInitialization = -1;
        var retry = runtime.Prepare(default, Rules, 1, Vector4.One, Context(1), sink);
        Assert.True(retry.InitializedSources); Assert.Equal(8, sink.InitCount); Assert.True(retry.DirectionUpdated);
    }

    [Fact]
    public void WholeCycleUpdatesAllocateNothingAfterWarmup()
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink();
        var previous = runtime.Prepare(default, Rules, .7f, Vector4.One, Context(1), sink).State;
        var rules = Rules with { MovementDirection = AlsMovementDirection.Left }; var context = Context(2);
        long allocated = -1; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 200; i++) { sink.Reset(); runtime.Prepare(previous, rules, .7f, Vector4.One, context, sink); }
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) { sink.Reset(); runtime.Prepare(previous, rules, .7f, Vector4.One, context, sink); }
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30))); Assert.Null(failure); Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData(1, 0, 0)] [InlineData(1, 1, 1)] [InlineData(2, 0, 1)]
    public void RejectsForeignOwnerGenerationAndInconsistentHistoryBeforeCallbacks(int generation, int character, int previousFrame)
    {
        var runtime = new AlsCrouchingCycleRuntime(Profile.Value.Runtime); var sink = new Sink();
        var previous = runtime.Prepare(default, Rules, .7f, Vector4.One, Context(1), sink).State;
        var invalid = previous with { Identity = new(previousFrame, (uint)character, (uint)generation) }; sink.Reset();
        Assert.Throws<ArgumentException>(() => runtime.Prepare(invalid, Rules, .7f, Vector4.One, Context(2), sink));
        Assert.Equal(0, sink.Count); Assert.Equal(0, sink.InitCount);
    }

    [Theory]
    [InlineData("curve-alpha")] [InlineData("alpha")] [InlineData("lod")] [InlineData("alpha-filter")]
    [InlineData("foreign-additive")] [InlineData("extra-node")] [InlineData("root-id")] [InlineData("layer-id")]
    [InlineData("source-id")] [InlineData("notify")] [InlineData("reentry")] [InlineData("source-start")]
    public void RejectsIncompleteOrUnsupportedWholeContent(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_grounded_dependencies.json"))!;
        var content = root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith(".(CLF) Locomotion Cycles.AnimStateNode_0.(CLF) Locomotion Cycles"))!;
        var nodes = content["nodes"]!.AsArray(); var additive = nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_ApplyAdditive")!;
        var data = additive["properties"]!["Node"]!;
        var machine = root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "(CLF) Locomotion Cycles")!;
        switch (mutation)
        {
            case "curve-alpha": data["alphaInputType"] = "Curve"; break;
            case "alpha": Pin(additive, "Alpha")["value"] = "0.5"; break;
            case "lod": data["lODThreshold"] = 0; break;
            case "alpha-filter": data["alphaScaleBiasClamp"]!["bInterpResult"] = true; break;
            case "foreign-additive": Pin(additive, "Additive")["links"]![0]!["node"] = "AnimGraphNode_SequencePlayer_0"; break;
            case "extra-node": var extra = additive.DeepClone(); extra["name"] = "UnusedAdditive"; nodes.Add(extra); break;
            case "root-id": machine["states"]![0]!["stateRootNodeIndex"] = 0; break;
            case "layer-id": machine["states"]![0]!["layerNodeIndices"]![0] = 0; break;
            case "source-id": machine["states"]![0]!["playerNodeIndices"]![0] = 0; break;
            case "notify": machine["states"]![0]!["startNotify"] = 0; break;
            case "reentry": var owner = root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith(":BaseLayer"))!["nodes"]!
                    .AsArray().Single(n => n!["name"]!.GetValue<string>() == "AnimGraphNode_StateMachine_4")!;
                owner["properties"]!["Node"]!["bReinitializeOnBecomingRelevant"] = false; break;
            case "source-start": nodes.Single(n => n!["name"]!.GetValue<string>() == "AnimGraphNode_SequencePlayer_0")!["properties"]!["Node"]!["startPosition"] = .1f; break;
        }
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString()));
    }

    private sealed class Sink : IAlsCrouchingCycleUpdateSink
    {
        public readonly int[] Initialized = new int[8], Players = new int[8];
        public readonly AlsPoseUpdateContext[] Contexts = new AlsPoseUpdateContext[8];
        public int InitCount, Count, FailAtInitialization = -1;
        public void Reset() => InitCount = Count = 0;
        public void InitializeSource(int playerId)
        { if (InitCount == FailAtInitialization) throw new InvalidOperationException("Injected initialization failure."); Initialized[InitCount++] = playerId; }
        public void UpdateSource(int playerId, in AlsPoseUpdateContext context) { Players[Count] = playerId; Contexts[Count++] = context; }
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
    }
    private static AlsCrouchingCycleProfile Compile(string? graph = null) => AlsCrouchingCycleCompiler.Compile(graph ?? Read("v4_grounded_dependencies.json"),
        Read("v4_pose_cache_graph.json"), Read("v4_locomotion_curves.json"), Read("v4_lean_sampling.json"), Sources.Value, Set.Value);
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
}
