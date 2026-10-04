using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraEmotePhysicsSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private readonly List<LyraSceneCharacter> _roles=[];
    private readonly int[] _calls=new int[6];
    private readonly List<object> _rows=[];
    private int _hz=60,_frame,_preRetry,_retry,_rejected,_rendered;
    private bool _render;
    private string? _report;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private void Reject(Action action)
    {try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or ObjectDisposedException){_rejected++;return;}throw new InvalidOperationException("Invalid Emote physical call accepted.");}
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();var hz=args.SingleOrDefault(a=>a.StartsWith("--emote-hz="));if(hz is not null)_hz=int.Parse(hz.Split('=')[1]);
            Require(_hz is 30 or 60 or 120,"Invalid Emote physical rate.");Engine.PhysicsTicksPerSecond=_hz;
            _report=args.SingleOrDefault(a=>a.StartsWith("--emote-report="))?.Split('=',2)[1];_render=args.Contains("--emote-render");
            _resources=new(includeMontageActions:true);var catalog=new LyraMontageCatalog();
            var floor=new StaticBody3D{CollisionLayer=5,CollisionMask=2};floor.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(50,.2f,50)},Position=new(0,-.1f,0)});AddChild(floor);
            for(int i=0;i<6;i++)
            {
                int role=i;var character=new LyraSceneCharacter(this,_resources,catalog,"EmoteRole"+i,new((i%3-1)*4,.92f,i/3*-4),new[]{"unarmed","pistol","rifle"}[i%3]);
                _roles.Add(character);character.Animation.MovementUpdated.BindUnique(new object(),_=>_calls[role]++);
            }
            var ceiling=new StaticBody3D{CollisionLayer=1,CollisionMask=2,Position=new(_roles[3].Body.Position.X,1.6f,_roles[3].Body.Position.Z)};
            ceiling.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(1,.15f,1)}});AddChild(ceiling);
            if(_render)
            {
                floor.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(50,.2f,50)},Position=new(0,-.1f,0)});
                ceiling.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(1,.15f,1)}});
                var camera=new Camera3D{Position=new(0,4.5f,8),Current=true};AddChild(camera);camera.LookAt(new(0,.8f,-2));
                AddChild(new DirectionalLight3D{RotationDegrees=new(-55,-35,0),LightEnergy=1.5f});
                RenderingServer.FramePostDraw+=Capture;
            }
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Emote actual delta differs.");float delta=(float)dt;int start=_hz/5;
            for(int i=0;i<6;i++)
            {
                var role=_roles[i];var animation=role.Animation;var emote=animation.Emote;
                if(_frame==start)
                {
                    if(i==1)role.Body.Velocity=new(.2f,0,0);
                    Require(role.RequestEmote(),"Initial Emote rejected.");Require(!role.RequestEmote(),"Active Emote retriggered.");
                }
                if(_frame==_hz&&i==4)animation.CancelEmote();
                if(_frame==_hz&&i==5)Require(animation.RequestWeaponAction(true),"Original interrupt action rejected.");
                if(_frame==_hz*2)
                {
                    bool keptActive=emote.Active,bound=emote.Bound;int keptEnds=emote.Ends;
                    Require(animation.Rebind(animation.Profile=="rifle"?"pistol":"rifle"),"Equipment switch missing.");
                    Require(keptActive==emote.Active&&bound==emote.Bound&&keptEnds==emote.Ends,"Layer rebind changed ability or movement binding.");
                }
                if(_frame==_hz*6&&i==0)role.Body.Velocity=new(.2f,0,0);
                if(_frame==_hz*7&&i==0)Require(role.RequestEmote(),"Completed ability failed to reactivate.");
                var before=role.Body.GlobalTransform;bool beforeActive=emote.Active;int beforeEnds=emote.Ends,callbacks=_calls[i];
                var pre=animation.PrepareMovement(delta);Reject(()=>animation.MoveCapsule(pre,new(float.NaN,0,0),false));animation.Cancel();
                Require(role.Body.GlobalTransform==before&&emote.Active==beforeActive&&emote.Ends==beforeEnds&&_calls[i]==callbacks,"Failed physical preparation published Emote.");
                Reject(()=>animation.MoveCapsule(pre,Vector3.Zero,false));_preRetry++;
                bool crouch=i==3||(i==2&&_frame<start);
                var observation=role.Move(new(Vector2.Zero,0,0,crouch,false,false,false),delta);
                if(_frame==start)
                {
                    if(i==1||i==3)Require(emote.Active&&!emote.Bound,"Movement cancellation ended before animation dispatch.");
                    if(i==2)Require(!observation.Main.Observation.Crouching&&emote.Active&&emote.Bound,"Original UnCrouch did not reach physics.");
                }
                var after=role.Body.GlobalTransform;bool active=emote.Active,boundNow=emote.Bound;int ends=emote.Ends,calls=_calls[i],moves=animation.CapsuleMoves;
                var first=observation.Prepare(animation,delta);var pose=first.Output.Pose.ToArray();animation.Cancel();
                Reject(()=>animation.Commit(first));var current=observation.Prepare(animation,delta);
                Require(after==role.Body.GlobalTransform&&animation.CapsuleMoves==moves&&active==emote.Active&&ends==emote.Ends&&boundNow==emote.Bound&&calls==_calls[i],"Animation retry replayed physical ability effects.");
                Require(pose.SequenceEqual(current.Output.Pose.ToArray()),"Emote retry changed complete Main pose.");_retry++;
                animation.Commit(current);
                if(_frame==start&&(i==1||i==3))Require(!emote.Active&&emote.Ends==1&&animation.MovementUpdated.Count==0,"Original movement cancel/clear did not dispatch.");
                if(_frame==_hz&&(i==4||i==5))Require(!emote.Active&&emote.Bound&&emote.Ends==1,"External cancel/interrupt lost original binding.");
                if(_frame==_hz*5&&(i==0||i==2))Require(emote.Active&&emote.Bound,"Natural Emote ended before original timeline.");
                if(_frame==_hz*6)
                {
                    if(i==0)Require(!emote.Active&&!emote.Bound&&emote.Ends==1,"Completed Emote did not retain then clear its movement binding.");
                    if(i==2)Require(!emote.Active&&emote.Bound&&emote.Ends==1,"Natural completion removed original movement delegate.");
                }
                Require(animation.MotionWarping is null,"Original Emote invented a MotionWarping component.");
                _rows.Add(new{frame=_frame,role=i,emote.Active,emote.Bound,emote.AnimatingAbility,emote.Activations,emote.Ends,emote.MovementClears,calls=_calls[i],
                    lookup=animation.MontageBank.Committed.ToArray().Any(m=>m.ActionDefinitionId==emote.Asset&&m.OwnsActiveActionLookup),
                    crouched=observation.Main.Observation.Crouching,profile=animation.Profile,position=new[]{role.Body.Position.X,role.Body.Position.Y,role.Body.Position.Z}});
            }
            _frame++;if(_frame>=_hz*8)Finish();
        }
        catch(Exception e){Fail(e);}
    }
    private void Capture()
    {if(_frame is not(31 or 91 or 271))return;var path=ProjectSettings.GlobalizePath($"res://artifacts/lyra-analysis/emote-render-{_frame}.png");if(File.Exists(path))return;GetViewport().GetTexture().GetImage().SavePng(path);_rendered++;}
    private void Finish()
    {
        var result=new{hz=_hz,roles=_roles.Count,frames=_frame,moves=_roles.Sum(r=>r.Animation.CapsuleMoves),preRetries=_preRetry,animationRetries=_retry,rejected=_rejected,
            activations=_roles.Sum(r=>r.Animation.Emote.Activations),ends=_roles.Sum(r=>r.Animation.Emote.Ends),clears=_roles.Sum(r=>r.Animation.Emote.MovementClears),
            actualJolt=true,actualAlsModel=true,originalAbilityReference=true,automaticMotionWarping=false,wholeMainNative=false,
            historySha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_rows))))};
        if(_report is not null){Require(!File.Exists(_report),"Preserve Emote report.");File.WriteAllText(_report,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true})+"\n");}
        GD.Print($"LYRA_EMOTE_PHYSICS_GODOT_OK hz={_hz} roles=6 frames={_frame} moves={result.moves} retry={_retry} activations={result.activations} ends={result.ends} clears={result.clears}");GetTree().Quit();
    }
    private void Fail(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    public override void _ExitTree(){if(_render)RenderingServer.FramePostDraw-=Capture;foreach(var r in _roles)r.Dispose();_resources?.Dispose();}
}
