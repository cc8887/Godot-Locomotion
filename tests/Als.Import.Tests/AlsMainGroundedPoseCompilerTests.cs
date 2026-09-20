using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMainGroundedPoseCompilerTests
{
    [Fact]
    public void MainContentPreservesCacheSourcesAndActualCurvePins()
    {
        var root = Read("v4_locomotion_source_graph.json"); var sources = Sources(root);
        var states = AlsMainGroundedPoseCompiler.Compile(root.ToJsonString(), sources);
        Assert.Equal(8, states.Count);
        Assert.Equal(new[] { AlsMainGroundedPoseKind.Reference, AlsMainGroundedPoseKind.StandingCache,
            AlsMainGroundedPoseKind.CrouchingCache, AlsMainGroundedPoseKind.Source, AlsMainGroundedPoseKind.Source,
            AlsMainGroundedPoseKind.Conduit, AlsMainGroundedPoseKind.Conduit, AlsMainGroundedPoseKind.Source }, states.Select(s => s.Kind));
        Assert.Equal(new[] { -1, -1, -1, 40, 41, -1, -1, 42 }, states.Select(s => s.PlayerId));
        Assert.Equal(1, states[1].CurveOverrides["BasePose_N"]);
        Assert.Equal(1, states[2].CurveOverrides["BasePose_CLF"]); Assert.Equal(1, states[2].CurveOverrides["Weight_Crouching"]);
        Assert.Equal(1, states[7].CurveOverrides["FootLock_L"]); Assert.Equal(1, states[7].CurveOverrides["FootLock_R"]);
        var graph = MainGraph(root, "(N) Standing");
        var modifier = graph["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_ModifyCurve")!;
        modifier["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "CurveValues_0")!["value"] = "0.25";
        var changed = AlsMainGroundedPoseCompiler.Compile(root.ToJsonString(), sources);
        Assert.Equal(.25f, changed[1].CurveOverrides["BasePose_N"]); Assert.Equal(1, states[1].CurveOverrides["BasePose_N"]);
    }

    [Fact]
    public void QuickFeetMatchesNativeEntryWeightsIncludingEndpointsAndMapsPhysicalBones()
    {
        var root = Read("v4_grounded_dependencies.json"); var skeleton = Skeleton();
        var profile = AlsGroundedPoseDependencyCompiler.Compile(root.ToJsonString(), skeleton);
        var native = root["quickFeet"]!; var entries = native["entries"]!.AsArray(); var checks = 0;
        Assert.Equal(skeleton.PhysicalBones.Length, profile.QuickFeetFactors.Length);
        Assert.Equal(15, profile.QuickFeetEntries.ToArray().Count(x => x));
        Assert.Equal(79, profile.QuickFeetLogicalFactors.Length);
        Assert.Equal(15, profile.QuickFeetLogicalEntries.ToArray().Count(x => x));
        foreach (var row in native["nativeCases"]!.AsArray())
        for (var entry = 0; entry < entries.Count; entry++)
        {
            var bone = skeleton.GetPhysicalBoneId(entries[entry]!["bone"]!.GetValue<string>());
            var weights = profile.QuickFeetWeights(bone, row!["alpha"]!.GetValue<float>());
            Near(row["incoming"]![entry]!.GetValue<float>(), weights.X);
            Near(row["outgoing"]![entry]!.GetValue<float>(), weights.Y); checks++;
            var logical = skeleton.GetLogicalBoneId(entries[entry]!["bone"]!.GetValue<string>());
            Assert.Equal(weights, profile.QuickFeetLogicalWeights(logical, row["alpha"]!.GetValue<float>()));
        }
        Assert.Equal(495, checks);
        var pelvis = skeleton.GetPhysicalBoneId("pelvis"); Assert.False(profile.QuickFeetEntries[pelvis]);
        Assert.Equal(new System.Numerics.Vector2(.25f, .75f), profile.QuickFeetWeights(pelvis, .25f));
        for (var bone = 68; bone < 79; bone++)
        {
            Assert.False(profile.QuickFeetLogicalEntries[bone]);
            Assert.Equal(1, profile.QuickFeetLogicalFactors[bone]);
            Assert.Equal(new System.Numerics.Vector2(.25f, .75f), profile.QuickFeetLogicalWeights(bone, .25f));
        }
        Assert.Equal(0, profile.ChangeStance(0)); Assert.Equal(.4f, profile.ChangeStance(.4f)); Assert.Equal(1, profile.ChangeStance(1));
        Assert.Equal(2.7303076f, profile.ChangeStanceKeys[1].ArriveTangent);
        Assert.Equal(2.7303076f, profile.ChangeStanceKeys[1].LeaveTangent);
        Assert.NotEqual(.5f, profile.ChangeStance(.5f));
        for (var i = 0; i <= 1000; i++) Assert.InRange(profile.ChangeStance(i / 1000f), 0, 1);
    }

    [Theory]
    [InlineData("cache")]
    [InlineData("curve-mode")]
    [InlineData("curve-name")]
    [InlineData("curve-link")]
    [InlineData("pose-link")]
    [InlineData("disconnected")]
    [InlineData("function")]
    [InlineData("nan")]
    public void MainContentRejectsUnsupportedGraphChanges(string mutation)
    {
        var root = Read("v4_locomotion_source_graph.json"); var sources = Sources(root);
        var graph = MainGraph(root, "(N) Standing"); var nodes = graph["nodes"]!.AsArray();
        var modifier = nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_ModifyCurve")!;
        var cache = nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_UseCachedPose")!;
        var pin = modifier["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "CurveValues_0")!;
        switch (mutation)
        {
            case "cache": cache["properties"]!["NameOfCache"] = "(CLF) Locomotion States"; break;
            case "curve-mode": modifier["properties"]!["Node"]!["applyMode"] = "Add"; break;
            case "curve-name": modifier["properties"]!["Node"]!["curveNames"]![0] = "Weight_Standing"; break;
            case "curve-link": pin["links"]!.AsArray().Add(new JsonObject { ["node"] = "Other", ["pin"] = "Value" }); break;
            case "pose-link": modifier["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "SourcePose")!["links"]![0]!["pin"] = "Other"; break;
            case "disconnected": var other = cache.DeepClone(); other["name"] = "Other"; nodes.Add(other); break;
            case "function": modifier["properties"]!["Node"]!["updateFunction"]!["functionName"] = "Other"; break;
            case "nan": pin["value"] = "NaN"; break;
        }
        Assert.Throws<FormatException>(() => AlsMainGroundedPoseCompiler.Compile(root.ToJsonString(), sources));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("mode")]
    [InlineData("skeleton")]
    [InlineData("parent")]
    [InlineData("entry")]
    [InlineData("scale")]
    [InlineData("curve-path")]
    [InlineData("weighted-tangent")]
    [InlineData("curve-order")]
    public void DependencyCompilerRejectsInvalidProvenanceAndUnsupportedPolicies(string mutation)
    {
        var root = Read("v4_grounded_dependencies.json"); var profile = root["quickFeet"]!;
        var curve = root["changeStanceCurve"]!["curve"]!;
        switch (mutation)
        {
            case "schema": root["groundedSourceSchemaVersion"] = 2; break;
            case "mode": profile["mode"] = 0; break;
            case "skeleton": profile["skeleton"] = "Other"; break;
            case "parent": profile["bones"]![1]!["parent"] = 7; break;
            case "entry": profile["entries"]![0]!["bone"] = "pelvis"; break;
            case "scale": profile["bones"]![0]!["scale"] = 2; break;
            case "curve-path": root["changeStanceCurve"]!["path"] = "Other"; break;
            case "weighted-tangent": curve["keys"]![0]!["tangentWeightMode"] = "RCTWM_WeightedBoth"; break;
            case "curve-order": curve["keys"]![1]!["time"] = 0; break;
        }
        Assert.Throws<FormatException>(() => AlsGroundedPoseDependencyCompiler.Compile(root.ToJsonString(), Skeleton()));
    }
    private static void Near(float expected, float actual) => Assert.InRange(MathF.Abs(expected - actual), 0, .000001f);
    private static JsonNode MainGraph(JsonNode root, string name) => root["graphs"]!.AsArray().Single(g =>
        g!["name"]!.GetValue<string>() == name && g["path"]!.GetValue<string>().Contains(".Main Grounded States."))!;
    private static JsonNode Read(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name)))!;
    private static AlsSkeletonDefinition Skeleton()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        return set.Skeletons[AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set).SkeletonId];
    }
    private static AlsLocomotionSourceProfile Sources(JsonNode root)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        return AlsLocomotionSourceCompiler.Compile(root.ToJsonString(), set, AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set).SkeletonId);
    }
}
