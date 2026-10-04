using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

internal readonly record struct LyraSceneMovement(Vector2 Direction,float Yaw,float Pitch,bool Crouching,bool Ads,bool Walk,bool Jump,bool Firing=false,bool WorldSpace=false);
internal readonly record struct LyraSceneObservation(LyraMainUpdateInput Main,AlsStopMovementSnapshot Movement,
    double GroundDistance,bool Hit,AlsDoubleVector Point,AlsDoubleVector Normal)
{
    public LyraCharacterAnimationCandidate Prepare(LyraCharacterAnimation animation,float delta)=>
        animation.Prepare(Main,delta,Movement,GroundDistance,Hit,Point,Normal);
}

// A scene actor owns movement, physics and its animation lifetime. Immutable
// locomotion/Montage resources can be shared with the player and other actors.
internal sealed class LyraSceneCharacter:IDisposable
{
    public CharacterBody3D Body {get;}
    public LyraCharacterAnimation Animation {get;}
    public LyraGameplayEventComponent GameplayEvents {get;}
    public LyraContextEffectComponent ContextEffects {get;}
    private readonly LyraSceneMovementService _movement;
    private bool _disposed;
    public LyraCharacterMovementSettings MovementSettings=>_movement.Settings;
    public bool Grounded=>_movement.Grounded;
    internal DemoPlayerHandoff CaptureDemoPlayer() => _movement.CaptureDemoPlayer();
    internal void ImportDemoHandoff(in DemoPlayerHandoff state) => _movement.ImportDemoHandoff(state);
    internal void PresentDemoHandoff(float delta, float pitch, bool ads)
    {
        var observation = _movement.ObserveDemoHandoff(pitch, ads);
        var candidate = observation.Prepare(Animation, delta);
        Animation.Commit(candidate);
    }
    public LyraSceneCharacter(Node3D parent,LyraLocomotionResources resources,LyraMontageCatalog catalog,
        string name,Vector3 position,string profile,bool linkInitially=true)
    {
        Body=new(){Name=name,Position=position,CollisionLayer=2,CollisionMask=1};
        var shape=new CollisionShape3D();
        var floor=new RayCast3D(){TargetPosition=new(0,-5,0),CollisionMask=1};
        var clearance=new ShapeCast3D(){Position=new(0,.002f,0),
            TargetPosition=Vector3.Zero,CollisionMask=1,Enabled=false};
        GameplayEvents=new(){Name="GameplayEvents"};
        ContextEffects=new(){Name="ContextEffects"};Body.AddChild(ContextEffects);
        Body.AddChild(shape);Body.AddChild(floor);Body.AddChild(clearance);Body.AddChild(GameplayEvents);parent.AddChild(Body);
        LyraCharacterAnimation? created=null;
        try{Animation=created=new(Body,resources,catalog,profile,1,4,linkInitially);_movement=new(Body,Animation,shape,floor,clearance);}
        catch{created?.Dispose();Body.Free();throw;}
    }
    public LyraSceneObservation Move(in LyraSceneMovement input,float delta)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        return _movement.Move(input,delta);
    }
    public void Advance(in LyraSceneMovement input,float delta)
    {var observation=Move(input,delta);var candidate=observation.Prepare(Animation,delta);Animation.Commit(candidate);}
    public bool RequestEmote()=>Animation.RequestEmote(_movement.Crouching);
    public void Dispose(){if(_disposed)return;_disposed=true;Animation.Dispose();if(GodotObject.IsInstanceValid(Body))Body.Free();_movement.Dispose();}
}
