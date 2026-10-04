using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

// Player and NPCs use the same input-to-physics-to-observation path. Animation
// candidates may be cancelled after this step; the physical receipt persists.
internal sealed class LyraSceneMovementService:IDisposable
{
    // LyraCharacterMovementComponent.cpp's default GroundTraceDistance CVar.
    private const float GroundTraceDistance=100000;
    private readonly CharacterBody3D _body;
    private readonly LyraCharacterAnimation _animation;
    private readonly CollisionShape3D _shape;
    private readonly RayCast3D _floor;
    private readonly ShapeCast3D _clearance;
    private readonly CapsuleShape3D _capsule,_clearanceCapsule;
    private readonly LyraCharacterFloorProbe _floorProbe;
    private readonly LyraCharacterSweep _sweep;
    private readonly LyraCharacterGroundMovement _groundMovement;
    private readonly LyraCharacterAirMovement _airMovement;
    private readonly AlsCharacterCrouch _crouch;
    private readonly AlsCharacterMotion _motion;
    public LyraCharacterMovementSettings Settings {get;}
    public bool Crouching {get;private set;}
    public bool Grounded {get;private set;}
    private AlsDoubleVector _velocity;
    private Vector3 _publishedVelocity;
    private bool _haveVelocity,_initialized,_disposed;
    internal void ImportDemoHandoff(in DemoPlayerHandoff state)
    {
        state.Validate();
        if (_initialized || _haveVelocity) throw new InvalidOperationException("Import into a fresh Lyra motor.");
        Crouching = state.Crouching; Grounded = state.Grounded;
        _capsule.Height = 2 * (Crouching ? Settings.CrouchedHalfHeight : Settings.StandingHalfHeight);
        _body.GlobalTransform = new(new Basis(Vector3.Up, state.Yaw), state.Feet + Vector3.Up * (_capsule.Height * .5f));
        _body.Velocity = _publishedVelocity = state.Velocity;
        _velocity = LyraGodotRigCollision.NativePosition(state.Velocity); _haveVelocity = true;
        _animation.Binding.Model.Position = new(0, -_capsule.Height * .5f, 0);
        _animation.SetMovementCrouched(Crouching);
    }

    internal DemoPlayerHandoff CaptureDemoPlayer() => new(
        _body.GlobalPosition - Vector3.Up * (_capsule.Height * .5f), _body.GlobalRotation.Y, _body.Velocity, Crouching, Grounded);

