using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementTraversalTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog;
        public readonly AlsRefactoredMovementDetailsPoseGraph Details;
        public readonly AlsRefactoredDirectionSourceProfile Direction;
        public readonly AlsRefactoredMovementCacheProfile Movement;
        public readonly AlsRefactoredDirectionPose DirectionPose;
        public readonly AlsRefactoredSyncBank Bank;
        public readonly IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> Triangles;
        public Fixture()
        {
            var index = MantlingHostFixture.Read("refactored_animation_sources");
            byte[] Read(string p) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p));
            Catalog = new(index, Read); var machines = MantlingHostFixture.Read("refactored_stance_machines");
            Details = new(Catalog, new(machines, Catalog)); Direction = new(Catalog, new(Catalog, new(machines, Catalog, false)));
            Bank = new(MantlingHostFixture.Read("refactored_sync_inputs"), Catalog);
            Triangles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), index, Read);
            Movement = new(Catalog, Triangles); DirectionPose = new(Catalog, Direction, Triangles);
        }
        public AlsRefactoredSourcePlayerRuntime Players() => new(Catalog, Bank, Triangles, Details.Players.Bind(0,
            new Dictionary<string, int> { ["Movement"] = 0, ["Run Start"] = 1, ["First Pivot"] = 2, ["Second Pivot"] = 3 }));
    }
    private static readonly Lazy<Fixture> Data = new(() => new());

    [Fact]
    public void ParentLatchesPreserveNativeEnumThresholdAndTransactionBoundaries()
    {
        var f = Data.Value; var callbacks = f.Details.Callbacks;
        var parent = new AlsRefactoredMovementParentRuntime(callbacks);
        var identity = new GodotAls.Core.Contracts.AlsFrameIdentity(0, 7, 1);
        parent.Prepare(identity);
        parent.ActivatePivot(0, MathF.BitDecrement(200), 200); Assert.True(parent.Candidate.PivotActive);
        parent.ActivatePivot(0, 200, 200); Assert.False(parent.Candidate.PivotActive);
        parent.ActivatePivot(0, 0, 200); parent.ActivatePivot(0, 201, 200); Assert.False(parent.Candidate.PivotActive);
        var hips = callbacks.Nodes.ToArray().Where(n => n.Function == AlsRefactoredStanceFunction.SetHipsDirection).ToArray();
        Assert.Equal(6, hips.Length);
        string[] nativeOrder = ["Forward", "Backward", "LeftForward", "LeftBackward", "RightForward", "RightBackward"];
        foreach (var command in hips)
        {
            parent.Apply(identity, f.Catalog.IndexDigest, command);
            Assert.Equal(Array.IndexOf(nativeOrder, command.HipsDirection), (int)parent.Candidate.HipsDirection);
            Assert.Equal(default, parent.Committed);
        }
        parent.ActivatePivot(0, 0, 200); var candidate = parent.Candidate;
        parent.Cancel(); parent.Prepare(identity); Assert.Equal(default, parent.Candidate);
        parent.ActivatePivot(0, 0, 200); parent.Apply(identity, f.Catalog.IndexDigest, hips[^1]);
        Assert.Equal(candidate, parent.Candidate); parent.Commit(0); Assert.Equal(candidate, parent.Committed);
        Assert.Throws<ArgumentException>(() => parent.Prepare(new(1, 8, 1)));
        parent.Prepare(new(1, 7, 1), true); Assert.Equal(default, parent.Candidate);
        parent.Cancel(); parent.Prepare(new(1, 7, 1)); Assert.Equal(candidate, parent.Candidate);
        var reset = callbacks.Nodes.ToArray().First(n => n.Function == AlsRefactoredStanceFunction.ResetPivot);
        parent.Apply(new(1, 7, 1), f.Catalog.IndexDigest, reset); Assert.False(parent.Candidate.PivotActive);
        Assert.Equal(candidate, parent.Committed); parent.Cancel();
        foreach (var bad in new[] { reset with { SourcePropertyIndex = -1 }, reset with { OnBecomeRelevant = false },
            hips[0] with { HipsDirection = "Invalid" }, callbacks.Nodes.ToArray().First(n => n.Function == AlsRefactoredStanceFunction.RefreshGroundedMovement) })
        {
            parent.Prepare(new(1, 7, 1));
            Assert.Throws<ArgumentException>(() => parent.Apply(new(1, 7, 1), f.Catalog.IndexDigest, bad));
            Assert.Throws<InvalidOperationException>(() => parent.Commit(1)); parent.Cancel();
        }
        parent.Prepare(new(1, 7, 1));
        Assert.Throws<ArgumentException>(() => parent.Apply(new(1, 8, 1), f.Catalog.IndexDigest, reset));
        Assert.Throws<InvalidOperationException>(() => parent.Commit(1)); parent.Cancel();
        parent.Prepare(new(1, 7, 1));
        Assert.Throws<ArgumentException>(() => parent.Apply(new(1, 7, 1), "foreign", reset));
        Assert.Throws<InvalidOperationException>(() => parent.Commit(1)); parent.Cancel();
        parent.Prepare(new(1, 7, 1));
        Assert.Throws<ArgumentException>(() => parent.ActivatePivot(1, float.NaN, 200));
        Assert.Throws<InvalidOperationException>(() => parent.Commit(1)); parent.Cancel();
        Assert.Equal(candidate, parent.Committed);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void OnePivotActivationIsConsumedBeforeSourcesAndExitsUsingRealClocks(int hz)
    {
        var f = Data.Value; var machine = new AlsRefactoredMovementDetailsRuntime(f.Details.Resources);
        var details = new AlsRefactoredMovementDetailsSourceRuntime(f.Details, 0); var players = f.Players();
        var graph = new AlsRefactoredMovementTraversal(f.Catalog, f.Details, f.Direction, 0);
        var parent = new AlsRefactoredMovementParentRuntime(f.Details.Callbacks);
        var init = new AlsGraphTraversalCounter(0, 0); var counter = init;
        var fired = false; var entered = 0; var pivotFrames = 0; var exited = false; var hipCallbacks = 0;
        for (var frame = 0; frame < hz * 4; frame++)
        {
            var context = new AlsPoseUpdateContext(new(frame, 7, 1), 1, 1f / hz).WithUpdateCounter(counter);
            var activate = !fired && machine.CommittedState.CurrentState == 2;
            var direction = (frame / Math.Max(1, hz / 3)) % 4;
            void Prepare()
            {
                parent.Prepare(context.Identity);
                if (activate) parent.ActivatePivot(frame, 150, 200);
                machine.Prepare(frame, new("Als.Gait.Running", 1, 1, 1, parent.Candidate.PivotActive),
                    details.CommittedObservations, context.Delta, updateCounter: counter);
                details.Prepare(machine, context, Vector4.One, parent: parent);
                // The callback has already consumed the request before deferred Movement runs.
                if (activate) Assert.False(parent.Candidate.PivotActive);
                graph.Prepare(frame, machine, details, init,
                    new(direction == 0, direction == 1, direction == 2, direction == 3, .7f, 0),
                    new(1, 1, 1, 1), new("Als.Gait.Running", 0, 0), parent: parent);
                if (!graph.Sources.CallbackCommands.IsEmpty)
                    Assert.Equal(graph.Sources.CallbackCommands[^1].HipsDirection, parent.Candidate.HipsDirection.ToString());
                players.Prepare(frame, graph.SourceInputs, context.Delta); details.CaptureSourceTimes(frame, players);
            }
            Prepare(); var expected = parent.Candidate; var ticks = graph.SourceInputs.ToArray();
            var state = machine.Candidate.State.CurrentState;
            if (activate) { Assert.Equal(3, state); entered++; }
            Assert.NotEqual(4, state); if (state == 3) pivotFrames++;
            if (fired && state == 2) exited = true;
            hipCallbacks += graph.Sources.CallbackCommands.Length;
            parent.Cancel(); machine.Cancel(); details.Cancel(); graph.Cancel(); players.Cancel();
            Prepare(); Assert.Equal(expected, parent.Candidate); Assert.Equal(ticks, graph.SourceInputs.ToArray());
            parent.ValidateCommit(frame); machine.ValidateCommit(frame); details.ValidateCommit(frame); graph.ValidateCommit(frame); players.ValidateCommit(frame);
            parent.Commit(frame); machine.Commit(frame); details.Commit(frame); graph.Commit(frame); players.Commit(frame);
            fired |= activate; counter = counter.Next((ulong)frame + 1);
        }
        Assert.Equal(1, entered); Assert.True(pivotFrames > 1); Assert.True(exited); Assert.True(hipCallbacks > 4);
        Assert.False(parent.Committed.PivotActive);
    }

    [Fact]
    public void InertiaRetainsUpdateOnlyRequestsAndResetsAfterMissingTraversal()
    {
        var f = Data.Value; var machine = new AlsRefactoredMovementDetailsRuntime(f.Details.Resources);
        var details = new AlsRefactoredMovementDetailsSourceRuntime(f.Details, 0); var players = f.Players();
        var graph = new AlsRefactoredMovementTraversal(f.Catalog, f.Details, f.Direction, 0);
        var movement = new AlsRefactoredMovementCacheRuntime(f.Movement, f.DirectionPose.BoneNames, f.DirectionPose.CurveNames);
        var profile = new AlsRefactoredMovementDetailsPose(f.Catalog, f.Details, f.Movement, movement.CurveNames);
        var node = new AlsRefactoredMovementInertialization(f.Catalog, profile);
        var init = new AlsGraphTraversalCounter(0, 0); var counter = init;
        var pose = Enumerable.Repeat(AlsPrecisePose.Identity, 79).ToArray(); var curves = new AlsInertialCurve[profile.CurveNames.Length];
        for (var frame = 0; frame < 5; frame++)
        {
            if (frame == 4) counter = counter.Next(4).Next(4);
            var context = new AlsPoseUpdateContext(new(frame, 4, 1), 1, .01f).WithUpdateCounter(counter);
            machine.Prepare(frame, new(frame == 0 ? "" : "Als.Gait.Running", 1, frame == 0 ? 0 : 1, 1, false), details.CommittedObservations, .01f, updateCounter: counter);
            details.Prepare(machine, AlsRefactoredMovementInertialization.SourceContext(context), Vector4.One);
            graph.Prepare(frame, machine, details, init, new(true, false, false, false, 0, 0), new(1, 1, 1, 1), new("Als.Gait.Running", 0, 0));
            players.Prepare(frame, graph.SourceInputs, .01f); details.CaptureSourceTimes(frame, players);
            node.Prepare(context, machine, graph);
            if (frame is 1 or 2)
            {
                Assert.Equal(1, node.PendingRequests); Assert.Equal(1, node.CommittedHistoryCount);
                node.Cancel(); node.Prepare(context, machine, graph); Assert.Equal(1, node.PendingRequests);
            }
            else
            {
                pose[1] = pose[1] with { Position = new(frame, 0, 0) };
                if (frame == 3)
                {
                    Assert.Throws<ArgumentException>(() => node.Evaluate(frame, [], curves, AlsPrecisePose.Identity));
                    Assert.Throws<ArgumentException>(() => node.Commit(frame)); node.Cancel(); node.Prepare(context, machine, graph);
                }
                node.Evaluate(frame, pose, curves, AlsPrecisePose.Identity);
                Assert.Equal(frame == 3, node.IsActive);
                if (frame == 4) Assert.Equal(pose, node.Pose.ToArray());
            }
            node.ValidateCommit(frame); graph.ValidateCommit(frame); details.ValidateCommit(frame); machine.ValidateCommit(frame); players.ValidateCommit(frame);
            node.Commit(frame); graph.Commit(frame); details.Commit(frame); machine.Commit(frame); players.Commit(frame);
            counter = counter.Next((ulong)frame + 1);
        }
        Assert.Equal(1, node.CommittedHistoryCount); Assert.Equal(0, node.PendingRequests);
    }

    [Fact]
    public void InitializationAndFailedDeferredUpdatesRemainCandidateOwned()
    {
        var f = Data.Value; var machine = new AlsRefactoredMovementDetailsRuntime(f.Details.Resources);
        var details = new AlsRefactoredMovementDetailsSourceRuntime(f.Details, 0); var players = f.Players();
        var graph = new AlsRefactoredMovementTraversal(f.Catalog, f.Details, f.Direction, 0);
        var initialization = new AlsGraphTraversalCounter(0, 0); var counter = initialization;
        void Details(int frame, bool reset = false)
        {
            machine.Prepare(frame, new("Als.Gait.Running", 1, 1, 1, false), details.CommittedObservations, .05f, reinitialize: reset, updateCounter: counter);
            details.Prepare(machine, new AlsPoseUpdateContext(new(frame, 4, 1), 1, .05f).WithUpdateCounter(counter).WithInertialization(119, true), Vector4.One, reset);
        }
        void Traverse(int frame, bool reset = false) => graph.Prepare(frame, machine, details, initialization,
            new(true, false, false, false, 0, 0), new(1, 1, 1, 1), new("Als.Gait.Running", 0, 0), reset);
        void Commit(int frame)
        {
            players.Prepare(frame, graph.SourceInputs, .05f); details.CaptureSourceTimes(frame, players);
            graph.ValidateCommit(frame); details.ValidateCommit(frame); machine.ValidateCommit(frame); players.ValidateCommit(frame);
            graph.Commit(frame); details.Commit(frame); machine.Commit(frame); players.Commit(frame); counter = counter.Next((ulong)frame + 1);
        }
        Details(0);
        // Invalid Forward input fails after the direction machine has prepared.
        Assert.Throws<ArgumentException>(() => graph.Prepare(0, machine, details, initialization,
            new(true, false, false, false, 0, 0), new(1, 1, 1, 1), new("Als.Gait.Running", float.NaN, 0)));
        Assert.Throws<ArgumentException>(() => graph.Commit(0));
        Traverse(0); Assert.True(graph.InitializeMovement); var inputs = graph.SourceInputs.ToArray(); graph.Cancel();
        Traverse(0); Assert.True(graph.InitializeMovement); Assert.Equal(inputs, graph.SourceInputs.ToArray()); Commit(0);
        Details(1); Traverse(1); Assert.False(graph.InitializeMovement); Commit(1);
        // Reset can be cancelled without resetting committed source/filter histories.
        Details(2, true); Traverse(2, true); Assert.True(graph.InitializeMovement);
        graph.Cancel(); details.Cancel(); machine.Cancel();
        Details(2); Traverse(2); Assert.False(graph.InitializeMovement); Commit(2);
        // A fresh graph initialization traversal does propagate on actual node initialization.
        initialization = initialization.Next(3);
        Details(3, true); Traverse(3, true); Assert.True(graph.InitializeMovement); Commit(3);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void SingleDrainCarriesSelectedContextsThroughRealDirectionLeanAndDetails(int hz)
    {
        var f = Data.Value; var machine = new AlsRefactoredMovementDetailsRuntime(f.Details.Resources);
        var details = new AlsRefactoredMovementDetailsSourceRuntime(f.Details, 0);
        var graph = new AlsRefactoredMovementTraversal(f.Catalog, f.Details, f.Direction, 0);
        Assert.Equal(new[] { 67,133,136,138,137,132,134,135 }, graph.Caches.UpdateOrder.ToArray()); Assert.Equal(32, graph.Caches.Reads.Length);
        var players = f.Players(); var direction = f.DirectionPose.CreateSampler();
        var movement = new AlsRefactoredMovementCacheRuntime(f.Movement, f.DirectionPose.BoneNames, f.DirectionPose.CurveNames);
        var poseProfile = new AlsRefactoredMovementDetailsPose(f.Catalog, f.Details, f.Movement, movement.CurveNames);
        var sampler = poseProfile.CreateSampler();
        var inertia = new AlsRefactoredMovementInertialization(f.Catalog, poseProfile);
        var basePose = new AlsPrecisePose[79]; var baseCurves = new AlsInertialCurve[f.DirectionPose.CurveNames.Length];
        var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[poseProfile.CurveNames.Length];
        var init = new AlsGraphTraversalCounter(0, 0); var counter = init; var stage = 0;
        int[] targets = [5,2,0,1,2,3,4,3,2,0,2];
        var states = new HashSet<int>(); var cacheIds = new HashSet<int>(); var skipped = 0; var inertialTicks = 0; var mixed = 0;
        var active = 0; var forwarded = 0;
        for (var frame = 0; frame < hz * 6; frame++)
        {
            var delta = frame % 31 == 0 ? 0 : 1f / hz;
            var input = stage is 2 or 9 ? new AlsRefactoredMovementDetailsInput("", 1, 0, 1, false) :
                new AlsRefactoredMovementDetailsInput("Als.Gait.Running", stage == 10 ? .5f : 1, 1, stage == 0 ? .5f : 1, stage is 5 or 6 or 7);
            var dir = (frame / Math.Max(1, hz / 3)) % 4;
            var directionInput = new AlsRefactoredDirectionInput(dir == 0, dir == 1, dir == 2, dir == 3, frame % 9 < 4 ? -.7f : .7f, 0);
            var context = new AlsPoseUpdateContext(new(frame, 5, 1), frame == 1 ? 0 : .8f, delta, .35f)
                .WithUpdateCounter(counter).WithState(65, 0).WithInertialization(119, true);
            if (frame % 17 == 0) context = context.AsInactive();
            // Non-unit total catches accidental normalization of already-normalized channels.
            var velocity = frame % 37 == 0 ? Vector4.Zero : new Vector4(.17f, .23f, .41f, .53f);
            void Prepare()
            {
                machine.Prepare(frame, input, details.CommittedObservations, delta, context.Weight, updateCounter: counter);
                details.Prepare(machine, context, velocity);
                graph.Prepare(frame, machine, details, init, directionInput, new(1.1f, 1, .83f, .75f),
                    new(frame % 13 < 5 ? "Als.Gait.Sprinting" : "Als.Gait.Running", .23f, .12f), yaw: new(2, -3, 4, -5));
                inertia.Prepare(context, machine, graph);
                players.Prepare(frame, graph.SourceInputs, delta); details.CaptureSourceTimes(frame, players);
                direction.Sample(frame, graph.Direction, graph.Sources, players, basePose, baseCurves);
                movement.Prepare(frame, input.UnweightedGaitRunningAmount, new(.3f, -.4f), delta, graph.InitializeMovement);
                movement.Evaluate(frame, basePose, baseCurves);
                sampler.Sample(frame, machine, details, players, movement, pose, curves);
                inertia.Evaluate(frame, pose, curves, AlsPrecisePose.Identity);
            }
            Prepare(); var expected = pose.ToArray(); var expectedCurves = curves.ToArray();
            var smoothed = inertia.Pose.ToArray(); var smoothedCurves = inertia.Curves.ToArray();
            active += inertia.IsActive ? 1 : 0; forwarded += inertia.ForwardAttempts;
            var updates = graph.CacheUpdates.ToArray(); var ticks = graph.SourceInputs.ToArray(); var paths = graph.SourceContexts.ToArray();
            var batches = graph.SkippedBatches.ToArray(); var skippedPaths = graph.SkippedContexts.ToArray();
            var chosen = details.CacheReads.ToArray().Aggregate((a, b) => b.Context.Weight > a.Context.Weight ? b : a).Context;
            Assert.Equal(chosen, graph.MovementContext); Assert.Equal(chosen.Weight, graph.Direction.Candidate.State.RecordedWeight);
            Assert.Equal(details.DirectionWeights, graph.Sources.DirectionWeights);
            Assert.Equal(updates.Select(u => u.PropertyIndex), graph.Caches.UpdateOrder.ToArray().Where(id => updates.Any(u => u.PropertyIndex == id)));
            Assert.Equal(ticks.Length, ticks.Select(t => t.PlayerId).Distinct().Count());
            for (var i = 0; i < ticks.Length; i++)
            {
                Assert.Equal(paths[i].Weight, ticks[i].Weight); Assert.Equal(paths[i].InertializationSync, ticks[i].RequestedInertialization);
                Assert.Equal(.35f, paths[i].RootMotionWeight); Assert.Equal(119, paths[i].InertializationRequester);
                if (!context.IsActive) Assert.False(paths[i].IsActive); // Active parents may have inactive gait children.
                inertialTicks += ticks[i].RequestedInertialization ? 1 : 0;
            }
            foreach (var u in updates) cacheIds.Add(u.PropertyIndex);
            Assert.All(batches, b => Assert.Equal(119, b.Handler)); skipped += skippedPaths.Length;
            mixed += details.CacheReads.Length > 1 ? 1 : 0; states.Add(machine.Candidate.State.CurrentState);
            var current = machine.Candidate.State.CurrentState;
            machine.Cancel(); details.Cancel(); graph.Cancel(); players.Cancel(); movement.Cancel(); inertia.Cancel();
            Prepare(); Assert.Equal(expected, pose); Assert.Equal(expectedCurves, curves);
            Assert.Equal(smoothed, inertia.Pose.ToArray()); Assert.Equal(smoothedCurves, inertia.Curves.ToArray());
            Assert.Equal(updates, graph.CacheUpdates.ToArray()); Assert.Equal(ticks, graph.SourceInputs.ToArray()); Assert.Equal(paths, graph.SourceContexts.ToArray());
            Assert.Equal(batches, graph.SkippedBatches.ToArray()); Assert.Equal(skippedPaths, graph.SkippedContexts.ToArray());
            machine.ValidateCommit(frame); details.ValidateCommit(frame); graph.ValidateCommit(frame); players.ValidateCommit(frame); movement.ValidateCommit(frame);
            inertia.ValidateCommit(frame);
            machine.Commit(frame); details.Commit(frame); graph.Commit(frame); players.Commit(frame); movement.Commit(frame); inertia.Commit(frame);
            if (current == targets[stage]) stage = (stage + 1) % targets.Length;
            counter = counter.Next((ulong)frame + 1);
        }
        Assert.Equal(6, states.Count); Assert.Equal(8, cacheIds.Count); Assert.True(skipped > 0); Assert.True(inertialTicks > 0); Assert.True(mixed > 0);
        Assert.True(active > 0); Assert.True(forwarded > 0);
    }
}
