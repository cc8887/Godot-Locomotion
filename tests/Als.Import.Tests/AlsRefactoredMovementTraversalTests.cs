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
        var basePose = new AlsPrecisePose[79]; var baseCurves = new AlsInertialCurve[f.DirectionPose.CurveNames.Length];
        var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[poseProfile.CurveNames.Length];
        var init = new AlsGraphTraversalCounter(0, 0); var counter = init; var stage = 0;
        int[] targets = [5,2,0,1,2,3,4,3,2,0,2];
        var states = new HashSet<int>(); var cacheIds = new HashSet<int>(); var skipped = 0; var inertialTicks = 0; var mixed = 0;
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
                players.Prepare(frame, graph.SourceInputs, delta); details.CaptureSourceTimes(frame, players);
                direction.Sample(frame, graph.Direction, graph.Sources, players, basePose, baseCurves);
                movement.Prepare(frame, input.UnweightedGaitRunningAmount, new(.3f, -.4f), delta, graph.InitializeMovement);
                movement.Evaluate(frame, basePose, baseCurves);
                sampler.Sample(frame, machine, details, players, movement, pose, curves);
            }
            Prepare(); var expected = pose.ToArray(); var expectedCurves = curves.ToArray();
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
            machine.Cancel(); details.Cancel(); graph.Cancel(); players.Cancel(); movement.Cancel();
            Prepare(); Assert.Equal(expected, pose); Assert.Equal(expectedCurves, curves);
            Assert.Equal(updates, graph.CacheUpdates.ToArray()); Assert.Equal(ticks, graph.SourceInputs.ToArray()); Assert.Equal(paths, graph.SourceContexts.ToArray());
            Assert.Equal(batches, graph.SkippedBatches.ToArray()); Assert.Equal(skippedPaths, graph.SkippedContexts.ToArray());
            machine.ValidateCommit(frame); details.ValidateCommit(frame); graph.ValidateCommit(frame); players.ValidateCommit(frame); movement.ValidateCommit(frame);
            machine.Commit(frame); details.Commit(frame); graph.Commit(frame); players.Commit(frame); movement.Commit(frame);
            if (current == targets[stage]) stage = (stage + 1) % targets.Length;
            counter = counter.Next((ulong)frame + 1);
        }
        Assert.Equal(6, states.Count); Assert.Equal(8, cacheIds.Count); Assert.True(skipped > 0); Assert.True(inertialTicks > 0); Assert.True(mixed > 0);
    }
}
