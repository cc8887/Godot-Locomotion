using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredSyncBankTests
{
    private static AlsRefactoredAnimationCatalog Catalog()=>new(MantlingHostFixture.Read("refactored_animation_sources"),
        p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
    private static string Inputs()=>MantlingHostFixture.Read("refactored_sync_inputs");
    [Fact]
    public void CompleteOriginalBankHasCanonicalIdsAndBindsEveryBlendSampleToCoreTiming()
    {
        var bank=new AlsRefactoredSyncBank(Inputs(),Catalog());Assert.Equal(136,bank.Assets.Count);Assert.Equal(127,bank.Sequences.Length);Assert.Equal(9,bank.BlendSpaces.Count);
        Assert.Equal(Enumerable.Range(0,136),bank.Assets.Values.Select(a=>a.AssetId).Order());
        var json=JsonNode.Parse(Inputs())!;var rows=json["assets"]!.AsArray();var reversed=rows.Reverse().Select(r=>r!.DeepClone()).ToArray();rows.Clear();foreach(var r in reversed)rows.Add(r);
        var other=new AlsRefactoredSyncBank(json.ToJsonString(),Catalog());Assert.Equal(bank.Assets.OrderBy(p=>p.Key),other.Assets.OrderBy(p=>p.Key));
        Assert.True(bank.Sequences.SequenceEqual(other.Sequences));Assert.True(bank.Markers.SequenceEqual(other.Markers));
        foreach(var space in bank.BlendSpaces.Values)
        {
            var count=space.SequenceIndices.Length;var weights=Enumerable.Range(0,count).Select(i=>new AlsAimGridVertex(i,1f/count)).ToArray();
            var samples=new AlsAssetSyncSample[count];Assert.Equal(count,space.BindSamples(weights,100,samples));
            var timing=new AlsBlendSpaceTimingSample[count];
            for(var i=0;i<count;i++)
            {
                Assert.Equal(100+i,samples[i].SampleId);Assert.Equal(space.SequenceIndices[i],samples[i].SequenceIndex);
                var s=bank.Sequences[samples[i].SequenceIndex];Assert.Equal(samples[i].SequenceIndex,s.AnimationId);
                timing[i]=new(samples[i].SampleId,s.AnimationId,samples[i].Weight,s.DurationSeconds,s.RateScale,samples[i].RateScale,samples[i].CachedPlayRate,s.MarkerCount>0);
            }
            Assert.True(AlsSyncRuntime.TryDescribeBlendSpaceTiming(timing,space.LegacyLength,out var description,out _));Assert.True(description.EffectiveLengthSeconds>0);
            var before=samples.ToArray();weights[0]=weights[0] with {Sample=-1};
            Assert.Throws<ArgumentException>(()=>space.BindSamples(weights,100,samples));Assert.Equal(before,samples);
        }
    }
    [Fact]
    public void OriginalMovingSamplesAdvanceThroughCoreMarkerGroupWithRetry()
    {
        var bank=new AlsRefactoredSyncBank(Inputs(),Catalog());
        const string path="/ALS/ALS/Animations/Grounded/WalkRun/BS_Als_WalkRun_Forward.BS_Als_WalkRun_Forward";
        var blend=bank.BlendSpaces[path];Assert.NotEqual(0UL,blend.MarkerMask);Assert.True(blend.AllowMarkers);
        var sample=new AlsAssetSyncSample[1];blend.BindSamples([new(0,1)],0,sample);
        var group=default(AlsAssetSyncGroupHistory);AlsAssetPlayerHistory[] history=[];AlsAssetSampleHistory[] sampleHistory=[];
        for(var frame=0;frame<240;frame++)
        {
            var time=history.Length==0?0:history[0].Time;
            AlsAssetSyncPlayer[] players=[new(0,blend.AssetId,1,AlsAssetSyncKind.BlendSpace,time,1,1,0,1,blend.MarkerMask,LegacyLength:blend.LegacyLength)];
            var result=new AlsAssetPlayerHistory[1];var samples=new AlsAssetSampleHistory[1];
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,group,players,sample,bank.Sequences,bank.Markers,history,sampleHistory,1f/60,result,samples,out var candidate,out var failure),failure.ToString());
            var repeated=new AlsAssetPlayerHistory[1];var repeatedSamples=new AlsAssetSampleHistory[1];
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,group,players,sample,bank.Sequences,bank.Markers,history,sampleHistory,1f/60,repeated,repeatedSamples,out var replay,out _));
            Assert.Equal(candidate,replay);Assert.Equal(result,repeated);Assert.Equal(samples,repeatedSamples);
            Assert.True(candidate.MarkerEnd.Valid);Assert.True(samples[0].Marker.Initialized);
            group=candidate;history=result;sampleHistory=samples;
        }
    }
    [Theory]
    [InlineData("missing")][InlineData("hash")][InlineData("rate")][InlineData("marker")][InlineData("duration")][InlineData("legacy")]
    public void RejectsForeignOrChangedResourceMetadata(string change)
    {
        var root=JsonNode.Parse(Inputs())!;var rows=root["assets"]!.AsArray();
        var sequence=rows.First(r=>r!["markers"] is JsonArray a&&a.Count>0)!;
        switch(change)
        {
            case "missing":rows.RemoveAt(0);break;
            case "hash":root["catalogSha256"]=new string('0',64);break;
            case "rate":sequence["rateScale"]=2;break;
            case "marker":sequence["markers"]![0]!["time"]=.123456;break;
            case "duration":sequence["length"]=900;break;
            case "legacy":rows.First(r=>r!["samples"] is JsonArray)!["legacyLength"]=true;break;
        }
        Assert.Throws<ArgumentException>(()=>new AlsRefactoredSyncBank(root.ToJsonString(),Catalog()));
    }
}
