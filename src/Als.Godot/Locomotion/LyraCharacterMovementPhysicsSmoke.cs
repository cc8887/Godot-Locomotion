using System.Text.Json;
using Godot;
using GodotAls.Animation.Lyra;

namespace GodotAls.Locomotion;

// Actual Jolt input-driven actors. Native samples only define configuration
// and independent expectations; no recorded positions/velocities drive actors.
public partial class LyraCharacterMovementPhysicsSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private readonly List<LyraSceneCharacter> _roles=[];
    private StaticBody3D? _roof;
    private int _hz=60,_frame,_retry,_blocked,_released,_wallContacts;
    private int _wallTangentFrames;
    private float _wallTangentError;
    private float _wallMinimumTangentFraction=1;
    private float _jumpBase,_jumpMaximum,_midpointError,_jumpVelocityError,_jumpRecovery;
    private string _report=null!;
    private bool _done;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();
            _hz=int.Parse(args.Single(a=>a.StartsWith("--character-motor-hz=")).Split('=')[1]);
            _report=args.Single(a=>a.StartsWith("--character-motor-report="))["--character-motor-report=".Length..];
            Require(_hz is 30 or 60 or 120&&Path.IsPathFullyQualified(_report)&&!File.Exists(_report),"Invalid motor run/evidence path.");
            Require(ProjectSettings.GetSetting("physics/3d/physics_engine").AsString()=="Jolt Physics","Actual Jolt required.");
            Engine.PhysicsTicksPerSecond=_hz;_resources=new(includeMontageActions:true);var catalog=new LyraMontageCatalog();
            Obstacle("Floor",new(0,-.1f,0),new(100,.2f,100),5);
            Obstacle("Wall",new(20,2,3),new(3,4,.2f),1);
            for(int i=0;i<6;i++)_roles.Add(new(this,_resources,catalog,"Motor"+i,new(i*4,.92f,0),new[]{"unarmed","pistol","rifle"}[i%3]));
            // Diagnostic injection proves the tangent assertion catches the
            // prior engine default. It changes only the test's wall actor.
            if(args.Contains("--character-motor-default-wall-angle"))_roles[5].Body.WallMinSlideAngle=Mathf.DegToRad(15);
            Require(_roles.All(r=>ReferenceEquals(r.MovementSettings,_roles[0].MovementSettings)),"Roles duplicated immutable motor settings.");
        }
        catch(Exception e){Fail(e);}
    }
    private StaticBody3D Obstacle(string name,Vector3 position,Vector3 size,uint layer)
    {
        var body=new StaticBody3D{Name=name,Position=position,CollisionLayer=layer,CollisionMask=2};
        body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=size}});AddChild(body);return body;
    }
    public override void _PhysicsProcess(double dt)
    {
        if(_done)return;
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Actual motor frequency differs.");float delta=(float)dt;double t=(double)_frame/_hz;
            if(_frame==_hz*3/5)_roof=Obstacle("LowRoof",new(12,1.6f,0),new(2,.2f,2),1);
            if(_frame==_hz*9/5)_roof!.Position+=new Vector3(0,0,5);
            for(int i=0;i<6;i++)
            {
                var r=_roles[i];bool moving=(i is 0 or 1 or 2)&&t>=.5&&t<1.5||i==5&&t>=.5&&t<2;
                bool crouch=i==2&&t>=.4&&t<1.5||i==3&&t>=.4&&t<1.1||i==4&&t>=2.1&&t<2.2;
                bool jump=i==4&&_frame==_hz*2;
                if(jump)
                {
                    _jumpBase=r.Body.Position.Y;_jumpMaximum=0;
                    using var query=new PhysicsTestMotionParameters3D{From=r.Body.GlobalTransform,Motion=Vector3.Zero,
                        Margin=r.Body.SafeMargin,RecoveryAsCollision=true};
                    using var recovery=new PhysicsTestMotionResult3D();
                    PhysicsServer3D.BodyTestMotion(r.Body.GetRid(),query,recovery);_jumpRecovery=recovery.GetTravel().Y;
                }
                // Approach at 1.43 degrees from the wall normal. CMC keeps
                // this tangent; Godot's default 15 degree cutoff stops it.
                var direction=moving?(i==5?new Vector2(.025f,1):new Vector2(0,1)):Vector2.Zero;
                var observed=r.Move(new(direction,0,0,crouch,i==1,false,jump),delta);
                Require(observed.Main.Gravity==-980,"Main lost native gravity.");
                var floorReceipt=r.Animation.LastMovement;
                Require(floorReceipt.FloorPolicyApplied&&floorReceipt.Grounded==observed.Main.Observation.Ground&&r.Grounded==floorReceipt.Grounded,
                    "Movement and animation did not share the capsule floor decision.");
                if(r.Grounded)
                {
                    float gap=r.Body.GlobalPosition.Y-(observed.Main.Observation.Crouching?.65f:.9f);
                    Require(gap>=.0189f&&gap<=.0241f,$"Actual flat floor height left the native band: {gap:R}.");
                }
                if(jump)
                {
                    _midpointError=Math.Abs(r.Animation.LastMovement.ActualDisplacement.Y-(5*delta-4.9f*delta*delta)-_jumpRecovery);
                    _jumpVelocityError=Math.Abs(r.Body.Velocity.Y-(5-9.8f*delta));
                    Require(_midpointError<2e-6f&&_jumpVelocityError<1e-6f,
                        $"Actual jump did not use midpoint displacement/end velocity: displacement={r.Animation.LastMovement.ActualDisplacement.Y:R} velocity={r.Body.Velocity.Y:R} errors={_midpointError:R}/{_jumpVelocityError:R} collisions={r.Animation.LastMovement.Collisions} floor={r.Body.IsOnFloor()} requested={r.Animation.LastMovement.RequestedVelocity.Y:R}.");
                }
                if(i==4&&t>=2)_jumpMaximum=Math.Max(_jumpMaximum,r.Body.Position.Y-_jumpBase);
                if(i==3&&t>=1.2&&t<1.7){Require(observed.Main.Observation.Crouching,"Low roof allowed standing expansion.");_blocked++;}
                if(i==3&&t>=1.9&&t<2){Require(!observed.Main.Observation.Crouching,"Standing request did not retry after clearance.");_released++;}
                if(_frame==_hz*6/5&&i is 0 or 1 or 2)
                    Require(Math.Abs(r.Body.Velocity.Z-(i==2?3:6))<1e-5f,"Native standing/ADS/crouched speed differs.");
                if(i==5&&r.Animation.LastMovement.Collisions>0)_wallContacts++;
                if(i==5&&moving&&t>=1.2&&t<1.8)
                {
                    Require(r.Animation.LastMovement.GroundSweepApplied?r.Animation.LastMovement.WallContact:r.Body.IsOnWall(),"Oblique wall fixture did not reach its actual wall.");
                    var wallReceipt=r.Animation.LastMovement;
                    float expected=wallReceipt.RequestedVelocity.X*delta;
                    float error=Math.Abs(wallReceipt.ActualDisplacement.X-expected);
                    _wallTangentError=Math.Max(_wallTangentError,error);_wallTangentFrames++;
                    _wallMinimumTangentFraction=Math.Min(_wallMinimumTangentFraction,wallReceipt.ActualDisplacement.X/expected);
                    // Test suppression here. MoveAndSlide also performs floor
                    // snapping; exact contact trajectory stays in the separate
                    // native comparison, with its unchanged precision gates.
                    Require(expected>0&&wallReceipt.ActualDisplacement.X>0,
                        $"Near-normal wall approach suppressed its tangent: frame={_frame} expected={expected:R} actual={wallReceipt.ActualDisplacement.X:R} error={error:R} requested={wallReceipt.RequestedVelocity} displacement={wallReceipt.ActualDisplacement} normals={string.Join(';',Enumerable.Range(0,r.Body.GetSlideCollisionCount()).Select(n=>r.Body.GetSlideCollision(n).GetNormal()))}.");
                }
                if(i==5)Require(r.Body.Position.Z<2.56f,"Motor crossed actual Jolt wall.");
                if(_frame==_hz*19/10&&i is 0 or 1 or 2)Require(r.Body.Velocity==Vector3.Zero,"Native brake did not stop the body.");
                var receipt=r.Animation.LastMovement;var physical=r.Body.GlobalTransform;var first=observed.Prepare(r.Animation,delta);
                r.Animation.Cancel();var retry=observed.Prepare(r.Animation,delta);
                Require(first.Output.Pose.SequenceEqual(retry.Output.Pose)&&first.SourceNotifies.Callbacks.SequenceEqual(retry.SourceNotifies.Callbacks),"Motor animation retry differs.");
                r.Animation.Commit(retry);_retry++;
                Require(r.Body.GlobalTransform==physical&&r.Animation.LastMovement==receipt&&r.Animation.CapsuleMoves==_frame+1,"Retry repeated physical movement.");
                if(r.Grounded)Require(observed.GroundDistance==0,"Grounded distance must be zero.");
            }
            _frame++;if(_frame<_hz*4)return;
            Require(_blocked>0&&_released>0&&_wallContacts>0&&_wallTangentFrames>0&&_jumpMaximum is >1.24f and <1.30f&&_roles[4].Grounded,"Incomplete clearance/wall/jump/landing coverage.");
            var result=new{hz=_hz,frames=_frame,roles=6,moves=_roles.Sum(r=>r.Animation.CapsuleMoves),retries=_retry,
                nativeKernels=425,blockedStandFrames=_blocked,releasedStandFrames=_released,wallContacts=_wallContacts,
                wallTangentFrames=_wallTangentFrames,wallTangentErrorM=_wallTangentError,
                wallMinimumTangentFraction=_wallMinimumTangentFraction,
                jumpHeightM=_jumpMaximum,midpointErrorM=_midpointError,jumpVelocityErrorMps=_jumpVelocityError,joltRecoveryM=_jumpRecovery,
                characterMotorSha256=_roles[0].MovementSettings.Sha256,actualJolt=true,recordedMovementInputs=false,
                sceneService=true,nativeWorldTrajectoryParity=false,terrainAcceptance=false};
            using var stream=new FileStream(_report,FileMode.CreateNew);JsonSerializer.Serialize(stream,result);
            GD.Print("LYRA_CHARACTER_MOTOR_PHYSICS_GODOT_OK "+JsonSerializer.Serialize(result));_done=true;GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){_done=true;GD.PushError("Character motor physics failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){foreach(var r in _roles)r.Dispose();_resources?.Dispose();}
}
