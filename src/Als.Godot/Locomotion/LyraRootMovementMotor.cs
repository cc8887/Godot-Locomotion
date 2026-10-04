using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

internal sealed record LyraPhysicalMovementCandidate(LyraRootMovementMotor Owner,AlsFrameIdentity Identity,
    ulong Tick,Rid Space,Transform3D Actor,Transform3D Component,float Delta,AlsMontageRootMotionRange Range,LyraRootMotionAttribute Motion);
internal readonly record struct LyraPhysicalMovementResult(AlsFrameIdentity Identity,AlsMontageRootMotionRange Range,
    AlsPrecisePose World,Vector3 RequestedVelocity,Vector3 ActualDisplacement,int Collisions,bool Falling)
{
    public AlsPrecisePose ActorBefore {get;init;}
    public AlsPrecisePose ComponentBefore {get;init;}
    public bool Grounded {get;init;}
    public bool FloorPolicyApplied {get;init;}
    public LyraCharacterFloorAdjustment FloorAdjustment {get;init;}
    public bool GroundSweepApplied {get;init;}
    public bool Stepped {get;init;}
    public bool StepReverted {get;init;}
    public LyraCharacterSweepHit[]? Contacts {get;init;}
    public bool AirSweepApplied {get;init;}
    public AlsDoubleVector? NativeVelocity {get;init;}
    public int AirSubsteps {get;init;}
    public int ApexSplits {get;init;}
    public float LandingRemaining {get;init;}
    public bool WallContact=>Contacts?.Any(c=>c.Valid&&Math.Abs(c.ImpactNormal.Y)<.71f)??false;
}

