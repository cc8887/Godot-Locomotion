using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredForwardSourceTests
{
    private static readonly Lazy<(AlsRefactoredAnimationCatalog Catalog, AlsRefactoredDirectionPoseGraph Graph, AlsRefactoredForwardSource Profile)> Fixture = new(() =>
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var resources = new AlsRefactoredDirectionResources(MantlingHostFixture.Read("refactored_stance_machines"), catalog, false);
        var graph = new AlsRefactoredDirectionPoseGraph(catalog, resources);
        return (catalog, graph, new(catalog, graph));
    });

    [Fact]
    public void OriginalForwardUsesSharedBaseAndRejectsChangedSprintPolicies()
    {
        var (catalog, graph, p) = Fixture.Value;
        Assert.Equal(138, p.Cache); Assert.Equal(137, p.BaseCache);
        Assert.Equal(141, p.GaitBaseRead); Assert.Equal(140, p.BlockBaseRead);
        Assert.Equal(127, p.Players.Players[p.BasePlayer].PropertyIndex);
        Assert.Equal(129, p.Players.Players[p.SprintPlayer].PropertyIndex);
        Assert.Equal(130, p.Players.Players[p.AccelerationPlayer].PropertyIndex);
        foreach (var mutation in new[] { "range", "speed", "reset", "time", "blend", "mode", "tag", "binding", "cache", "asset" })
        {
            var json = JsonNode.Parse(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false)).GetRawText())!;
            JsonNode Node(int id) => json["compiled"]!["nodes"]!.AsArray().Single(n => n!["propertyIndex"]!.GetValue<int>() == id)!;
            if (mutation == "range") Node(131)["runtime"]!["alphaScaleBiasClamp"]!["inRange"]!["max"] = 1;
            if (mutation == "speed") Node(131)["authoredProperties"]!["BlendNode"]!["alphaScaleBiasClamp"]!["interpSpeedDecreasing"] = 20;
            if (mutation == "reset") Node(131)["runtime"]!["bResetChildOnActivation"] = false;
            if (mutation == "time") Node(144)["runtime"]!["blendTime"]![0] = .2;
            if (mutation == "blend") Node(144)["runtime"]!["blendType"] = "HermiteCubic";
            if (mutation == "mode") Node(144)["runtime"]!["childUpateMode"] = "AlwaysTickChildren";
            if (mutation == "tag") Node(144)["runtime"]!["tags"]![0]!["tagName"] = "Als.Gait.Running";
            if (mutation == "binding") json["nativeText"] = json["nativeText"]!.GetValue<string>().Replace("\"StandingState\",\"SprintBlockAmount\"", "\"StandingState\",\"SprintAccelerationAmount\"", StringComparison.Ordinal);
            if (mutation == "cache") Node(140)["runtime"]!["linkToCachingNode"]!["linkId"] = 138;
            if (mutation == "asset") Node(130)["runtime"]!["sequence"] = Node(129)["runtime"]!["sequence"]!.DeepClone();
            using var document = JsonDocument.Parse(json.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredForwardSource.Compile(document.RootElement, graph));
        }
    }

    [Fact]
    public void AccelerationResetAndHiddenHistoryFollowOriginalSubtree()
    {
        var p = Fixture.Value.Profile; var runtime = new AlsRefactoredForwardSourceRuntime(p, 7);
        var movement = new AlsRefactoredMovementPlayerInput(1, 1, 1, 1);
        runtime.Prepare(0, new("Als.Gait.Sprinting", 0, 0), movement, 0);
        Assert.Equal(0, runtime.Weights.Acceleration); Assert.Empty(runtime.BaseReads.ToArray());
        Assert.Single(runtime.SourceInputs.ToArray()); Assert.True(runtime.SourceInputs[0].Reinitialize);
        runtime.Commit(0);
        runtime.Prepare(1, new("Als.Gait.Sprinting", 0, .25f), movement, .025f);
        Assert.Equal(.5f, runtime.Weights.Acceleration); Assert.Equal(2, runtime.SourceInputs.Length);
        Assert.False(runtime.SourceInputs[0].Reinitialize); Assert.True(runtime.SourceInputs[1].Reinitialize);
        Assert.Equal(p.Players.Players[p.AccelerationPlayer].Start, runtime.SourceInputs[1].StartPosition);
        Assert.Equal(1 / p.Players.Players[p.AccelerationPlayer].RateBasis, runtime.SourceInputs[1].PlayRate);
        runtime.Commit(1);
        runtime.Prepare(2, new("Als.Gait.Sprinting", 1, 0), movement, 3);
        Assert.False(runtime.Weights.GaitUpdated); Assert.False(runtime.Weights.AccelerationUpdated);
        Assert.Empty(runtime.SourceInputs.ToArray()); Assert.Equal(p.BlockBaseRead, Assert.Single(runtime.BaseReads.ToArray()).ReadPropertyIndex);
        runtime.Commit(2);
        runtime.Prepare(3, new("Als.Gait.Sprinting", 0, 0), movement, .025f);
        Assert.Equal(.45f, runtime.Weights.Acceleration); Assert.All(runtime.SourceInputs.ToArray(), t => Assert.False(t.Reinitialize));
        runtime.Commit(3);
        runtime.Prepare(4, new("Als.Gait.Sprinting", 0, 1), movement, 1);
        Assert.Equal(1, runtime.Weights.Acceleration); runtime.Commit(4);
        runtime.Prepare(5, new("Als.Gait.Sprinting", 0, 0), movement, 1);
        Assert.Equal(0, runtime.Weights.Acceleration); Assert.True(Assert.Single(runtime.SourceInputs.ToArray()).Reinitialize);
        runtime.Commit(5);
        runtime.Prepare(6, new("Als.Gait.Sprinting", 1, .125f), movement, 1, reinitialize: true);
        Assert.Empty(runtime.SourceInputs.ToArray()); runtime.Commit(6);
        runtime.Prepare(7, new("Als.Gait.Sprinting", 0, .125f), movement, 0);
        Assert.Equal(.5f, runtime.Weights.Acceleration);
        Assert.Equal(2, runtime.SourceInputs.Length); Assert.All(runtime.SourceInputs.ToArray(), t => Assert.True(t.Reinitialize));
        runtime.Cancel();
    }

    [Fact]
    public void GaitUsesAuthoredDurationsAndKeepsTwoBaseReadContextsSeparate()
    {
        var p = Fixture.Value.Profile; var runtime = new AlsRefactoredForwardSourceRuntime(p, 0);
        var movement = new AlsRefactoredMovementPlayerInput(1, 1, 1, 1);
        runtime.Prepare(0, new("Als.Gait.Running", 0, 0), movement, 0); runtime.Commit(0);
        runtime.Prepare(1, new("Als.Gait.Sprinting", .3f, 0), movement, .1f);
        Assert.Equal(.5f, runtime.Weights.Gait.X, 6); Assert.Equal(.5f, runtime.Weights.Gait.Y, 6);
        Assert.Equal(2, runtime.BaseReads.Length);
        Assert.Equal(p.GaitBaseRead, runtime.BaseReads[0].ReadPropertyIndex); Assert.True(runtime.BaseReads[0].Inactive);
        Assert.Equal(p.BlockBaseRead, runtime.BaseReads[1].ReadPropertyIndex); Assert.False(runtime.BaseReads[1].Inactive);
        Assert.Equal(.35f, runtime.BaseReads[0].Weight, 6); Assert.Equal(.3f, runtime.BaseReads[1].Weight, 6);
        runtime.Commit(1);
        // Reverse halfway through: outgoing duration .3 is shortened to .15.
        runtime.Prepare(2, new("Als.Gait.Walking", 0, 0), movement, .075f);
        Assert.Equal(.75f, runtime.Weights.Gait.X, 6); Assert.Equal(.25f, runtime.Weights.Gait.Y, 6);
        Assert.True(Assert.Single(runtime.SourceInactive.ToArray())); runtime.Commit(2);
        runtime.Prepare(3, new("Als.Gait.Sprinting.Child", 0, 0), movement, 1);
        // ALS Find uses exact gameplay-tag equality, not parent-tag matching.
        Assert.Equal(1, runtime.Weights.Gait.X); Assert.Empty(runtime.SourceInputs.ToArray()); runtime.Cancel();
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void ContinuousRequestsRetryAndRemainBoundToSharedPlayerIdentities(int hz)
    {
        var (catalog, _, p) = Fixture.Value;
        var runtime = new AlsRefactoredForwardSourceRuntime(p, 0); var clean = new AlsRefactoredForwardSourceRuntime(p, 0);
        var index = MantlingHostFixture.Read("refactored_animation_sources");
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", path));
        var bank = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        var profiles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), index, Read);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog, bank, profiles, p.Players.Bind(0,
            new Dictionary<string, int> { ["Movement"] = 0, ["Run Start"] = 1, ["First Pivot"] = 2, ["Second Pivot"] = 3 }));
        var movement = new AlsRefactoredMovementPlayerInput(1.2f, 1, .7f, .8f);
        var doubleReads = 0; var hidden = 0; var resets = 0;
        for (var frame = 0; frame < hz * 3; frame++)
        {
            var input = new AlsRefactoredForwardInput(frame % hz < hz / 2 ? "Als.Gait.Sprinting" : "Als.Gait.Running", frame % 13 == 0 ? 1 : .3f, (frame % 19) / 18f);
            var delta = frame % 23 == 0 ? 0 : 1f / hz; var reset = frame == hz;
            runtime.Prepare(frame, input, movement, delta, reinitialize: reset);
            var weights = runtime.Weights; var reads = runtime.BaseReads.ToArray(); var ticks = runtime.SourceInputs.ToArray();
            runtime.Cancel(); runtime.Prepare(frame, input, movement, delta, reinitialize: reset);
            clean.Prepare(frame, input, movement, delta, reinitialize: reset);
            Assert.Equal(weights, runtime.Weights); Assert.Equal(weights, clean.Weights);
            Assert.Equal(reads, runtime.BaseReads.ToArray()); Assert.Equal(reads, clean.BaseReads.ToArray());
            Assert.Equal(ticks, runtime.SourceInputs.ToArray()); Assert.Equal(ticks, clean.SourceInputs.ToArray());
            Assert.Equal(clean.SourceInactive.ToArray(), runtime.SourceInactive.ToArray());
            Assert.InRange(reads.Sum(r => r.Weight) + ticks.Sum(t => t.Weight), .999998f, 1.000002f);
            doubleReads += reads.Length == 2 ? 1 : 0; hidden += !weights.GaitUpdated ? 1 : 0; resets += ticks.Count(t => t.Reinitialize);
            // Only sprint sources are submitted here. Base requests must be
            // resolved by the enclosing cache scheduler alongside other readers.
            players.Prepare(frame, ticks, delta);
            foreach (var tick in ticks) players.Evaluate(frame, tick.PlayerId);
            var times = ticks.Select(t => players.Players.ToArray().Single(v => v.PlayerId == t.PlayerId).Time).ToArray();
            players.Cancel(); players.Prepare(frame, ticks, delta);
            Assert.Equal(times, ticks.Select(t => players.Players.ToArray().Single(v => v.PlayerId == t.PlayerId).Time).ToArray());
            players.Commit(frame); runtime.Commit(frame); clean.Commit(frame);
        }
        Assert.True(doubleReads > 0); Assert.True(hidden > 0); Assert.True(resets > 0);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(hz * 3, new("Als.Gait.Sprinting", float.NaN, 0), movement, .01f));
    }
}
