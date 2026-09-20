using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRagdollPoseCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config", name));
    [Fact]
    public void CompilesFlailPhysicsInputAndRealSnapshotName()
    {
        var input = Read("v4_ragdoll_inputs.json"); var profile = AlsRagdollPoseCompiler.Compile(input, Read("v4_layering_inputs.json"), Set.Value);
        Assert.Equal("RagdollPose", profile.SnapshotName); Assert.Equal("root", profile.Input.VelocityBone);
        Assert.Equal(16, profile.PlayerNodeIndex); Assert.Equal(1000, profile.Input.InputMaximum);
        Assert.EndsWith("/ALS_Flail.ALS_Flail", Set.Value.Animations[profile.AnimationId].ObjectPath);
        using var doc = JsonDocument.Parse(input);
        foreach (var row in doc.RootElement.GetProperty("nativeFlailRateCases").EnumerateArray())
        {
            var v = row.GetProperty("velocityCm");
            Assert.Equal(row.GetProperty("mappedRate").GetDouble(), profile.Input.FlailRate(new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble())), 12);
        }
        Assert.Throws<ArgumentException>(() => profile.Input.FlailRate(new(double.NaN, 0, 0)));
    }
    [Fact]
    public void CompilesExactExportedRawFlailClosure()
    {
        var profile = AlsRagdollPoseCompiler.Compile(Read("v4_ragdoll_inputs.json"), Read("v4_layering_inputs.json"), Set.Value);
        var bank = AlsRawAnimationSourceCompiler.Compile(Read("v4_ragdoll_source_inputs.json"), Set.Value,
            profile.BindingDigest, 1, 1, [profile.AnimationId], file => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", file)));
        Assert.Equal(1, bank.Sources.Length); Assert.Equal(79, bank.GetSkeleton(profile.SkeletonId).LogicalBoneCount);
        Assert.Equal(AlsRawAnimationAdditiveType.None, bank.GetSource(profile.AnimationId).Policy.AdditiveType);
    }
    [Theory] [InlineData("snapshot")] [InlineData("physics_bone")] [InlineData("velocity_source")]
    public void RejectsDisconnectedOrIncompatibleRecoveryInputs(string mutation)
    {
        var input = JsonNode.Parse(Read("v4_ragdoll_inputs.json"))!;
        var path = mutation == "snapshot" ? ".AnimStateNode_1.Blend Out Pose" : ":UpdateRagdollValues";
        var graph = input["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().StartsWith(
            "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP:", StringComparison.Ordinal) &&
            g["path"]!.GetValue<string>().EndsWith(path, StringComparison.Ordinal))!;
        var text = graph["nativeText"]!.GetValue<string>();
        graph["nativeText"] = mutation switch
        {
            "snapshot" => text.Replace("DefaultValue=\"RagdollPose\"", "DefaultValue=\"None\"", StringComparison.Ordinal),
            "physics_bone" => text.Replace("DefaultValue=\"root\"", "DefaultValue=\"pelvis\"", StringComparison.Ordinal),
            _ => text.Replace("MemberName=\"GetPhysicsLinearVelocity\"", "MemberName=\"GetComponentVelocity\"", StringComparison.Ordinal),
        };
        Assert.ThrowsAny<Exception>(() => AlsRagdollPoseCompiler.Compile(input.ToJsonString(), Read("v4_layering_inputs.json"), Set.Value));
    }
}
