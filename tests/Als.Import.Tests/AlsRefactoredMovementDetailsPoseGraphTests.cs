using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementDetailsPoseGraphTests
{
    private static readonly Lazy<(AlsRefactoredAnimationCatalog Catalog, AlsRefactoredMovementDetailsResources Resources,
        AlsRefactoredMovementDetailsPoseGraph Graph)> Fixture = new(() =>
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var resources = new AlsRefactoredMovementDetailsResources(MantlingHostFixture.Read("refactored_stance_machines"), catalog);
        return (catalog, resources, new(catalog, resources));
    });
    [Fact]
    public void SixStatesKeepSharedMovementBaseAndSeparateAdditivePlayerIdentities()
    {
        var (catalog, resources, graph) = Fixture.Value;
        Assert.Equal(new AlsRefactoredDirectionCache(67, "Movement", 122), graph.MovementCache);
        Assert.Equal(6, graph.States.Length);
        Assert.Equal(new[] { 107,110,105,98,89,80 }, graph.States.ToArray().Select(s => s.ReadPropertyIndex));
        Assert.Equal(new[] { -1,115,-1,103,94,85 }, graph.States.ToArray().Select(s => s.ApplyPropertyIndex));
        Assert.Equal(new[] { -1,-1,-1,97,88,-1 }, graph.States.ToArray().Select(s => s.CallbackPropertyIndex));
        Assert.Equal(16, graph.States.ToArray().SelectMany(s => s.PlayerPropertyIndices.ToArray()).Distinct().Count());
        for (var s = 0; s < 6; s++)
        {
            var state = graph.States[s]; Assert.Equal(resources.States[s].RootPropertyIndex, state.RootPropertyIndex);
            Assert.Equal(resources.States[s].PlayerPropertyIndices.ToArray(), state.PlayerPropertyIndices.ToArray());
            Assert.Equal(s is not (0 or 2), state.HasAdditive);
            if (state.CallbackPropertyIndex >= 0)
            {
                var callback = Assert.Single(graph.Callbacks.Nodes.ToArray(), c => c.PropertyIndex == state.CallbackPropertyIndex);
                Assert.Equal(AlsRefactoredStanceFunction.ResetPivot, callback.Function); Assert.True(callback.OnBecomeRelevant);
                Assert.Equal(state.ApplyPropertyIndex, callback.SourcePropertyIndex);
            }
        }
        var sources = resources.TimingPlayers.ToArray().Select(p => p.Source).Distinct().ToArray(); Assert.Equal(4, sources.Length);
        foreach (var source in sources) Assert.Equal(79, catalog.CompileAdditivePose(source).BoneNames.Length);
        // Cache 67 deliberately stops before its ModifyCurve/lean/direction chain.
        Assert.DoesNotContain(graph.MovementCache.SourcePropertyIndex, graph.Players.Players.ToArray().Select(p => p.PropertyIndex));
    }
    [Fact]
    public void AlteredPoseLinksBindingsAndAdditivePoliciesAreRejected()
    {
        var (catalog, resources, _) = Fixture.Value; var original = catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false));
        foreach (var change in new[] { "base", "source", "channel", "alpha", "clamp", "normalize", "additive", "binding", "callback", "state-callback", "cache", "cache-source", "cache-name", "state-index", "unconsumed" })
        {
            var json = JsonNode.Parse(original.GetRawText())!;
            JsonNode Node(int property) => json["compiled"]!["nodes"]!.AsArray().Single(n => n!["propertyIndex"]!.GetValue<int>() == property)!;
            switch (change)
            {
                case "base": Node(115)["runtime"]!["base"]!["linkId"] = 109; break;
                case "source": Node(115)["runtime"]!["additive"]!["sourceLinkId"] = -1; break;
                case "channel": Node(109)["runtime"]!["poses"]![0]!["linkId"] = 113; break;
                case "alpha": Node(115)["runtime"]!["alpha"] = .5; break;
                case "clamp": Node(115)["authoredProperties"]!["Node"]!["alphaScaleBiasClamp"]!["bInterpResult"] = true; break;
                case "normalize": Node(109)["runtime"]!["bNormalizeAlpha"] = false; break;
                case "additive": Node(109)["runtime"]!["bAdditiveNode"] = true; break;
                case "binding": json["nativeText"] = json["nativeText"]!.GetValue<string>().Replace("\"VelocityBlend\",\"ForwardAmount\"", "\"VelocityBlend\",\"BackwardAmount\"", StringComparison.Ordinal); break;
                case "callback": Node(97)["runtime"]!["callSite"] = "OnUpdate"; break;
                case "state-callback": Node(104)["runtime"]!["stateEntryFunction"]!["functionName"] = "Unknown"; break;
                case "cache": Node(110)["runtime"]!["linkToCachingNode"]!["linkId"] = 66; break;
                case "cache-source": Node(67)["runtime"]!["pose"]!["linkId"] = 201; break;
                case "cache-name": Node(67)["runtime"]!["cachePoseName"] = "Movement Details"; break;
                case "state-index": Node(116)["runtime"]!["stateIndex"] = 2; break;
                case "unconsumed": Node(118)["graph"] = Node(116)["graph"]!.DeepClone(); break;
            }
            using var doc = JsonDocument.Parse(json.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredMovementDetailsPoseGraph.Compile(doc.RootElement, resources));
        }
    }
}
