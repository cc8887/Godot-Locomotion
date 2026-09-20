using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsBasePoseRetargetCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsBasePosesDefinition> BasePoses = new(() =>
        AlsBasePosesCompiler.Compile(Read("assets/config/v4_layering_inputs.json"), Set.Value,
            Array.FindIndex(Set.Value.Skeletons, s => s.AssetId == "b5b52715012cad50bf7a625ddf01e4335bb4fcf0")));

    [Fact]
    public void CompilesActualNativeDefaultsModesTrackCoverageAndSeparateEvaluatorIdentities()
    {
        var definitions = Compile(ReadInput()); Assert.Equal(2, definitions.Length);
        for (var source = 0; source < 2; source++)
        {
            var definition = definitions[source]; Assert.Equal(BasePoses.Value.Evaluators[source], definition.Evaluator);
            Assert.Equal(79, definition.LogicalCount); Assert.Equal(68, definition.PhysicalToLogical.Length);
            Assert.Equal(59, definition.Modes.ToArray().Count(m => m == AlsBasePoseTranslationRetargetMode.Skeleton));
            Assert.Equal(9, definition.Modes.ToArray().Count(m => m == AlsBasePoseTranslationRetargetMode.Animation));
            Assert.All(definition.Tracked.ToArray(), Assert.True);
            Assert.Equal(Enumerable.Range(0, 68), definition.PhysicalToLogical.ToArray());
        }
        Assert.NotEqual(definitions[0].Evaluator.NodeIndex, definitions[1].Evaluator.NodeIndex);
    }

    [Fact]
    public void ExplicitAnimationDefaultsAreEquivalentToNativeOmission()
    {
        var root = JsonNode.Parse(ReadInput())!;
        root["skeletonText"] = root["skeletonText"]!.GetValue<string>().Replace("   BoneTree(67)=()",
            "   BoneTree(0)=(TranslationRetargetingMode=Animation)\n   BoneTree(67)=(TranslationRetargetingMode=Animation)", StringComparison.Ordinal);
        foreach (var asset in root["assets"]!.AsArray())
            asset!["nativeText"] = Insert(asset["nativeText"]!.GetValue<string>(), "   RetargetSource=None\n");
        Assert.Equal(2, Compile(root.ToJsonString()).Length);
    }

    [Theory]
    [InlineData("schema")] [InlineData("skeleton-source")] [InlineData("skeleton-class")]
    [InlineData("skeleton-owner")] [InlineData("skeleton-mode")] [InlineData("scaled-mode")]
    [InlineData("unknown-mode-field")] [InlineData("missing-array-end")] [InlineData("foreign-index")]
    [InlineData("duplicate-mode")] [InlineData("compatible-skeleton")]
    [InlineData("asset-source")] [InlineData("asset-owner")] [InlineData("asset-class")]
    [InlineData("named-source")] [InlineData("source-mesh")] [InlineData("source-reference")]
    [InlineData("future-retarget-field")] [InlineData("missing-track")]
    [InlineData("duplicate-track")] [InlineData("virtual-track")] [InlineData("curve")]
    public void RejectsChangedNativePolicyProvenanceAndSourceCoverage(string mutation)
    {
        var root = JsonNode.Parse(ReadInput())!; var asset = root["assets"]![0]!;
        switch (mutation)
        {
            case "schema": root["schemaVersion"] = 2; break;
            case "skeleton-source": root["skeletonSource"] = "/OtherSkeleton"; break;
            case "skeleton-class": SkeletonReplace(root, "Class=/Script/Engine.Skeleton", "Class=/Script/Engine.SkeletalMesh"); break;
            case "skeleton-owner": SkeletonReplace(root, "ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton", "ALS_Mannequin_Skeleton.Other"); break;
            case "skeleton-mode": SkeletonReplace(root, "BoneTree(2)=(TranslationRetargetingMode=Skeleton)", "BoneTree(2)=()"); break;
            case "scaled-mode": SkeletonReplace(root, "BoneTree(2)=(TranslationRetargetingMode=Skeleton)", "BoneTree(2)=(TranslationRetargetingMode=AnimationScaled)"); break;
            case "unknown-mode-field": SkeletonReplace(root, "BoneTree(2)=(TranslationRetargetingMode=Skeleton)", "BoneTree(2)=(TranslationRetargetingMode=Skeleton,Unknown=1)"); break;
            case "missing-array-end": SkeletonReplace(root, "   BoneTree(67)=()", ""); break;
            case "foreign-index": SkeletonReplace(root, "BoneTree(67)=()", "BoneTree(79)=()"); break;
            case "duplicate-mode": SkeletonReplace(root, "   BoneTree(67)=()", "   BoneTree(67)=()\n   BoneTree(67)=()"); break;
            case "compatible-skeleton": root["skeletonText"] = Insert(root["skeletonText"]!.GetValue<string>(), "   CompatibleSkeletons(0)=Other\n"); break;
            case "asset-source": asset["source"] = "/OtherPose"; break;
            case "asset-owner": AssetReplace(asset, "Skeleton.ALS_Mannequin_Skeleton", "Skeleton.Other"); break;
            case "asset-class": AssetReplace(asset, "Class=/Script/Engine.AnimSequence ", "Class=/Script/Engine.AnimComposite "); break;
            case "named-source": AssetInsert(asset, "   RetargetSource=Other\n"); break;
            case "source-mesh": AssetInsert(asset, "   RetargetSourceAsset=Other\n"); break;
            case "source-reference": AssetInsert(asset, "   RetargetSourceAssetReferencePose(0)=()\n"); break;
            case "future-retarget-field": AssetInsert(asset, "   RetargetUnknownPolicy=True\n"); break;
            case "missing-track": asset["tracks"]!.AsArray().RemoveAt(0); break;
            case "duplicate-track": asset["tracks"]![0] = asset["tracks"]![1]!.DeepClone(); break;
            case "virtual-track": asset["tracks"]![0] = "VB Curves"; break;
            case "curve": asset["curveNames"]!.AsArray().Add("BasePose_N"); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.ThrowsAny<Exception>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void RuntimeUsesSourceModesAndReferenceFormulaWithoutOracleLookup()
    {
        var definitions = Compile(ReadInput());
        using var fixture = JsonDocument.Parse(Read("tests/Als.Core.Tests/Fixtures/P3/v4_logical_pose_native.json"));
        foreach (var definition in definitions)
        {
            var asset = fixture.RootElement.GetProperty("assets").EnumerateArray()
                .Single(a => a.GetProperty("source").GetString() == definition.Evaluator.AssetPath);
            var raw = asset.GetProperty("rawPoseWithoutRetarget").EnumerateArray().Select(Pose).ToArray();
            var expected = asset.GetProperty("pose").EnumerateArray().Select(Pose).ToArray();
            var reference = asset.GetProperty("reference").EnumerateArray().Select(Pose).ToArray();
            var model = new AlsBasePoseRetargetModel(definition, reference); model.Apply(raw);
            for (var bone = 0; bone < raw.Length; bone++)
            {
                Assert.InRange(Vector3.Distance(raw[bone].Position, expected[bone].Position), 0, .00002f);
                Assert.InRange(1 - MathF.Abs(Quaternion.Dot(raw[bone].Rotation, expected[bone].Rotation)), -.000002f, .000002f);
                Assert.InRange(Vector3.Distance(raw[bone].Scale, expected[bone].Scale), 0, .000002f);
            }
            // Changing a new sampled key must flow through Animation mode; no baked
            // expected output can reproduce this arbitrary source translation.
            raw[0] = raw[0] with { Position = new(700, 800, 900) }; model.Apply(raw);
            Assert.Equal(new Vector3(700, 800, 900), raw[0].Position);
        }
    }

    private static AlsBasePoseRetargetDefinition[] Compile(string json) => AlsBasePoseRetargetCompiler.Compile(json, Set.Value, BasePoses.Value);
    private static string ReadInput() => Read("assets/config/v4_base_poses_inputs.json");
    private static string Read(string path) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), path));
    private static string Insert(string native, string property)
    {
        var end = native.LastIndexOf("End Object", StringComparison.Ordinal); Assert.True(end >= 0);
        return native.Insert(end, property);
    }
    private static void AssetInsert(JsonNode asset, string property) => asset["nativeText"] = Insert(asset["nativeText"]!.GetValue<string>(), property);
    private static void SkeletonReplace(JsonNode root, string before, string after)
    { var value = root["skeletonText"]!.GetValue<string>(); Assert.Contains(before, value, StringComparison.Ordinal); root["skeletonText"] = value.Replace(before, after, StringComparison.Ordinal); }
    private static void AssetReplace(JsonNode asset, string before, string after)
    { var value = asset["nativeText"]!.GetValue<string>(); Assert.Contains(before, value, StringComparison.Ordinal); asset["nativeText"] = value.Replace(before, after, StringComparison.Ordinal); }
    private static AlsLocalPose Pose(JsonElement atom)
    {
        var p = atom.GetProperty("position"); var q = atom.GetProperty("rotation"); var s = atom.GetProperty("scale");
        return new(new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
            new(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()), new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
}
