using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementPlayerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualSourcesResetToAuthoredStartsAndRetryWithoutLeakingClocks(bool crouching)
    {
        var json=MantlingHostFixture.Read("refactored_animation_sources");
        byte[] Read(string p)=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p));
        var catalog=new AlsRefactoredAnimationCatalog(json,Read);
        var bank=new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"),catalog);
        var profiles=AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"),json,Read);
        var binding=new AlsRefactoredMovementPlayers(catalog,crouching);
        var runtime=new AlsRefactoredSourcePlayerRuntime(catalog,bank,profiles,binding.Bind(0,new Dictionary<string,int>{["Movement"]=0,["Run Start"]=1,["First Pivot"]=2,["Second Pivot"]=3}));
        long frame=0;
        for(var id=0;id<binding.Players.Length;id++)
        {
            var player=binding.Players[id];
            foreach(var delta in new[]{0f,10f,0f})
            {
                var tick=binding.Input(0,id,new(1.7f,2.3f,.6f,.8f),1,reinitialize:delta==0);
                runtime.Prepare(frame,[tick],delta);runtime.Evaluate(frame,id);
                var time=runtime.Players[0].Time;
                if(!player.BlendSpace)
                {
                    if(delta==0)Assert.Equal(player.Start,time);
                    else if(!player.Loop)Assert.Equal(bank.Sequences[bank.Assets[player.Source].SequenceIndex].DurationSeconds,time);
                }
                var pose=runtime.Pose(id).ToArray();var curves=runtime.Curves(id).ToArray();
                runtime.Cancel();runtime.Prepare(frame,[tick],delta);runtime.Evaluate(frame,id);
                Assert.Equal(time,runtime.Players[0].Time);Assert.Equal(pose,runtime.Pose(id).ToArray());Assert.Equal(curves,runtime.Curves(id).ToArray());
                runtime.Commit(frame++);
            }
        }
    }

    [Fact]
    public void AuthoredStartsBindingsAndRateBasisCannotSilentlyDrift()
    {
        var catalog=new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        var original=catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false));
        foreach(var change in new[]{"start","basis","binding","loop","callback"})
        {
            var json=JsonNode.Parse(original.GetRawText())!;
            var node=json["compiled"]!["nodes"]!.AsArray().Single(n=>n!["propertyIndex"]!.GetValue<int>()==130)!;
            if(change=="start")node["runtime"]!["startPosition"]=.4;
            if(change=="basis")node["runtime"]!["playRateBasis"]=1;
            if(change=="loop")node["runtime"]!["bLoopAnimation"]=false;
            if(change=="callback")node["runtime"]!["updateFunction"]!["functionName"]="Unsupported";
            if(change=="binding")json["nativeText"]=json["nativeText"]!.GetValue<string>().Replace("\"StandingState\",\"PlayRate\"","\"StandingState\",\"UnknownRate\"",StringComparison.Ordinal);
            using var changed=JsonDocument.Parse(json.ToJsonString());
            Assert.Throws<ArgumentException>(()=>AlsRefactoredMovementPlayers.Compile(changed.RootElement,false));
        }
    }

    [Theory]
    [InlineData(false,24)]
    [InlineData(true,6)]
    public void OriginalMovementIdentitiesPreserveGroupsStartsAndDynamicInputs(bool crouching,int count)
    {
        var catalog=new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        var profile=new AlsRefactoredMovementPlayers(catalog,crouching);Assert.Equal(count,profile.Players.Length);
        var groups=new Dictionary<string,int>{["Movement"]=0,["Run Start"]=1,["First Pivot"]=2,["Second Pivot"]=3};
        var definitions=profile.Bind(5,groups);Assert.Equal(Enumerable.Range(5,count),definitions.Select(d=>d.PlayerId));
        var input=new AlsRefactoredMovementPlayerInput(1.7f,2.3f,.6f,.8f);
        for(var i=0;i<count;i++)
        {
            var p=profile.Players[i];var tick=profile.Input(5,i,input,.7f,true);
            Assert.Equal(definitions[i].StartPosition,tick.StartPosition);Assert.True(tick.Reinitialize);
            Assert.Equal(groups[p.Group],definitions[i].GroupId);Assert.Equal(p.Loop,definitions[i].Looping);
            Assert.Equal(p.BlendSpace?new Vector2(.6f,.8f):default,tick.BlendInput);
            Assert.Equal((p.RateBinding==""?1.25f:crouching?2.3f:1.7f)/p.RateBasis,tick.PlayRate);
        }
        if(crouching){Assert.All(profile.Players.ToArray(),p=>{Assert.Equal(0,p.Start);Assert.True(p.Loop);Assert.NotEmpty(p.RateBinding);});}
        else
        {
            Assert.Equal(6,profile.Players.ToArray().Count(p=>p.BlendSpace));
            var acceleration=profile.Players.ToArray().Where(p=>p.Source.Contains("/Acceleration/",StringComparison.Ordinal)).ToArray();
            Assert.Equal(16,acceleration.Length);Assert.All(acceleration,p=>Assert.False(p.Loop));
            Assert.Equal(4,acceleration.Select(p=>p.Source).Distinct().Count());
            Assert.Equal(new[]{.1f,.15f,.25f},acceleration.Select(p=>p.Start).Distinct().Order());
            Assert.Equal(new[]{"First Pivot","Run Start","Second Pivot"},acceleration.Select(p=>p.Group).Distinct().Order());
        }
        Assert.Throws<ArgumentException>(()=>profile.Input(0,0,input with{StandingRate=float.NaN},1));
        Assert.Throws<ArgumentException>(()=>profile.Bind(0,new Dictionary<string,int>()));
    }
}
