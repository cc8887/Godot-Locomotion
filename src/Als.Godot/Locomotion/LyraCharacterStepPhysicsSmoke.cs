using System.Text.Json;
using Godot;
using GodotAls.Animation.Lyra;

namespace GodotAls.Locomotion;

// Actual ordinary scene service, with independently authored step/roof geometry.
public partial class LyraCharacterStepPhysicsSmoke:Node3D
{
    private LyraLocomotionResources? _resources;
    private readonly List<LyraSceneCharacter> _actors=[];
    private readonly int[] _steps=new int[5],_reverts=new int[5];
    private readonly float[] _maximum=new float[5];
    private readonly List<object> _rows=[];
    private int _hz,_frame,_retries;
    private string _report=null!;
    private bool _done;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();
            _hz=int.Parse(args.Single(a=>a.StartsWith("--step-physics-hz="))["--step-physics-hz=".Length..]);
            _report=args.Single(a=>a.StartsWith("--step-physics-report="))["--step-physics-report=".Length..];
            Require(_hz is 30 or 60 or 120&&Path.IsPathFullyQualified(_report)&&!File.Exists(_report),"Preserve step evidence.");
            Engine.PhysicsTicksPerSecond=_hz;
            StaticBody3D Box(Vector3 center,Vector3 size)
            {var b=new StaticBody3D{Position=center,CollisionLayer=1,CollisionMask=2};b.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Margin=0,Size=size}});AddChild(b);return b;}
            Box(new(8,-.05f,0),new(50,.1f,50));
            _resources=new(includeMontageActions:true);var catalog=new LyraMontageCatalog();
            for(int i=0;i<5;i++)
            {
                float height=i==2?.55f:.3f;
                var step=Box(new(i*4,height*.5f,2.1f),new(2,height,2));
                if(i==4)step.SetMeta("lyra_can_step_up",false);
                if(i==3)Box(new(i*4,2.10f,2.1f),new(2,.10f,4));
                _actors.Add(new(this,_resources,catalog,"StepPolicy"+i,new(i*4,.92f,0),"rifle"));
            }
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double dt)
    {
        if(_done)return;
        try
        {
            Require(Math.Abs(dt-1d/_hz)<1e-9,"Actual step frequency differs.");float delta=(float)dt;
            for(int i=0;i<_actors.Count;i++)
            {
                var a=_actors[i];bool crouch=i==1,moving=_frame<_hz*13/20;
                var observed=a.Move(new(moving?new Vector2(0,1):Vector2.Zero,0,0,crouch,false,false,false),delta);
                var receipt=a.Animation.LastMovement;var physical=a.Body.GlobalTransform;
                Require(receipt.GroundSweepApplied&&receipt.FloorPolicyApplied&&receipt.Grounded==a.Grounded&&a.Grounded==observed.Main.Observation.Ground,"Step movement has multiple ground owners.");
                Require(a.Grounded,"Authored step role lost floor support.");
                _steps[i]+=receipt.Stepped?1:0;_reverts[i]+=receipt.StepReverted?1:0;
                _maximum[i]=Math.Max(_maximum[i],a.Body.GlobalPosition.Y-(crouch?.65f:.9f));
                var first=observed.Prepare(a.Animation,delta);a.Animation.Cancel();var retry=observed.Prepare(a.Animation,delta);
                Require(first.Output.Pose.SequenceEqual(retry.Output.Pose),"Step animation retry differs.");a.Animation.Commit(retry);_retries++;
                Require(a.Body.GlobalTransform==physical&&receipt==a.Animation.LastMovement&&a.Animation.CapsuleMoves==_frame+1,"Step retry repeated physical movement.");
                _rows.Add(new{frame=_frame,role=i,crouch,position=new[]{physical.Origin.X,physical.Origin.Y,physical.Origin.Z},a.Grounded,receipt.Stepped,receipt.StepReverted,receipt.Collisions});
            }
            _frame++;if(_frame<_hz*2)return;
            for(int i=0;i<2;i++)Require(_steps[i]>0&&_maximum[i] is >.319f and <.325f&&_actors[i].Body.GlobalPosition.Z>1.1f,$"Low step was not climbed: role={i} max={_maximum[i]:R} steps={_steps[i]} pos={_actors[i].Body.GlobalPosition}.");
            Require(_steps[2]==0&&_reverts[2]>0&&_maximum[2]<.025f&&_actors[2].Body.GlobalPosition.Z<1.1f,$"High step crossed geometry: max={_maximum[2]:R} steps={_steps[2]} pos={_actors[2].Body.GlobalPosition}.");
            // CanStepUp controls StepUp, while walkable edge/floor handling
            // remains active. Report that motion without treating this flag
            // as a prohibition on walking onto a walkable surface.
            Require(_steps[4]==0&&_reverts[4]>0,
                $"Disabled StepUp was applied: max={_maximum[4]:R} steps={_steps[4]} pos={_actors[4].Body.GlobalPosition}.");
            // A rounded capsule can advance onto the step's edge until its
            // upper hemisphere meets the roof. Check the actual clearance
            // and rejected advance, rather than requiring zero partial climb.
            Require(_reverts[3]>0&&_maximum[3]<.30f&&_actors[3].Body.GlobalPosition.Y+.9f<=2.0501f&&_actors[3].Body.GlobalPosition.Z<1.1f,
                $"Low roof did not block the full step: max={_maximum[3]:R} reverted={_reverts[3]} pos={_actors[3].Body.GlobalPosition}.");
            using var stream=new FileStream(_report,FileMode.CreateNew);
            JsonSerializer.Serialize(stream,new{hz=_hz,frames=_frame,actors=_actors.Count,moves=_actors.Sum(a=>a.Animation.CapsuleMoves),retries=_retries,
                actualJolt=true,sceneService=true,standingAndCrouchingLowStep=true,highStepRejected=true,roofBlocksFullClimb=true,roofRollback=true,disabledStepPolicyRespected=true,
                steps=_steps,reverts=_reverts,maximumHeightM=_maximum,nativeWorldTrajectoryParity=false,terrainAcceptance=false,rows=_rows});
            GD.Print($"LYRA_CHARACTER_STEP_PHYSICS_GODOT_OK hz={_hz} frames={_frame} actors={_actors.Count} retries={_retries}");_done=true;GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){_done=true;GD.PushError("Actual step physics failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){foreach(var a in _actors)a.Dispose();_resources?.Dispose();}
}
