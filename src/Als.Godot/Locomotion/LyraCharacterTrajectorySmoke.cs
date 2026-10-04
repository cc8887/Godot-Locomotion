using System.Text.Json;
using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

// Authored world-space controls drive the real service. Reference physical
// observations are read only after movement, never assigned to the actor.
public partial class LyraCharacterTrajectorySmoke:Node3D
{
    private JsonDocument? _reference;
    private JsonElement _trace;
    private LyraLocomotionResources? _resources;
    private LyraSceneCharacter? _actor;
    private readonly List<object> _rows=[];
    private int _hz,_frame,_mismatches,_ground,_stance,_retry;
    private double _position,_planar,_vertical,_velocity,_acceleration;
    private string _report=null!,_tag=null!;
    private bool _done;
    private static AlsDoubleVector V(JsonElement e)=>new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble());
    private static double[] Values(AlsDoubleVector v)=>[v.X,v.Y,v.Z];
    private static void Require(bool v,string message){if(!v)throw new InvalidOperationException(message);}
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();string Arg(string name)=>args.Single(a=>a.StartsWith(name,StringComparison.Ordinal))[name.Length..];
            _hz=int.Parse(Arg("--trajectory-hz="));_report=Arg("--trajectory-report=");_tag=Arg("--trajectory-tag=");
            Require(_hz is 30 or 60 or 120&&Path.IsPathFullyQualified(_report)&&!File.Exists(_report),"Invalid trajectory run/evidence path.");
            Engine.PhysicsTicksPerSecond=_hz;
            _reference=JsonDocument.Parse(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/character-motor-trajectory-v1-reference.json")));
            var root=_reference.RootElement;
            Require(root.GetProperty("actualOriginalCMC").GetBoolean()&&root.GetProperty("controlsOnlyDriveGodot").GetBoolean(),"Trajectory reference provenance missing.");
            _trace=root.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("hz").GetInt32()==_hz);
            var floor=new StaticBody3D{CollisionLayer=5,CollisionMask=2,Position=new(0,-.05f,0)};
            floor.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Margin=0,Size=new(200,.1f,200)}});AddChild(floor);
            foreach(var obstacle in _trace.GetProperty("obstacles").EnumerateArray())
            {
                var extent=V(obstacle.GetProperty("extent"));
                var wall=new StaticBody3D{CollisionLayer=5,CollisionMask=2,Position=LyraGodotRigCollision.Position(V(obstacle.GetProperty("center")))};
                wall.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Margin=0,Size=new((float)(extent.Y*.02),(float)(extent.Z*.02),(float)(extent.X*.02))}});AddChild(wall);
            }
            _resources=new(includeMontageActions:true);
            _actor=new(this,_resources,new LyraMontageCatalog(),"ActualTrajectory",LyraGodotRigCollision.Position(V(root.GetProperty("start"))),"rifle");
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        if(_done||_actor is null)return;
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Actual trajectory delta differs.");
            var f=_trace.GetProperty("frames")[_frame];var control=f.GetProperty("control");float delta=(float)f.GetProperty("delta").GetDouble();
            Require(delta==(float)dt,"Authoring/native delta differs from actual physics.");
            var world=LyraGodotRigCollision.Position(V(control.GetProperty("direction"))*100);
            var observed=_actor.Move(new(new(world.X,world.Z),-Mathf.DegToRad(control.GetProperty("yaw").GetSingle()),
                Mathf.DegToRad(control.GetProperty("pitch").GetSingle()),control.GetProperty("crouching").GetBoolean(),
                f.GetProperty("ads").GetBoolean(),false,control.GetProperty("jump").GetBoolean(),WorldSpace:true),delta);
            var animation=_actor.Animation;var receipt=animation.LastMovement;var physical=_actor.Body.GlobalTransform;
            Require(!receipt.Range.HasMotion,"Unexpected physical root override in authored CMC-only trace.");
            var first=observed.Prepare(animation,delta);animation.Cancel();var retry=observed.Prepare(animation,delta);
            Require(first.Output.Pose.SequenceEqual(retry.Output.Pose),"Actual trajectory animation retry differs.");animation.Commit(retry);_retry++;
            Require(physical==_actor.Body.GlobalTransform&&receipt==animation.LastMovement&&animation.CapsuleMoves==_frame+1,"Trajectory retry repeated physics.");
            // References enter only here, after the real move and animation.
            var native=f.GetProperty("physical");var o=observed.Main.Observation;
            var location=LyraGodotRigCollision.NativePosition(_actor.Body.GlobalPosition);
            var velocity=LyraGodotRigCollision.NativePosition(_actor.Body.Velocity);
            var difference=location-V(native.GetProperty("location"));
            double p=Math.Sqrt(difference.LengthSquared),xy=Math.Sqrt(difference.X*difference.X+difference.Y*difference.Y),z=Math.Abs(difference.Z);
            double v=Math.Sqrt((velocity-V(native.GetProperty("velocity"))).LengthSquared);
            double a=Math.Sqrt((o.Acceleration-V(native.GetProperty("acceleration"))).LengthSquared);
            bool ground=o.Ground==native.GetProperty("ground").GetBoolean(),stance=o.Crouching==native.GetProperty("crouching").GetBoolean();
            _ground+=ground?0:1;_stance+=stance?0:1;_position=Math.Max(_position,p);_planar=Math.Max(_planar,xy);
            _vertical=Math.Max(_vertical,z);_velocity=Math.Max(_velocity,v);_acceleration=Math.Max(_acceleration,a);
            bool match=p<=.01&&v<=.001&&a<=.001&&ground&&stance;_mismatches+=match?0:1;
            _rows.Add(new{frame=_frame,control,actual=new{location=Values(location),velocity=Values(velocity),acceleration=Values(o.Acceleration),
                o.Ground,o.Crouching,collisions=receipt.Collisions,requested=Values(LyraGodotRigCollision.NativePosition(receipt.RequestedVelocity)),
                displacement=Values(LyraGodotRigCollision.NativePosition(receipt.ActualDisplacement)),
                floorPolicyApplied=receipt.FloorPolicyApplied,receipt.Grounded,
                        receipt.GroundSweepApplied,receipt.Stepped,receipt.StepReverted,
                receipt.AirSweepApplied,receipt.AirSubsteps,receipt.ApexSplits,receipt.LandingRemaining,
                floorWalkable=receipt.FloorAdjustment.Floor.Walkable,
                floorGapCm=receipt.FloorAdjustment.Floor.FloorDistance*100d,
                floorLine=receipt.FloorAdjustment.Floor.LineTrace,
                floorLineGapCm=receipt.FloorAdjustment.Floor.LineDistance*100d,
                floorAdjustment=Values(LyraGodotRigCollision.NativePosition(receipt.FloorAdjustment.Movement))},native,p,xy,z,v,a,ground,stance,match});
            _frame++;if(_frame<_trace.GetProperty("frames").GetArrayLength())return;
            var result=new{tag=_tag,hz=_hz,frames=_frame,moves=animation.CapsuleMoves,retries=_retry,mismatchFrames=_mismatches,
                groundMismatchFrames=_ground,stanceMismatchFrames=_stance,maxPositionCm=_position,maxPlanarCm=_planar,maxVerticalCm=_vertical,
                maxVelocityCmps=_velocity,maxAccelerationCmps2=_acceleration,positionToleranceCm=.01,velocityToleranceCmps=.001,
                originalActorStart=true,actualJolt=true,authoredWorldControls=true,replayedPhysicalObservations=false,
                comparisonPassed=_mismatches==0,nativeWorldTrajectoryParity=false,completeAcceptance=false,rows=_rows};
            using var stream=new FileStream(_report,FileMode.CreateNew);JsonSerializer.Serialize(stream,result);
            GD.Print($"LYRA_CHARACTER_TRAJECTORY_DIAGNOSTIC hz={_hz} frames={_frame} mismatches={_mismatches} ground={_ground} stance={_stance} maxP={_position:R} maxXY={_planar:R} maxZ={_vertical:R} maxV={_velocity:R} maxA={_acceleration:R} replayPhysical=false");
            _done=true;GetTree().Quit(_mismatches==0?0:1);
        }
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){_done=true;GD.PushError("Actual movement trajectory failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){_actor?.Dispose();_resources?.Dispose();_reference?.Dispose();}
}