// One capsule move in the main physical frame. Animation retry reuses its
// receipt, never repeats published capsule sweeps or root rotation.
internal sealed class LyraRootMovementMotor
{
    private readonly CharacterBody3D _body;
    private readonly Node3D _component;
    private readonly uint _character,_generation;
    private readonly ContactNormals _contacts;
    private LyraPhysicalMovementCandidate? _pending;
    private long _lastFrame=-1;
    public LyraPhysicalMovementResult Last {get;private set;}
    public int Moves {get;private set;}
    public int RootMoves {get;private set;}
    public LyraRootMovementMotor(CharacterBody3D body,Node3D component,uint character,uint generation)
    { _body=body;_component=component;_character=character;_generation=generation;_contacts=new(body); }
    private sealed class ContactNormals(CharacterBody3D body):IAlsCharacterContactNormals
    {
        public int Count=>body.GetSlideCollisionCount();
        public System.Numerics.Vector3 Normal(int index)
        {var n=body.GetSlideCollision(index).GetNormal();return new(n.X,n.Y,n.Z);}
    }
    private void Live()
    {
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||!GodotObject.IsInstanceValid(_body)||
            !_body.IsInsideTree()||_body.IsQueuedForDeletion()||!GodotObject.IsInstanceValid(_component)||
            !_component.IsInsideTree()||!_body.IsAncestorOf(_component))throw new InvalidOperationException("Root motor requires its live main physics actor/component.");
        for(Node? node=_component;node is not null;node=node.GetParent())
        {if(node.IsQueuedForDeletion())throw new InvalidOperationException("Root motor component ancestry is retiring.");if(node==_body)break;}
    }
    public LyraPhysicalMovementCandidate Prepare(AlsFrameIdentity id,float delta,AlsMontageRootMotionRange range,LyraRootMotionAttribute motion)
    {
        Live();if(_pending is not null||id.CharacterId!=_character||id.SlotGeneration!=_generation||id.FrameId<=_lastFrame||
            !float.IsFinite(delta)||delta<=0||range.Identity!=id||range.HasMotion!=motion.Present)
            throw new ArgumentException("Invalid physical root candidate.");
        if(motion.Present)motion.Value.Validate();
        return _pending=new(this,id,Engine.GetPhysicsFrames(),_body.GetWorld3D().Space,_body.GlobalTransform,_component.GlobalTransform,delta,range,motion);
    }
    public LyraPhysicalMovementResult Step(LyraPhysicalMovementCandidate c,Vector3 normalVelocity,bool falling,Vector3? finalVelocity=null,
        LyraCharacterFloorProbe? floor=null,float capsuleHalfHeight=0,LyraCharacterGroundMovement? groundMovement=null,
        LyraCharacterAirMovement? airMovement=null,LyraCharacterFallingInput? fallingInput=null)
    {
        Live();if(!ReferenceEquals(c,_pending)||c.Owner!=this||c.Tick!=Engine.GetPhysicsFrames()||
        c.Space!=_body.GetWorld3D().Space||_body.GlobalTransform!=c.Actor||_component.GlobalTransform!=c.Component||!normalVelocity.IsFinite())
            throw new InvalidOperationException("Foreign, replayed or stale capsule movement.");
        if(finalVelocity is {} supplied&&!supplied.IsFinite())throw new ArgumentException("Nonfinite final movement velocity.");
        if(floor is not null&&(!floor.BelongsTo(_body)||!float.IsFinite(capsuleHalfHeight)||capsuleHalfHeight<=0))
            throw new ArgumentException("Foreign floor owner or invalid capsule height.");
        if(groundMovement is not null&&(floor is null||!groundMovement.BelongsTo(_body,floor)))
            throw new ArgumentException("Foreign ground movement owner.");
        if(airMovement is not null&&(floor is null||!airMovement.BelongsTo(_body,floor)||falling&&!fallingInput.HasValue))
            throw new ArgumentException("Foreign air movement owner or missing physical input.");
        var world=AlsPrecisePose.Identity;var velocity=normalVelocity;var endVelocity=finalVelocity??normalVelocity;var rotation=Basis.Identity;
        AlsDoubleVector? rootVelocity=null;
        if(c.Motion.Present)
        {
            world=AlsAnimationRootMotionConversion.ToWorld(c.Motion.Value,LyraGodotRigCollision.NativeTransform(c.Actor),LyraGodotRigCollision.NativeTransform(c.Component));
            var v=AlsAnimationRootMotionConversion.Velocity(world,c.Delta,LyraGodotRigCollision.NativePosition(endVelocity),falling);
            rootVelocity=v;
            velocity=new((float)(v.Y*.01),(float)(v.Z*.01),(float)(-v.X*.01));
            endVelocity=velocity;
            if(finalVelocity.HasValue&&falling)velocity.Y=normalVelocity.Y;
            var q=world.Rotation;rotation=new(new Quaternion((float)-q.Y,(float)-q.Z,(float)q.X,(float)q.W));
            if((rotation*_body.GlobalBasis).Y.Normalized().Dot(Vector3.Up)<.99999f)
                throw new NotSupportedException("Animated root motion cannot tilt this upright capsule.");
        }
        if(!velocity.IsFinite())throw new ArgumentException("Nonfinite root movement velocity.");
        var start=_body.GlobalPosition;
        bool sourceGround=groundMovement is not null&&!falling;
        bool sourceAir=airMovement is not null&&falling;
        LyraCharacterGroundMove groundMove=default;LyraCharacterFloorAdjustment floorAdjustment;
        LyraCharacterAirMove airMove=default;
        if(sourceAir)
        {
            airMove=airMovement!.Fall(fallingInput!.Value,c.Delta,capsuleHalfHeight,rootVelocity);
            endVelocity=LyraGodotRigCollision.Position(airMove.Velocity);_body.Velocity=endVelocity;floorAdjustment=airMove.Floor;
        }
        else if(sourceGround)
        {
            groundMove=groundMovement!.Walk(velocity,endVelocity,c.Delta,capsuleHalfHeight,c.Motion.Present);
            endVelocity=groundMove.Velocity;_body.Velocity=endVelocity;floorAdjustment=groundMove.Floor;
        }
        else
        {
            _body.Velocity=velocity;_body.MoveAndSlide();
            floorAdjustment=floor?.AfterMove(capsuleHalfHeight,!falling,start.Y+velocity.Y*c.Delta,endVelocity,velocity*c.Delta)??default;
        }
        bool grounded=floor is null?_body.IsOnFloor():floorAdjustment.Grounded;
        if(finalVelocity.HasValue&&!sourceGround&&!sourceAir)
        {
            // Jolt moves with the midpoint velocity while animation observes
            // the end-of-step velocity. Project that velocity through the
            // actual contacts; retry never executes this physical step again.
            var resolved=AlsCharacterContactVelocity.Resolve(new(endVelocity.X,endVelocity.Y,endVelocity.Z),grounded,_contacts);
            endVelocity=new(resolved.X,resolved.Y,resolved.Z);
            _body.Velocity=endVelocity;
        }
        if(c.Motion.Present){_body.GlobalBasis=rotation*_body.GlobalBasis;RootMoves++;}
        _lastFrame=c.Identity.FrameId;Moves++;_pending=null;
        var contacts=sourceAir?airMove.Contacts:sourceGround?groundMove.Contacts:null;
        return Last=new(c.Identity,c.Range,world,velocity,_body.GlobalPosition-start,contacts?.Length??_body.GetSlideCollisionCount(),falling)
            {ActorBefore=LyraGodotRigCollision.NativeTransform(c.Actor),ComponentBefore=LyraGodotRigCollision.NativeTransform(c.Component),
                Grounded=grounded,FloorPolicyApplied=floor is not null,FloorAdjustment=floorAdjustment,
                GroundSweepApplied=sourceGround,Contacts=contacts,Stepped=groundMove.Stepped,StepReverted=groundMove.StepReverted,
                AirSweepApplied=sourceAir,NativeVelocity=sourceAir?airMove.Velocity:null,AirSubsteps=airMove.Substeps,ApexSplits=airMove.ApexSplits,LandingRemaining=airMove.LandingRemaining};
    }
    public void Cancel()=>_pending=null;
}
