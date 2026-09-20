using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsLayeredBonePoseBlendTests
{
    private static readonly string[] Names = ["root", "pelvis", "thigh_l", "calf_l", "thigh_r",
        "spine_01", "spine_02", "spine_03", "clavicle_l", "upperarm_l", "VB Curves"];
    private static readonly int[] Parents = [-1, 0, 1, 2, 1, 1, 5, 6, 7, 8, 0];

    [Fact]
    public void PelvisFilterExcludesLegDescendantsAndUsesCaseInsensitiveBoneNames()
    {
        var indices = new int[Names.Length]; var weights = new float[Names.Length];
        AlsLayeredBonePoseBlend.BuildWeights(Names, Parents,
            [[new("PELVIS", 0), new("THIGH_L", -1), new("thigh_r", -1)]], indices, weights);
        Assert.Equal(new float[] { 0, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0 }, weights);
        Assert.All(indices, source => Assert.Equal(0, source));
    }

    [Fact]
    public void SpineDepthRampsFromBranchRootAndLaterFiltersAccumulateInOrder()
    {
        var indices = new int[Names.Length]; var weights = new float[Names.Length];
        AlsLayeredBonePoseBlend.BuildWeights(Names, Parents, [[new("spine_01", 3)]], indices, weights);
        Assert.Equal(1f / 3, weights[5]); Assert.Equal(2f / 3, weights[6]); Assert.Equal(1, weights[7]);
        Assert.Equal(1, weights[9]); Assert.Equal(0, weights[1]);
        AlsLayeredBonePoseBlend.BuildWeights(Names, Parents,
            [[new("spine_01", 0), new("spine_02", -1), new("clavicle_l", 2)]], indices, weights);
        Assert.Equal(new float[] { 0, 0, 0, 0, 0, 1, 0, 0, .5f, 1, 0 }, weights);
    }

    [Fact]
    public void LaterLayerOwnsOverlappingBranchesEvenWhenItsExclusionLeavesZeroWeight()
    {
        var indices = new int[Names.Length]; var weights = new float[Names.Length];
        AlsLayerBranchFilter[][] layers = [[new("pelvis", 0)], [new("clavicle_l", -1)], [new("VB CURVES", 0)]];
        AlsLayeredBonePoseBlend.BuildWeights(Names, Parents, layers, indices, weights);
        Assert.Equal(1, indices[8]); Assert.Equal(0, weights[8]);
        Assert.Equal(1, indices[9]); Assert.Equal(0, weights[9]);
        Assert.Equal(2, indices[10]); Assert.Equal(1, weights[10]);
        var maximum = new float[3];
        AlsLayeredBonePoseBlend.UpdateWeights(indices, weights, [1, 1, .4f], indices, weights, maximum);
        Assert.Equal(0, indices[8]); Assert.Equal(0, indices[9]);
        Assert.Equal(new float[] { 1, 0, .4f }, maximum);
    }

    [Fact]
    public void MissingBranchIsNativeNoOpAndDoesNotChangeEarlierSource()
    {
        var indices = new int[Names.Length]; var weights = new float[Names.Length];
        AlsLayeredBonePoseBlend.BuildWeights(Names, Parents, [[new("pelvis", 0)], [new("missing", 1)]], indices, weights);
        Assert.All(indices, source => Assert.Equal(0, source)); Assert.Equal(1, weights[9]);
    }

    [Fact]
    public void DynamicRelevanceResetsSourcesAndClampsOnlyEvaluationMaximum()
    {
        int[] staticIndices = [1, 1, 2, 3, 3]; float[] staticWeights = [1, .5f, 1, 1, .25f];
        var indices = new int[5]; var weights = new float[5]; var maximum = new float[4];
        AlsLayeredBonePoseBlend.UpdateWeights(staticIndices, staticWeights,
            [1, AlsPoseBlender.WeightThreshold, MathF.BitIncrement(AlsPoseBlender.WeightThreshold), 2], indices, weights, maximum);
        Assert.Equal(new[] { 0, 0, 2, 3, 3 }, indices);
        Assert.Equal(0, weights[0]); Assert.Equal(0, weights[1]); Assert.Equal(2, weights[3]); Assert.Equal(.5f, weights[4]);
        Assert.Equal(0, maximum[1]); Assert.True(maximum[2] > AlsPoseBlender.WeightThreshold); Assert.Equal(1, maximum[3]);
        AlsLayeredBonePoseBlend.UpdateWeights(staticIndices, staticWeights, [1, -1, 0, 0], indices, weights, maximum);
        Assert.All(indices, value => Assert.Equal(0, value)); Assert.All(weights, value => Assert.Equal(0, value));
        Assert.All(maximum, value => Assert.Equal(0, value));
    }

    [Fact]
    public void VirtualCurveBoneContributesEvenWhenPhysicalBonesAreUnchanged()
    {
        var indices = new int[Names.Length]; var weights = new float[Names.Length]; var maximum = new float[1];
        AlsLayeredBonePoseBlend.BuildWeights(Names, Parents, [[new("VB Curves", 0)]], indices, weights);
        AlsLayeredBonePoseBlend.UpdateWeights(indices, weights, [1], indices, weights, maximum);
        Assert.All(weights[..^1], value => Assert.Equal(0, value)); Assert.Equal(1, maximum[0]);
        var curves = new AlsInertialCurve[1];
        AlsLayeringCurves.BlendLayers([new(2)], [new(5)], maximum, AlsLayerCurveBlendMode.BlendByWeight, curves);
        Assert.Equal(new AlsInertialCurve(7), curves[0]);
    }

    [Fact]
    public void LocalRootBlendMatchesExistingNativePerBoneGoldenCases()
    {
        // Mesh and local rotation spaces coincide at root; this reuses independent UE output.
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "P3", "v4_mesh_space_blend_native.json")));
        var root = fixture.RootElement; var basis = ReadPose(root.GetProperty("base")[0]);
        var layer = ReadPose(root.GetProperty("layer")[0]); var output = new AlsLocalPose[1];
        Assert.Equal(80, root.GetProperty("cases").GetArrayLength());
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            AlsLayeredBonePoseBlend.BlendLocal([basis], [layer], [0], [row.GetProperty("weights")[0].GetSingle()], output);
            var expected = ReadPose(row.GetProperty("output")[0]);
            Assert.InRange(Vector3.Distance(expected.Position, output[0].Position), 0, .000003f);
            Assert.InRange(Vector3.Distance(expected.Scale, output[0].Scale), 0, .000003f);
            Assert.InRange(MathF.Min((expected.Rotation - output[0].Rotation).Length(),
                (expected.Rotation + output[0].Rotation).Length()), 0, .000006f);
        }
    }

    [Fact]
    public void LocalLayersPreserveZeroWeightChildLocalRotationAndSelectSeparateSources()
    {
        AlsLocalPose[] basis = [Pose(.2f), Pose(.3f), Pose(.4f)];
        AlsLocalPose[] layers = [Pose(.8f), Pose(.9f), Pose(1), Pose(-.8f), Pose(-.9f), Pose(-1)];
        var output = new AlsLocalPose[3];
        AlsLayeredBonePoseBlend.BlendLocal(basis, layers, [0, 1, 0], [1, 1, 0], output);
        Assert.Equal(layers[0], output[0]); Assert.Equal(layers[4], output[1]); Assert.Equal(basis[2], output[2]);
        AlsLayeredBonePoseBlend.BlendLocal(basis, layers, [0, 1, 0], [1, 1, 0], layers.AsSpan(3, 3));
        Assert.Equal(output, layers[3..]);
    }

    [Fact]
    public void InvalidLayoutsAreRejectedBeforeOutputMutation()
    {
        int[] indices = [7, 7]; float[] weights = [7, 7]; float[] maximum = [7];
        Assert.Throws<ArgumentException>(() => AlsLayeredBonePoseBlend.BuildWeights(["root", "ROOT"], [-1, 0],
            [[new("root", 0)]], indices, weights));
        Assert.Throws<ArgumentException>(() => AlsLayeredBonePoseBlend.BuildWeights(["root", "child"], [-1, 2],
            [[new("root", 0)]], indices, weights));
        Assert.Throws<ArgumentException>(() => AlsLayeredBonePoseBlend.UpdateWeights([0, 1], [1, 1], [1], indices, weights, maximum));
        Assert.Equal(new[] { 7, 7 }, indices); Assert.Equal(new float[] { 7, 7 }, weights); Assert.Equal(7, maximum[0]);
    }

    [Fact]
    public void RuntimeWeightAndLocalPoseOperationsDoNotAllocate()
    {
        int[] sources = [0, 1]; float[] staticWeights = [1, .5f]; float[] alphas = [.8f, .6f];
        var indices = new int[2]; var weights = new float[2]; var maximum = new float[2];
        AlsLocalPose[] basis = [Pose(.2f), Pose(.3f)]; AlsLocalPose[] layers = [Pose(.5f), Pose(.6f), Pose(.8f), Pose(.9f)];
        var output = new AlsLocalPose[2];
        for (var i = 0; i < 64; i++)
        {
            AlsLayeredBonePoseBlend.UpdateWeights(sources, staticWeights, alphas, indices, weights, maximum);
            AlsLayeredBonePoseBlend.BlendLocal(basis, layers, indices, weights, output);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            AlsLayeredBonePoseBlend.UpdateWeights(sources, staticWeights, alphas, indices, weights, maximum);
            AlsLayeredBonePoseBlend.BlendLocal(basis, layers, indices, weights, output);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static AlsLocalPose Pose(float yaw) => new(new(yaw, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), Vector3.One);
    private static AlsLocalPose ReadPose(JsonElement row)
    {
        var p = row.GetProperty("position"); var r = row.GetProperty("rotation"); var s = row.GetProperty("scale");
        return new(new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
            new(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()),
            new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
}
