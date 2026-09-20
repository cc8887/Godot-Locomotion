using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsSprintInputCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set=new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources=new(()=>AlsLocomotionSourceCompiler.Compile(Read(),Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(),Set.Value).SkeletonId));
    [Fact]
    public void CompilesAuthoredIndependentSourcesRateBasisAndAlphaPolicy()
    {
        var profile=AlsSprintInputCompiler.Compile(Read(),Sources.Value,Set.Value);var players=Sources.Value.Players;
        Assert.NotEqual(profile.FirstPlayerId,profile.ImpulsePlayerId);
        Assert.Equal(players[profile.FirstPlayerId].SyncGroupId,players[profile.ImpulsePlayerId].SyncGroupId);
        Assert.Equal(1,players[profile.FirstPlayerId].PlayRateBasis);
        Assert.Equal(.833f,players[profile.ImpulsePlayerId].PlayRateBasis);
        Assert.Equal(new AlsOverlayAlphaPolicy(1,0,true,0,1,true,20,.5f,true,0,.25f,0,1),profile.Alpha);
    }
    [Theory]
    [InlineData("reset")] [InlineData("always-update")] [InlineData("axis")] [InlineData("mode")]
    [InlineData("source-order")] [InlineData("callback")] [InlineData("missing-runtime")]
    public void RejectsUnsupportedNodeSemantics(string mutation)
    {
        var root=JsonNode.Parse(Read())!;var graph=root["graphs"]!.AsArray().Single(g=>g!["path"]!.GetValue<string>().EndsWith(
            ".(N) Locomotion Cycles.AnimStateNode_0.(N) Locomotion Cycles",StringComparison.Ordinal))!;
        var blend=graph["nodes"]!.AsArray().Single(n=>n!["name"]!.GetValue<string>()=="AnimGraphNode_TwoWayBlend_0")!;
        var data=blend["properties"]!["BlendNode"]!;
        if(mutation=="reset")data["bResetChildOnActivation"]=false;
        if(mutation=="always-update")data["bAlwaysUpdateChildren"]=true;
        if(mutation=="mode")data["alphaInputType"]="Curve";
        if(mutation=="callback")data["updateFunction"]!["functionName"]="Other";
        if(mutation=="missing-runtime")blend["properties"]!.AsObject().Remove("BlendNode");
        if(mutation=="axis")Pin("Alpha")["links"]![0]!["pin"]="RelativeAccelerationAmount_Y";
        if(mutation=="source-order")Pin("A")["links"]![0]!["node"]="AnimGraphNode_SequencePlayer_2";
        Assert.ThrowsAny<Exception>(()=>AlsSprintInputCompiler.Compile(root.ToJsonString(),Sources.Value,Set.Value));
        JsonNode Pin(string name)=>blend["pins"]!.AsArray().Single(p=>p!["name"]!.GetValue<string>()==name)!;
    }
    private static string Read()=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets","config","v4_locomotion_source_graph.json"));
}