    internal LyraSceneObservation ObserveDemoHandoff(float pitch, bool ads)
    {
        var state = _animation.Observation;
        var actual = LyraGodotRigCollision.NativePosition(_body.Velocity);
        var observation = new LyraMainObservationInput(LyraGodotRigCollision.NativePosition(_body.GlobalPosition),
            new(0, -Mathf.RadToDeg(_body.Rotation.Y), 0, state.First, state.Crouching, state.Ads),
            actual, default, Grounded, Crouching, Grounded ? 1 : 3, ads, false, state.RootYaw);
        var feet = CaptureDemoPlayer().Feet;
        return new(new(observation, Mathf.RadToDeg(pitch), Settings.GravityZ, false, false, true, 0),
            Settings.Snapshot(actual), Grounded ? 0 : GroundTraceDistance, Grounded,
            LyraGodotRigCollision.NativePosition(feet), new(0, 0, 1));
    }
    public LyraSceneMovementService(CharacterBody3D body,LyraCharacterAnimation animation,
        CollisionShape3D shape,RayCast3D floor,ShapeCast3D clearance,LyraCharacterMovementSettings? settings=null)
    {
        _body=body;_animation=animation;_shape=shape;_floor=floor;_clearance=clearance;Settings=settings??LyraCharacterMovementSettings.Default;
        _capsule=new(){Radius=Settings.Radius,Height=Settings.StandingHalfHeight*2};
        _floorProbe=new(body,Settings.Radius);
        _sweep=new(body,Settings.Radius);_groundMovement=new(body,_floorProbe,_sweep);
        _airMovement=new(body,_floorProbe,_sweep,_groundMovement,Settings);
        _clearanceCapsule=new(){Radius=Settings.Radius,Height=(Settings.StandingHalfHeight+.00001f)*2};
        _shape.Shape=_capsule;_clearance.Shape=_clearanceCapsule;_clearance.Enabled=false;_clearance.TargetPosition=Vector3.Zero;
        _floor.Position=Vector3.Zero;_floor.TargetPosition=new(0,-GroundTraceDistance*.01f-Settings.StandingHalfHeight,0);
        _floor.AddException(_body);_clearance.AddException(_body);_body.FloorMaxAngle=Settings.FloorAngle;
        // CMC slides along every blocking wall. Godot's default 15 degree
        // cutoff suppresses the tangent of a nearly perpendicular approach.
        _body.WallMinSlideAngle=0;
        _animation.Binding.Model.Position=new(0,-Settings.StandingHalfHeight,0);
        _crouch=new(new CrouchWorld(this),new(Settings.Radius,Settings.StandingHalfHeight,Settings.CrouchedHalfHeight,Settings.CanCrouch));
        _motion=new(new(Settings.Standing,Settings.Crouching,Settings.Falling,Settings.MaxAcceleration,Settings.JumpVelocity));
    }
    private void SetCrouching(bool desired)
    {
        var next=_crouch.Decide(desired,Crouching,Grounded,_capsule.Height*.5f);if(!next.Changed)return;
        _body.Position+=Vector3.Up*next.Shift;_capsule.Height=2*next.HalfHeight;
        _animation.Binding.Model.Position=new(0,-next.HalfHeight,0);Crouching=next.Crouching;
    }
    private sealed class CrouchWorld(LyraSceneMovementService owner):IAlsCharacterCrouchWorld
    {
        public bool Blocked(float offset)
        {owner._clearance.Position=new(0,offset,0);owner._clearance.ForceShapecastUpdate();return owner._clearance.IsColliding();}
        public AlsCharacterCrouchFloor Floor()
        {
            owner._floor.ForceRaycastUpdate();var p=owner._floor.GlobalPosition;
            if(!owner._floor.IsColliding())return new(false,new(p.X,p.Y,p.Z),default);
            var hit=owner._floor.GetCollisionPoint();return new(true,new(p.X,p.Y,p.Z),new(hit.X,hit.Y,hit.Z));
        }
        public IAlsCharacterCrouchAirQuery AirQuery(float radius)=>new Air(owner._body,radius);
        private sealed class Air:IAlsCharacterCrouchAirQuery
        {
            private readonly SphereShape3D _sphere;
            private readonly PhysicsShapeQueryParameters3D _query;
            private readonly PhysicsDirectSpaceState3D _space;
            public Air(CharacterBody3D body,float radius)
            {
                _sphere=new(){Radius=radius};PhysicsShapeQueryParameters3D? query=null;
                try
                {
                    query=new(){Shape=_sphere,Transform=new(Basis.Identity,body.GlobalPosition),
                        Motion=new(0,-radius,0),CollisionMask=body.CollisionMask,Margin=0,Exclude=new(){body.GetRid()}};
                    _query=query;_space=body.GetWorld3D().DirectSpaceState;
                }
                catch{query?.Dispose();_sphere.Dispose();throw;}
            }
            public bool Overlap()=>_space.IntersectShape(_query,1).Count>0;
            public float SweepFraction()=>_space.CastMotion(_query)[0];
            public void Dispose(){_query.Dispose();_sphere.Dispose();}
        }
    }
    public LyraSceneObservation Move(in LyraSceneMovement input,float delta)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!Engine.IsInPhysicsFrame()||!float.IsFinite(delta)||delta<=0||delta>.05f)
            throw new InvalidOperationException("Scene movement requires a live physics substep of at most 50ms.");
        if(!_initialized)
        {
            // The initial CMC walking floor search precedes crouch. Its
            // support and height policy come from the real capsule queries.
            Grounded=_floorProbe.Initialise(_capsule.Height*.5f).Grounded;
            _initialized=true;
        }
        SetCrouching(input.Crouching&&!_animation.Emote.UncrouchRequested);
        _animation.Emote.UncrouchApplied();_animation.SetMovementCrouched(Crouching);
        var motion=_animation.PrepareMovement(delta);
        if(!motion.Motion.Present||_animation.MovementReader.UseControllerYaw)_body.Rotation=new(0,input.Yaw,0);
        var consumed=AlsCharacterInput.Consume(new(input.Direction.X,0,input.Direction.Y),System.Numerics.Vector3.UnitY,
            input.Yaw,input.WorldSpace,input.Walk?.5f:1);
        var direction=new Vector3(consumed.X,consumed.Y,consumed.Z);
        if(!_haveVelocity||_publishedVelocity!=_body.Velocity)_velocity=LyraGodotRigCollision.NativePosition(_body.Velocity);
        var step=_motion.Advance(_velocity,LyraGodotRigCollision.NativeVector(direction),Grounded,Crouching,input.Jump,motion.Motion.Present,delta);
        _animation.MoveCapsule(motion,LyraGodotRigCollision.Position(step.Displacement*(1d/delta)),step.Falling,
            LyraGodotRigCollision.Position(step.Velocity),_floorProbe,_capsule.Height*.5f,_groundMovement,_airMovement,
            step.Falling?new LyraCharacterFallingInput(step.StartVelocity,step.Acceleration,step.Analog,Crouching):null);
        Grounded=_animation.LastMovement.Grounded;
        _publishedVelocity=_body.Velocity;_haveVelocity=true;
        _velocity=AlsCharacterMotion.ResolveVelocity(step,LyraGodotRigCollision.NativePosition(_body.Velocity),
            _animation.LastMovement.NativeVelocity,_animation.LastMovement.Collisions,Grounded,motion.Motion.Present);
        _floor.TargetPosition=new(0,-GroundTraceDistance*.01f-_capsule.Height*.5f,0);
        _floor.ForceRaycastUpdate();bool ground=Grounded,rayHit=_floor.IsColliding();
        var currentFloor=_animation.LastMovement.FloorAdjustment.Floor;
        bool hit=ground?currentFloor.Blocking:rayHit;
        var actual=LyraGodotRigCollision.NativePosition(_body.Velocity);var state=_animation.Observation;
        var observation=new LyraMainObservationInput(LyraGodotRigCollision.NativePosition(_body.GlobalPosition),
            new(0,-Mathf.RadToDeg(_body.Rotation.Y),0,state.First,state.Crouching,state.Ads),actual,step.Acceleration,
            ground,Crouching,ground?1:3,input.Ads,input.Firing,state.RootYaw);
        double distance=ground?0:rayHit?Math.Max(0,_floor.GlobalPosition.DistanceTo(_floor.GetCollisionPoint())*100d-_capsule.Height*50d):GroundTraceDistance;
        return new(new(observation,Mathf.RadToDeg(input.Pitch),Settings.GravityZ,false,false,true,0),Settings.Snapshot(actual),distance,
            hit,hit?LyraGodotRigCollision.NativePosition(ground?currentFloor.Hit.Point:_floor.GetCollisionPoint()):default,
            hit?LyraGodotRigCollision.NativeVector(ground?currentFloor.Hit.Normal:_floor.GetCollisionNormal()):new(0,0,1));
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_sweep.Dispose();_floorProbe.Dispose();_capsule.Dispose();_clearanceCapsule.Dispose();}
}
