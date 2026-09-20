using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsFallingLeanSamplingTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.CompileWithMovement(
        Read("v4_main_movement_graph.json"), Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));
    private static readonly Lazy<AlsLeanSamplingProfile> Fall = new(() => Compile(Read(), 260));
    private static readonly Lazy<AlsLeanSamplingProfile> Jump = new(() => Compile(Read(), 282));

    [Fact]
    public void RetainsSeparateFallAndJumpSourcesAndTheActualFallingReferencePose()
    {
        Assert.Equal(67, Fall.Value.PlayerId); Assert.Equal(68, Jump.Value.PlayerId);
        Assert.Equal(93, Fall.Value.SampleStart); Assert.Equal(98, Jump.Value.SampleStart);
        Assert.Equal(Fall.Value.BaseAnimationId, Jump.Value.BaseAnimationId);
        Assert.Equal("ALS_N_FallLoop", Set.Value.Animations[Fall.Value.BaseAnimationId].Name);
        Assert.NotSame(Fall.Value.Runtime, Jump.Value.Runtime);
    }

    [Theory]
    [InlineData(260)] [InlineData(282)]
    public void MatchesEveryNativeStaticPointIncludingClampedEdgesAndSampleOrder(int node)
    {
        using var document = JsonDocument.Parse(Read()); var root = document.RootElement;
        Assert.Equal(9, root.GetProperty("gridSamples").GetArrayLength());
        var rows = root.GetProperty("staticSamples"); Assert.Equal(441, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray()) Compare(row, node == 260 ? Fall.Value : Jump.Value);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void MatchesContinuousNativeInputsForBothIdentitiesWithoutExtraFiltering(int hz)
    {
        using var document = JsonDocument.Parse(Read());
        var frames = document.RootElement.GetProperty("runs").EnumerateArray()
            .Single(r => r.GetProperty("hz").GetInt32() == hz).GetProperty("frames");
        Assert.Equal(hz * 4, frames.GetArrayLength());
        foreach (var row in frames.EnumerateArray())
        {
            Assert.Equal(row.GetProperty("x").GetDouble(), row.GetProperty("filteredX").GetDouble());
            Assert.Equal(row.GetProperty("y").GetDouble(), row.GetProperty("filteredY").GetDouble());
            Compare(row, Fall.Value); Compare(row, Jump.Value);
        }
    }

    [Theory]
    [InlineData("ground-grid")] [InlineData("ground-asset")] [InlineData("ground-base")]
    [InlineData("grid-order")] [InlineData("grid-weight")] [InlineData("grid-index")]
    [InlineData("axis-filter")] [InlineData("sample-filter")] [InlineData("per-bone")]
    [InlineData("mesh-space")] [InlineData("sample-identity")] [InlineData("base-frame")]
    [InlineData("source-identity")]
    public void RejectsAnExportThatDoesNotDescribeTheSupportedAirGraph(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        switch (mutation)
        {
            case "ground-grid": root["axes"]![0]!["divisions"] = 4; break;
            case "ground-asset": root["objectPath"] = JsonNode.Parse(Read("v4_lean_sampling.json"))!["objectPath"]!.GetValue<string>(); break;
            case "ground-base": root["samples"]![0]!["basePath"] = "ALS_N_Run_BasePose"; break;
            case "grid-order": root["gridSamples"]![0]!["x"] = 1; break;
            case "grid-weight": root["gridSamples"]![0]!["weights"]![0] = 2; break;
            case "grid-index": root["gridSamples"]![0]!["indices"]![0] = 5; break;
            case "axis-filter": root["axes"]![0]!["seconds"] = .1; break;
            case "sample-filter": root["sampleWeightSpeed"] = 1; break;
            case "per-bone": root["PerBoneBlendProfile"] = "Other"; break;
            case "mesh-space": root["allowMeshSpace"] = true; break;
            case "sample-identity": root["samples"]![0]!["index"] = 1; break;
            case "base-frame": root["samples"]![0]!["baseFrame"] = 1; break;
            case "source-identity": root["source"] = "UE UBlendSpace::FilterInput + UpdateBlendSamples; ALS V4 Lean"; break;
        }
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString(), 260));
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString(), 282));
    }

    [Fact]
    public void CannotBindAnAirAssetToAnUnrelatedOrMissingPlaybackNode()
    {
        Assert.Throws<FormatException>(() => Compile(Read(), 251));
        Assert.Throws<FormatException>(() => AlsLeanSamplingCompiler.Compile(Read(), Sources.Value, Set.Value, AlsLocomotionSourceDomain.Cycle));
        var grounded = AlsLocomotionSourceCompiler.Compile(Read("v4_grounded_dependencies.json"), Set.Value, Sources.Value.SkeletonId);
        Assert.Throws<FormatException>(() => AlsLeanSamplingCompiler.CompileFalling(Read(), grounded, Set.Value, 260));
        Assert.Throws<ArgumentException>(() => new AlsLeanBlendSpace(new AlsLeanGridVertex[27]));
        Assert.Throws<ArgumentException>(() => new AlsLeanBlendSpace(new AlsLeanGridVertex[75], 2));
    }

    private static void Compare(JsonElement row, AlsLeanSamplingProfile profile)
    {
        var weights = new float[5]; var order = new int[5]; var again = new float[5]; var againOrder = new int[5];
        var input = new Vector2(row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle());
        var count = profile.Runtime.Evaluate(input, weights, order);
        Assert.Equal(row.GetProperty("order").EnumerateArray().Select(v => v.GetInt32()), order[..count]);
        for (var i = 0; i < 5; i++) Assert.Equal(row.GetProperty("weights")[i].GetSingle(), weights[i]);
        Assert.Equal(count, profile.Runtime.Evaluate(input, again, againOrder));
        Assert.Equal(weights, again); Assert.Equal(order, againOrder);
    }

    private static AlsLeanSamplingProfile Compile(string json, int node) => AlsLeanSamplingCompiler.CompileFalling(json, Sources.Value, Set.Value, node);
    private static string Read(string name = "v4_falling_lean_sampling.json") => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config", name));
}
