using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsAimSamplingCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Manifest = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsAimPoseDefinition> Graph = new(AlsAimPoseCompilerTests.Model);
    private static string Read(string name) => AlsAimPoseCompilerTests.Read(name);
    private static AlsAimSamplingProfile Compile(string? json = null) =>
        AlsAimSamplingCompiler.Compile(json ?? Read("v4_aim_sampling.json"), Graph.Value, Manifest.Value);

    [Fact]
    public void NativeGridAndFilterOracleCompilesWithAllMeshSpaceAdditiveReferences()
    {
        var profile = Compile(); var set = Manifest.Value;
        Assert.Equal(3, profile.AnimationIds.Length); Assert.Equal(64, profile.BindingDigest.Length);
        Assert.Equal("ALS_N_Pose", set.Animations[profile.BaseAnimationId].Name);
        Assert.Equal(new[] { "ALS_N_Look_F_Sweep", "ALS_N_Look_D_Sweep", "ALS_N_Look_U_Sweep" },
            profile.AnimationIds.ToArray().Select(id => set.Animations[id].Name));
        Assert.All(profile.AnimationIds.ToArray(), id => Assert.Equal(2, set.Animations[id].AdditiveType));
        // Native oracle includes 253 static inputs and 840 continuous filter/cache updates.
        Span<float> weights = stackalloc float[3]; Span<int> order = stackalloc int[3];
        Assert.Equal(2, profile.Runtime.Evaluate(-45, weights, order));
        Assert.Equal(new[] { 1, 0, -1 }, order.ToArray()); Assert.Equal(new[] { .5f, .5f, 0 }, weights.ToArray());
        Assert.Equal(2, profile.Runtime.Evaluate(45, weights, order));
        Assert.Equal(new[] { 0, 2, -1 }, order.ToArray()); Assert.Equal(new[] { .5f, 0, .5f }, weights.ToArray());
        Assert.Equal(1, profile.Runtime.Evaluate(120, weights, order)); Assert.Equal(2, order[0]);
        Assert.Throws<ArgumentException>(() => profile.Runtime.Evaluate(float.NaN, new float[3], new int[3]));
    }

    [Fact]
    public void AimRawSourceBankIncludesTheRealBasePoseAndKeepsSevenPlayerIdentities()
    {
        var profile = Compile(); var bank = AlsRawAnimationSourceCompiler.Compile(Read("v4_aim_source_inputs.json"), Manifest.Value,
            profile.BindingDigest, AlsAimSamplingProfile.PlayerCount, AlsAimSamplingProfile.SampleCount, profile.AnimationIds,
            relative => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", relative)));
        Assert.Equal(4, bank.Sources.Length); Assert.Equal(7, bank.PlayerCount); Assert.Equal(17, bank.SampleCount);
        Assert.Equal(79, bank.GetSkeleton(profile.SkeletonId).LogicalBoneCount);
        Assert.Equal(68, bank.GetSkeleton(profile.SkeletonId).PhysicalBoneCount);
        foreach (var id in profile.AnimationIds)
        {
            var source = bank.GetSource(id);
            Assert.Equal(31, source.PoseData.SampledKeyCount);
            Assert.Equal(AlsRawAnimationAdditiveType.RotationOffsetMeshSpace, source.Policy.AdditiveType);
            Assert.Equal(profile.BaseAnimationId, source.Policy.BaseAnimationId);
        }
        Assert.Equal(AlsRawAnimationAdditiveType.None, bank.GetSource(profile.BaseAnimationId).Policy.AdditiveType);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ResourceSharingPreservesGraphBindingsAndRejectsChangedRawContent(bool changed)
    {
        var profile = Compile(); var originalJson = Read("v4_aim_source_inputs.json");
        byte[] ReadFile(string path) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", path));
        var original = AlsRawAnimationSourceCompiler.Compile(originalJson, Manifest.Value, profile.BindingDigest,
            AlsAimSamplingProfile.PlayerCount, AlsAimSamplingProfile.SampleCount, profile.AnimationIds, ReadFile);
        var index = JsonNode.Parse(originalJson)!; index["request"]!["bindingDigest"] = "another_graph";
        var asset = index["assets"]![0]!; var file = asset["file"]!.GetValue<string>(); var bytes = ReadFile(file);
        if (changed)
        {
            var source = JsonNode.Parse(bytes)!; var position = source["tracks"]![0]!["positions"]![0]!;
            position[0] = position[0]!.GetValue<double>() + 1;
            bytes = System.Text.Encoding.UTF8.GetBytes(source.ToJsonString());
            asset["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }
        var existing = AlsRawAnimationSourceCompiler.Compile(index.ToJsonString(), Manifest.Value, "another_graph",
            AlsAimSamplingProfile.PlayerCount, AlsAimSamplingProfile.SampleCount, profile.AnimationIds, path => path == file ? bytes : ReadFile(path));
        if (changed) { Assert.Throws<ArgumentException>(() => original.ReuseResourcesFrom(existing)); return; }
        var shared = original.ReuseResourcesFrom(existing);
        Assert.Equal(original.BindingDigest, shared.BindingDigest); Assert.NotEqual(existing.BindingDigest, shared.BindingDigest);
        foreach (var source in shared.Sources)
        {
            Assert.Same(existing.GetSource(source.PoseData.Identity.AnimationId), source);
            Assert.NotSame(original.GetSource(source.PoseData.Identity.AnimationId), source);
        }
        Assert.Same(existing.GetSkeleton(profile.SkeletonId), shared.GetSkeleton(profile.SkeletonId));
    }

    [Theory]
    [InlineData("source")] [InlineData("grid")] [InlineData("sample_smoothing")] [InlineData("axis_smoothing")]
    [InlineData("mesh_space")] [InlineData("scale_time")] [InlineData("base_frame")] [InlineData("sample_rate")]
    [InlineData("grid_order")] [InlineData("grid_weight")] [InlineData("oracle_weight")] [InlineData("oracle_order")]
    [InlineData("filter_oracle")] [InlineData("sample_asset")]
    public void RejectsChangedSamplingAndSourceSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_aim_sampling.json"))!;
        switch (mutation)
        {
            case "source": root["source"] = "other"; break;
            case "grid": root["grid"] = false; break;
            case "sample_smoothing": root["sampleWeightSpeed"] = 2; break;
            case "axis_smoothing": root["axes"]![0]!["seconds"] = .1; break;
            case "mesh_space": root["meshSpaceSamples"] = false; break;
            case "scale_time": root["scaleAnimation"] = true; break;
            case "base_frame": root["samples"]![0]!["baseFrame"] = 1; break;
            case "sample_rate": root["samples"]![0]!["rate"] = 2; break;
            case "sample_asset": root["samples"]![0]!["path"] = "other"; break;
            case "grid_order": root["gridSamples"]![1]!["indices"]![0] = 2; break;
            case "grid_weight": root["gridSamples"]![1]!["weights"]![0] = .2; break;
            case "oracle_weight": root["staticSamples"]![0]!["weights"]![1] = .2; break;
            case "oracle_order": root["staticSamples"]![0]!["order"]![0] = 0; break;
            case "filter_oracle": root["runs"]![0]!["frames"]![0]!["filteredX"] = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.ThrowsAny<Exception>(() => Compile(root.ToJsonString()));
    }

    [Fact]
    public void SharedGridHasNoMutableHistoryOrHotAllocations()
    {
        var profile = Compile(); var serial = new float[10]; var parallel = new float[10];
        for (var owner = 0; owner < 10; owner++) serial[owner] = Run(owner);
        Parallel.For(0, 10, owner => parallel[owner] = Run(owner)); Assert.Equal(serial, parallel);
        Span<float> weights = stackalloc float[3]; Span<int> order = stackalloc int[3];
        for (var i = 0; i < 1000; i++) profile.Runtime.Evaluate(i % 241 - 120, weights, order);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) profile.Runtime.Evaluate(i % 241 - 120, weights, order);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        float Run(int owner)
        {
            Span<float> values = stackalloc float[3]; Span<int> sequence = stackalloc int[3]; var result = 0f;
            for (var i = 0; i < 1000; i++)
            {
                profile.Runtime.Evaluate((i + owner) % 241 - 120, values, sequence);
                result += values[0] + values[1] * 2 + values[2] * 3 + sequence[0];
            }
            return result;
        }
    }
}
