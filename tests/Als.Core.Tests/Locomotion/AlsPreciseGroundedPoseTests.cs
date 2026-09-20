using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsPreciseGroundedPoseTests
{
    [Fact]
    public void MeshSpaceLayersMatchAllNativeSamplesWithoutProjectingInputs()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "P3", "v4_mesh_space_blend_native.json")));
        var root = fixture.RootElement;
        var basis = root.GetProperty("base").EnumerateArray().Select(ReadPose).ToArray();
        var layer = root.GetProperty("layer").EnumerateArray().Select(ReadPose).ToArray();
        var parents = root.GetProperty("parents").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var output = new AlsPrecisePose[basis.Length]; var selected = new AlsPrecisePose[basis.Length];
        var indices = new int[basis.Length]; var scratch = new AlsQuaternion[basis.Length * 3];
        Assert.Equal(80, root.GetProperty("cases").GetArrayLength());
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            var weights = row.GetProperty("weights").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            var expected = row.GetProperty("output").EnumerateArray().Select(ReadPose).ToArray();
            AlsMeshSpacePoseBlend.Blend(basis, layer, parents, weights, scratch, output);
            AlsMeshSpacePoseBlend.BlendLayers(basis, layer, parents, indices, weights, scratch, selected);
            Assert.Equal(output, selected);
            for (var bone = 0; bone < output.Length; bone++)
            {
                var label = $"pattern={row.GetProperty("pattern")} alpha={row.GetProperty("alpha")} bone={bone}";
                Assert.True((expected[bone].Position - output[bone].Position).LengthSquared <= 1e-18, label + " position");
                Assert.True((expected[bone].Scale - output[bone].Scale).LengthSquared <= 1e-18, label + " scale");
                var a = expected[bone].Rotation; var b = output[bone].Rotation;
                var dot = System.Math.Abs(AlsQuaternion.Dot(a, b)) / System.Math.Sqrt(a.LengthSquared * b.LengthSquared);
                Assert.True(1 - dot <= 1e-12, label + " rotation");
            }
        }
    }

    [Fact]
    public void DetailAdditiveRetainsSubFloatMotionAndSupportsInPlaceOutput()
    {
        AlsPrecisePose[] basis = [AlsPrecisePose.Identity with { Position = new(1.0000000001, 0, 0) }];
        AlsPrecisePose[] rest = [AlsPrecisePose.Identity];
        var additives = Enumerable.Repeat(new AlsPrecisePose(new(1e-10, 0, 0), AlsQuaternion.Identity, default), 4).ToArray();
        var output = new AlsPrecisePose[1];
        AlsDetailPoseComposer.Compose(basis, additives, rest, Vector4.UnitX, output);
        Assert.Equal(1.0000000002, output[0].Position.X, 14);
        Assert.NotEqual(new AlsPrecisePose(output[0].ToSingle()), output[0]);
        AlsDetailPoseComposer.Compose(basis, additives, rest, Vector4.UnitX, basis);
        Assert.Equal(output, basis);
    }

    [Fact]
    public void PrecisePlantAndStrideEvaluationAllocateNothingAfterWarmup()
    {
        AlsPrecisePose[] basis = [AlsPrecisePose.Identity, AlsPrecisePose.Identity];
        var samples = Enumerable.Repeat(AlsPrecisePose.Identity, 12).ToArray();
        var scratch = new AlsPrecisePose[2]; var output = new AlsPrecisePose[2]; var rotations = new AlsQuaternion[6];
        int[] parents = [-1, 0]; float[] mask = [0, 1];
        var stride = new AlsCrouchingStrideState(true, .4f, .4f);
        for (var i = 0; i < 100; i++) Run();
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Run();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        void Run()
        {
            AlsStopPlantComposer.Compose(basis, samples, new(.2f, .3f, .1f, .4f), .3f, .7f,
                parents, mask, scratch, rotations, output);
            AlsCrouchingStride.Compose(stride, basis, output, output);
        }
    }

    private static AlsPrecisePose ReadPose(JsonElement row)
    {
        var p = row.GetProperty("position"); var q = row.GetProperty("rotation"); var s = row.GetProperty("scale");
        return new(new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()),
            new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()),
            new(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
    }
}
