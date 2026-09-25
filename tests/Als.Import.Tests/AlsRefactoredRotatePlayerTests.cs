using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredRotatePlayerTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
    [Theory]
    [InlineData(false, 12, 9)]
    [InlineData(true, 20, 17)]
    public void OriginalRotationBindingsDistinguishStandingAndCrouching(bool crouching, int left, int right)
    {
        var catalog=Catalog();var profile=new AlsRefactoredRotatePlayers(catalog,crouching);
        Assert.Equal(new[]{left,right},profile.Players.ToArray().Select(p=>p.PropertyIndex));
        Assert.All(profile.Bind(7),p=>{Assert.Equal(-1,p.GroupId);Assert.True(p.Looping);});
        Assert.Equal(false,profile.Input(7,0,1.5f,false,true,.6f).Looping);
        Assert.Equal(true,profile.Input(7,1,1.5f,false,true,.4f).Looping);
        Assert.Equal(8,profile.Input(7,1,1.5f,false,true,.4f).PlayerId);
        Assert.Throws<ArgumentException>(()=>profile.Input(0,2,1,false,false,1));
        Assert.Throws<ArgumentException>(()=>profile.Input(0,0,float.NaN,false,false,1));
        var payload=catalog.Read(AlsRefactoredRotatePlayers.Blueprint(crouching));
        foreach(var change in new[]{"loop","group","callback","binding"})
        {
            var json=JsonNode.Parse(payload.GetRawText())!;
            var node=json["compiled"]!["nodes"]!.AsArray().Single(n=>n!["propertyIndex"]!.GetValue<int>()==left)!;
            if(change=="loop")node["runtime"]!["bLoopAnimation"]=false;
            if(change=="group")node["runtime"]!["method"]="SyncGroup";
            if(change=="callback")node["runtime"]!["updateFunction"]!["functionName"]="Unknown";
            if(change=="binding")json["nativeText"]=json["nativeText"]!.GetValue<string>().Replace("\"RotateInPlaceState\",\"PlayRate\"","\"RotateInPlaceState\",\"OtherRate\"",StringComparison.Ordinal);
            using var changed=JsonDocument.Parse(json.ToJsonString());Assert.Throws<ArgumentException>(()=>AlsRefactoredRotatePlayers.Compile(changed.RootElement,crouching));
        }
    }
    [Theory]
    [InlineData(false,0)]
    [InlineData(false,1)]
    [InlineData(true,0)]
    [InlineData(true,1)]
    public void RotationLoopChangesClampAndResumeWithoutResettingIdentity(bool crouching,int side)
    {
        var catalog=Catalog();var binding=new AlsRefactoredRotatePlayers(catalog,crouching);var bank=new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"),catalog);
        var runtime=new AlsRefactoredSourcePlayerRuntime(catalog,bank,new Dictionary<string,AlsRefactoredTriangulationProfile>(),binding.Bind(0));
        var path=binding.Players[side].Source;var sequence=bank.Sequences[bank.Assets[path].SequenceIndex];var length=sequence.DurationSeconds;
        Assert.Equal(1,sequence.RateScale);
        var looping=new[]{true,false,false,true,false,true};var rates=new[]{1f,1,1,1,-1,-1};
        var deltas=new[]{length*1.25f,length,length,length*.25f,length,length*.25f};
        var expected=new[]{length*.25f,length,length,length*.25f,0,length*.75f};
        for(var frame=0;frame<looping.Length;frame++)
        {
            var tick=binding.Input(0,side,rates[frame],looping[frame],looping[frame],1);
            runtime.Prepare(frame,[tick],deltas[frame]);runtime.Evaluate(frame,side);
            Assert.Equal(looping[frame],runtime.Ticks[0].Looping);Assert.InRange(Math.Abs(runtime.Players[0].Time-expected[frame]),0,2e-6);
            Assert.False(tick.Reinitialize);
            var time=runtime.Players[0];var pose=runtime.Pose(side).ToArray();var curves=runtime.Curves(side).ToArray();
            runtime.Cancel();runtime.Prepare(frame,[tick],deltas[frame]);runtime.Evaluate(frame,side);
            Assert.Equal(time,runtime.Players[0]);Assert.Equal(pose,runtime.Pose(side).ToArray());Assert.Equal(curves,runtime.Curves(side).ToArray());runtime.Commit(frame);
        }
        // An omitted override returns to the resource policy, not the last
        // submitted override; the override is a per-frame input, not history.
        runtime.Prepare(6,[new(side,default,1,1)],length*.5f);
        Assert.True(runtime.Ticks[0].Looping);Assert.InRange(Math.Abs(runtime.Players[0].Time-length*.25f),0,2e-6);
    }
}
