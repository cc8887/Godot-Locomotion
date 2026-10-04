using System.Text.Json;
using Godot;
using GodotAls.Animation.Lyra;

namespace GodotAls.Locomotion;

// Source floor band and perch policy exercised through the ordinary service,
// with actual blocking geometry and immutable receipts across animation retry.
public partial class LyraCharacterFloorPhysicsSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private readonly List<LyraSceneCharacter> _actors=[];
    private readonly float[] _gaps=[0,.018f,.020f,.021f,.030f,.25f,0,.020f];
    private readonly List<object> _rows=[];
    private string _report=null!;
    private int _hz,_frame,_retries;
    private bool _done;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();
            _hz=int.Parse(args.Single(a=>a.StartsWith("--floor-physics-hz="))["--floor-physics-hz=".Length..]);
            _report=args.Single(a=>a.StartsWith("--floor-physics-report="))["--floor-physics-report=".Length..];
            Require(_hz is 30 or 60 or 120&&Path.IsPathFullyQualified(_report)&&!File.Exists(_report),"Preserve floor physics evidence.");
            Engine.PhysicsTicksPerSecond=_hz;
            void Box(Vector3 center,Vector3 size)
            {var b=new StaticBody3D{Position=center,CollisionLayer=1,CollisionMask=2};b.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Margin=0,Size=size}});AddChild(b);}
            Box(new(0,-.05f,0),new(200,.1f,200));
            // Initial capsule fits; its requested height correction is blocked.
            Box(new(24,1.856f,0),new(2,.1f,2));
            _resources=new(includeMontageActions:true);var catalog=new LyraMontageCatalog();
            for(int i=0;i<_gaps.Length;i++)
                _actors.Add(new(this,_resources,catalog,"FloorPolicy"+i,i==7?new(0,.92f,-100.30f):new(i*4,.9f+_gaps[i],0),"rifle"));
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        if(_done)return;
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Actual floor frequency differs.");float delta=(float)dt;
            for(int i=0;i<_actors.Count;i++)
            {
                var a=_actors[i];bool crouch=i<6&&_frame>=_hz/5&&_frame<_hz*2/5;
                var observation=a.Move(new(Vector2.Zero,0,0,crouch,false,false,false),delta);
                var receipt=a.Animation.LastMovement;var position=a.Body.GlobalPosition;
                Require(receipt.FloorPolicyApplied&&receipt.Grounded==a.Grounded&&a.Grounded==observation.Main.Observation.Ground,"Floor decision has multiple owners.");
                if(i<6)
                {
                    float expectedGap=_gaps[i] is >=.019f and <=.024f?_gaps[i]:.0215f;
                    float expected=(crouch?.65f:.9f)+expectedGap+(_frame>=_hz*2/5?.00001f:0);
                    Require(a.Grounded&&Math.Abs(position.Y-expected)<.0001f,$"Floor band/stance differs: role={i} frame={_frame} actual={position.Y:R} expected={expected:R}.");
                }
                else if(i==6)
                    Require(a.Grounded&&position.Y>=.9f&&position.Y<.9061f,"Height correction crossed its actual blocking roof.");
                else
                {
                    Require(!a.Grounded&&!receipt.FloorAdjustment.Floor.Walkable,"Perch rejection was replaced by a backend contact flag.");
                    Require(!observation.Hit&&observation.GroundDistance==100000,"Missing ground trace did not keep Lyra's finite fallback.");
                }
                var first=observation.Prepare(a.Animation,delta);a.Animation.Cancel();var retry=observation.Prepare(a.Animation,delta);
                Require(first.Output.Pose.SequenceEqual(retry.Output.Pose),"Floor animation retry differs.");a.Animation.Commit(retry);_retries++;
                Require(position==a.Body.GlobalPosition&&receipt==a.Animation.LastMovement&&a.Animation.CapsuleMoves==_frame+1,"Floor retry repeated physical movement.");
                _rows.Add(new{frame=_frame,role=i,position=new[]{position.X,position.Y,position.Z},a.Grounded,crouch,
                    floorWalkable=receipt.FloorAdjustment.Floor.Walkable,receipt.Collisions,
                    floorMovement=new[]{receipt.FloorAdjustment.Movement.X,receipt.FloorAdjustment.Movement.Y,receipt.FloorAdjustment.Movement.Z}});
            }
            _frame++;if(_frame<_hz*3/5)return;
            Require(_actors[7].Body.GlobalPosition.Y<.92f,"Rejected edge support did not fall.");
            var result=new{hz=_hz,frames=_frame,actors=_actors.Count,moves=_actors.Sum(a=>a.Animation.CapsuleMoves),retries=_retries,
                actualJolt=true,sceneService=true,nativeBand=true,blockedHeight=true,perchRejected=true,
                floorSettingsSha256=LyraCharacterFloorSettings.Default.Sha256,nativeWorldTrajectoryParity=false,terrainAcceptance=false,rows=_rows};
            using var stream=new FileStream(_report,FileMode.CreateNew);JsonSerializer.Serialize(stream,result);
            GD.Print($"LYRA_CHARACTER_FLOOR_PHYSICS_GODOT_OK hz={_hz} frames={_frame} actors={_actors.Count} retries={_retries} nativeBand=true blockedHeight=true perchRejected=true");
            _done=true;GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){_done=true;GD.PushError("Actual floor physics failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){foreach(var a in _actors)a.Dispose();_resources?.Dispose();}
}
