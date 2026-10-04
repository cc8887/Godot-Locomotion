using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Locomotion;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

internal readonly record struct LyraCharacterSweepHit(bool Blocking,bool Penetrating,float Time,float RawTime,
    Vector3 Point,Vector3 Normal,Vector3 ImpactNormal,Vector3 Location,CollisionObject3D? Collider) : IAlsCharacterPenetrationHit
{
    public bool Valid=>Blocking&&!Penetrating;
    public float PenetrationDepth {get;init;}
    bool IAlsCharacterPenetrationHit.Pawn=>Collider is CharacterBody3D;
    bool IAlsCharacterGroundHit.CanStep => Valid && Collider is PhysicsBody3D && Collider is not CharacterBody3D &&
        (!Collider.HasMeta("lyra_can_step_up") || Collider.GetMeta("lyra_can_step_up").AsBool());
    System.Numerics.Vector3 IAlsCharacterGroundHit.Point => new(Point.X,Point.Y,Point.Z);
    System.Numerics.Vector3 IAlsCharacterGroundHit.Normal => new(Normal.X,Normal.Y,Normal.Z);
    System.Numerics.Vector3 IAlsCharacterGroundHit.ImpactNormal => new(ImpactNormal.X,ImpactNormal.Y,ImpactNormal.Z);
    System.Numerics.Vector3 IAlsCharacterGroundHit.Location => new(Location.X,Location.Y,Location.Z);
}

// Real capsule sweeps retain contact time separately from the original
// PrimitiveComponent pullback. Querying does not move the actor.
internal sealed class LyraCharacterSweep:IDisposable
{
    private readonly CharacterBody3D _body;
    private readonly CapsuleShape3D _capsule=new(){Margin=0};
    private readonly LyraCharacterPenetrationSettings _recovery=LyraCharacterPenetrationSettings.Default;
    public float Radius {get;}
    public int Queries {get;private set;}
    public bool Recovered=>_runtime.Recovered;
    public int TeleportRecoveries=>_runtime.TeleportRecoveries;
    public int SweptRecoveries=>_runtime.SweptRecoveries;
    public int CombinedRecoveries=>_runtime.CombinedRecoveries;
    public int AdjustedRecoveries=>_runtime.AdjustedRecoveries;
    public int OriginalRecoveries=>_runtime.OriginalRecoveries;
    private bool _disposed;
    private readonly AlsCharacterSweep<LyraCharacterSweepHit,Transform3D> _runtime;
    public LyraCharacterSweep(CharacterBody3D body,float radius){_body=body;Radius=radius;_runtime=new(new World(this),radius,_recovery.Core);}
    private void Check(float half,Vector3 motion)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||!GodotObject.IsInstanceValid(_body)||!_body.IsInsideTree()||_body.IsQueuedForDeletion())
            throw new InvalidOperationException("Capsule sweep requires its live physics owner.");
        if(!float.IsFinite(half)||half<Radius||!motion.IsFinite())throw new ArgumentException("Invalid capsule sweep.");
    }
    public LyraCharacterSweepHit Query(Vector3 motion,float half)=>Query(motion,half,false);
    private LyraCharacterSweepHit Query(Vector3 motion,float half,bool ignoreOutward)
    {
        Check(half,motion);_capsule.Radius=Radius;_capsule.Height=half*2;
        var start=_body.GlobalTransform;var space=_body.GetWorld3D().DirectSpaceState;
        using var q=new PhysicsShapeQueryParameters3D{Shape=_capsule,Transform=start,Motion=motion,Margin=0,
            CollisionMask=_body.CollisionMask,Exclude=new(){_body.GetRid()}};
        Godot.Collections.Dictionary Rest(float t)
        {q.Transform=new(start.Basis,start.Origin+motion*t);Queries++;return space.GetRestInfo(q);}
        LyraCharacterSweepHit Hit(Godot.Collections.Dictionary info,float raw,bool penetrating)
        {
            var normal=info["normal"].AsVector3();var impact=normal;
            var collider=GodotObject.InstanceFromId(info["collider_id"].AsUInt64()) as CollisionObject3D;
            if(collider is not null&&!penetrating)impact=LyraGodotRigCollision.OpposingBoxNormal(collider,info["shape"].AsInt32(),motion,normal,out _);
            float t=raw;
            if(!penetrating&&motion.LengthSquared()>0)
            {t=AlsCharacterSweepMath.PullBackFraction(new(motion.X,motion.Y,motion.Z),raw);}
            var point=info["point"].AsVector3();float depth=0;
            if(penetrating)
            {
                depth=AlsCharacterSweepMath.PenetrationDepth(N(start.Origin),N(start.Basis.Y),N(point),N(normal),half,Radius);
            }
            return new(true,penetrating,t,raw,point,normal,impact,start.Origin+motion*raw,collider){PenetrationDepth=depth};
        }
        var initial=Rest(0);
        if(ignoreOutward)
        {
            var ignored=q.Exclude;
            while(initial.Count>0)
            {
                if(_runtime.BlocksInitialOverlap(N(motion),N(initial["normal"].AsVector3())))return Hit(initial,0,true);
                var rid=initial["rid"].AsRid();if(ignored.Contains(rid))throw new InvalidOperationException("Initial overlap exclusion did not advance.");
                ignored.Add(rid);q.Exclude=ignored;initial=Rest(0);
            }
        }
        else if(initial.Count>0)return Hit(initial,0,true);
        if(motion==Vector3.Zero)return new(false,false,1,1,default,default,default,start.Origin,null);
        q.Transform=start;Queries++;var fractions=space.CastMotion(q);
        float low=fractions[0],high=Math.Min(1,fractions[1]+.00001f);var contact=Rest(high);
        if(contact.Count==0)return new(false,false,1,1,default,default,default,start.Origin+motion,null);
        for(int i=0;i<20;i++)
        {
            float middle=(low+high)*.5f;if(middle==low||middle==high)break;
            var info=Rest(middle);if(info.Count>0){high=middle;contact=info;}else low=middle;
        }
        return Hit(contact,high,false);
    }
    private static NVector N(Vector3 v)=>new(v.X,v.Y,v.Z);
    private static Vector3 G(NVector v)=>new(v.X,v.Y,v.Z);
    public LyraCharacterSweepHit Move(Vector3 motion,float half,bool safe=true)
    {Check(half,motion);return _runtime.Move(N(motion),half,safe);}
    private sealed class World(LyraCharacterSweep owner):IAlsCharacterSweepWorld<LyraCharacterSweepHit,Transform3D>
    {
        public NVector Position=>N(owner._body.GlobalPosition);
        public Transform3D Capture()=>owner._body.GlobalTransform;
        public LyraCharacterSweepHit Query(NVector motion,float half,bool ignoreOutward)=>owner.Query(G(motion),half,ignoreOutward);
        public bool Overlap(Transform3D start,NVector adjustment,float radius,float half,float inflation)
        {
            owner._capsule.Radius=radius+inflation;owner._capsule.Height=(half+inflation)*2;
            using var q=new PhysicsShapeQueryParameters3D{Shape=owner._capsule,Transform=new(start.Basis,start.Origin+G(adjustment)),
                CollisionMask=owner._body.CollisionMask,Margin=0,Exclude=new(){owner._body.GetRid()}};
            owner.Queries++;return owner._body.GetWorld3D().DirectSpaceState.IntersectShape(q,1).Count>0;
        }
        public void Translate(NVector travel)=>owner._body.GlobalPosition+=G(travel);
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_capsule.Dispose();}
}
