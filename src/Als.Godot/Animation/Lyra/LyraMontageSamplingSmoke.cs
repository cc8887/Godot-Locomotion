using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMontageSamplingSmoke : Node
{
    private static void Require(bool value,string label) { if(!value)throw new InvalidOperationException(label); }
    private static void Exact(float a,float b,string label) => Require(BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b),$"{label}: {a:R}/{b:R}");
    public override void _Ready()
    { try { Run();GetTree().Quit(); } catch(Exception e) { GD.PushError("Montage sampling failed: "+e);GetTree().Quit(1); } }
    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        byte[] Bytes(string path)=>Godot.FileAccess.GetFileAsBytes(root+path);
        using var requests=JsonDocument.Parse(Bytes("montage_sampling_v1_requests.json"));
        using var capture=JsonDocument.Parse(Bytes("montage_sampling_v1_native.json"));
        var native=capture.RootElement;
        Require(native.GetProperty("schemaVersion").GetInt32()==1&&native.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Bytes("montage_sampling_v1_requests.json")),"Stale time capture.");
        foreach(var d in native.GetProperty("dependencies").EnumerateObject())Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Bytes(d.Name)),"Stale sampling dependency.");
        var catalog=new LyraMontageCatalog();var history=new Dictionary<(int,int),AlsMontageEvaluation[]>();
        var frames=0;var frozen=0;var retry=0;var retained=0;var zero=0;var reverse=0;var dual=0;
        foreach(var (trace,ti) in native.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var runtime=catalog.CreateRuntime();var input=requests.RootElement.GetProperty("traces")[ti];
            foreach(var (row,fi) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=input.GetProperty("frames")[fi];var id=new AlsFrameIdentity(fi,(uint)(71+ti),31);string? signature=null;
                for(var attempt=0;attempt<2;attempt++)
                {
                    runtime.Begin(id,frame.GetProperty("delta").GetSingle());var entries=runtime.Evaluation.ToArray();
                    var physical=entries.DistinctBy(v=>v.InstanceId).ToArray();var expected=row.GetProperty("frozen");
                    Require(physical.Length==expected.GetArrayLength(),$"{ti}/{fi}/physical count");
                    for(var pi=0;pi<physical.Length;pi++)
                    {
                        var e=physical[pi];var n=expected[pi];var label=$"{ti}/{fi}/{pi}";
                        Require(e.ActionDefinitionId==n.GetProperty("asset").GetInt32(),label+"/asset");
                        Exact(e.MontagePosition,n.GetProperty("position").GetSingle(),label+"/position");
                        Exact(e.Weight,n.GetProperty("weight").GetSingle(),label+"/weight");
                        Exact(e.DeltaTimeRecord.PreviousPosition,n.GetProperty("previous").GetSingle(),label+"/previous");
                        Exact(e.DeltaTimeRecord.Delta,n.GetProperty("delta").GetSingle(),label+"/delta");
                        foreach(var t in entries.Where(v=>v.InstanceId==e.InstanceId))Require(t.DeltaTimeRecord==e.DeltaTimeRecord&&t.MontagePosition==e.MontagePosition,label+"/track interval");
                        if(attempt==1)
                        {
                            frozen++;if(e.DeltaTimeRecord.Delta<0)reverse++;if(frame.GetProperty("delta").GetSingle()==0)zero++;
                            if(!runtime.Candidate.ToArray().Single(v=>v.InstanceId==e.InstanceId).Playing&&e.DeltaTimeRecord.Delta!=0)retained++;
                            if(entries.Count(v=>v.InstanceId==e.InstanceId)>1)dual++;
                        }
                    }
                    foreach(var q in frame.GetProperty("commands").EnumerateArray())
                    {
                        var asset=q.GetProperty("asset").GetInt32();
                        if(q.GetProperty("stop").GetBoolean())
                        {
                            var instanceStop=q.TryGetProperty("instanceStop",out var explicitStop)&&explicitStop.GetBoolean();
                            var active=runtime.Candidate.ToArray().LastOrDefault(v=>v.MontageId==asset&&(instanceStop||v.OwnsActiveActionLookup));
                            if(active.InstanceId>0)Require(runtime.StopInstance(active.InstanceId,q.GetProperty("blend").GetSingle(),catalog.Definitions[asset].Lifecycle.BlendOutOption),"Stop rejected.");
                        }
                        else Require(runtime.PlayAction(asset,q.GetProperty("rate").GetSingle(),q.GetProperty("start").GetSingle(),stopGroup:q.GetProperty("stopGroup").GetBoolean()),"Play rejected.");
                    }
                    Require(entries.SequenceEqual(runtime.Evaluation.ToArray()),"Commands mutated frozen time.");
                    var current=JsonSerializer.Serialize(runtime.Candidate.ToArray());
                    if(attempt==0){signature=current;runtime.Discard();retry++;}
                    else {Require(signature==current,"Retry changed physical time.");history.Add((ti,fi),entries);runtime.Commit(id);}
                }
                frames++;
            }
        }
        Require(frames==15870&&retry==frames&&retained>0&&zero>0&&reverse>0&&dual>0,"Incomplete physical interval coverage.");
        var bank=LyraLogicalSourceBank.Load(includeMainLean:true,includeLocomotionExtras:true,includeMontageActions:true);
        using var sampler=new LyraMontageTrackSampler(bank,catalog);
        using var trackRequests=JsonDocument.Parse(Bytes("montage_sampling_v1_track_requests.json"));
        using var tracks=JsonDocument.Parse(Bytes("montage_sampling_v1_tracks.json"));
        Require(native.GetProperty("trackRequestSha256").GetString()==LyraLogicalSourceBank.Sha(Bytes("montage_sampling_v1_track_requests.json"))&&native.GetProperty("trackSha256").GetString()==LyraLogicalSourceBank.Sha(Bytes("montage_sampling_v1_tracks.json")),"Changed track capture.");
        var queries=trackRequests.RootElement.GetProperty("samples");var rows=tracks.RootElement.GetProperty("rows");Require(queries.GetArrayLength()==rows.GetArrayLength(),"Missing track samples.");
        var output=new LyraCompositionPoseBuffer(bank);var copy=new LyraCompositionPoseBuffer(bank);
        var samples=0;var rootChecks=0;var curveValues=0;var attributeValues=0;double maxP=0,maxQ=0,maxS=0,rootMaxP=0,rootMaxQ=0,rootMaxS=0;
        var assets=new HashSet<int>();var covered=new HashSet<(int,int)>();
        for(var qi=0;qi<queries.GetArrayLength();qi++)
        {
            var q=queries[qi];var n=rows[qi];var asset=q.GetProperty("asset").GetInt32();var track=q.GetProperty("track").GetInt32();var a=catalog.Definitions[asset];
            var t=track==0?new AlsMontageTrack(a.AnimationId,a.Slot,a.ClipStart,a.ClipRate,a.AdditiveType){ClipEnd=a.ClipEnd}:a.AdditionalTracks[track-1];
            var position=q.GetProperty("position").GetSingle();var ti=q.GetProperty("trace").GetInt32();
            var e=ti>=0?history[(ti,q.GetProperty("frame").GetInt32())].First(v=>v.ActionDefinitionId==asset&&v.Slot==t.Slot&&BitConverter.SingleToInt32Bits(v.MontagePosition)==BitConverter.SingleToInt32Bits(position)):
                new AlsMontageEvaluation(1,t.AnimationId,t.Slot,t.SamplePosition(position),1,asset){MontagePosition=position,AdditiveType=t.AdditiveType,DeltaTimeRecord=new(q.GetProperty("previous").GetSingle(),q.GetProperty("delta").GetSingle())};
            Exact(e.Position,n.GetProperty("time").GetSingle(),$"sample{qi}/time");
            Exact((float)((double)e.Position-e.DeltaTimeRecord.Delta),n.GetProperty("sequencePrevious").GetSingle(),$"sample{qi}/previous");
            var extract=q.GetProperty("extract").GetBoolean();
            Require(n.GetProperty("extract").GetBoolean()==(extract&&bank.Get(bank.SlotForSource(catalog.SequencePaths[e.AnimationId])).EnableRootMotion),"Extraction flag differs.");
            sampler.Sample(e,extract,output,false);Check(n.GetProperty("sequence"),"sequence");
            sampler.Sample(e,extract,output);Check(n.GetProperty("output"),"combined");copy.Copy(output.Input);sampler.Sample(e,extract,output);
            Require(copy.Pose.SequenceEqual(output.Pose)&&copy.Curves.SequenceEqual(output.Curves)&&copy.Attributes.SequenceEqual(output.Attributes)&&copy.RootMotion==output.RootMotion,"Repeated sample changed output.");
            assets.Add(asset);covered.Add((asset,track));samples++;
            void Check(JsonElement expected,string stage)
            {
                LyraLogicalSourceSmoke.Compare(expected.GetProperty("pose"),output.Pose,ref maxP,ref maxQ,ref maxS,$"track{asset}/{track}",e.Position,stage);
                LyraSourceCurveSmoke.CheckCurves(bank.Curves,output.Curves,expected.GetProperty("curves"),"track",e.Position,stage,ref curveValues);
                var attributes=expected.GetProperty("attributes");Require(attributes.GetArrayLength()==output.Attributes.Count(v=>v.Present),"Missing or unknown track attributes.");
                for(var ai=0;ai<output.Attributes.Length;ai++)
                {
                    var id=bank.Curves.Attributes.Layout[ai];var found=attributes.EnumerateArray().Where(v=>string.Equals(v.GetProperty("name").GetString(),id.Name,StringComparison.OrdinalIgnoreCase)&&string.Equals(v.GetProperty("bone").GetString(),id.Bone,StringComparison.OrdinalIgnoreCase)&&v.GetProperty("type").GetString()==id.Type&&v.GetProperty("namespace").GetString()==id.Namespace).ToArray();
                    Require(output.Attributes[ai].Present==(found.Length==1),$"sample{qi}/{stage}/attribute {id}");
                    if(found.Length==1)Require(output.Attributes[ai].Value==found[0].GetProperty("value").GetInt32(),"Track attribute value differs.");attributeValues++;
                }
                var present=expected.TryGetProperty("rootMotion",out var rm);Require(present==output.RootMotion.Present,"Root attribute presence differs.");
                if(!present)return;
                Require(rm.GetProperty("name").GetString()=="RootMotionDelta"&&rm.GetProperty("bone").GetString()=="root"&&rm.GetProperty("namespace").GetString()=="bone"&&rm.GetProperty("type").GetString()=="/Script/Engine.TransformAnimationAttribute","Root attribute identity differs.");
                var value=LyraLogicalSourceBank.ParsePose(rm);var sign=AlsQuaternion.Dot(value.Rotation,output.RootMotion.Value.Rotation)<0?-1:1;
                var dp=(value.Position-output.RootMotion.Value.Position).LengthSquared;var dq=(value.Rotation+output.RootMotion.Value.Rotation*-sign).LengthSquared;var ds=(value.Scale-output.RootMotion.Value.Scale).LengthSquared;
                rootMaxP=Math.Max(rootMaxP,Math.Sqrt(dp));rootMaxQ=Math.Max(rootMaxQ,Math.Sqrt(dq));rootMaxS=Math.Max(rootMaxS,Math.Sqrt(ds));
                Require(dp<=1e-16&&dq<=1e-20&&ds<=1e-24,$"sample{qi}/root: P={Math.Sqrt(dp)} Q={Math.Sqrt(dq)} S={Math.Sqrt(ds)}");rootChecks++;
            }
        }
        Require(samples==2522&&assets.Count==45&&covered.Count==60&&rootChecks>0,"Incomplete track coverage.");
        GD.Print($"LYRA_MONTAGE_SAMPLING_GODOT_OK frames={frames} frozen={frozen} retry={retry} retained={retained} zero={zero} reverse={reverse} dual={dual} samples={samples} tracks={covered.Count} rootChecks={rootChecks} curves={curveValues} attributes={attributeValues} positionCm={maxP:R} quaternion={maxQ:R} scale={maxS:R} rootP={rootMaxP:R} rootQ={rootMaxQ:R} rootS={rootMaxS:R} exactTime=true slotPose=false production=false");
    }
}
