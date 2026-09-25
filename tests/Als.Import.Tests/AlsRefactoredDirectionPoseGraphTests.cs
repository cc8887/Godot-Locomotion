using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredDirectionPoseGraphTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalStatesKeepDirectionalCacheOrderAndCallbackIdentity(bool crouching)
    {
        var catalog = Catalog();
        var resources = new AlsRefactoredDirectionResources(MantlingHostFixture.Read("refactored_stance_machines"), catalog, crouching);
        var graph = new AlsRefactoredDirectionPoseGraph(catalog, resources);
        Assert.Equal(6, graph.States.Length); Assert.Equal(6, graph.Caches.Length);
        var caches = graph.Caches.ToArray().ToDictionary(c => c.PropertyIndex);
        string[][] expected = [
            ["Forward", "Backward", "Left Forward", "Right Forward"],
            ["Forward", "Backward", "Left Backward", "Right Backward"],
            ["Forward", "Backward", "Left Backward", "Right Forward"],
            ["Forward", "Backward", "Left Forward", "Right Backward"],
            ["Forward", "Backward", "Left Forward", "Right Backward"],
            ["Forward", "Backward", "Left Backward", "Right Forward"]];
        for (var s = 0; s < 6; s++)
        {
            Assert.Equal(resources.States[s].RootPropertyIndex, graph.States[s].RootPropertyIndex);
            Assert.Equal(expected[s].Select(n => "Move " + n), graph.States[s].CachePropertyIndices.ToArray().Select(id => caches[id].Name));
        }
        Assert.Equal(24, graph.States.ToArray().SelectMany(s => s.ReadPropertyIndices.ToArray()).Distinct().Count());
        var players = new AlsRefactoredMovementPlayers(catalog, crouching).Players.ToArray().Select(p => p.PropertyIndex).ToHashSet();
        // Forward standing is a composed cache, not a seventh independent player.
        Assert.Equal(crouching ? 6 : 5, graph.Caches.ToArray().Count(c => players.Contains(c.SourcePropertyIndex)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsChangedBindingsPoliciesAndRuntimeOrAuthoredLinks(bool crouching)
    {
        var catalog = Catalog();
        var resources = new AlsRefactoredDirectionResources(MantlingHostFixture.Read("refactored_stance_machines"), catalog, crouching);
        var graph = new AlsRefactoredDirectionPoseGraph(catalog, resources); var state = graph.States[0];
        var original = catalog.Read(AlsRefactoredRotatePlayers.Blueprint(crouching));
        foreach (var mutation in new[] { "normalize", "additive", "alpha", "curve", "binding", "yaw", "cache", "link", "callback", "writer" })
        {
            var changed = JsonNode.Parse(original.GetRawText())!;
            JsonNode Node(int id) => changed["compiled"]!["nodes"]!.AsArray().Single(n => n!["propertyIndex"]!.GetValue<int>() == id)!;
            if (mutation == "normalize") Node(state.BlendPropertyIndex)["runtime"]!["bNormalizeAlpha"] = false;
            if (mutation == "additive") Node(state.BlendPropertyIndex)["authoredProperties"]!["Node"]!["bAdditiveNode"] = true;
            if (mutation == "alpha") Node(state.CurvePropertyIndex)["runtime"]!["alpha"] = .5;
            if (mutation == "curve") Node(state.CurvePropertyIndex)["runtime"]!["curveNames"]![0] = "Wrong";
            if (mutation == "binding") changed["nativeText"] = changed["nativeText"]!.GetValue<string>().Replace("\"VelocityBlend\",\"ForwardAmount\"", "\"VelocityBlend\",\"BackwardAmount\"", StringComparison.Ordinal);
            if (mutation == "yaw") changed["nativeText"] = changed["nativeText"]!.GetValue<string>().Replace("\"RotationYawOffsets\",\"ForwardAngle\"", "\"RotationYawOffsets\",\"LeftAngle\"", StringComparison.Ordinal);
            if (mutation == "cache") Node(state.ReadPropertyIndices[0])["runtime"]!["linkToCachingNode"]!["linkId"] = state.CachePropertyIndices[1];
            if (mutation == "link") Node(state.BlendPropertyIndex)["runtime"]!["poses"]![0]!["linkId"] = state.ReadPropertyIndices[1];
            if (mutation == "callback") Node(state.RootPropertyIndex)["runtime"]!["stateEntryFunction"]!["functionName"] = "Unknown";
            if (mutation == "writer") Node(state.CachePropertyIndices[0])["runtime"]!["pose"]!["sourceLinkId"] = -1;
            using var document = JsonDocument.Parse(changed.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredDirectionPoseGraph.Compile(document.RootElement, resources));
        }
    }
}
