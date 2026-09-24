using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredHeadSettingsTests
{
    private static string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
    [Fact]
    public void ActualParentSettingsBindToHeadStateModel()
    {
        var settings=AlsRefactoredHeadSettingsCompiler.Compile(Read("head_inputs"),Read("layering_graphs"));
        Assert.Equal(new AlsRefactoredHeadSettings(.1f,.1f,.2f,.01f,.01f),settings);
        var input=new AlsRefactoredViewInput(90,30,0,0,0,0,0,0,false,false,
            GodotAls.Core.Contracts.AlsRotationMode.LookingDirection,true,false,false,0,0,.1f,0,0);
        var head=AlsRefactoredViewModel.RefreshHead(input,new(90,30,.5f,1),
            AlsRefactoredHeadState.Initial with {InitializationRequired=false},settings);
        Assert.Equal(0,head.Yaw);Assert.True(head.Pitch>25);
    }
    [Fact]
    public void NativePoseCaptureBindsExactInputsAndCompleteLookSourceClosure()
    {
        var json=Read("head_inputs");var inputs=JsonNode.Parse(json)!;
        var reference=JsonNode.Parse(Read("head_pose_reference"))!;
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),
            reference["inputsSha256"]!.GetValue<string>().ToUpperInvariant());
        var sources=inputs["sequences"]!.AsArray();Assert.Equal(4,sources.Count);
        var paths=sources.Select(s=>s!["raw"]!["source"]!.GetValue<string>()).ToHashSet();
        Assert.Equal(4,paths.Count);
        var samples=inputs["blendSpace"]!["samples"]!.AsArray();Assert.Equal(3,samples.Count);
        foreach(var sample in samples)
        {
            var path=sample!["sequence"]!.GetValue<string>();Assert.Contains(path,paths);
            var source=sources.Single(s=>s!["raw"]!["source"]!.GetValue<string>()==path)!;
            Assert.Equal("AAT_RotationOffsetMeshSpace",source["evaluation"]!["additiveType"]!.GetValue<string>());
            Assert.Contains(source["evaluation"]!["baseAsset"]!.GetValue<string>(),paths);
        }
        var poses=reference["poses"]!.AsArray();Assert.Equal(35,poses.Count);
        foreach(var pose in poses)
        {
            Assert.Equal(inputs["blendSpace"]!["source"]!.GetValue<string>(),pose!["source"]!.GetValue<string>());
            Assert.Equal(79,pose["names"]!.AsArray().Count);Assert.Equal(79,pose["pose"]!.AsArray().Count);
            foreach(var atom in pose["pose"]!.AsArray())
                foreach(var field in new[]{"position","rotation","scale"})
                    Assert.All(atom![field]!.AsArray(),v=>Assert.True(double.IsFinite(v!.GetValue<double>())));
        }
    }
    [Theory]
    [InlineData("hash")][InlineData("parent")][InlineData("asset")][InlineData("extra")][InlineData("negative")]
    public void InvalidResourceBindingIsRejected(string mutation)
    {
        var input=JsonNode.Parse(Read("head_inputs"))!;
        if(mutation=="hash")input["graphsSha256"]="00";
        if(mutation=="parent")input["parentClass"]="Other";
        if(mutation=="asset")input["settings"]!["source"]="Other";
        if(mutation=="extra")input["settings"]!["head"]!["Unknown"]=1;
        if(mutation=="negative")input["settings"]!["head"]!["yaw_angle_interpolation_half_life"]=-1;
        Assert.Throws<ArgumentException>(()=>AlsRefactoredHeadSettingsCompiler.Compile(input.ToJsonString(),Read("layering_graphs")));
    }
}
