using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraWeaponMontageSmoke:Node3D
{
    private readonly List<(LyraWeaponMontageBank Bank,LyraWeaponModelBinding Model)> _models=[];
    private int _physicsFrame,_published,_modelRetries,_modelRejects;
    private bool _render;
    private int _captures;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Exact(float a,float b,string label)=>Require(BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b),$"{label}: {a:R}/{b:R}");
    private static void Reject(Action action)
    {try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or ObjectDisposedException){return;}throw new InvalidOperationException("Invalid weapon operation accepted.");}
    public override void _Ready()
    {
        try
        {
            _render=OS.GetCmdlineUserArgs().Contains("--weapon-montage-render");
            var resources=new LyraWeaponResources();RunNative(resources);
            int index=0;
            foreach(string kind in new[]{"pistol","rifle","shotgun"})
            {
                var actor=new Node3D{Position=new((index-1)*.9f,.5f,0)};AddChild(actor);
                var model=new LyraWeaponModelBinding(actor,resources,kind);_models.Add((new(new(resources,kind),(uint)(401+index++),1),model));
                if(_render)LyraWeaponResourcesSmoke.PreviewMaterial(model.Model,kind);
            }
            if(_render)
            {
                var camera=new Camera3D{Position=new(0,1.1f,2.2f),Current=true};AddChild(camera);camera.LookAt(new(0,.5f,0));
                AddChild(new DirectionalLight3D{RotationDegrees=new(-50,-25,0),LightEnergy=1.5f});
            }
        }
        catch(Exception e){Fail(e);}
    }
    private static void RunNative(LyraWeaponResources resources)
    {
        const string root="res://assets/generated/lyra_als/";
        byte[] Bytes(string name)=>Godot.FileAccess.GetFileAsBytes(root+name);
        using var requests=JsonDocument.Parse(Bytes("weapon_montage_v1_requests.json"));
        using var capture=JsonDocument.Parse(Bytes("weapon_montage_v1_native.json"));var native=capture.RootElement;
        Require(native.GetProperty("schemaVersion").GetInt32()==1&&native.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Bytes("weapon_montage_v1_requests.json")),"Stale weapon capture.");
        foreach(var d in native.GetProperty("dependencies").EnumerateObject())Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Bytes(d.Name)),"Changed weapon native dependency.");
        int frames=0,poses=0,bones=0,retries=0,rejected=0,reverse=0,zero=0,overweight=0,reference=0,hiddenSource=0,updateOnly=0;
        double maxP=0,maxQ=0,maxS=0;
        foreach(var (trace,ti) in native.GetProperty("trace").GetProperty("traces").EnumerateArray().Select((t,i)=>(t,i)))
        {
            var kind=trace.GetProperty("kind").GetString()!;var catalog=new LyraWeaponMontageCatalog(resources,kind);uint character=(uint)(101+ti);
            using var bank=new LyraWeaponMontageBank(catalog,character,1);using var foreign=new LyraWeaponMontageBank(catalog,character,1);
            Require(trace.GetProperty("names").EnumerateArray().Select(p=>p.GetString()).SequenceEqual(catalog.PhysicalBones.Select(b=>catalog.Names[b])),"Native physical weapon mapping differs.");
            var input=requests.RootElement.GetProperty("traces")[ti];Require(input.GetProperty("montages").EnumerateArray().Select(p=>p.GetString()).SequenceEqual(catalog.Paths),"Native weapon assets differ.");
            foreach(var (n,fi) in trace.GetProperty("frames").EnumerateArray().Select((f,i)=>(f,i)))
            {
                var q=input.GetProperty("frames")[fi];var identity=new AlsFrameIdentity(fi,character,1);var delta=q.GetProperty("delta").GetSingle();bool sample=q.GetProperty("sample").GetBoolean();
                LyraWeaponMontageCandidate? discarded=null;string? first=null;
                for(int attempt=0;attempt<2;attempt++)
                {
                    var candidate=bank.Prepare(identity,delta,sample);string label=$"{kind}/{trace.GetProperty("hz").GetInt32()}/{fi}";
                    Reject(()=>foreign.Validate(candidate));Reject(()=>bank.Prepare(identity,delta));rejected+=2;
                    if(discarded is not null){Reject(()=>bank.Validate(discarded));rejected++;}
                    var expected=n.GetProperty("frozen");Require(expected.GetArrayLength()==candidate.Evaluations.Length,label+"/frozen count");
                    for(int e=0;e<candidate.Evaluations.Length;e++)
                    {
                        var actual=candidate.Evaluations[e];var target=expected[e];
                        Require(actual.ActionDefinitionId==target.GetProperty("asset").GetInt32()&&!target.GetProperty("profile").GetBoolean()&&actual.BlendSnapshot.ProfileId==-1,label+"/asset");
                        Exact(actual.MontagePosition,target.GetProperty("position").GetSingle(),label+"/position");
                        Exact(actual.Weight,target.GetProperty("weight").GetSingle(),label+"/weight");
                        Exact(actual.DeltaTimeRecord.PreviousPosition,target.GetProperty("previous").GetSingle(),label+"/previous");
                        Exact(actual.DeltaTimeRecord.Delta,target.GetProperty("delta").GetSingle(),label+"/delta");
                        var blend=actual.BlendSnapshot;
                        Exact(blend.Alpha,target.GetProperty("alpha").GetSingle(),label+"/alpha");Exact(blend.BeginWeight,target.GetProperty("begin").GetSingle(),label+"/begin");
                        Exact(blend.DesiredWeight,target.GetProperty("desired").GetSingle(),label+"/desired");Exact(blend.StartAlpha,target.GetProperty("startAlpha").GetSingle(),label+"/start alpha");
                        Require((int)blend.Option==target.GetProperty("option").GetInt32(),label+"/blend option");
                        if(attempt==1){if(actual.DeltaTimeRecord.Delta<0)reverse++;if(delta==0)zero++;}
                    }
                    Exact(candidate.Weights.SourceWeight,n.GetProperty("sourceWeight").GetSingle(),label+"/source weight");
                    Exact(candidate.Weights.SlotNodeWeight,n.GetProperty("slotWeight").GetSingle(),label+"/slot weight");
                    Exact(candidate.Weights.TotalNodeWeight,n.GetProperty("totalWeight").GetSingle(),label+"/total weight");
                    if(sample)
                    {
                        var logical=candidate.Pose.ToArray();var physical=catalog.PhysicalBones.Select(b=>logical[b]).ToArray();
                        LyraLogicalSourceSmoke.Compare(n.GetProperty("pose"),physical,ref maxP,ref maxQ,ref maxS,label,fi,"original weapon AnimBP");
                        Require(n.GetProperty("curves").GetArrayLength()==0&&n.GetProperty("attributes").GetInt32()==0,label+"/channels");
                        if(attempt==1){poses++;bones+=physical.Length;}
                    }
                    else {Require(candidate.Pose.IsEmpty,label+"/update-only evaluated");if(attempt==1)updateOnly++;}
                    foreach(var cmd in q.GetProperty("commands").EnumerateArray())
                    {
                        var path=catalog.Paths[cmd.GetProperty("asset").GetInt32()];
                        if(cmd.GetProperty("stop").GetBoolean())bank.Stop(candidate,path,cmd.GetProperty("blend").GetSingle());
                        else bank.Play(candidate,path,cmd.GetProperty("rate").GetSingle(),cmd.GetProperty("start").GetSingle(),cmd.GetProperty("stopGroup").GetBoolean());
                    }
                    bank.Validate(candidate);var signature=JsonSerializer.Serialize(new{Pose=candidate.Pose.ToArray(),candidate.Evaluations,candidate.Weights});
                    if(attempt==0){first=signature;discarded=candidate;bank.Cancel();Reject(()=>bank.Validate(candidate));rejected++;retries++;}
                    else {Require(signature==first,label+"/cancel retry differs");bank.Commit(candidate);Reject(()=>bank.Commit(candidate));rejected++;}
                }
                if(n.GetProperty("totalWeight").GetSingle()>1f+AlsPoseBlender.WeightThreshold)overweight++;
                if(n.GetProperty("slotWeight").GetSingle()==0)reference++;
                if(n.GetProperty("sourceWeight").GetSingle()<=AlsPoseBlender.WeightThreshold)hiddenSource++;
                Reject(()=>bank.Prepare(new(fi+1,character+100,1),delta));Reject(()=>bank.Prepare(new(fi+1,character,2),delta));rejected+=2;frames++;
            }
            var late=bank.Prepare(new(trace.GetProperty("frames").GetArrayLength(),character,1),0);bank.Dispose();Reject(()=>bank.Commit(late));rejected++;
        }
        Require(frames==8820&&poses==7560&&bones==52920&&retries==frames&&updateOnly==1260&&reference>0&&hiddenSource>0&&overweight>0&&reverse>0&&zero>0,"Incomplete weapon native coverage.");
        GD.Print($"LYRA_WEAPON_MONTAGE_GODOT_OK traces=9 frames={frames} poses={poses} bones={bones} retries={retries} rejected={rejected} updateOnly={updateOnly} reverse={reverse} zero={zero} overweight={overweight} reference={reference} hiddenSource={hiddenSource} positionCm={maxP:R} quaternion={maxQ:R} scale={maxS:R} originalGraph=true characterIntegration=false");
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            foreach(var (bank,model) in _models)
            {
                var index=_models.FindIndex(m=>ReferenceEquals(m.Bank,bank));var id=new AlsFrameIdentity(_physicsFrame,(uint)(401+index),1);
                var cancelled=bank.Prepare(id,(float)delta);var cancelledSkin=model.Stage(_physicsFrame,cancelled.Pose);bank.Cancel();model.Cancel();
                Reject(()=>bank.Validate(cancelled));Reject(()=>model.Publish(cancelledSkin));_modelRejects+=2;
                var current=bank.Prepare(id,(float)delta);Require(cancelled.Pose.SequenceEqual(current.Pose),"Weapon model retry changed pose.");
                var skin=model.Stage(_physicsFrame,current.Pose);bank.Validate(current);model.ValidatePublish(skin);
                if(_physicsFrame is 0 or 40 or 100)bank.Play(current,bank.Catalog.Paths[_physicsFrame==100?0:1],bank.Catalog.Kind=="pistol"?1.5f:1);
                bank.Commit(current);model.Publish(skin);Reject(()=>model.Publish(skin));_modelRejects++;_modelRetries++;_published++;
            }
            _physicsFrame++;
            if(_render&&_physicsFrame is 1 or 60 or 120)_=Capture(_physicsFrame);
            if(_physicsFrame<180)return;
            Require(!_render||_captures==3,"Incomplete weapon Montage render evidence.");
            GD.Print($"LYRA_WEAPON_MONTAGE_MODEL_OK frames=180 models=3 publications={_published} retries={_modelRetries} rejected={_modelRejects} characterIntegration=false");GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){GD.PushError("Weapon Montage failed: "+e);GetTree().Quit(1);}
    private async Task Capture(int frame)
    {
        try
        {
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var path=ProjectSettings.GlobalizePath($"res://artifacts/lyra-analysis/weapon-montage-render-{frame}.png");
            Require(!System.IO.File.Exists(path),"Preserve weapon Montage render evidence.");
            Require(GetViewport().GetTexture().GetImage().SavePng(path)==Error.Ok,"Weapon Montage render save failed.");_captures++;
            GD.Print($"LYRA_WEAPON_MONTAGE_CAPTURE frame={frame} physics={Engine.GetPhysicsFrames()} models=3");
        }
        catch(Exception e){Fail(e);}
    }
    public override void _ExitTree(){foreach(var (bank,model) in _models){bank.Dispose();model.Dispose();}}
}
