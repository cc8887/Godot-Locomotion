using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredSyncTraceTests(ITestOutputHelper output)
{
    [Fact]
    public void NativeTicksMatchWithIndependentFiltersWeightsTimeAndMarkerHistory()
    {
        var catalogJson=MantlingHostFixture.Read("refactored_animation_sources");var syncJson=MantlingHostFixture.Read("refactored_sync_inputs");
        byte[] Read(string p)=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p));
        var catalog=new AlsRefactoredAnimationCatalog(catalogJson,Read);var bank=new AlsRefactoredSyncBank(syncJson,catalog);
        var profiles=AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"),catalogJson,Read);
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_sync_trace"));var root=doc.RootElement;
        Assert.True(root.GetProperty("refactored").GetBoolean());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(syncJson))).ToLowerInvariant(),root.GetProperty("syncInputsSha256").GetString());
        var assets=root.GetProperty("assets").EnumerateArray().Select(a=>a.GetProperty("path").GetString()!).ToArray();
        var poseSources=assets.Where(profiles.ContainsKey).ToDictionary(p=>p,p=>new AlsRefactoredBlendPoseSource(profiles[p],catalog));
        var poseSamplers=poseSources.ToDictionary(p=>p.Key,p=>p.Value.CreateSampler());
        int frames=0,sampleFrames=0,changes=0,poseCount=0;float maximum=0,maxCurve=0;double maxPosition=0,maxRotation=0;
        void Near(float expected,float actual,string label)
        {var error=MathF.Abs(expected-actual);maximum=MathF.Max(maximum,error);Assert.True(error<=.00003f,$"{label}: expected={expected:R} actual={actual:R} error={error:R}");}
        int Symbol(string name)=>name=="None"?0:Array.IndexOf(bank.Symbols.ToArray(),name);
        void Position(JsonElement expected,AlsAssetMarkerPosition actual,string label)
        {
            Assert.Equal(Symbol(expected.GetProperty("previous").GetString()!),actual.PreviousSymbol);
            Assert.Equal(Symbol(expected.GetProperty("next").GetString()!),actual.NextSymbol);
            Near(expected.GetProperty("alpha").GetSingle(),actual.Alpha,label);
        }
        void Marker(JsonElement expected,AlsAssetMarkerRecord actual,string label)
        {
            Assert.True(expected.GetProperty("previous").GetInt32()==actual.PreviousIndex,label+" previous index");
            Assert.True(expected.GetProperty("next").GetInt32()==actual.NextIndex,label+" next index");
            Near(expected.GetProperty("previousDistance").GetSingle(),actual.PreviousDistance,label+" previous distance");
            Near(expected.GetProperty("nextDistance").GetSingle(),actual.NextDistance,label+" next distance");
        }
        foreach(var trace in root.GetProperty("traces").EnumerateArray())
        {
            var scenario=trace.GetProperty("scenario").GetInt32();var independent=scenario==6;
            float[] times=[.2f,.2f,.93f,.2f,.33f,.2f,0,0];var epochs=Enumerable.Repeat(1L,8).ToArray();
            var filters=new AlsRefactoredBlendFilterState[8];var caches=Enumerable.Repeat(-1,8).ToArray();
            var group=default(AlsAssetSyncGroupHistory);AlsAssetPlayerHistory[] history=[];AlsAssetSampleHistory[] sampleHistory=[];
            var frameIndex=0;
            foreach(var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var label=$"hz={trace.GetProperty("hz")} scenario={scenario} frame={frameIndex}";var delta=frame.GetProperty("delta").GetSingle();
                var candidates=filters.ToArray();var nextCaches=caches.ToArray();var players=new List<AlsAssetSyncPlayer>();var samples=new List<AlsAssetSyncSample>();
                foreach(var input in frame.GetProperty("input").EnumerateArray())
                {
                    var slot=input.GetProperty("slot").GetInt32();var path=assets[input.GetProperty("asset").GetInt32()];var binding=bank.Assets[path];
                    var reset=input.GetProperty("reset").GetBoolean();if(reset){epochs[slot]++;times[slot]=.1f;candidates[slot]=default;nextCaches[slot]=-1;}
                    Near(input.GetProperty("time").GetSingle(),times[slot],label+" input time");
                    var start=samples.Count;var isBlend=bank.BlendSpaces.TryGetValue(path,out var blend);
                    if(isBlend)
                    {
                        var profile=profiles[path];var raw=new Vector2(input.GetProperty("x").GetSingle(),input.GetProperty("y").GetSingle());
                        candidates[slot]=AlsRefactoredBlendFilter.Advance(candidates[slot],raw,delta,profile.FilterWindows);
                        var filtered=candidates[slot].Output;var weights=new AlsAimGridVertex[3];
                        var count=profile.Weights.Evaluate(new(filtered.X,filtered.Y),nextCaches[slot],weights,out nextCaches[slot]);
                        var bound=new AlsAssetSyncSample[count];blend!.BindSamples(weights.AsSpan(0,count),slot*16,bound);samples.AddRange(bound);
                    }
                    else samples.Add(new(slot*16,binding.SequenceIndex,1));
                    players.Add(new(slot,binding.AssetId,epochs[slot],isBlend?AlsAssetSyncKind.BlendSpace:AlsAssetSyncKind.Sequence,times[slot],
                        input.GetProperty("rate").GetSingle(),input.GetProperty("weight").GetSingle(),start,samples.Count-start,binding.MarkerMask,
                        LegacyLength:isBlend&&blend!.LegacyLength,RequestedInertialization:reset));
                }
                var ticks=players.ToArray();var resolved=samples.ToArray();var result=new AlsAssetPlayerHistory[ticks.Length];var sampleResult=new AlsAssetSampleHistory[resolved.Length];
                bool Evaluate(AlsAssetPlayerHistory[] p,AlsAssetSampleHistory[] s,out AlsAssetSyncGroupHistory next)
                {
                    if(independent)
                    {next=new(0,false,-1,-1,0,-1,0,0,0,0,default,default);return AlsSyncRuntime.TryEvaluateIndependentAssetPlayers(ticks,resolved,bank.Sequences,bank.Markers,history,sampleHistory,delta,p,s,out _);}
                    return AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,group,ticks,resolved,bank.Sequences,bank.Markers,history,sampleHistory,delta,p,s,out next,out _);
                }
                Assert.True(Evaluate(result,sampleResult,out var nextGroup),label+" rejected");
                Assert.Equal(frame.GetProperty("leader").GetInt32(),nextGroup.LeaderPlayerId);
                Near(frame.GetProperty("previousRatio").GetSingle(),nextGroup.PreviousRatio,label+" previous ratio");
                Near(frame.GetProperty("ratio").GetSingle(),nextGroup.Ratio,label+" ratio");
                Assert.Equal(frame.GetProperty("markerSync").GetBoolean(),nextGroup.ValidMarkerMask!=0);
                if(frame.TryGetProperty("markerStart",out var ms))Position(ms,nextGroup.MarkerStart,label+" marker start");
                if(frame.TryGetProperty("markerEnd",out var me))Position(me,nextGroup.MarkerEnd,label+" marker end");
                foreach(var expected in frame.GetProperty("output").EnumerateArray())
                {
                    var slot=expected.GetProperty("slot").GetInt32();var actual=result.Single(p=>p.PlayerId==slot);var ownLabel=label+$" slot={slot}";
                    Near(expected.GetProperty("time").GetSingle(),actual.Time,ownLabel+" time");Near(expected.GetProperty("previous").GetSingle(),actual.DeltaPrevious,ownLabel+" previous");
                    Near(expected.GetProperty("delta").GetSingle(),actual.Delta,ownLabel+" delta");Marker(expected.GetProperty("marker"),actual.Marker,ownLabel);
                    if(expected.TryGetProperty("filteredX",out var fx))
                    {Near(fx.GetSingle(),candidates[slot].Output.X,ownLabel+" filter x");Near(expected.GetProperty("filteredY").GetSingle(),candidates[slot].Output.Y,ownLabel+" filter y");}
                    var expectedSamples=expected.GetProperty("samples").EnumerateArray().ToArray();
                    if(expectedSamples.Length>0)Assert.Equal(expectedSamples.Length,actual.SampleCount);
                    for(var i=0;i<expectedSamples.Length;i++)
                    {
                        var e=expectedSamples[i];var a=sampleResult[actual.SampleStart+i];var l=ownLabel+$" sample={i}";
                        Assert.Equal(slot*16+e.GetProperty("index").GetInt32(),a.SampleId);
                        Near(e.GetProperty("weight").GetSingle(),resolved[actual.SampleStart+i].Weight,l+" weight");
                        Near(e.GetProperty("time").GetSingle(),a.Time,l+" time");Near(e.GetProperty("previous").GetSingle(),a.PreviousTime,l+" previous");
                        Near(e.GetProperty("deltaPrevious").GetSingle(),a.DeltaPrevious,l+" delta previous");Near(e.GetProperty("delta").GetSingle(),a.Delta,l+" delta");
                        Marker(e.GetProperty("marker"),a.Marker,l);sampleFrames++;
                    }
                    if(expected.TryGetProperty("poseReference",out var poseReference))
                    {
                        var path=assets[independent?8:slot];var source=poseSources[path];var timed=new AlsRefactoredTimedBlendSample[actual.SampleCount];
                        for(var i=0;i<timed.Length;i++)
                        {
                            var s=sampleResult[actual.SampleStart+i];var w=resolved[actual.SampleStart+i];
                            timed[i]=new(s.SampleId-slot*16,w.Weight,s.Time);
                        }
                        var pose=new AlsPrecisePose[source.BoneNames.Length];var curves=new AlsInertialCurve[source.CurveNames.Length];
                        poseSamplers[path].SampleTimes(timed,pose,curves);
                        Assert.Equal(source.BoneNames.ToArray(),poseReference.GetProperty("names").EnumerateArray().Select(v=>v.GetString()!));
                        for(var bone=0;bone<pose.Length;bone++)
                        {
                            var e=poseReference.GetProperty("pose")[bone];
                            double[] V(string key)=>e.GetProperty(key).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                            var p=V("position");var q=V("rotation");var s=V("scale");var a=pose[bone];
                            var dp=Math.Sqrt(Math.Pow(a.Position.X-p[0],2)+Math.Pow(a.Position.Y-p[1],2)+Math.Pow(a.Position.Z-p[2],2));
                            var sign=a.Rotation.X*q[0]+a.Rotation.Y*q[1]+a.Rotation.Z*q[2]+a.Rotation.W*q[3]<0?-1:1;
                            var dq=new[]{Math.Abs(a.Rotation.X-sign*q[0]),Math.Abs(a.Rotation.Y-sign*q[1]),Math.Abs(a.Rotation.Z-sign*q[2]),Math.Abs(a.Rotation.W-sign*q[3])}.Max();
                            var ds=new[]{Math.Abs(a.Scale.X-s[0]),Math.Abs(a.Scale.Y-s[1]),Math.Abs(a.Scale.Z-s[2])}.Max();
                            maxPosition=Math.Max(maxPosition,dp);maxRotation=Math.Max(maxRotation,dq);
                            Assert.True(dp<=.01&&dq<=.0001&&ds<=1e-6,$"{ownLabel} bone={bone} p={dp:R} q={dq:R} s={ds:R}");
                        }
                        var nativeCurves=poseReference.GetProperty("curves");var present=0;
                        for(var i=0;i<curves.Length;i++)
                        {
                            Assert.Equal(nativeCurves.TryGetProperty(source.CurveNames[i],out var e),curves[i].Present);
                            if(!curves[i].Present)continue;
                            var error=MathF.Abs(curves[i].Value-e.GetSingle());maxCurve=MathF.Max(maxCurve,error);present++;
                            Assert.True(error<=.0001f,ownLabel+" curve "+source.CurveNames[i]);
                        }
                        Assert.Equal(nativeCurves.EnumerateObject().Count(),present);poseCount++;
                    }
                    times[slot]=actual.Time;
                }
                var retry=new AlsAssetPlayerHistory[result.Length];var retrySamples=new AlsAssetSampleHistory[sampleResult.Length];
                Assert.True(Evaluate(retry,retrySamples,out var retried),label);Assert.Equal(nextGroup,retried);Assert.Equal(result,retry);Assert.Equal(sampleResult,retrySamples);
                if(group.HasLeader&&nextGroup.HasLeader&&group.LeaderPlayerId!=nextGroup.LeaderPlayerId)changes++;
                filters=candidates;caches=nextCaches;group=nextGroup;history=result;sampleHistory=sampleResult;frameIndex++;frames++;
            }
        }
        Assert.Equal(1344,frames);Assert.Equal(384,poseCount);Assert.True(changes>0);
        output.WriteLine($"frames={frames} samples={sampleFrames} leaderChanges={changes} maximum={maximum:R} poses={poseCount} maxP_cm={maxPosition:R} maxQ={maxRotation:R} maxCurve={maxCurve:R}");
    }
}
