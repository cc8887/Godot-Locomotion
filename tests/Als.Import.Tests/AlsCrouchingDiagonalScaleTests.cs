using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingDiagonalScaleTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.Compile(ReadGraph(), Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));

    [Fact]
    public void CompilesActualComponentScaleAndConvertsAxes()
    {
        var profile = Compile(ReadGraph());
        Assert.Equal(new Vector3(1.4f, 1, 1.4f), profile.Scale);
        Assert.Equal(new Vector3(1.4f, 1.4f, 1), profile.FbxBoneSpaceScale);
        Assert.Equal("ik_foot_root", Set.Value.Skeletons[profile.SkeletonId].LogicalBones[profile.LogicalBoneId].Name, ignoreCase: true);
        Assert.Equal("ik_foot_root", Set.Value.Skeletons[profile.SkeletonId].PhysicalBones[profile.PhysicalBoneId].Name, ignoreCase: true);
        Assert.Equal("DiagonalScaleAmount", profile.AlphaInput);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void MatchesNativeLocalBlendAndAncestorRoundTrips(int pattern)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "tests", "Als.Core.Tests", "Fixtures", "P3", "v4_component_scale_native.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var parents = root.GetProperty("parents").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var scale = AlsCoordinateConverter.Scale(Vector(root.GetProperty("scale"))); var cases = 0;
        foreach (var row in root.GetProperty("cases").EnumerateArray().Where(r => r.GetProperty("pattern").GetInt32() == pattern))
        {
            var source = row.GetProperty("input").EnumerateArray().Select(Pose).ToArray();
            var expected = row.GetProperty("output").EnumerateArray().Select(Pose).ToArray();
            var output = new AlsLocalPose[source.Length]; var scratch = new AlsLocalPose[source.Length];
            var target = row.GetProperty("bone").GetInt32(); var alpha = row.GetProperty("alpha").GetSingle();
            AlsDiagonalScalePose.Apply(source, parents, target, scale, alpha, scratch, output);
            for (var bone = 0; bone < output.Length; bone++)
            {
                Assert.InRange(Vector3.Distance(output[bone].Position, expected[bone].Position), 0, .000003f);
                Assert.InRange(Vector3.Distance(output[bone].Scale, expected[bone].Scale), 0, .000003f);
                Assert.InRange(MathF.Min((output[bone].Rotation - expected[bone].Rotation).Length(),
                    (output[bone].Rotation + expected[bone].Rotation).Length()), 0, .000003f);
            }
            var inPlace = source.ToArray(); AlsDiagonalScalePose.Apply(inPlace, parents, target, scale, alpha, scratch, inPlace);
            Assert.Equal(output, inPlace); cases++;
        }
        Assert.Equal(36, cases);
    }

    [Fact]
    public void ScalesFootRootButLeavesDescendantLocalOffsetsUntouched()
    {
        var basis = new[] { AlsLocalPose.Identity, new AlsLocalPose(Vector3.One, Quaternion.Identity, Vector3.One),
            new AlsLocalPose(new(2, 3, 4), Quaternion.Identity, Vector3.One) };
        var output = new AlsLocalPose[3]; var scratch = new AlsLocalPose[3];
        AlsDiagonalScalePose.Apply(basis, new[] { -1, 0, 1 }, 1, new(1.4f, 1, 1.4f), .5f, scratch, output);
        Assert.Equal(new Vector3(1.2f, 1, 1.2f), output[1].Scale);
        Assert.Equal(basis[2], output[2]); Assert.Equal(basis[0], output[0]);
        Assert.Equal(basis[1].Position, output[1].Position);
        Assert.Equal(new Vector3(3.4f, 4, 5.8f), output[1].Position + output[1].Scale * output[2].Position);
    }

    [Fact]
    public void PrecisePathConsumesOriginalDoubleNativeTransforms()
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"tests","Als.Core.Tests","Fixtures","P3","v4_component_scale_native.json")));
        var root=document.RootElement; var parents=root.GetProperty("parents").EnumerateArray().Select(v=>v.GetInt32()).ToArray();
        var scale=Vector(root.GetProperty("scale")); var count=0;
        foreach(var row in root.GetProperty("cases").EnumerateArray())
        {
            var source=row.GetProperty("input").EnumerateArray().Select(Precise).ToArray();
            var expected=row.GetProperty("output").EnumerateArray().Select(Precise).ToArray();
            var output=new AlsPrecisePose[source.Length]; var scratch=new AlsPrecisePose[source.Length];
            AlsDiagonalScalePose.Apply(source,parents,row.GetProperty("bone").GetInt32(),scale,row.GetProperty("alpha").GetSingle(),scratch,output);
            for(var b=0;b<source.Length;b++)
            {
                Assert.InRange(Math.Sqrt((output[b].Position-expected[b].Position).LengthSquared),0,3e-7);
                Assert.InRange(Math.Sqrt((output[b].Scale-expected[b].Scale).LengthSquared),0,1e-6);
                var norm=Math.Sqrt(output[b].Rotation.LengthSquared*expected[b].Rotation.LengthSquared);
                Assert.InRange(Math.Max(0,1-Math.Abs(AlsQuaternion.Dot(output[b].Rotation,expected[b].Rotation))/norm),0,1e-12);
            }
            count++;
        }
        Assert.Equal(108,count);
        static AlsPrecisePose Precise(JsonElement row)
        {
            var p=row.GetProperty("position"); var q=row.GetProperty("rotation"); var s=row.GetProperty("scale");
            return new(new(p[0].GetDouble()*.01,p[1].GetDouble()*.01,p[2].GetDouble()*.01),
                new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),
                new(s[0].GetDouble(),s[1].GetDouble(),s[2].GetDouble()));
        }
    }

    [Fact]
    public void InvalidInputsAndNegativeScaleAreRejectedBeforeOutputChanges()
    {
        var basis = new[] { AlsLocalPose.Identity, AlsLocalPose.Identity }; var parents = new[] { -1, 0 };
        var scratch = new AlsLocalPose[2]; var output = basis.ToArray();
        Assert.Throws<ArgumentException>(() => AlsDiagonalScalePose.Apply(basis, parents, 1, Vector3.One, float.NaN, scratch, output));
        Assert.Throws<ArgumentException>(() => AlsDiagonalScalePose.Apply(basis, new[] { -1, 1 }, 1, Vector3.One, 1, scratch, output));
        Assert.Throws<ArgumentException>(() => AlsDiagonalScalePose.Apply(basis, parents, 1, Vector3.One, 1, basis, output));
        basis[0] = basis[0] with { Scale = new(-1, 1, 1) };
        Assert.Throws<ArgumentException>(() => AlsDiagonalScalePose.Apply(basis, parents, 1, Vector3.One, 1, scratch, output));
        Assert.All(output, p => Assert.Equal(AlsLocalPose.Identity, p));
        AlsDiagonalScalePose.Apply(basis, parents, 1, Vector3.One, 0, scratch, output);
        Assert.Equal(basis, output);
    }

    [Fact]
    public void ActiveComponentScaleAndCandidateRetryAllocateNothing()
    {
        var source = Enumerable.Repeat(AlsLocalPose.Identity, 68).ToArray(); var output = source.ToArray(); var scratch = source.ToArray();
        var parents = Enumerable.Range(0, 68).Select(i => i == 0 ? -1 : 0).ToArray();
        long bytes = -1; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 200; i++) Run();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) Run();
                bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30))); Assert.Null(failure); Assert.Equal(0, bytes);
        void Run() => AlsDiagonalScalePose.Apply(source, parents, 65, new(1.4f, 1, 1.4f), .4f, scratch, output);
    }

    [Theory]
    [InlineData("bone")] [InlineData("mode")] [InlineData("space")] [InlineData("translation")]
    [InlineData("alpha")] [InlineData("scale")] [InlineData("filter")] [InlineData("lod")] [InlineData("input")] [InlineData("lifecycle")]
    public void RejectsUnsupportedSourceBoneControl(string mutation)
    {
        var root = JsonNode.Parse(ReadGraph())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>()
            .EndsWith(".(CLF) Locomotion Cycles.AnimStateNode_0.(CLF) Locomotion Cycles"))!;
        var control = graph["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_ModifyBone")!;
        var data = control["properties"]!["Node"]!;
        switch (mutation)
        {
            case "bone": data["boneToModify"]!["boneName"] = "root"; break;
            case "mode": data["scaleMode"] = "BMM_Replace"; break;
            case "space": data["scaleSpace"] = "BCS_BoneSpace"; break;
            case "translation": data["translationMode"] = "BMM_Additive"; break;
            case "alpha": data["alphaInputType"] = "Curve"; break;
            case "scale": data["scale"]!["x"] = 1; break;
            case "filter": data["alphaScaleBiasClamp"]!["bInterpResult"] = true; break;
            case "lod": data["lODThreshold"] = 0; break;
            case "input": control["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == "Alpha")!["links"]![0]!["pin"] = "Other"; break;
            case "lifecycle": data["updateFunction"]!["functionName"] = "Other"; break;
        }
        Assert.Throws<FormatException>(() => Compile(root.ToJsonString()));
    }

    private static Vector3 Vector(JsonElement values) => new(values[0].GetSingle(), values[1].GetSingle(), values[2].GetSingle());
    private static AlsLocalPose Pose(JsonElement row)
    {
        var rotation = row.GetProperty("rotation");
        return new(AlsCoordinateConverter.PositionCentimetersToMeters(Vector(row.GetProperty("position"))),
            AlsCoordinateConverter.Rotation(new(rotation[0].GetSingle(), rotation[1].GetSingle(), rotation[2].GetSingle(), rotation[3].GetSingle())),
            AlsCoordinateConverter.Scale(Vector(row.GetProperty("scale"))));
    }
    private static AlsCrouchingDiagonalScaleProfile Compile(string json) => AlsCrouchingDiagonalScaleCompiler.Compile(json, Sources.Value, Set.Value);
    private static string ReadGraph() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_grounded_dependencies.json"));
}
