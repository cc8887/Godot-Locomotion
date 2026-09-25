using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredDirectionSourceTests
{
    private static readonly Lazy<(AlsRefactoredAnimationCatalog, AlsRefactoredDirectionSourceProfile)> Standing = new(() => Create(false));
    private static readonly Lazy<(AlsRefactoredAnimationCatalog, AlsRefactoredDirectionSourceProfile)> Crouching = new(() => Create(true));
    private static (AlsRefactoredAnimationCatalog, AlsRefactoredDirectionSourceProfile) Create(bool crouching)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var graph = new AlsRefactoredDirectionPoseGraph(catalog, new(MantlingHostFixture.Read("refactored_stance_machines"), catalog, crouching));
        return (catalog, new(catalog, graph));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UsesOriginalCacheOrderAndRejectsIncompleteOrBackwardsDependencies(bool crouching)
    {
        var (catalog, p) = (crouching ? Crouching : Standing).Value;
        Assert.Equal(crouching ? new[] { 47, 51, 46, 49, 50, 48 } : new[] { 133, 136, 138, 137, 132, 134, 135 }, p.Caches.UpdateOrder.ToArray());
        Assert.Equal(crouching ? 24 : 26, p.Caches.Reads.Length);
        var payload = catalog.Read(AlsRefactoredRotatePlayers.Blueprint(crouching));
        foreach (var mutation in crouching ? new[] { "duplicate", "missing" } : new[] { "duplicate", "missing", "order" })
        {
            var json = JsonNode.Parse(payload.GetRawText())!;
            var order = json["compiled"]!["orderedSavedPoseNodes"]![0]!["compiledNodeIndices"]!.AsArray();
            if (mutation == "duplicate") order[1] = order[0]!.DeepClone();
            if (mutation == "missing") order.RemoveAt(0);
            if (mutation == "order") { var first = order[4]!.DeepClone(); order[4] = order[5]!.DeepClone(); order[5] = first; }
            using var doc = JsonDocument.Parse(json.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredDirectionSourceProfile.CompileCaches(doc.RootElement, p.Graph, p.Forward));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedReadersSelectMaximumWeightAndDoNotRestartOnStateEntry(bool crouching)
    {
        var (_, p) = (crouching ? Crouching : Standing).Value;
        var machine = new AlsRefactoredDirectionRuntime(p.Graph.Resources); var source = new AlsRefactoredDirectionSourceRuntime(p, 0);
        var initialization = new AlsGraphTraversalCounter(0, 0); var counter = initialization;
        var movement = new AlsRefactoredMovementPlayerInput(1, 1, 1, 1);
        for (var frame = 0; frame < 2; frame++)
        {
            var delta = frame == 0 ? 0 : crouching ? .35f : .25f;
            machine.Prepare(frame, new(frame == 0, frame == 1, false, false, 0, 0), delta, updateCounter: counter);
            var context = new AlsPoseUpdateContext(new(frame, 19, 1), 1, delta).WithUpdateCounter(counter);
            source.Prepare(machine, context, initialization, new(.5f, .5f, 0, 0), movement, new("Als.Gait.Running", .5f, 0));
            Assert.Equal(frame == 0 ? "Forward" : "Backward", Assert.Single(source.CallbackCommands.ToArray()).HipsDirection);
            Assert.Equal(crouching ? 2 : 3, source.CacheUpdates.Length);
            var expectedWeight = frame == 0 ? .5f : .25f;
            foreach (var update in source.CacheUpdates)
            {
                Assert.Equal(!crouching && update.PropertyIndex == p.Forward!.BaseCache ? expectedWeight * .5f : expectedWeight, update.Context.Weight, 6);
                // In the halfway transition both states read the same cache;
                // the first equal-weight context (Forward) must win.
                Assert.Equal(0, update.Context.GetState(0).StateIndex);
            }
            Assert.Equal(2, source.SourceInputs.Length);
            Assert.All(source.SourceInputs.ToArray(), t => Assert.Equal(frame == 0, t.Reinitialize));
            Assert.Equal(crouching ? frame == 0 ? 2 : 4 : frame == 0 ? 4 : 6, source.CacheReadCount);
            source.Commit(frame); machine.Commit(frame); counter = counter.Next((ulong)frame + 1);
        }
    }

    [Theory]
    [InlineData(false, 30)]
    [InlineData(false, 60)]
    [InlineData(false, 120)]
    [InlineData(true, 30)]
    [InlineData(true, 60)]
    [InlineData(true, 120)]
    public void ActualSourceBatchPreservesContextsResetsAndRollback(bool crouching, int hz)
    {
        var (catalog, p) = (crouching ? Crouching : Standing).Value;
        var machine = new AlsRefactoredDirectionRuntime(p.Graph.Resources); var source = new AlsRefactoredDirectionSourceRuntime(p, 0);
        var cleanMachine = new AlsRefactoredDirectionRuntime(p.Graph.Resources); var clean = new AlsRefactoredDirectionSourceRuntime(p, 0);
        var index = MantlingHostFixture.Read("refactored_animation_sources");
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", path));
        var bank = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        var triangles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), index, Read);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog, bank, triangles, p.Players.Bind(0,
            new Dictionary<string, int> { ["Movement"] = 0, ["Run Start"] = 1, ["First Pivot"] = 2, ["Second Pivot"] = 3 }));
        var counter = new AlsGraphTraversalCounter(short.MaxValue - 3, 0); var initialization = new AlsGraphTraversalCounter(0, 0);
        var sharedFrames = 0; var zeroFrames = 0;
        for (var frame = 0; frame < hz * 2; frame++)
        {
            var delta = frame % 31 == 0 ? 0 : 1f / hz; var reset = frame == hz;
            if (reset) initialization = initialization.Next((ulong)frame);
            var direction = frame / (hz / 4) % 4;
            var input = new AlsRefactoredDirectionInput(direction == 0, direction == 1, direction == 2, direction == 3, frame % 13 < 5 ? .5f : -.5f, 0);
            var velocity = frame % 17 == 0 ? Vector4.Zero : new(.2f, .3f, .25f, .25f);
            var forward = new AlsRefactoredForwardInput(frame % hz < hz / 2 ? "Als.Gait.Sprinting" : "Als.Gait.Running", frame % 11 == 0 ? 1 : .3f, frame % 7 / 6f);
            var movement = new AlsRefactoredMovementPlayerInput(1.2f, .9f, .7f, .8f);
            var context = new AlsPoseUpdateContext(new(frame, 23, 1), .7f, delta, .4f).WithUpdateCounter(counter).WithState(999, 2);
            machine.Prepare(frame, input, delta, .7f, reset, counter); cleanMachine.Prepare(frame, input, delta, .7f, reset, counter);
            source.Prepare(machine, context, initialization, velocity, movement, forward, reset);
            var ticks = source.SourceInputs.ToArray(); var contexts = source.SourceContexts.ToArray(); var updates = source.CacheUpdates.ToArray(); var commands = source.CallbackCommands.ToArray();
            source.Cancel(); source.Prepare(machine, context, initialization, velocity, movement, forward, reset);
            clean.Prepare(cleanMachine, context, initialization, velocity, movement, forward, reset);
            Assert.Equal(ticks, source.SourceInputs.ToArray()); Assert.Equal(ticks, clean.SourceInputs.ToArray());
            Assert.Equal(contexts, source.SourceContexts.ToArray()); Assert.Equal(contexts, clean.SourceContexts.ToArray());
            Assert.Equal(updates, source.CacheUpdates.ToArray()); Assert.Equal(updates, clean.CacheUpdates.ToArray());
            Assert.Equal(commands, source.CallbackCommands.ToArray()); Assert.Equal(commands, clean.CallbackCommands.ToArray());
            Assert.Equal(ticks.Length, ticks.Select(t => t.PlayerId).Distinct().Count());
            Assert.Equal(updates.Select(u => u.PropertyIndex), p.Caches.UpdateOrder.ToArray().Where(id => updates.Any(u => u.PropertyIndex == id)));
            foreach (var c in contexts) { Assert.Equal(2, c.StateCount); Assert.Equal(new AlsActiveAnimationState(999, 2), c.GetState(0)); Assert.Equal(.4f, c.RootMotionWeight); }
            sharedFrames += source.CacheReadCount > updates.Length ? 1 : 0; zeroFrames += ticks.Length == 0 ? 1 : 0;
            players.Prepare(frame, ticks, delta, reset);
            foreach (var tick in ticks) players.Evaluate(frame, tick.PlayerId);
            var times = players.Players.ToArray(); players.Cancel(); players.Prepare(frame, ticks, delta, reset);
            Assert.Equal(times, players.Players.ToArray());
            players.Commit(frame); source.Commit(frame); machine.Commit(frame); clean.Commit(frame); cleanMachine.Commit(frame);
            counter = counter.Next((ulong)frame + 1);
        }
        Assert.True(sharedFrames > 0); Assert.True(zeroFrames > 0);
        var foreign = new AlsPoseUpdateContext(new(hz * 2, 99, 1), 1, .01f).WithUpdateCounter(counter);
        Assert.Throws<ArgumentException>(() => source.Prepare(machine, foreign, initialization, Vector4.One, new(1, 1, 1, 1)));
    }
}
