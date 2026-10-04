using Godot;
using GodotAls.Core.Locomotion;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

internal readonly record struct LyraCharacterFallingInput(AlsDoubleVector Velocity,AlsDoubleVector Acceleration,float Analog,bool Crouching);
internal readonly record struct LyraCharacterAirMove(AlsDoubleVector Velocity,LyraCharacterFloorAdjustment Floor,
    LyraCharacterSweepHit[] Contacts,int Substeps,int ApexSplits,float LandingRemaining,bool RandomEscape);

// Per-character physical binding. Air decisions and ALS integration live in Core.
internal sealed class LyraCharacterAirMovement
{
    private readonly CharacterBody3D _body;
    private readonly LyraCharacterFloorProbe _floor;
    private readonly AlsCharacterAirMovement<LyraCharacterSweepHit,LyraCharacterFloorResult,LyraCharacterFloorAdjustment> _runtime;
    internal uint RandomSeed=>_runtime.RandomSeed;
    public LyraCharacterAirMovement(CharacterBody3D body,LyraCharacterFloorProbe floor,LyraCharacterSweep sweep,
        LyraCharacterGroundMovement ground,LyraCharacterMovementSettings settings,uint? randomSeed=null)
    {
        _body=body;_floor=floor;var f=floor.Settings;
        _runtime=new(new World(body,floor,sweep,ground),new(sweep.Radius,f.EdgeReject,f.WalkableZ,f.PerchRadiusThreshold,
            f.MaxJumpApexAttempts,settings.MaxSimulationTimeStep,settings.MaxSimulationIterations,settings.JumpVelocity,
            settings.Falling,settings.Standing,settings.Crouching),randomSeed??unchecked((uint)body.GetInstanceId()));
    }
    public bool BelongsTo(CharacterBody3D body,LyraCharacterFloorProbe floor)=>ReferenceEquals(_body,body)&&ReferenceEquals(_floor,floor);
    public LyraCharacterAirMove Fall(LyraCharacterFallingInput input,float delta,float half,AlsDoubleVector? rootVelocity=null)
    {
        var result=_runtime.Fall(new(input.Velocity,input.Acceleration,input.Analog,input.Crouching),delta,half,rootVelocity);
        return new(result.Velocity,result.Floor,result.Contacts,result.Substeps,result.ApexSplits,result.LandingRemaining,result.RandomEscape);
    }
    private sealed class World(CharacterBody3D body,LyraCharacterFloorProbe floor,LyraCharacterSweep sweep,LyraCharacterGroundMovement ground):
        IAlsCharacterAirWorld<LyraCharacterSweepHit,LyraCharacterFloorResult,LyraCharacterFloorAdjustment>
    {
        public NVector Position {get{var p=body.GlobalPosition;return new(p.X,p.Y,p.Z);}}
        public bool Recovered=>sweep.Recovered;
        public LyraCharacterSweepHit Move(NVector motion,float halfHeight)=>sweep.Move(new(motion.X,motion.Y,motion.Z),halfHeight);
        public LyraCharacterFloorResult Find(float halfHeight)=>floor.Find(body.GlobalPosition,halfHeight,false);
        public LyraCharacterSweepHit FloorHit(LyraCharacterFloorResult floor)
        {var h=floor.Hit;return new(h.Hit,h.Penetrating,h.Time,h.Time,h.Point,h.Normal,h.Normal,h.Location,null);}
        public LyraCharacterFloorAdjustment AfterLanding(float halfHeight)=>floor.AfterSweep(halfHeight) with{Grounded=true};
        public AlsCharacterGroundMove<LyraCharacterSweepHit,LyraCharacterFloorAdjustment> Walk(NVector velocity,float delta,float halfHeight,bool root)
        {
            var v=new Vector3(velocity.X,velocity.Y,velocity.Z);var result=ground.Walk(v,v,delta,halfHeight,root);
            return new(new(result.Velocity.X,result.Velocity.Y,result.Velocity.Z),result.Floor,result.Contacts,result.Stepped,result.StepReverted);
        }
    }
}
