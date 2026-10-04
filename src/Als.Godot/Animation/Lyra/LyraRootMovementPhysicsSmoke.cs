using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraRootMovementPhysicsSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private LyraMontageCatalog _catalog=null!;
    private readonly List<LyraSceneCharacter> _roles=[];
    private int _frame,_hz=60,_retry,_preRetry,_rootFrames,_normalFrames,_stops,_switches,_rejected,_captures;
    private bool _render;
    private CharacterBody3D? _lateBody;
    private LyraRootMovementMotor? _lateMotor;
    private LyraPhysicalMovementCandidate? _latePlan;
    private static void Require(bool v,string label){if(!v)throw new InvalidOperationException(label);}
    private void Reject(Action action)
    {try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or ObjectDisposedException){_rejected++;return;}throw new InvalidOperationException("Invalid root lifecycle operation accepted.");}
    public override void _Ready()
    {
        try
        {
            var rate=OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--root-movement-hz="));if(rate is not null)_hz=int.Parse(rate.Split('=')[1]);
            Require(_hz is 30 or 60 or 120,"Invalid root rate.");Engine.PhysicsTicksPerSecond=_hz;_render=OS.GetCmdlineUserArgs().Contains("--root-movement-render");
            _resources=new(includeMontageActions:true);_catalog=new();
            var floor=new StaticBody3D{CollisionLayer=5,CollisionMask=2};floor.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(40,.2f,40)},Position=new(0,-.1f,0)});AddChild(floor);
            for(int i=0;i<6;i++)_roles.Add(new(this,_resources,_catalog,"RootRole"+i,new((i%3-1)*3,.92f,(i/3)*-4),new[]{"pistol","rifle","unarmed"}[i%3]));
            Require(_roles.All(r=>ReferenceEquals(r.Animation.MovementReader,_roles[0].Animation.MovementReader)),"Roles duplicated immutable root resources.");
            if(_render)
            {
                floor.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(40,.2f,40)},Position=new(0,-.1f,0)});
                var camera=new Camera3D{Position=new(0,5,8),Current=true};AddChild(camera);camera.LookAt(new(0,1,-2));
                AddChild(new DirectionalLight3D{RotationDegrees=new(-55,-35,0),LightEnergy=1.5f});
            }
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Root physics rate differs.");float delta=(float)dt;
            if(_frame==0)ControlledCollision(delta);
            if(_frame==1)
            {
                var before=_lateBody!.GlobalTransform;Reject(()=>_lateMotor!.Step(_latePlan!,Vector3.Zero,false));
                Require(_lateBody.GlobalTransform==before&&_lateMotor!.Moves==0,"Late physical frame moved its body.");_lateMotor!.Cancel();_lateBody.Free();_lateBody=null;
                GD.Print("LYRA_ROOT_MOVEMENT_LATE_FRAME_OK rejected=1 moves=0");
            }
            for(int i=0;i<_roles.Count;i++)
            {
                var role=_roles[i];var animation=role.Animation;
                int action=Array.FindIndex(_catalog.Paths.ToArray(),p=>animation.Profile=="unarmed"?p.Contains("AM_MF_Emote_FingerGuns_Emote_MW."):
                    p.Contains(animation.Profile=="pistol"?"AM_MM_Pistol_Reload_Emote_MW.":"AM_MM_Rifle_Reload_Emote_MW."));Require(action>=0,"Missing actual root action.");
                if(_frame==_hz/5||_frame==_hz*4)animation.RequestMontageAction(action);
                if(_frame==_hz*3){animation.RequestMontageAction(action,-1,_catalog.Definitions[action].Duration-.03f);}
                if(_frame==_hz*7/2){animation.RequestMontageStop(action,.2f);_stops++;}
                if(_frame==_hz*2||_frame==_hz*6){animation.Rebind(animation.Profile=="pistol"?"rifle":animation.Profile=="rifle"?"unarmed":"pistol");_switches++;}
                var before=role.Body.GlobalTransform;var instances=animation.MontageBank.Committed.ToArray();
                var pre=animation.PrepareMovement(delta);animation.Cancel();
                Require(role.Body.GlobalTransform==before&&instances.SequenceEqual(animation.MontageBank.Committed.ToArray()),"Pre-physics cancel moved capsule or committed bank.");
                Reject(()=>animation.MoveCapsule(pre,Vector3.Zero,false));_preRetry++;
                var input=new LyraSceneMovement(new(.35f,0),.15f+.02f*i,.1f,_frame>_hz&&_frame<_hz*2,false,false,_frame==_hz/2);
                var observation=role.Move(input,delta);var receipt=animation.LastMovement;var position=role.Body.GlobalTransform;
                Require(receipt.Identity==pre.Identity&&receipt.Range==pre.Range,"Scene did not consume the same physical root interval.");
                if(receipt.Range.HasMotion)_rootFrames++;else _normalFrames++;
                int moves=animation.CapsuleMoves;Reject(()=>animation.PrepareMovement(delta));Reject(()=>animation.Rebind(animation.Profile));
                var first=observation.Prepare(animation,delta);animation.Cancel();
                Require(role.Body.GlobalTransform==position&&animation.CapsuleMoves==moves&&animation.LastMovement==receipt,"Animation cancellation repeated or reverted published physical movement.");
                Reject(()=>animation.MoveCapsule(pre,Vector3.Zero,false));Reject(()=>animation.Commit(first));
                var current=observation.Prepare(animation,delta);
                Require(first.Output.Pose.SequenceEqual(current.Output.Pose)&&first.SourceNotifies.Callbacks.SequenceEqual(current.SourceNotifies.Callbacks),"Root animation retry differs.");
                Require(animation.MontageBank.RootMotionRange==receipt.Range,"Main and physical root clocks diverged.");
                animation.Commit(current);Require(animation.CapsuleMoves==_frame+1,"Capsule moved more than once per committed animation frame.");_retry++;
            }
            _frame++;
            if(_render&&_frame is 30 or 90 or 270)_=Capture(_frame);
            if(_frame<_hz*8)return;
            Require(_rootFrames>0&&_normalFrames>0&&_stops==6&&_switches==12&&(!_render||_captures==3),"Incomplete root/stop/rebind/render scope.");
            GD.Print($"LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz={_hz} roles=6 frames={_frame} moves={_roles.Sum(r=>r.Animation.CapsuleMoves)} root={_rootFrames} normal={_normalFrames} preRetries={_preRetry} animationRetries={_retry} stops={_stops} switches={_switches} rejected={_rejected} captures={_captures} originalNonzero=false motionWarping=false wholeWorldNative=false");GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private void ControlledCollision(float delta)
    {
        var bodies=new List<CharacterBody3D>();var motors=new List<LyraRootMovementMotor>();var plans=new List<LyraPhysicalMovementCandidate>();
        for(int i=0;i<5;i++)
        {
            var body=new CharacterBody3D{Name="ControlledRoot"+i,Position=new(-10+i*4,2,5),CollisionLayer=2,CollisionMask=1};
            body.AddChild(new CollisionShape3D{Shape=new CapsuleShape3D{Radius=.33f,Height=1.8f}});var component=new Node3D();body.AddChild(component);AddChild(body);bodies.Add(body);
            if(i is 1 or 2)
            {var wall=new StaticBody3D{CollisionLayer=1};wall.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(3,5,.2f)},Position=body.Position+new Vector3(0,0,-1)});AddChild(wall);}
            var motor=new LyraRootMovementMotor(body,component,(uint)i+100,1);motors.Add(motor);var id=new AlsFrameIdentity(0,(uint)i+100,1);
            var motion=new AlsPrecisePose(new(200,i==2?100:0,i==3?150:0),i==4?AlsQuaternion.FromAxisAngle(System.Numerics.Vector3.UnitZ,.7f):AlsQuaternion.Identity,AlsDoubleVector.One);
            var range=new AlsMontageRootMotionRange(id,1,0,0,delta);var pre=motor.Prepare(id,delta,range,new(motion,true));var start=body.GlobalTransform;motor.Cancel();Reject(()=>motor.Step(pre,Vector3.Zero,false));
            Require(body.GlobalTransform==start&&motor.Moves==0,"Cancelled controlled motion published.");plans.Add(motor.Prepare(id,delta,range,new(motion,true)));
        }
        Reject(()=>motors[0].Step(plans[1],Vector3.Zero,false));
        for(int i=0;i<5;i++)
        {
            var body=bodies[i];var start=body.GlobalPosition;var transform=body.GlobalTransform;body.Position+=new Vector3(.01f,0,0);
            Reject(()=>motors[i].Step(plans[i],Vector3.Zero,false));body.GlobalTransform=transform;
            var result=motors[i].Step(plans[i],new(0,-.5f,0),i==3,new(0,-.7f,0));var after=body.GlobalTransform;
            Reject(()=>motors[i].Step(plans[i],Vector3.Zero,false));Require(body.GlobalTransform==after&&motors[i].Moves==1,"Controlled root replay moved capsule.");
            if(i==0)Require(Math.Abs(body.GlobalPosition.Z-start.Z+2)<1e-5&&result.Collisions==0,"Open root translation differs.");
            if(i is 1 or 2)Require(body.GlobalPosition.Z>start.Z-1&&result.Collisions>0,"Root capsule crossed actual Jolt wall.");
            for(int j=0;j<body.GetSlideCollisionCount();j++)
                Require(body.Velocity.Dot(body.GetSlideCollision(j).GetNormal())>=-1e-5f,"Final root velocity still points into a physical contact.");
            if(i==2)Require(body.GlobalPosition.X>start.X+.5f,"Root wall slide lost tangent movement.");
            if(i==3)Require(Math.Abs(result.RequestedVelocity.Y+.5f)<1e-6&&Math.Abs(body.GlobalPosition.Y-start.Y+.5f*delta)<1e-5,"Falling root replaced vertical gravity velocity.");
            if(i==4)Require(Math.Abs(body.GlobalBasis.GetEuler().Y+.7f)<1e-5,"Root rotation did not apply after translation.");
            body.Free();
        }
        _lateBody=new(){Name="LateRootFrame",Position=new(15,2,6),CollisionLayer=2,CollisionMask=1};
        _lateBody.AddChild(new CollisionShape3D{Shape=new CapsuleShape3D{Radius=.33f,Height=1.8f}});var lateComponent=new Node3D();_lateBody.AddChild(lateComponent);AddChild(_lateBody);
        _lateMotor=new(_lateBody,lateComponent,200,1);var lateId=new AlsFrameIdentity(0,200,1);
        _latePlan=_lateMotor.Prepare(lateId,delta,new(lateId,1,0,0,delta),new(new(new(200,0,0),AlsQuaternion.Identity,AlsDoubleVector.One),true));
        GD.Print("LYRA_ROOT_MOVEMENT_CONTROLLED_COLLISION_OK cases=5 open=1 wall=1 slide=1 falling=1 rotation=1 originalActionMotion=false");
        GD.Print("LYRA_ROOT_MOVEMENT_CONTACT_VELOCITY_OK cases=5 finalVelocity=true actualJoltContacts=true");
    }
    private async Task Capture(int frame)
    {
        try{await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);var path=$"res://artifacts/lyra-analysis/root-movement-render-{frame}.png";
            Require(!System.IO.File.Exists(ProjectSettings.GlobalizePath(path)),"Preserve root render evidence.");Require(GetViewport().GetTexture().GetImage().SavePng(path)==Error.Ok,"Root capture failed.");_captures++;}
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){GD.PushError("Root movement physics failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){if(_lateBody is not null&&GodotObject.IsInstanceValid(_lateBody))_lateBody.Free();foreach(var role in _roles)role.Dispose();_resources?.Dispose();}
}
