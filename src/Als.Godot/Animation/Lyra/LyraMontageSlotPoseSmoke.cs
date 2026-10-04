using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMontageSlotPoseSmoke : Node
{
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Exact(float a,float b,string label)=>Require(BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b),$"{label}: {a:R}/{b:R}");
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Slot pose failed: "+e);GetTree().Quit(1);}}
    private static void Run()
    {
        const string root="res://assets/generated/lyra_als/";
        byte[] Bytes(string path)=>Godot.FileAccess.GetFileAsBytes(root+path);
        using var request=JsonDocument.Parse(Bytes("slot_pose_v1_requests.json"));
        using var capture=JsonDocument.Parse(Bytes("slot_pose_v1_native.json"));var native=capture.RootElement;
        Require(native.GetProperty("schemaVersion").GetInt32()==1&&native.GetProperty("rootMotionMode").GetInt32()==3&&
            native.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Bytes("slot_pose_v1_requests.json")),"Changed Slot pose capture.");
        foreach(var d in native.GetProperty("dependencies").EnumerateObject())Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Bytes(d.Name)),"Stale Slot dependency.");
        var catalog=new LyraMontageCatalog();var bank=LyraLogicalSourceBank.Load(includeMainLean:true,includeLocomotionExtras:true,includeMontageActions:true);
        using var sampler=new LyraMontageTrackSampler(bank,catalog);var slots=new LyraMontageSlotPose(bank,catalog,sampler);
        var basis=new LyraCompositionPoseBuffer(bank);var output=new LyraCompositionPoseBuffer(bank);var repeated=new LyraCompositionPoseBuffer(bank);
        var frameCount=0;var rowCount=0;var poses=0;var curves=0;var attributes=0;var rootChecks=0;var profilePoses=0;var normalized=0;var localAdd=0;var meshAdd=0;var fullSourceHidden=0;
        var retries=0;var rejected=0;double maxP=0,maxQ=0,maxS=0,rootP=0,rootQ=0,rootS=0;
        foreach(var (trace,ti) in native.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var runtime=catalog.CreateRuntime();var input=request.RootElement.GetProperty("traces")[ti];
            foreach(var (n,fi) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var q=input.GetProperty("frames")[fi];var id=new AlsFrameIdentity(fi,(uint)(ti+131),53);string? first=null;
                for(var attempt=0;attempt<2;attempt++)
                {
                    runtime.Begin(id,q.GetProperty("delta").GetSingle());var frozen=runtime.Evaluation.ToArray();
                    var physical=frozen.DistinctBy(e=>e.InstanceId).ToArray();var expected=n.GetProperty("frozen");
                    Require(physical.Length==expected.GetArrayLength(),$"{ti}/{fi}/freeze count");
                    for(var pi=0;pi<physical.Length;pi++)
                    {
                        var p=physical[pi];var e=expected[pi];var label=$"{ti}/{fi}/{pi}";
                        Require(p.ActionDefinitionId==e.GetProperty("asset").GetInt32()&&(p.BlendSnapshot.ProfileId>=0)==e.GetProperty("profile").GetBoolean(),label+"/physical identity");
                        Exact(p.MontagePosition,e.GetProperty("position").GetSingle(),label+"/position");Exact(p.Weight,e.GetProperty("weight").GetSingle(),label+"/weight");
                        Exact(p.DeltaTimeRecord.PreviousPosition,e.GetProperty("previous").GetSingle(),label+"/previous");Exact(p.DeltaTimeRecord.Delta,e.GetProperty("delta").GetSingle(),label+"/delta");
                    }
                    var ai=q.GetProperty("basis").GetInt32();var a=catalog.Definitions[ai];
                    var baseTrack=new AlsMontageEvaluation(1,a.AnimationId,a.Slot,new AlsMontageTrack(a.AnimationId,a.Slot,a.ClipStart,a.ClipRate,a.AdditiveType){ClipEnd=a.ClipEnd}.SamplePosition(.27f),1,ai)
                    {MontagePosition=.27f,AdditiveType=a.AdditiveType,DeltaTimeRecord=new(.233f,.037f)};
                    sampler.Sample(baseTrack,catalog.Metadata[ai].GetProperty("rootMotion").GetBoolean(),basis);
                    var unchanged=Signature(basis);var candidate=runtime.Frame;
                    foreach(var row in n.GetProperty("slots").EnumerateArray())
                    {
                        var slot=new AlsMontageSlot(row.GetProperty("slot").GetInt32());var weights=candidate.SlotWeights(slot);var label=$"{ti}/{fi}/{slot.Id}";
                        Exact(weights.SourceWeight,row.GetProperty("sourceWeight").GetSingle(),label+"/source weight");
                        Exact(weights.SlotNodeWeight,row.GetProperty("slotWeight").GetSingle(),label+"/slot weight");
                        Exact(weights.TotalNodeWeight,row.GetProperty("totalWeight").GetSingle(),label+"/total weight");
                        if(attempt==1)rowCount++;
                        if(!row.TryGetProperty("output",out var pose))continue;
                        slots.Evaluate(candidate,id,slot,basis.Input,output);Require(unchanged==Signature(basis),"Slot mutated its input.");
                        repeated.Copy(output.Input);slots.Evaluate(candidate,id,slot,basis.Input,output);Require(Signature(repeated)==Signature(output),"Repeated Slot evaluation differs.");
                        LyraLogicalSourceSmoke.Compare(pose.GetProperty("pose"),output.Pose,ref maxP,ref maxQ,ref maxS,label,fi,"Slot");
                        LyraSourceCurveSmoke.CheckCurves(bank.Curves,output.Curves,pose.GetProperty("curves"),label,fi,"Slot",ref curves);
                        var attrs=pose.GetProperty("attributes");Require(attrs.GetArrayLength()==output.Attributes.Count(v=>v.Present),label+"/attribute count");
                        for(var i=0;i<output.Attributes.Length;i++)
                        {
                            var identity=bank.Curves.Attributes.Layout[i];var matches=attrs.EnumerateArray().Where(v=>
                                string.Equals(v.GetProperty("name").GetString(),identity.Name,StringComparison.OrdinalIgnoreCase)&&
                                string.Equals(v.GetProperty("bone").GetString(),identity.Bone,StringComparison.OrdinalIgnoreCase)&&
                                v.GetProperty("type").GetString()==identity.Type&&v.GetProperty("namespace").GetString()==identity.Namespace).ToArray();
                            Require(output.Attributes[i].Present==(matches.Length==1),label+"/attribute presence");
                            if(matches.Length==1)Require(output.Attributes[i].Value==matches[0].GetProperty("value").GetInt32(),$"{label}/{identity.Name} integer {output.Attributes[i].Value}/{matches[0].GetProperty("value").GetInt32()}");attributes++;
                        }
                        var present=pose.TryGetProperty("rootMotion",out var rm);Require(present==output.RootMotion.Present,label+"/root presence");
                        if(present)
                        {
                            var value=LyraLogicalSourceBank.ParsePose(rm);var actual=output.RootMotion.Value;var sign=AlsQuaternion.Dot(value.Rotation,actual.Rotation)<0?-1:1;
                            var dp=(value.Position-actual.Position).LengthSquared;var dq=(value.Rotation+actual.Rotation*-sign).LengthSquared;var ds=(value.Scale-actual.Scale).LengthSquared;
                            rootP=Math.Max(rootP,Math.Sqrt(dp));rootQ=Math.Max(rootQ,Math.Sqrt(dq));rootS=Math.Max(rootS,Math.Sqrt(ds));
                            Require(dp<=1e-16&&dq<=1e-20&&ds<=1e-24,$"{label}/root P={Math.Sqrt(dp):R} Q={Math.Sqrt(dq):R} S={Math.Sqrt(ds):R}");rootChecks++;
                        }
                        Require(row.GetProperty("sourceEvaluations").GetInt32()==(weights.SourceWeight>AlsPoseBlender.WeightThreshold?1:0),label+"/source evaluation count");
                        if(attempt==1)
                        {
                            poses++;if(frozen.Any(e=>e.Slot==slot&&e.BlendSnapshot.ProfileId>=0))profilePoses++;
                            if(weights.TotalNodeWeight>1f+AlsPoseBlender.WeightThreshold)normalized++;
                            if(frozen.Any(e=>e.Slot==slot&&e.AdditiveType==1))localAdd++;
                            if(frozen.Any(e=>e.Slot==slot&&e.AdditiveType==2))meshAdd++;
                            if(weights.SourceWeight<=AlsPoseBlender.WeightThreshold)fullSourceHidden++;
                        }
                    }
                    Require(unchanged==Signature(basis),"Input changed after five Slots.");
                    try{slots.Evaluate(candidate,new(id.FrameId+1,id.CharacterId,id.SlotGeneration),new(0),basis.Input,output);throw new Exception("Stale identity accepted.");}
                    catch(ArgumentException){rejected++;}
                    foreach(var command in q.GetProperty("commands").EnumerateArray())
                    {
                        var asset=command.GetProperty("asset").GetInt32();
                        if(command.GetProperty("stop").GetBoolean())
                        {
                            var explicitStop=command.TryGetProperty("instanceStop",out var stop)&&stop.GetBoolean();
                            var active=runtime.Candidate.ToArray().LastOrDefault(v=>v.MontageId==asset&&(explicitStop||v.OwnsActiveActionLookup));
                            if(active.InstanceId>0)Require(runtime.StopInstance(active.InstanceId,command.GetProperty("blend").GetSingle(),catalog.Definitions[asset].Lifecycle.BlendOutOption),"Stop rejected.");
                        }
                        else Require(runtime.PlayAction(asset,command.GetProperty("rate").GetSingle(),command.GetProperty("start").GetSingle(),stopGroup:command.GetProperty("stopGroup").GetBoolean()),"Play rejected.");
                    }
                    Require(frozen.SequenceEqual(runtime.Evaluation.ToArray()),"Commands changed frozen Slot output.");
                    var signature=JsonSerializer.Serialize(runtime.Candidate.ToArray());
                    if(attempt==0){first=signature;runtime.Discard();retries++;}
                    else {Require(first==signature,"Cancelled Slot replay changed physical history.");runtime.Commit(id);}
                }
                frameCount++;
            }
        }
        Require(frameCount==15870&&rowCount==frameCount*5&&retries==frameCount&&profilePoses>0&&normalized>0&&localAdd>0&&meshAdd>0&&rootChecks>0&&fullSourceHidden>0,"Incomplete actual Slot coverage.");
        GD.Print($"LYRA_SLOT_POSE_GODOT_OK frames={frameCount} slotRows={rowCount} poses={poses} profiles={profilePoses} normalized={normalized} localAdd={localAdd} meshAdd={meshAdd} hiddenSource={fullSourceHidden} retry={retries} rejected={rejected} curves={curves} attributes={attributes} roots={rootChecks} positionCm={maxP:R} quaternion={maxQ:R} scale={maxS:R} rootP={rootP:R} rootQ={rootQ:R} rootS={rootS:R} originalNodes=true mainIntegration=false");
    }
    private static string Signature(LyraCompositionPoseBuffer b)=>JsonSerializer.Serialize(new{b.Pose,b.Curves,b.Attributes,b.RootMotion});
}
