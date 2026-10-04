using Godot;
using GodotAls.Animation.Lyra;
using GodotAls.Core.Locomotion;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Locomotion;

internal readonly record struct LyraCharacterFloorHit(bool Hit,bool Penetrating,float Time,Vector3 Point,
    Vector3 Normal,Vector3 Location,Vector3 TraceStart,Rid Collider)
{
    public bool Valid=>Hit&&!Penetrating;
}
internal readonly record struct LyraCharacterFloorResult(LyraCharacterFloorHit Hit,bool Walkable,
    bool LineTrace,float FloorDistance,float LineDistance) : IAlsCharacterGroundFloor
{
    public bool Blocking=>Hit.Valid;
    System.Numerics.Vector3 IAlsCharacterGroundFloor.Point => new(Hit.Point.X,Hit.Point.Y,Hit.Point.Z);
    System.Numerics.Vector3 IAlsCharacterGroundFloor.Normal => new(Hit.Normal.X,Hit.Normal.Y,Hit.Normal.Z);
}
internal readonly record struct LyraCharacterFloorAdjustment(bool Grounded,LyraCharacterFloorResult Floor,Vector3 Movement) : IAlsCharacterFloorAdjustment;

// Actual queries use the original short-capsule, edge retry, line fallback and
// perch rules. No recorded floor position or contact is supplied at runtime.
internal sealed class LyraCharacterFloorProbe:IDisposable
{
    private readonly CharacterBody3D _body;
    private readonly CapsuleShape3D _capsule=new(){Margin=0};
    private bool _disposed;
    private readonly AlsCharacterFloorProbe<Rid> _runtime;
    public LyraCharacterFloorSettings Settings {get;}
    public int Queries {get;private set;}
    public int PerchQueries=>_runtime.PerchQueries;
    public bool BelongsTo(CharacterBody3D body)=>ReferenceEquals(_body,body);
    public LyraCharacterFloorProbe(CharacterBody3D body,float radius,LyraCharacterFloorSettings? settings=null)
    {_body=body;Settings=settings??LyraCharacterFloorSettings.Default;_runtime=new(new World(this),radius,Settings.Core);}
    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||!GodotObject.IsInstanceValid(_body)||
            !_body.IsInsideTree()||_body.IsQueuedForDeletion())throw new InvalidOperationException("Floor query requires its live physics owner.");
    }
    private static Vector3 OverlapBoxPoint(CollisionObject3D body,int shape,Vector3 start,float radius,float half,Vector3 normal,Vector3 original)
    {
        if(body is not PhysicsBody3D||shape<0)return original;
        var rid=body.GetRid();var geometry=PhysicsServer3D.BodyGetShape(rid,shape);
        if(PhysicsServer3D.ShapeGetType(geometry)!=PhysicsServer3D.ShapeType.Box||PhysicsServer3D.ShapeGetMargin(geometry)!=0)return original;
        var world=PhysicsServer3D.BodyGetState(rid,PhysicsServer3D.BodyState.Transform).AsTransform3D()*PhysicsServer3D.BodyGetShapeTransform(rid,shape);
        var inverse=world.AffineInverse();var extent=PhysicsServer3D.ShapeGetData(geometry).AsVector3();
        // Chaos CoreSegment::SupportCore selects endpoint 2 for an
        // orthogonal direction. Project that capsule support onto the actual
        // sharp box; Jolt otherwise chooses a different point on this manifold.
        var core=start+Vector3.Up*((normal.Y<=0?1:-1)*(half-radius));
        var local=inverse*core;var point=local.Clamp(-extent,extent);
        if(point==local)
        {
            var n=inverse.Basis*normal;int axis=(int)n.Abs().MaxAxisIndex();
            point[axis]=n[axis]>=0?extent[axis]:-extent[axis];
        }
        return world*point;
    }
    private LyraCharacterFloorHit Sweep(Vector3 start,float radius,float half,float distance)
    {
        _capsule.Radius=radius;_capsule.Height=Math.Max(radius,half)*2;
        var space=_body.GetWorld3D().DirectSpaceState;var motion=Vector3.Down*distance;
        using var q=new PhysicsShapeQueryParameters3D{Shape=_capsule,Transform=new(Basis.Identity,start),Motion=motion,
            CollisionMask=_body.CollisionMask,Margin=0,Exclude=new(){_body.GetRid()}};
        Godot.Collections.Dictionary Rest(float t)
        {q.Transform=new(Basis.Identity,start+motion*t);Queries++;return space.GetRestInfo(q);}
        LyraCharacterFloorHit Hit(Godot.Collections.Dictionary info,float t,bool penetrating)
        {
            var point=info["point"].AsVector3();var normal=info["normal"].AsVector3();var rid=info["rid"].AsRid();
            if(GodotObject.InstanceFromId(info["collider_id"].AsUInt64()) is CollisionObject3D body)
            {
                int shape=info["shape"].AsInt32();
                if(penetrating)point=OverlapBoxPoint(body,shape,start,radius,half,normal,point);
                else normal=LyraGodotRigCollision.OpposingBoxNormal(body,shape,motion,normal,out _);
            }
            return new(true,penetrating,t,point,normal,start+motion*t,start,rid);
        }
        var initial=Rest(0);
        if(initial.Count>0)return Hit(initial,0,true);
        q.Transform=new(Basis.Identity,start);Queries++;var fractions=space.CastMotion(q);
        if(fractions[1]>=1)return new(false,false,1,default,default,start+motion,start,default);
        float low=fractions[0],high=Math.Min(1,fractions[1]+.00001f);var contact=Rest(high);
        if(contact.Count==0)throw new InvalidOperationException("Floor sweep lost its actual contact.");
        // Jolt's public cast has millimeter brackets. Refine using actual
        // overlaps, stopping when the float fraction can no longer change.
        for(int i=0;i<16;i++)
        {
            float mid=(low+high)*.5f;if(mid==low||mid==high)break;
            var info=Rest(mid);if(info.Count>0){high=mid;contact=info;}else low=mid;
        }
        return Hit(contact,high,false);
    }
    private static NVector N(Vector3 v)=>new(v.X,v.Y,v.Z);
    private static Vector3 G(NVector v)=>new(v.X,v.Y,v.Z);
    private static AlsCharacterFloorHit<Rid> CoreHit(LyraCharacterFloorHit h)=>new(h.Hit,h.Penetrating,h.Time,N(h.Point),N(h.Normal),N(h.Location),N(h.TraceStart),h.Collider);
    private static LyraCharacterFloorResult Result(AlsCharacterFloorResult<Rid> r)
    {var h=r.Hit;return new(new(h.Hit,h.Penetrating,h.Time,G(h.Point),G(h.Normal),G(h.Location),G(h.TraceStart),h.Collider),r.Walkable,r.LineTrace,r.FloorDistance,r.LineDistance);}
    private static LyraCharacterFloorAdjustment Adjustment(AlsCharacterFloorAdjustment<Rid> a)=>new(a.Grounded,Result(a.Floor),G(a.Movement));
    public LyraCharacterFloorResult Compute(Vector3 start,float half,float line,float sweep,float? sweepRadius=null)
    {Check();return Result(_runtime.Compute(N(start),half,line,sweep,sweepRadius));}
    public LyraCharacterFloorResult Find(Vector3 start,float half,bool walking)
    {Check();return Result(_runtime.Find(N(start),half,walking));}
    public LyraCharacterFloorAdjustment Initialise(float half)
    {Check();return Adjustment(_runtime.Initialise(half));}
    public LyraCharacterFloorAdjustment AfterMove(float half,bool walking,float predictedY,Vector3 endVelocity,Vector3 motion)
    {Check();return Adjustment(_runtime.AfterMove(half,walking,predictedY,N(endVelocity),N(motion)));}
    public LyraCharacterFloorAdjustment AfterSweep(float half)
    {Check();return Adjustment(_runtime.AfterSweep(half));}
    private sealed class World(LyraCharacterFloorProbe owner):IAlsCharacterFloorWorld<Rid>
    {
        public NVector Position=>N(owner._body.GlobalPosition);
        public NVector Velocity=>N(owner._body.Velocity);
        public bool OnFloor=>owner._body.IsOnFloor();
        public AlsCharacterFloorHit<Rid> Sweep(NVector start,float radius,float half,float distance)=>CoreHit(owner.Sweep(G(start),radius,half,distance));
        public AlsCharacterFloorRay<Rid> Ray(NVector start,float distance)
        {
            var p=G(start);using var q=PhysicsRayQueryParameters3D.Create(p,p+Vector3.Down*distance,owner._body.CollisionMask,new(){owner._body.GetRid()});
            q.HitFromInside=true;owner.Queries++;var info=owner._body.GetWorld3D().DirectSpaceState.IntersectRay(q);
            return info.Count==0?default:new(true,N(info["position"].AsVector3()),N(info["normal"].AsVector3()),info["rid"].AsRid());
        }
        public IEnumerable<AlsCharacterFloorContact> Contacts(NVector motion)
        {
            for(int i=0;i<owner._body.GetSlideCollisionCount();i++)
            {
                var c=owner._body.GetSlideCollision(i);
                for(int j=0;j<c.GetCollisionCount();j++)
                {
                    var normal=c.GetNormal(j);
                    if(c.GetCollider(j) is CollisionObject3D body)
                        normal=LyraGodotRigCollision.OpposingBoxNormal(body,c.GetColliderShapeIndex(j),G(motion),normal,out _);
                    yield return new(N(c.GetPosition(j)),N(normal));
                }
            }
        }
        public void Snap(float distance)
        {
            float previous=owner._body.FloorSnapLength;
            try{owner._body.FloorSnapLength=Math.Max(previous,distance);owner._body.ApplyFloorSnap();}
            finally{owner._body.FloorSnapLength=previous;}
        }
        public AlsCharacterFloorHeightSweep SweepHeight(NVector motion)
        {
            using var q=new PhysicsTestMotionParameters3D{From=owner._body.GlobalTransform,Motion=G(motion),Margin=0,MaxCollisions=6};
            using var r=new PhysicsTestMotionResult3D();owner.Queries++;
            bool hit=PhysicsServer3D.BodyTestMotion(owner._body.GetRid(),q,r);
            return new(hit,N(r.GetTravel()),r.GetCollisionUnsafeFraction(),r.GetCollisionSafeFraction());
        }
        public void Translate(NVector travel)=>owner._body.GlobalPosition+=G(travel);
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_capsule.Dispose();}
}
