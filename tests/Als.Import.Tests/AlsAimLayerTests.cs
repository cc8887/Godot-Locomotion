using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsAimLayerTests
{
    private static readonly string[] Bones = ["root", "pelvis", "spine_01", "spine_02", "spine_03", "head"];
    private static readonly int[] Parents = [-1, 0, 1, 2, 3, 4];
    private static readonly string[] Curves = ["Enable_SpineRotation", "Shared", "AimOnly"];

    [Fact]
    public void CompilesSharedCacheAndFourOrderedComponentRotations()
    {
        var definition = Compile();
        Assert.Equal(new[] { 22 }, definition.Cache.UpdateOrder.ToArray());
        Assert.Equal(13, definition.AimCacheRead); Assert.Equal(7, definition.SpineCacheRead);
        Assert.Equal(Bones[1..5], definition.SpineBones.ToArray());
    }

    [Theory]
    [InlineData("cache")]
    [InlineData("rotation-space")]
    [InlineData("root-additive")]
    [InlineData("blend-reset")]
    [InlineData("alpha-interpolation")]
    public void RejectsNativeSemanticChanges(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        JsonNode Node(string name) => root["compiledNodeInventory"]!.AsArray().Single(n =>
            n!["path"]!.GetValue<string>().EndsWith(":AnimGraph." + name, StringComparison.Ordinal))!;
        switch (mutation)
        {
            case "cache": Node("AnimGraphNode_UseCachedPose_23")["cacheSourcePropertyIndex"] = 1; break;
            case "rotation-space": Node("AnimGraphNode_ModifyBone_0")["properties"]!["Node"]!["rotationSpace"] = "BCS_BoneSpace"; break;
            case "root-additive": Node("AnimGraphNode_ApplyMeshSpaceAdditive_0")["properties"]!["Node"]!["bRootSpaceAdditive"] = true; break;
            case "blend-reset": Node("AnimGraphNode_TwoWayBlend_6")["properties"]!["BlendNode"]!["bResetChildOnActivation"] = true; break;
            case "alpha-interpolation": Node("AnimGraphNode_ModifyBone_1")["properties"]!["Node"]!["alphaScaleBiasClamp"]!["bInterpResult"] = true; break;
        }
        Assert.ThrowsAny<Exception>(() => AlsAimLayerCompiler.Compile(root.ToJsonString()));
    }

    [Theory]
    [InlineData(0, 1, .8f, .8f, true)]
    [InlineData(.25f, .5f, .6f, .3f, true)]
    [InlineData(.5f, 1, .4f, .4f, true)]
    [InlineData(.75f, 1, .6f, .2f, true)]
    [InlineData(1, 1, .8f, 0, false)]
    [InlineData(0, 0, .8f, 0, false)]
    public void CachedUpdateUsesLargestRequestAndAimKeepsItsOwnFraction(float spine, float aim, float post, float aimWeight, bool relevant)
    {
        var runtime = New(); var id = new AlsFrameIdentity(1, 11, 2);
        runtime.Prepare(new(id, .8f, 1f / 60, .7f), Layer(id, aim), Input(id, 0), [new(spine), default, default]);
        Assert.Equal(post, runtime.PostContext.Weight, 6); Assert.Equal(.7f, runtime.PostContext.RootMotionWeight);
        Assert.Equal(aimWeight, runtime.AimContext.Weight, 6); Assert.Equal(relevant, runtime.AimRelevant);
    }

    [Fact]
    public void SpineRotatesImportedFbxBonesAroundNativeYawAxisAndPreservesCurvePresence()
    {
        var runtime = New(); var id = new AlsFrameIdentity(1, 11, 2);
        var pose = Enumerable.Repeat(AlsLocalPose.Identity, 6).ToArray();
        pose[0] = pose[0] with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, .7f) };
        var curves = new AlsInertialCurve[] { new(1), new(0), default };
        runtime.Prepare(new(id, 1, 1f / 60), Layer(id, 1), Input(id, 22.5), curves);
        runtime.Evaluate(pose, curves, [], []);
        var component = Quaternion.Identity;
        for (var bone = 0; bone < 6; bone++)
        {
            component *= runtime.Pose[bone].Rotation;
            // Native +Z yaw reflected into the imported FBX pose is -Z.
            // Godot world -Y is a different space, even for the same character.
            var expected = Quaternion.CreateFromAxisAngle(-Vector3.UnitZ, MathF.PI / 8 * Math.Min(bone, 4)) * pose[0].Rotation;
            Assert.True(MathF.Min((component - expected).Length(), (component + expected).Length()) < 2e-6f, $"Component rotation differs at {bone}.");
            Assert.Equal(pose[bone].Position, runtime.Pose[bone].Position);
        }
        Assert.Equal(curves, runtime.Curves.ToArray());
    }

    [Fact]
    public void AimIsAddedToPostLayeringAndSourceFailureCannotCommit()
    {
        var runtime = New(); var id = new AlsFrameIdentity(1, 11, 2);
        var post = Enumerable.Repeat(AlsLocalPose.Identity with { Position = Vector3.UnitX }, 6).ToArray();
        var additive = Enumerable.Repeat(new AlsLocalPose(Vector3.UnitZ, Quaternion.Identity, Vector3.Zero), 6).ToArray();
        var curves = new AlsInertialCurve[] { default, new(3), default };
        var added = new AlsInertialCurve[] { default, new(2), new(0) };
        Prepare(); runtime.Evaluate(post, curves, additive, added);
        var expected = runtime.Pose.ToArray(); var expectedCurves = runtime.Curves.ToArray(); runtime.Cancel();
        Prepare(); added[1] = new(float.NaN);
        Assert.Throws<ArgumentException>(() => runtime.Evaluate(post, curves, additive, added));
        Assert.Throws<InvalidOperationException>(() => runtime.Commit(id)); Assert.Equal(default, runtime.CommittedIdentity);
        added[1] = new(2); Prepare(); runtime.Evaluate(post, curves, additive, added);
        Assert.Equal(expected, runtime.Pose.ToArray()); Assert.Equal(expectedCurves, runtime.Curves.ToArray());
        Assert.Equal(new AlsInertialCurve(4), runtime.Curves[1]); Assert.True(runtime.Curves[2].Present);
        Assert.Equal(new Vector3(1, 0, .5f), runtime.Pose[3].Position);
        runtime.Commit(id); Assert.Equal(id, runtime.CommittedIdentity);
        void Prepare() => runtime.Prepare(new(id, 1, 1f / 60), Layer(id, .5), Input(id, 0), curves);
    }

    [Fact]
    public void MixedAimSpineFrameDoesNotAllocateAfterWarmup()
    {
        var runtime = New();
        var post = Enumerable.Repeat(AlsLocalPose.Identity, 6).ToArray();
        var additive = Enumerable.Repeat(new AlsLocalPose(Vector3.Zero, Quaternion.Identity, Vector3.Zero), 6).ToArray();
        var curves = new AlsInertialCurve[] { new(.4f), new(1), default };
        var added = new AlsInertialCurve[3];
        for (var frame = 1; frame <= 1000; frame++) Step(frame);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 1001; frame <= 3000; frame++) Step(frame);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        void Step(int frame)
        {
            var id = new AlsFrameIdentity(frame, 11, 2);
            runtime.Prepare(new(id, 1, 1f / 60), Layer(id, .5), Input(id, frame % 35), curves);
            runtime.Evaluate(post, curves, additive, added); runtime.Commit(id);
        }
    }

    private static AlsLayeringInput Layer(AlsFrameIdentity id, double aim) => default(AlsLayeringInput) with { Identity = id, EnableAimOffset = aim };
    private static AlsAimingInputState Input(AlsFrameIdentity id, double yaw) => default(AlsAimingInputState) with { Identity = id, SpineRotation = new(0, yaw, 0) };
    private static AlsAimLayerRuntime New() => new(Compile(), Bones, Parents, Curves);
    private static AlsAimLayerDefinition Compile() => AlsAimLayerCompiler.Compile(Read());
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config/v4_layering_inputs.json"));
}
