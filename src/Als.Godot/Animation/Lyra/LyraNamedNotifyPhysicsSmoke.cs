using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraNamedNotifyPhysicsSmoke:Node3D
{
    private sealed class Observer(Action<LyraNamedNotifyMessage> invoke):ILyraNamedNotifyHandler
    {public bool IsAlive {get;set;}=true;public void Receive(in LyraNamedNotifyMessage message)=>invoke(message);}
    private LyraLocomotionResources? _resources;
    private readonly List<LyraSceneCharacter> _roles=[];
    private readonly List<Observer[]> _handlers=[];
    private readonly List<string> _callbacks=[];
    private int _hz=60,_frame,_retry,_switches,_named,_rejects,_late,_captures;
    private bool _render;
    private string? _report;
    private StaticBody3D? _floor;
    private BoxShape3D? _shape;
    private static void Require(bool v,string label){if(!v)throw new InvalidOperationException(label);}
    private void Reject(Action a){try{a();}catch(InvalidOperationException){_rejects++;return;}throw new InvalidOperationException("Invalid named operation accepted.");}
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_hz=int.Parse(args.SingleOrDefault(a=>a.StartsWith("--named-hz="))?.Split('=')[1]??"60");
            Require(_hz is 30 or 60 or 120,"Invalid named physics rate.");Engine.PhysicsTicksPerSecond=_hz;
            _report=args.SingleOrDefault(a=>a.StartsWith("--named-report="))?.Split('=',2)[1];_render=args.Contains("--named-render");
            _resources=new(includeMontageActions:true);var catalog=new LyraMontageCatalog();
            _floor=new(){CollisionLayer=5,CollisionMask=2};_shape=new(){Size=new(40,.2f,40)};
            _floor.AddChild(new CollisionShape3D{Shape=_shape,Position=new(0,-.1f,0)});AddChild(_floor);
            for(int i=0;i<6;i++)
            {
                int roleId=i;var role=new LyraSceneCharacter(this,_resources,catalog,"NamedRole"+i,new((i%3-1)*2.1f,.92f,i/3*-2.5f),new[]{"unarmed","pistol","rifle"}[i%3]);_roles.Add(role);
                var animation=role.Animation;
                var handlers=Enumerable.Range(0,4).Select(h=>new Observer(m=>
                {
                    Require(m.Identity.CharacterId==animation.CharacterId&&animation.NamedNotifies.CommittedFrame==m.Identity.FrameId&&
                        animation.SourceNotifies.CommittedFrame==m.Identity.FrameId&&animation.NextIdentity.FrameId==m.Identity.FrameId+1,"Named receiver ran before complete own-role commit.");
                    Require(h!=3,"Main queue forwarded an external handler to Linked.");
                    long epoch=animation.LayerEpoch;Reject(()=>animation.Rebind(animation.Profile=="rifle"?"pistol":"rifle"));Require(epoch==animation.LayerEpoch,"Callback rebound before rejection.");
                    _callbacks.Add($"{_frame}:{roleId}:{h}:{m.Name}:{m.Reference.Playback.Owner}:{m.Reference.Playback.Epoch}");
                })).ToArray();_handlers.Add(handlers);
                foreach(var name in new[]{"SaveAttack","ResetCombo"})
                {animation.NamedNotifies.Main.AddExternal(name,handlers[0]);animation.NamedNotifies.Main.AddExternal(name,handlers[1]);animation.NamedNotifies.Linked.AddExternal(name,handlers[3]);}
                animation.NamedNotifies.Main.AddExternal("SaveAttack",handlers[0]);
            }
            if(_render)
            {
                _floor.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(40,.2f,40)},Position=new(0,-.1f,0)});
                var camera=new Camera3D{Position=new(0,5,7),Current=true};AddChild(camera);camera.LookAt(new(0,.9f,-1.3f));
                AddChild(new DirectionalLight3D{RotationDegrees=new(-55,-35,0),LightEnergy=1.5f});RenderingServer.FramePostDraw+=Capture;
            }
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Named actual physics delta differs.");float delta=(float)dt;
            for(int i=0;i<6;i++)
            {
                var role=_roles[i];var animation=role.Animation;var handlers=_handlers[i];
                if(_frame==_hz*2)
                {
                    var main=animation.NamedNotifies.Main;var linked=animation.NamedNotifies.Linked;
                    Require(animation.Rebind(animation.Profile=="rifle"?"pistol":"rifle"),"Named layer replacement failed.");
                    Require(ReferenceEquals(main,animation.NamedNotifies.Main)&&!linked.IsAlive&&animation.NamedNotifies.Linked.Epoch==animation.LayerEpoch,"Named Main/Linked lifetime differs.");
                    Reject(()=>linked.AddExternal("SaveAttack",handlers[0]));_switches++;
                    foreach(var name in new[]{"SaveAttack","ResetCombo"})animation.NamedNotifies.Linked.AddExternal(name,handlers[3]);
                }
                if(_frame%_hz==0)Require(animation.RequestWeaponAction(false)==(animation.Profile!="unarmed"),"Original weapon action route differs.");
                var observation=role.Move(new(new(0,_frame<_hz?.2f:0),0,0,i>=3,false,false,false),delta);
                int oldCount=_callbacks.Count;long oldDelivered=animation.NamedNotifies.Delivered;
                var first=observation.Prepare(animation,delta);animation.Cancel();
                Require(_callbacks.Count==oldCount&&animation.NamedNotifies.Delivered==oldDelivered,"Cancelled named candidate dispatched.");
                var candidate=observation.Prepare(animation,delta);_retry++;
                Require(first.Output.Pose.SequenceEqual(candidate.Output.Pose)&&first.NamedNotifies.Commands.SequenceEqual(candidate.NamedNotifies.Commands),"Named retry changed source/pose.");
                Reject(()=>animation.NamedNotifies.ValidateCommit(first.NamedNotifies));
                Reject(()=>_roles[(i+1)%6].Animation.NamedNotifies.ValidateCommit(candidate.NamedNotifies));
                bool late=candidate.NamedNotifies.Commands.Length>0;
                if(late)
                {
                    Reject(()=>animation.Rebind(animation.Profile=="rifle"?"pistol":"rifle"));
                    // Native registries are resolved at dispatch, including bindings added after Prepare.
                    foreach(var name in new[]{"SaveAttack","ResetCombo"})animation.NamedNotifies.Main.AddExternal(name,handlers[2]);_late++;
                }
                animation.Commit(candidate);
                var expected=new List<string>();
                foreach(var command in candidate.NamedNotifies.Commands)
                {
                    string name=_resources!.Catalog.Notifies.Event(command.Reference.Core.PolicyIndex).Name;
                    foreach(int h in name=="SaveAttack"?new[]{2,0,1,0}:new[]{2,1,0})
                        expected.Add($"{_frame}:{i}:{h}:{name}:{command.Reference.Playback.Owner}:{command.Reference.Playback.Epoch}");
                }
                Require(_callbacks.Skip(oldCount).SequenceEqual(expected),"Production named callback order/count differs.");_named+=candidate.NamedNotifies.Commands.Length;
                Require(animation.NamedNotifies.Delivered-oldDelivered==candidate.NamedNotifies.Commands.Length,"Named source count differs.");
                if(late)
                {
                    Reject(()=>animation.NamedNotifies.DispatchAt(candidate.NamedNotifies,0));Reject(()=>animation.NamedNotifies.BeginCommitted(candidate.NamedNotifies));
                    foreach(var name in new[]{"SaveAttack","ResetCombo"})animation.NamedNotifies.Main.RemoveExternal(name,handlers[2]);
                }
            }
            _frame++;if(_frame>=_hz*4)Finish();
        }
        catch(Exception e){Fail(e);}
    }
    private void Capture()
    {
        if(_frame is not(31 or 151 or 211))return;
        string path=ProjectSettings.GlobalizePath($"res://artifacts/lyra-analysis/named-notify-render-{_frame}.png");if(File.Exists(path))return;
        Require(GetViewport().GetTexture().GetImage().SavePng(path)==Error.Ok,"Named render save failed.");_captures++;
    }
    private void Finish()
    {
        Require(_named>0&&_switches==6&&_late>0,"Actual named role path absent.");
        var result=new{hz=_hz,roles=6,frames=_frame,moves=_roles.Sum(r=>r.Animation.CapsuleMoves),retries=_retry,named=_named,callbacks=_callbacks.Count,switches=_switches,
            lateBindings=_late,rejections=_rejects,actualAlsModel=true,actualJolt=true,wholeMainNative=false,
            callbackSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_callbacks))))};
        if(_report is not null){Require(!File.Exists(_report),"Preserve named physics report.");File.WriteAllText(_report,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true})+"\n");}
        GD.Print($"LYRA_NAMED_NOTIFY_PHYSICS_GODOT_OK hz={_hz} roles=6 frames={_frame} moves={result.moves} retries={_retry} named={_named} callbacks={_callbacks.Count} switches={_switches} lateBindings={_late} rejections={_rejects} captures={_captures}");GetTree().Quit();
    }
    private void Fail(Exception e){SetPhysicsProcess(false);GD.PushError(e.ToString());GetTree().Quit(1);}
    public override void _ExitTree()
    {
        if(_render)RenderingServer.FramePostDraw-=Capture;foreach(var role in _roles)role.Dispose();_resources?.Dispose();
        if(_floor is not null&&GodotObject.IsInstanceValid(_floor))_floor.Free();_shape?.Dispose();
    }
}
