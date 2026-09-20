using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsOverlayPropCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static string Root => Path.Combine(RepositoryRoot.Find(),"assets","config");
    private static string Read(string name) => File.ReadAllText(Path.Combine(Root,name));
    private static AlsOverlayPropProfile Compile(string json) => AlsOverlayPropCompiler.Compile(json,Set.Value,
        Array.FindIndex(Set.Value.Skeletons,s=>s.ObjectPath.EndsWith("/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton",StringComparison.Ordinal)));

    [Fact]
    public void CompilesAllNativeEquipmentBranchesAndExactBowSource()
    {
        var profile=Compile(Read("v4_overlay_props_inputs.json"));
        var meshes=new[]{"M4A1","M9","M9","Bow","Torch","Binoculars","Box","Barrel"};
        for(var i=0;i<13;i++)
        {
            var binding=profile.Get((AlsOverlayKind)i); Assert.Equal(i>=5,binding.HasProp);
            if(i<5)continue;
            Assert.Equal(meshes[i-5],binding.Skeletal?Set.Value.SkeletalMeshes[binding.MeshId].Name:Set.Value.StaticMeshes[binding.MeshId].Name);
            Assert.Equal(i is 8 or 9 or 12?"VB LHS_ik_hand_gun":"VB RHS_ik_hand_gun",binding.Socket);
        }
        var bank=AlsRawAnimationSourceCompiler.Compile(Read("v4_overlay_prop_source_inputs.json"),Set.Value,profile.Digest,1,1,
            [profile.BowAnimationId],file=>File.ReadAllBytes(Path.Combine(Root,file)));
        Assert.Equal(24,bank.GetSource(profile.BowAnimationId).PoseData.LogicalBoneCount);
        Assert.Equal("Enable_SpineRotation",profile.DrawCurve);
    }

    [Theory]
    [InlineData("components")]
    [InlineData("cases")]
    public void RejectsMissingNativeExecutionEvidence(string key)
    {
        var root=JsonNode.Parse(Read("v4_overlay_props_inputs.json"))!;
        root[key]!.AsArray().Clear();
        Assert.ThrowsAny<Exception>(()=>Compile(root.ToJsonString()));
    }

    [Theory]
    [InlineData("VB RHS_ik_hand_gun","hand_r")]
    [InlineData("SnapToTarget","KeepRelative")]
    [InlineData("Enable_SpineRotation","OtherCurve")]
    [InlineData("/Props/Meshes/M4A1.M4A1","/Props/Meshes/Missing.Missing")]
    [InlineData("MemberName=\"ClearHeldObject\"","MemberName=\"UnsupportedClear\"")]
    public void RejectsChangedNativeAttachmentAndGameplay(string from,string to)
    {
        var root=JsonNode.Parse(Read("v4_overlay_props_inputs.json"))!;
        foreach(var graph in root["graphs"]!.AsArray())
            graph!["nativeText"]=graph["nativeText"]!.GetValue<string>().Replace(from,to,StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(()=>Compile(root.ToJsonString()));
    }
}
