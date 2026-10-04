using Godot;
using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraRootMovementSmoke:Node3D
{
    private static void Require(bool v,string label){if(!v)throw new InvalidOperationException(label);}
    private static void Compare(AlsPrecisePose a,AlsPrecisePose b,string label)
    {Require((a.Position-b.Position).LengthSquared<=1e-16&&(a.Scale-b.Scale).LengthSquared<=1e-24&&Math.Min((a.Rotation+-b.Rotation).LengthSquared,(a.Rotation+b.Rotation).LengthSquared)<=1e-20,label+" P/Q/S "+a+" / "+b);}
    public override void _Ready()
    {
        try
        {
            using var resources=new LyraLocomotionResources(includeMontageActions:true);var catalog=new LyraMontageCatalog();using var reader=new LyraMontageMovementReader(resources.Catalog.Bank,catalog);
            const string root="res://assets/generated/lyra_als/";using var input=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"root_movement_v1_requests.json"));
            using var capture=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"root_movement_v1_native.json"));var native=capture.RootElement;
            Require(native.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"root_movement_v1_requests.json")),"Stale root requests.");
            Require(native.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"root_movement_v1_policy.json")),"Stale root policy.");
            int traces=0,frames=0,present=0,retries=0;
            foreach(var trace in input.RootElement.GetProperty("traces").EnumerateArray())
            {
                var bank=catalog.CreateRuntime();int fi=0;
                foreach(var f in trace.GetProperty("frames").EnumerateArray())
                {
                    var expected=native.GetProperty("trace").GetProperty("traces")[traces].GetProperty("frames")[fi];var id=new AlsFrameIdentity(fi,(uint)traces+1,1);
                    float delta=f.GetProperty("delta").GetSingle();var requests=f.GetProperty("plays").EnumerateArray().Select(p=>new AlsMontageActionRequest(p.GetProperty("asset").GetInt32(),p.GetProperty("rate").GetSingle(),p.GetProperty("start").GetSingle(),p.GetProperty("stopGroup").GetBoolean())).ToArray();
                    AlsMontageStopRequest[] stops=f.GetProperty("stop").GetInt32() is var stop&&stop>=0?[new(stop,f.GetProperty("stopTime").GetSingle())]:[];
                    void Prepare()=>bank.BeginWithActionRequests(id,delta,requests,stops);
                    Prepare();var range=bank.RootMotionRange;var motion=reader.Read(range);var owner=bank.CandidateRootMotionInstance;
                    bank.Discard();Prepare();Require(range==bank.RootMotionRange&&motion==reader.Read(bank.RootMotionRange)&&owner==bank.CandidateRootMotionInstance,"Root retry differs.");retries++;
                    Require(motion.Present==expected.GetProperty("present").GetBoolean(),$"Root presence trace={traces} frame={fi}");
                    Compare(motion.Present?motion.Value:AlsPrecisePose.Identity,LyraLogicalSourceBank.ParsePose(expected.GetProperty("local")),$"Root local trace={traces} frame={fi}");
                    Require(owner==expected.GetProperty("after").GetInt64(),$"Root owner trace={traces} frame={fi}");
                    var actor=LyraLogicalSourceBank.ParsePose(expected.GetProperty("actor"));var component=LyraLogicalSourceBank.ParsePose(expected.GetProperty("component"));
                    var world=AlsAnimationRootMotionConversion.ToWorld(motion.Present?motion.Value:AlsPrecisePose.Identity,actor,component);
                    Compare(world,LyraLogicalSourceBank.ParsePose(expected.GetProperty("world")),$"Root world trace={traces} frame={fi}");
                    var current=new AlsDoubleVector(120,-90,-170);var velocity=AlsAnimationRootMotionConversion.Velocity(world,delta,current,f.GetProperty("falling").GetBoolean());
                    var v=expected.GetProperty("constrained");Require((velocity-new AlsDoubleVector(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble())).LengthSquared<=1e-12,$"Root velocity trace={traces} frame={fi}");
                    if(motion.Present)present++;bank.Commit(id);frames++;fi++;
                }
                traces++;
            }
            GD.Print($"LYRA_ROOT_MOVEMENT_NATIVE_GODOT_OK traces={traces} frames={frames} present={present} retries={retries} wholeWorld=false");GetTree().Quit();
        }
        catch(Exception e){GD.PushError("Root movement failed: "+e);GetTree().Quit(1);}
    }
}
