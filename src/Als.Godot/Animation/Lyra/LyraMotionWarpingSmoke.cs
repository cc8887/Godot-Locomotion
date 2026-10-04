using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMotionWarpingSmoke:Node3D
{
    private double _maxP,_maxQ,_maxS;
    private int _rejected;
    private static void Require(bool v,string label){if(!v)throw new InvalidOperationException(label);}
    private void Reject(Action a){try{a();}catch(InvalidOperationException){_rejected++;return;}throw new InvalidOperationException("Invalid MotionWarping lifecycle accepted.");}
    private static AlsPrecisePose Pose(JsonElement p)=>LyraLogicalSourceBank.ParsePose(p);
    private void Compare(in AlsPrecisePose a,in AlsPrecisePose b,string label)
    {
        double p=Math.Sqrt((a.Position-b.Position).LengthSquared),q=Math.Sqrt(Math.Min((a.Rotation+-b.Rotation).LengthSquared,(a.Rotation+b.Rotation).LengthSquared)),s=Math.Sqrt((a.Scale-b.Scale).LengthSquared);
        _maxP=Math.Max(_maxP,p);_maxQ=Math.Max(_maxQ,q);_maxS=Math.Max(_maxS,s);
        Require(p<=1e-8&&q<=1e-10&&s<=1e-12,$"{label} P={p:R} Q={q:R} S={s:R} actual={a} expected={b}");
    }
    public override void _Ready()
    {
        try
        {
            using var resources=new LyraLocomotionResources(includeMontageActions:true);var catalog=new LyraMontageCatalog();var source=resources.Movement(catalog);
            const string root="res://assets/generated/lyra_als/";
            using var pd=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"motion_warping_v1_policy.json"));var policy=pd.RootElement;
            using var ud=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"motion_warping_v1_usage.json"));var usage=ud.RootElement;
            using var rd=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"motion_warping_v1_requests.json"));var request=rd.RootElement;
            using var nd=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"motion_warping_v1_native.json"));var native=nd.RootElement;
            Require(usage.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"motion_warping_v1_policy.json"))&&
                native.GetProperty("usageSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"motion_warping_v1_usage.json"))&&
                native.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"motion_warping_v1_requests.json")),"Stale MotionWarping closure.");
            foreach(var d in policy.GetProperty("dependencies").EnumerateObject())Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)),"Stale MotionWarping dependency.");
            var windows=resources.MotionWarping(catalog).Windows;
            Require(request.GetProperty("montages").EnumerateArray().Select(v=>v.GetString()).SequenceEqual(catalog.Paths),"Native Montage IDs differ.");
            int traces=0,frames=0,retries=0,nonzero=0,active=0,disabled=0,bankContextFrames=0;
            foreach(var trace in request.GetProperty("traces").EnumerateArray())
            {
                var runtime=new AlsMotionWarpingRuntime((uint)traces+1,1,windows,source);int fi=0;var bank=catalog.CreateRuntime();bool bankContext=trace.GetProperty("mode").GetString()!="seek";
                var n= native.GetProperty("trace").GetProperty("traces")[traces];var offset=Pose(n.GetProperty("settings").GetProperty("baseOffset"));
                Require(!n.GetProperty("settings").GetProperty("searchSegments").GetBoolean(),"Changed original segment search.");
                foreach(var f in trace.GetProperty("frames").EnumerateArray())
                {
                    var expected=n.GetProperty("frames")[fi];var c=expected.GetProperty("context");var context=new AlsMotionWarpingContext(c.GetProperty("asset").GetInt32(),c.GetProperty("previous").GetSingle(),c.GetProperty("current").GetSingle(),c.GetProperty("weight").GetSingle(),c.GetProperty("rate").GetSingle(),f.GetProperty("delta").GetSingle());
                    if(bankContext)
                    {
                        var plays=f.GetProperty("plays").EnumerateArray().Select(p=>new AlsMontageActionRequest(p.GetProperty("asset").GetInt32(),p.GetProperty("rate").GetSingle(),p.GetProperty("start").GetSingle())).ToArray();
                        int stop=f.GetProperty("stop").GetInt32();AlsMontageStopRequest[] stops=stop<0?[]:[new(stop,f.GetProperty("stopTime").GetSingle())];
                        bank.BeginWithActionRequests(new(fi,(uint)traces+1,1),context.Delta,plays,stops);
                        var actualContext=LyraMontageMovementReader.WarpContext(bank,context.Delta);
                        Require(actualContext==context,$"Actual Montage context differs trace={traces} frame={fi}: {actualContext} vs {context}");
                        context=actualContext;bankContextFrames++;
                    }
                    var id=new AlsFrameIdentity(fi,(uint)traces+1,1);int operation=f.GetProperty("targetOperation").GetInt32();
                    AlsMotionWarpingTargetRequest[] targetRequests=operation==0?[]:[new(operation==1?AlsMotionWarpingTargetOperation.Set:AlsMotionWarpingTargetOperation.Remove,
                        new("Align",Pose(f.GetProperty("target")),f.GetProperty("warpPaused").GetBoolean(),f.GetProperty("rootPaused").GetBoolean()))];
                    var actor=Pose(expected.GetProperty("actor"));var visual=Pose(expected.GetProperty("visualRoot"));var local=Pose(expected.GetProperty("local"));bool present=expected.GetProperty("present").GetBoolean();
                    if(bankContext){var rootMotion=source.Read(bank.RootMotionRange);Require(rootMotion.Present==present,"Live bank root presence differs.");Compare(rootMotion.Present?rootMotion.Value:AlsPrecisePose.Identity,local,"Live bank extracted root");}
                    AlsMotionWarpingCandidate Begin()=>runtime.Begin(id,present,local,context,actor,visual,offset,targetRequests,f.GetProperty("disable").GetBoolean());
                    var prior=runtime.Committed;var first=Begin();runtime.Cancel();Require(prior.SequenceEqual(runtime.Committed),"MotionWarping cancellation committed history.");Reject(()=>runtime.Commit(first));
                    var current=Begin();Require(first.Warped==current.Warped&&first.Modifiers.SequenceEqual(current.Modifiers),"MotionWarping retry differs.");Reject(()=>Begin());retries++;
                    Compare(current.Warped,Pose(expected.GetProperty("warped")), $"trace={traces} frame={fi} warp");
                    var world=AlsAnimationRootMotionConversion.ToWorld(current.Warped,actor,Pose(expected.GetProperty("component")));Compare(world,Pose(expected.GetProperty("world")), $"trace={traces} frame={fi} world");
                    var mods=expected.GetProperty("modifiers");Require(mods.GetArrayLength()==current.Modifiers.Length,$"Modifier count trace={traces} frame={fi}");
                    for(int mi=0;mi<mods.GetArrayLength();mi++)
                    {
                        var e=mods[mi];var m=current.Modifiers[mi];Require(m.Id==e.GetProperty("id").GetInt64()&&(int)m.State==e.GetProperty("state").GetInt32()&&catalog.Paths[m.Window.AssetId]==e.GetProperty("animation").GetString(),$"Modifier identity/state trace={traces} frame={fi}");
                        Require(m.Window.Start==e.GetProperty("start").GetSingle()&&m.Window.End==e.GetProperty("end").GetSingle()&&m.Previous==e.GetProperty("previous").GetSingle()&&m.Current==e.GetProperty("current").GetSingle()&&
                            m.Weight==e.GetProperty("weight").GetSingle()&&m.Rate==e.GetProperty("rate").GetSingle()&&m.ActualStart==e.GetProperty("actualStart").GetSingle(),"Modifier clocks differ.");
                        Compare(m.StartTransform,Pose(e.GetProperty("startTransform")),"Modifier start");Compare(m.TotalWindow,Pose(e.GetProperty("totalWindow")),"Modifier total");
                        Compare(m.Target,Pose(e.GetProperty("target")),"Modifier target");Compare(m.Offset,Pose(e.GetProperty("offset")),"Modifier offset");
                        Require(m.OffsetPresent==e.GetProperty("offsetPresent").GetBoolean()&&m.RootPaused==e.GetProperty("rootPaused").GetBoolean()&&m.WarpPaused==e.GetProperty("warpPaused").GetBoolean(),"Modifier presence/pause differs.");
                        if(m.State==AlsMotionWarpingModifierState.Active)active++;else if(m.State==AlsMotionWarpingModifierState.Disabled)disabled++;
                    }
                    if(!current.Warped.Position.NearlyZero(1e-9))nonzero++;
                    runtime.Commit(current);if(bankContext)bank.Commit(id);Reject(()=>runtime.Commit(current));frames++;fi++;
                }
                traces++;
            }
            Require(traces==120&&frames==16800&&nonzero==3030&&active>0&&disabled>0&&bankContextFrames==15120,"Incomplete MW scope.");
            GD.Print($"LYRA_MOTION_WARPING_NATIVE_GODOT_OK traces={traces} frames={frames} nonzero={nonzero} active={active} disabled={disabled} retries={retries} rejected={_rejected} positionCm={_maxP:R} quaternion={_maxQ:R} scale={_maxS:R} bankContextFrames={bankContextFrames} recordedContextForSeek=true capsuleIntegrated=false wholeMainNative=false");GetTree().Quit();
        }
        catch(Exception e){GD.PushError("MotionWarping failed: "+e);GetTree().Quit(1);}
    }
}
