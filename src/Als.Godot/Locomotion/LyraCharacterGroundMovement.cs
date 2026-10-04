using Godot;
using GodotAls.Core.Locomotion;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

internal readonly record struct LyraCharacterGroundMove(Vector3 Velocity,LyraCharacterFloorAdjustment Floor,
    LyraCharacterSweepHit[] Contacts,bool Stepped,bool StepReverted);

// Godot physical owner/query adapter. The full walking, ramp, slide, StepUp
// rollback and contact-velocity controller is shared engine code in Core.
internal sealed class LyraCharacterGroundMovement
{
    private readonly CharacterBody3D _body;
    private readonly LyraCharacterFloorProbe _floor;
    private readonly AlsCharacterGroundMovement<LyraCharacterSweepHit,LyraCharacterFloorResult,
        LyraCharacterFloorAdjustment,Transform3D> _runtime;
    public LyraCharacterGroundMovement(CharacterBody3D body,LyraCharacterFloorProbe floor,LyraCharacterSweep sweep)
    {
        _body=body;_floor=floor;var s=floor.Settings;
        _runtime=new(new World(body,floor,sweep),new(sweep.Radius,s.Minimum,s.Maximum,s.EdgeReject,s.StepHeight,s.WalkableZ));
    }
    public bool BelongsTo(CharacterBody3D body,LyraCharacterFloorProbe floor)=>ReferenceEquals(_body,body)&&ReferenceEquals(_floor,floor);
    public LyraCharacterGroundMove Walk(Vector3 velocity,Vector3 endVelocity,float delta,float half,bool root)
    {
        var result=_runtime.Walk(new(velocity.X,velocity.Y,velocity.Z),new(endVelocity.X,endVelocity.Y,endVelocity.Z),delta,half,root);
        return new(new(result.Velocity.X,result.Velocity.Y,result.Velocity.Z),result.Floor,result.Contacts,result.Stepped,result.StepReverted);
    }
    private sealed class World(CharacterBody3D body,LyraCharacterFloorProbe floor,LyraCharacterSweep sweep) :
        IAlsCharacterGroundWorld<LyraCharacterSweepHit,LyraCharacterFloorResult,LyraCharacterFloorAdjustment,Transform3D>
    {
        public NVector Position {get{var p=body.GlobalPosition;return new(p.X,p.Y,p.Z);}}
        public bool Recovered=>sweep.Recovered;
        public Transform3D Capture()=>body.GlobalTransform;
        public void Restore(Transform3D checkpoint)=>body.GlobalTransform=checkpoint;
        public LyraCharacterSweepHit Move(NVector motion,float halfHeight,bool safe)=>sweep.Move(new(motion.X,motion.Y,motion.Z),halfHeight,safe);
        public LyraCharacterFloorResult Find(float halfHeight,bool walking)=>floor.Find(body.GlobalPosition,halfHeight,walking);
        public LyraCharacterFloorAdjustment AfterSweep(float halfHeight)=>floor.AfterSweep(halfHeight);
    }
}
