using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraAimWeightSmoke : Node
{
    private const string Root="res://assets/generated/lyra_als/";
    private static JsonDocument Load(string name)=>JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+name));
    private static void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);}
    private static void Exact(double actual,double expected,string label)
    {Require(BitConverter.DoubleToInt64Bits(actual)==BitConverter.DoubleToInt64Bits(expected),$"{label}: actual={actual:R} expected={expected:R}");}
    private static void Exact(float actual,float expected,string label)
    {Require(BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(expected),$"{label}: actual={actual:R} expected={expected:R}");}
    public override void _Ready(){try{Run();GetTree().Quit();}catch(Exception error){GD.PushError("Aiming weight failed: "+error);GetTree().Quit(1);}}
    private static void Run()
    {
        using var native=Load("aim_weight_v1_native.json");using var requests=Load("aim_weight_v1_requests.json");using var policy=Load("aim_weight_v1_policy.json");
        var data=native.RootElement;var graphs=LyraMainLayerGraphCatalog.Load();var frames=0;var pins=0;var hidden=0;var retries=0;var rejects=0;var mixed=0;
        Require(data.GetProperty("schemaVersion").GetInt32()==1 && policy.RootElement.GetProperty("stage").GetString()=="OriginalProviderAimWeightUpdate" &&
            !policy.RootElement.GetProperty("graphEvaluation").GetBoolean(),"Wrong Aiming weight fixture");
        foreach(var dep in data.GetProperty("dependencies").EnumerateObject())
            Require(dep.Value.GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+dep.Name)),"Stale Aiming weight dependency");
        Require(data.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"aim_weight_v1_requests.json")) &&
            data.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+"aim_weight_v1_policy.json")),"Stale Aiming weight fixture");
        void Reject(Action action,string label)
        {try{action();}catch(InvalidOperationException){rejects++;return;}throw new InvalidOperationException("Accepted bad weight update: "+label);}
        foreach(var (trace,ti) in data.GetProperty("traces").EnumerateArray().Select((v,i)=>(v,i)))
        {
            var profile=trace.GetProperty("profile").GetString()!;var host=new LyraAimWeightHost(graphs,profile);var foreign=new LyraAimWeightHost(graphs,profile);
            var definition=new LyraAimingGraphDefinition(graphs,profile);var feedback=0f;
            Require(definition.IdleAsset==policy.RootElement.GetProperty("policies").GetProperty(profile).GetProperty("IdleAimOffset").GetProperty("value").GetString() &&
                definition.RelaxedAsset==policy.RootElement.GetProperty("policies").GetProperty(profile).GetProperty("RelaxedAimOffset").GetProperty("value").GetString(),"Changed Aiming asset bindings");
            foreach(var (row,i) in trace.GetProperty("frames").EnumerateArray().Select((v,i)=>(v,i)))
            {
                var frame=requests.RootElement.GetProperty("traces")[ti].GetProperty("frames")[i];var main=frame.GetProperty("main");var label=$"AimWeight/{ti}/{i}";
                var before=host.Weights;Exact(before.HipFire,row.GetProperty("before").GetProperty("hipFire").GetDouble(),label+"/before hip");
                Exact(before.Aim,row.GetProperty("before").GetProperty("aim").GetDouble(),label+"/before aim");Exact(feedback,row.GetProperty("feedbackBefore").GetSingle(),label+"/retained feedback");
                var input=new LyraAimWeightInput(main.GetProperty("IsCrouching").GetBoolean(),main.GetProperty("IsOnGround").GetBoolean(),main.GetProperty("GameplayTag_IsADS").GetBoolean(),
                    main.GetProperty("TimeSinceFiredWeapon").GetDouble(),row.GetProperty("rootYaw").GetDouble(),main.GetProperty("HasAcceleration").GetBoolean(),frame.GetProperty("delta").GetDouble(),feedback);
                var c=host.Prepare(input);Reject(()=>host.Prepare(input),label+"/duplicate prepare");
                var other=foreign.Prepare(input);Reject(()=>host.Validate(other),label+"/foreign candidate");foreign.Cancel();
                Exact(c.Weights.HipFire,row.GetProperty("after").GetProperty("hipFire").GetDouble(),label+"/double hip");
                Exact(c.Weights.Aim,row.GetProperty("after").GetProperty("aim").GetDouble(),label+"/double aim");
                if(frame.GetProperty("visited").GetBoolean())
                {
                    var exposed=definition.Expose(c.Weights,frame.GetProperty("AimYaw").GetDouble(),frame.GetProperty("AimPitch").GetDouble());
                    Exact(exposed.Blend,row.GetProperty("blendPin").GetSingle(),label+"/blend pin");
                    foreach(var name in new[]{"relaxed","idle"})
                    {var pin=row.GetProperty("pins").GetProperty(name);Exact(exposed.Yaw,pin.GetProperty("x").GetSingle(),label+"/yaw pin");Exact(exposed.Pitch,pin.GetProperty("y").GetSingle(),label+"/pitch pin");Exact(1f,pin.GetProperty("alpha").GetSingle(),label+"/node alpha");}
                    pins++;
                }
                else hidden++;
                var saved=c;host.Cancel();Require(host.Weights==before,label+"/cancel published");Reject(()=>host.Validate(saved),label+"/cancelled candidate");
                c=host.Prepare(input);Require(c.Weights==saved.Weights,label+"/retry diverged");Reject(()=>host.Validate(saved),label+"/old candidate after retry");
                host.Validate(c);host.Commit(c);Reject(()=>host.Commit(c),label+"/duplicate commit");Require(host.Weights==saved.Weights,label+"/commit diverged");
                if(frame.GetProperty("evaluateMain").GetBoolean())
                    feedback=frame.GetProperty("finalFeedback").TryGetProperty("applyHipfireOverridePose",out var value)?value.GetSingle():0;
                Exact(feedback,row.GetProperty("feedbackAfter").GetSingle(),label+"/curve copy boundary");
                mixed+=c.Weights.Aim>1e-5&&c.Weights.Aim<1-1e-5?1:0;frames++;retries++;
            }
        }
        var counts=data.GetProperty("counts");Require(frames==11340 && retries==frames && pins==counts.GetProperty("pins").GetInt32() && hidden==counts.GetProperty("hidden").GetInt32() && mixed==counts.GetProperty("mixed").GetInt32(),"Incomplete Aiming weight coverage");
        GD.Print($"LYRA_AIM_WEIGHT_GODOT_OK frames={frames} pins={pins} hidden={hidden} mixed={mixed} retries={retries} rejected={rejects} graphs=3 nodes=8 doubleExact=true exposedPinsExact=true pose=false production=false");
    }
}
