using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLeanSamplingTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.Compile(Read("v4_grounded_dependencies.json"), Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));
    private static readonly Lazy<AlsLeanSamplingProfile> Profile = new(() => Compile(Read()));

    [Fact]
    public void CompilesIndependentCrouchingAndStandingLeanIdentitiesWithSameReference()
    {
        var profile = Profile.Value;
        Assert.Equal(48, profile.PlayerId); Assert.Equal(70, profile.SampleStart);
        var standing = AlsLeanSamplingCompiler.Compile(Read(), Sources.Value, Set.Value, AlsLocomotionSourceDomain.Cycle);
        Assert.NotEqual(profile.PlayerId, standing.PlayerId); Assert.Equal(profile.BaseAnimationId, standing.BaseAnimationId);
        Assert.Equal("ALS_N_Run_BasePose", Set.Value.Animations[profile.BaseAnimationId].Name);
    }

    [Fact]
    public void Matches441NativeStaticSamplesIncludingClampedEdgesAndEqualWeightOrder()
    {
        using var document = JsonDocument.Parse(Read()); var rows = document.RootElement.GetProperty("staticSamples");
        Assert.Equal(441, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray()) Compare(row);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void MatchesNativeContinuousInputWithoutIntroducingSmoothingOrHistory(int hz)
    {
        using var document = JsonDocument.Parse(Read()); var run = document.RootElement.GetProperty("runs").EnumerateArray()
            .Single(r => r.GetProperty("hz").GetInt32() == hz);
        var frames = run.GetProperty("frames"); Assert.Equal(hz * 4, frames.GetArrayLength());
        foreach (var row in frames.EnumerateArray())
        {
            Assert.Equal(row.GetProperty("x").GetDouble(), row.GetProperty("filteredX").GetDouble());
            Assert.Equal(row.GetProperty("y").GetDouble(), row.GetProperty("filteredY").GetDouble());
            Compare(row);
        }
    }

    [Fact]
    public void AppliesLocalAdditivesInNativeSampleOrderAndIncludesReferenceCurveDifference()
    {
        var weights = new float[5]; var order = new int[5]; var count = Profile.Value.Runtime.Evaluate(new(.3f, -.4f), weights, order);
        var basis = new[] { new AlsLocalPose(new(1, 2, 3), Quaternion.Identity, new(2, 3, 4)) };
        var additives = Enumerable.Range(0, 5).Select(i => new AlsLocalPose(new(i, i * 2, -i), Quaternion.Identity, new(i * .1f))).ToArray();
        var output = new AlsLocalPose[1]; var expected = 0f;
        foreach (var i in order.AsSpan(0, count)) expected += i * weights[i];
        AlsLeanBlendSpace.Apply(basis, additives, weights, order.AsSpan(0, count), output);
        Assert.Equal(1 + expected, output[0].Position.X, 5);
        Assert.Equal(2 * (1 + expected * .1f), output[0].Scale.X, 5);
        Assert.Equal(Quaternion.Identity, output[0].Rotation);
        Assert.Equal(7 + expected, AlsLeanBlendSpace.Curve(7, new float[] { 0, 1, 2, 3, 4 }, weights, order.AsSpan(0, count)), 5);
        var inPlace = basis.ToArray(); AlsLeanBlendSpace.Apply(inPlace, additives, weights, order.AsSpan(0, count), inPlace);
        Assert.Equal(output, inPlace);
    }

    [Fact]
    public void RejectsInvalidInputAndSampleLayoutBeforeWritingPose()
    {
        var weights = new float[5]; var order = new int[5];
        Assert.Throws<ArgumentException>(() => Profile.Value.Runtime.Evaluate(new(float.NaN, 0), weights, order));
        var basis = new[] { AlsLocalPose.Identity }; var additives = Enumerable.Repeat(AlsLocalPose.Identity, 5).ToArray();
        var output = basis.ToArray();
        Assert.Throws<ArgumentException>(() => AlsLeanBlendSpace.Apply(basis, additives, new float[] { .5f, .5f, 0, 0, 0 }, new[] { 0, 0 }, output));
        Assert.Equal(basis, output);
        Assert.Throws<ArgumentException>(() => AlsLeanBlendSpace.Apply(basis, additives, new float[] { 1, 0, 0, 0, 0 }, new[] { 0 }, additives.AsSpan(0, 1)));
    }

    [Theory]
    [InlineData(0,0)] [InlineData(.5f,45)] [InlineData(1,90)]
    public void StandingCurveAlphaWeightsRotationTranslationAndAdditiveScale(float alpha,float degrees)
    {
        var basis=new[]{new AlsLocalPose(new(1,2,3),Quaternion.Identity,new(2,3,4))};
        var delta=new AlsLocalPose(new(4,0,0),Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2),new(.5f,0,0));
        var samples=Enumerable.Repeat(delta,5).ToArray();
        var output=new AlsLocalPose[1];
        AlsLeanBlendSpace.Apply(basis,samples,new float[]{1,0,0,0,0},new[]{0},output,alpha);
        Assert.Equal(1+4*alpha,output[0].Position.X,5);
        Assert.Equal(2*(1+.5f*alpha),output[0].Scale.X,5);
        var direction=Vector3.Transform(Vector3.UnitX,output[0].Rotation);
        var radians=degrees*MathF.PI/180;
        Assert.True(Vector3.Distance(direction,new(MathF.Cos(radians),0,-MathF.Sin(radians)))<1e-6f);
    }

    [Fact]
    public void SamplingAndActiveAdditiveCompositionAllocateNothing()
    {
        var model = Profile.Value.Runtime; var weights = new float[5]; var order = new int[5];
        var basis = Enumerable.Repeat(AlsLocalPose.Identity, 68).ToArray(); var output = basis.ToArray();
        var additives = Enumerable.Repeat(new AlsLocalPose(Vector3.Zero, Quaternion.Identity, Vector3.Zero), 68 * 5).ToArray();
        var curves = new float[5]; long allocated = -1; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 200; i++) Run();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) Run();
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30))); Assert.Null(failure); Assert.Equal(0, allocated);
        void Run()
        {
            var count = model.Evaluate(new(.3f, -.4f), weights, order);
            AlsLeanBlendSpace.Apply(basis, additives, weights, order.AsSpan(0, count), output);
            AlsLeanBlendSpace.Curve(0, curves, weights, order.AsSpan(0, count));
        }
    }

    [Theory]
    [InlineData("axis-filter")] [InlineData("axis-wrap")] [InlineData("axis-range")] [InlineData("axis-divisions")]
    [InlineData("grid-mode")] [InlineData("grid-order")] [InlineData("grid-weight")] [InlineData("grid-index")]
    [InlineData("per-bone")] [InlineData("sample-smoothing")] [InlineData("mesh-space")]
    [InlineData("base-pose")] [InlineData("base-frame")] [InlineData("additive-type")] [InlineData("sample-identity")]
    public void RejectsUnsupportedNativeSampling(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        switch (mutation)
        {
            case "axis-filter": root["axes"]![0]!["seconds"] = .1f; break;
            case "axis-wrap": root["axes"]![0]!["wrap"] = true; break;
            case "axis-range": root["axes"]![0]!["min"] = 0; break;
            case "axis-divisions": root["axes"]![0]!["divisions"] = 2; break;
            case "grid-mode": root["grid"] = false; break;
            case "grid-order": root["gridSamples"]![0]!["x"] = 1; break;
            case "grid-weight": root["gridSamples"]![0]!["weights"]![0] = 2; break;
            case "grid-index": root["gridSamples"]![0]!["indices"]![0] = 5; break;
            case "per-bone": root["ManualPerBoneOverrides"] = "unsupported"; break;
            case "sample-smoothing": root["sampleWeightSpeed"] = 1; break;
            case "mesh-space": root["meshSpaceSamples"] = true; break;
            case "base-pose": root["samples"]![0]!["basePath"] = "wrong"; break;
            case "base-frame": root["samples"]![0]!["baseFrame"] = 1; break;
            case "additive-type": root["samples"]![0]!["additiveType"] = 0; break;
            case "sample-identity": root["samples"]![0]!["index"] = 1; break;
        }
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString()));
    }

    private static void Compare(JsonElement row)
    {
        var weights = new float[5]; var order = new int[5]; var retryWeights = new float[5]; var retryOrder = new int[5];
        var input = new Vector2(row.GetProperty("x").GetSingle(), row.GetProperty("y").GetSingle());
        var count = Profile.Value.Runtime.Evaluate(input, weights, order);
        Assert.Equal(row.GetProperty("order").EnumerateArray().Select(v => v.GetInt32()).ToArray(), order[..count]);
        for (var i = 0; i < 5; i++) Assert.InRange(MathF.Abs(weights[i] - row.GetProperty("weights")[i].GetSingle()), 0, 1e-6f);
        Assert.Equal(count, Profile.Value.Runtime.Evaluate(input, retryWeights, retryOrder));
        Assert.Equal(weights, retryWeights); Assert.Equal(order, retryOrder);
    }
    private static AlsLeanSamplingProfile Compile(string json) => AlsLeanSamplingCompiler.Compile(json, Sources.Value, Set.Value, AlsLocomotionSourceDomain.Crouching);
    private static string Read(string name = "v4_lean_sampling.json") => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
}
