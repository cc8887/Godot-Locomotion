using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMotionWarpingPhysicsSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private LyraMontageCatalog _catalog=null!;
    private readonly List<LyraSceneCharacter> _roles=[];
    private readonly Node3D?[] _targets=new Node3D?[6];
    private readonly Vector3[] _goals=new Vector3[6];
    private readonly AlsPrecisePose[] _baseOffsets=new AlsPrecisePose[6];
    private readonly List<object> _rows=[];
    private readonly List<StaticBody3D> _walls=[];
    private int _hz=60,_frame,_nonzero,_blocked,_disabled,_paused,_preRetry,_postRetry,_rejected,_changed,_destroyed,_captures,_reached;
    private bool _render;
    private string? _report;
    private static void Require(bool v,string message){if(!v)throw new InvalidOperationException(message);}
    private void Reject(Action a)
    {try{a();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or ObjectDisposedException){_rejected++;return;}throw new InvalidOperationException("Invalid Warp lifecycle accepted.");}
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();var hz=args.SingleOrDefault(a=>a.StartsWith("--warp-physics-hz="));if(hz is not null)_hz=int.Parse(hz.Split('=')[1]);
            Require(_hz is 30 or 60 or 120,"Invalid Warp physics rate.");Engine.PhysicsTicksPerSecond=_hz;
            _render=args.Contains("--warp-physics-render");_report=args.SingleOrDefault(a=>a.StartsWith("--warp-physics-report="))?.Split('=',2)[1];
            _resources=new(includeMontageActions:true);_catalog=new();
            var floor=new StaticBody3D{CollisionLayer=5,CollisionMask=2};floor.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(50,.2f,50)},Position=new(0,-.1f,0)});AddChild(floor);
            for(int i=0;i<6;i++)_roles.Add(new(this,_resources,_catalog,"WarpRole"+i,new((i%3-1)*4,.92f,i/3*-5),new[]{"unarmed","pistol","rifle"}[i%3]));
            if(_render)
            {
                floor.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(50,.2f,50)},Position=new(0,-.1f,0)});
                var camera=new Camera3D{Position=new(0,6,10),Current=true};AddChild(camera);camera.LookAt(new(0,.7f,-3));
                AddChild(new DirectionalLight3D{RotationDegrees=new(-55,-35,0),LightEnergy=1.5f});
            }
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Warp physical rate differs.");float delta=(float)dt;
            int phase=_frame/(_hz*2),local=_frame%(_hz*2);
            if(local==0)Setup(phase);
            for(int i=0;i<6;i++)
            {
                var role=_roles[i];var animation=role.Animation;var warp=animation.MotionWarping!;
                Require(warp.BaseOffset==_baseOffsets[i],"Crouch or rebind changed the initial visual offset.");
                int asset=_resources!.MotionWarping(_catalog).Windows[phase].AssetId;
                int start=_hz/10;
                if(local==start)animation.RequestMontageAction(asset);
                if(i==1&&local==start+_hz/10)
                { _targets[i]!.Position+=new Vector3(.25f,0,-.15f);_targets[i]!.Rotation=new(0,.35f,0);_goals[i]=_targets[i]!.Position;_changed++; }
                if(i==2&&local==start+_hz/10)animation.RequestWarpTarget(new("Align",LyraGodotRigCollision.NativeTransform(new(Basis.Identity,_goals[i]))));
                if(i==3&&local==start+_hz/10){_targets[i]!.Free();_targets[i]=null;_destroyed++;}
                if(i==5&&local==start+_hz/10)animation.RequestWarpTarget(new("Align",LyraGodotRigCollision.NativeTransform(new(Basis.Identity,_goals[i])),RootPaused:true));
                if(i==5&&local==start+_hz/5)animation.RequestWarpTarget(new("Align",LyraGodotRigCollision.NativeTransform(new(Basis.Identity,_goals[i]))));
                if(i>=2&&phase==2&&local==_hz/2)animation.RequestMontageStop(asset,.1f);
                if(i>=2&&phase==2&&local==_hz)animation.RequestMontageAction(asset,-1,_catalog.Definitions[asset].Duration-.03f);
                if(i>=2&&phase==3&&local==_hz/2){animation.RequestMontageStop(asset,.1f);animation.RequestDisableWarpModifiers();}
                if(i>=2&&phase==3&&local==_hz)animation.RequestMontageAction(asset);

                var actor=role.Body.GlobalTransform;var history=warp.Modifiers;int frames=warp.Frames;
                var pre=animation.PrepareMovement(delta);
                // This fails after Warp preparation but before the one physical
                // move, so target/start history and queued commands must survive.
                Reject(()=>animation.MoveCapsule(pre,new(float.NaN,0,0),false));animation.Cancel();
                Require(role.Body.GlobalTransform==actor&&warp.Frames==frames&&history.SequenceEqual(warp.Modifiers),"Failed physical preparation published Warp history.");
                Reject(()=>animation.MoveCapsule(pre,Vector3.Zero,false));_preRetry++;
                bool crouch=i==5&&phase==1;
                var observation=role.Move(new(Vector2.Zero,0,0,crouch,false,false,false),delta);
                var receipt=animation.LastMovement;var committedWarp=warp.Last!;var after=role.Body.GlobalTransform;
                Require(warp.Frames==frames+1&&committedWarp.Core.Identity==receipt.Identity&&committedWarp.Context==pre.WarpContext,"Warp/Montage/physical identity differs.");
                var capsule=(CapsuleShape3D)role.Body.GetChildren().OfType<CollisionShape3D>().Single().Shape;
                Require(committedWarp.Actor==receipt.ActorBefore&&committedWarp.Component==receipt.ComponentBefore,
                    "Warp used a stale actor/component rather than the independently captured physical start.");
                double halfHeight=capsule.Height*.5f*100;
                Require(Math.Abs(committedWarp.Actor.Position.Z-committedWarp.VisualRoot.Position.Z-halfHeight)<1e-5,"Warp visual root did not use current capsule feet.");
                if(receipt.Range.HasMotion)
                {
                    var expected=AlsAnimationRootMotionConversion.ToWorld(committedWarp.Core.Warped,committedWarp.Actor,committedWarp.Component);
                    Require(receipt.World==expected,"Physical motor ignored warped root motion.");
                    if(!expected.Position.NearlyZero(1e-8))_nonzero++;
                    if(i==4&&receipt.Collisions>0&&!expected.Position.NearlyZero(1e-8))_blocked++;
                    if(committedWarp.Core.Modifiers.Any(m=>m.RootPaused))
                    {Require(committedWarp.Core.Warped.Position.NearlyZero(1e-10),"Root pause still moved.");_paused++;}
                }
                foreach(var m in warp.Modifiers)if(m.State==AlsMotionWarpingModifierState.Disabled)_disabled++;
                if(i==2&&local>=start&&local<start+_hz/3&&receipt.Range.HasMotion)
                    Require(warp.Modifiers.All(m=>m.State==AlsMotionWarpingModifierState.Disabled),"Late target reactivated a disabled original window.");
                if(i==3&&local>=start+_hz/10&&local<start+_hz/3&&receipt.Range.HasMotion)
                    Require(warp.Modifiers.All(m=>m.State==AlsMotionWarpingModifierState.Disabled),"Destroyed target kept its active window.");
                if(i==4)Require(role.Body.GlobalPosition.Z>_goals[i].Z+.3f,"Warp capsule crossed its Jolt wall.");
                if(i is 0 or 1&&local==start+(int)Math.Ceiling(_resources.MotionWarping(_catalog).Windows[phase].End*_hz)+2)
                {Require(new Vector2(role.Body.GlobalPosition.X-_goals[i].X,role.Body.GlobalPosition.Z-_goals[i].Z).Length()<2e-4f,"Open Warp did not reach the actual target.");_reached++;}
                int moves=animation.CapsuleMoves;Reject(()=>animation.RequestRemoveWarpTarget("Align"));
                var first=observation.Prepare(animation,delta);animation.Cancel();
                Require(role.Body.GlobalTransform==after&&animation.CapsuleMoves==moves&&ReferenceEquals(warp.Last,committedWarp)&&warp.Frames==frames+1&&committedWarp.Core.Modifiers.SequenceEqual(warp.Modifiers),
                    "Post-move animation cancellation changed physical Warp history.");
                Reject(()=>animation.Commit(first));var retry=observation.Prepare(animation,delta);
                Require(first.Output.Pose.SequenceEqual(retry.Output.Pose)&&first.SourceNotifies.Callbacks.SequenceEqual(retry.SourceNotifies.Callbacks),"Warp animation retry differs.");
                animation.Commit(retry);Require(animation.CapsuleMoves==_frame+1,"Warp moved a capsule twice.");_postRetry++;
                _rows.Add(new{frame=_frame,role=i,actor=new[]{after.Origin.X,after.Origin.Y,after.Origin.Z},context=committedWarp.Context,
                    mods=warp.Modifiers.Select(m=>new{m.Id,state=(int)m.State,m.ActualStart,m.Current,m.Previous}),
                    worldP=new[]{receipt.World.Position.X,receipt.World.Position.Y,receipt.World.Position.Z},receipt.Collisions});
            }
            _frame++;
            if(_render&&_frame is 30 or 90 or 270)_=Capture(_frame);
            if(_frame<_hz*8)return;
            Require(_nonzero>0&&_blocked>0&&_disabled>0&&_paused>0&&_changed==4&&_destroyed==4&&_reached==8&&(!_render||_captures==3),"Incomplete actual Warp coverage.");
            var json=JsonSerializer.Serialize(_rows);var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            var summary=new{hz=_hz,roles=6,frames=_frame,moves=_roles.Sum(r=>r.Animation.CapsuleMoves),nonzero=_nonzero,blocked=_blocked,disabled=_disabled,paused=_paused,
                preRetries=_preRetry,animationRetries=_postRetry,rejected=_rejected,changed=_changed,destroyed=_destroyed,reached=_reached,digest,actualJolt=true,actualAlsComponent=true,
                originalAuthoredRootIdentity=true,explicitComponent=true,originalAbilityExecuted=false,wholeWorldNative=false};
            if(_report is not null){Require(!System.IO.File.Exists(_report),"Preserve Warp physics evidence.");System.IO.File.WriteAllText(_report,JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));}
            GD.Print("LYRA_MOTION_WARPING_PHYSICS_GODOT_OK "+JsonSerializer.Serialize(summary));GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private void Setup(int phase)
    {
        foreach(var wall in _walls)wall.Free();_walls.Clear();
        for(int i=0;i<6;i++)
        {
            var role=_roles[i];var animation=role.Animation;animation.EnableMotionWarping();
            if(phase==0)_baseOffsets[i]=animation.MotionWarping!.BaseOffset;
            if(phase>0)animation.Rebind(animation.Profile=="unarmed"?"pistol":animation.Profile=="pistol"?"rifle":"unarmed");
            if(_targets[i] is {} old&&GodotObject.IsInstanceValid(old))old.Free();
            var goal=role.Body.GlobalPosition+new Vector3(.1f*i,-.9f,-1f);_goals[i]=goal;
            var target=new Node3D{Position=goal};AddChild(target);_targets[i]=target;
            if(i==2)animation.RequestRemoveWarpTarget("Align");
            else animation.RequestWarpTarget(new("Align",LyraGodotRigCollision.NativeTransform(target.GlobalTransform)),i is 1 or 3?target:null);
            if(i==4)
            {
                var wall=new StaticBody3D{CollisionLayer=1};wall.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(3,3,.15f)},Position=role.Body.GlobalPosition+new Vector3(0,0,-.6f)});AddChild(wall);_walls.Add(wall);
                if(_render)wall.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(3,3,.15f)},Position=role.Body.GlobalPosition+new Vector3(0,0,-.6f)});
            }
        }
    }
    private async Task Capture(int frame)
    {
        try{await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);var path=$"res://artifacts/lyra-analysis/warp-physics-render-{frame}.png";
            Require(!System.IO.File.Exists(ProjectSettings.GlobalizePath(path)),"Preserve Warp render evidence.");Require(GetViewport().GetTexture().GetImage().SavePng(path)==Error.Ok,"Warp capture failed.");_captures++;}
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){GD.PushError("MotionWarping physics failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){foreach(var role in _roles)role.Dispose();_resources?.Dispose();}
}
