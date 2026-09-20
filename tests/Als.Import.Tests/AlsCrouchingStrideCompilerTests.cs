using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingStrideCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.Compile(Read(), Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));

    [Fact]
    public void CompilesRealWalkPoseIdentityLayerAndFilter()
    {
        var profile = Compile(Read());
        Assert.Equal(Sources.Value.SkeletonId, profile.SkeletonId); Assert.Equal(54, profile.WalkPosePlayerId);
        Assert.Equal("(CLF) CycleBlending", profile.DirectionLayer); Assert.Equal(new(10, 10), profile.Settings);
    }

    [Fact]
    public void ReadsDynamicAlphaInsteadOfSerializedDefaultAndKeepsFilterSpeeds()
    {
        var graph = JsonNode.Parse(Read())!; var node = Blend(graph); var data = node["properties"]!["BlendNode"]!;
        data["alpha"] = .8f; data["alphaScaleBiasClamp"]!["interpSpeedIncreasing"] = 7;
        data["alphaScaleBiasClamp"]!["interpSpeedDecreasing"] = 13;
        Pin(node, "Alpha")["value"] = "0.9";
        Assert.Equal(new AlsCrouchingStrideSettings(7, 13), Compile(graph.ToJsonString()).Settings);
    }

    [Theory]
    [InlineData("type")] [InlineData("reset")] [InlineData("always-update")] [InlineData("filter")]
    [InlineData("clamp")] [InlineData("map")] [InlineData("bias")] [InlineData("alpha-link")]
    [InlineData("alpha-owner")] [InlineData("reverse")] [InlineData("time")]
    [InlineData("layer")] [InlineData("lifecycle")] [InlineData("extra-pin")]
    public void RejectsUnsupportedStrideSubgraphs(string mutation)
    {
        var graph = JsonNode.Parse(Read())!; var node = Blend(graph); var data = node["properties"]!["BlendNode"]!;
        var nodes = Content(graph)["nodes"]!.AsArray();
        switch (mutation)
        {
            case "type": data["alphaInputType"] = "Curve"; break;
            case "reset": data["bResetChildOnActivation"] = true; break;
            case "always-update": data["bAlwaysUpdateChildren"] = true; break;
            case "filter": data["alphaScaleBiasClamp"]!["bInterpResult"] = false; break;
            case "clamp": data["alphaScaleBiasClamp"]!["bClampResult"] = true; break;
            case "map": data["alphaScaleBiasClamp"]!["bMapRange"] = true; break;
            case "bias": data["alphaScaleBias"]!["bias"] = .1f; break;
            case "alpha-link": Pin(node, "Alpha")["links"]![0]!["pin"] = "Other"; break;
            case "alpha-owner": var getter = nodes.Single(n => n!["name"]!.GetValue<string>() == Pin(node, "Alpha")["links"]![0]!["node"]!.GetValue<string>())!;
                getter["properties"]!["VariableReference"]!["bSelfContext"] = false; break;
            case "reverse": var first = Pin(node, "A")["links"]!.DeepClone();
                Pin(node, "A")["links"] = Pin(node, "B")["links"]!.DeepClone(); Pin(node, "B")["links"] = first; break;
            case "time": var evaluator = nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_SequenceEvaluator")!;
                Pin(evaluator, "ExplicitTime")["value"] = "0.1"; break;
            case "layer": nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_LinkedAnimLayer")!["properties"]!["Node"]!["layer"] = "Other"; break;
            case "lifecycle": data["updateFunction"]!["functionName"] = "Other"; break;
            case "extra-pin": var extra = Pin(node, "AlphaCurveName").DeepClone(); extra["name"] = "Other"; node["pins"]!.AsArray().Add(extra); break;
        }
        Assert.Throws<FormatException>(() => Compile(graph.ToJsonString()));
    }

    private static JsonNode Content(JsonNode graph) => graph["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>()
        .EndsWith(".(CLF) Locomotion Cycles.AnimStateNode_0.(CLF) Locomotion Cycles"))!;
    private static JsonNode Blend(JsonNode graph) => Content(graph)["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_TwoWayBlend")!;
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
    private static AlsCrouchingStrideProfile Compile(string graph) => AlsCrouchingStrideCompiler.Compile(graph, Sources.Value, Set.Value);
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_grounded_dependencies.json"));
}
