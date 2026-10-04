using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCharacterPenetrationSettings(float Pullback,float Inflation,float InitialOverlapTolerance,
    float Geometry,float GeometryProxy,float Pawn,float PawnProxy)
{public float Limit(bool pawn,bool proxy)=>pawn?(proxy?PawnProxy:Pawn):(proxy?GeometryProxy:Geometry);}
public interface IAlsCharacterPenetrationHit:IAlsCharacterGroundHit
{
    float PenetrationDepth {get;}
    bool Pawn {get;}
}
public interface IAlsCharacterSweepWorld<THit,TCheckpoint>
    where THit:struct,IAlsCharacterPenetrationHit where TCheckpoint:struct
{
    Vector3 Position {get;}
    TCheckpoint Capture();
    THit Query(Vector3 motion,float half,bool ignoreOutward);
    bool Overlap(TCheckpoint start,Vector3 adjustment,float radius,float half,float inflation);
    void Translate(Vector3 travel);
}

// Per-character sweep/recovery controller. Backend queries preserve collision
// payloads and the full checkpoint transform; Core owns decisions and counters.
public sealed class AlsCharacterSweep<THit,TCheckpoint>
    where THit:struct,IAlsCharacterPenetrationHit where TCheckpoint:struct
{
    private readonly IAlsCharacterSweepWorld<THit,TCheckpoint> _world;
    private readonly AlsCharacterPenetrationSettings _settings;
    private readonly float _radius;
    private readonly bool _proxy;
    public bool Recovered {get;private set;}
    public int TeleportRecoveries {get;private set;}
    public int SweptRecoveries {get;private set;}
    public int CombinedRecoveries {get;private set;}
    public int AdjustedRecoveries {get;private set;}
    public int OriginalRecoveries {get;private set;}
    public AlsCharacterSweep(IAlsCharacterSweepWorld<THit,TCheckpoint> world,float radius,AlsCharacterPenetrationSettings settings,bool proxy=false)
    {
        ArgumentNullException.ThrowIfNull(world);
        float[] fields=[radius,settings.Pullback,settings.Inflation,settings.InitialOverlapTolerance,
            settings.Geometry,settings.GeometryProxy,settings.Pawn,settings.PawnProxy];
        if(fields.Any(v=>!float.IsFinite(v))||radius<=0||settings.Pullback<0||settings.Inflation<0||settings.Geometry<0||
            settings.GeometryProxy<0||settings.Pawn<0||settings.PawnProxy<0)throw new ArgumentException("Invalid penetration settings.");
        _world=world;_radius=radius;_settings=settings;_proxy=proxy;
    }
    public bool BlocksInitialOverlap(Vector3 motion,Vector3 normal)=>
        AlsCharacterSweepMath.Dot(normal,AlsCharacterSweepMath.Normalize(motion))<=_settings.InitialOverlapTolerance;
    public Vector3 Adjustment(THit hit)
    {
        if(!hit.Penetrating)return default;
        var delta=hit.Normal*((hit.PenetrationDepth>0?hit.PenetrationDepth:.00125f)+_settings.Pullback);
        float limit=_settings.Limit(hit.Pawn,_proxy);
        return AlsCharacterSweepMath.Dot(delta,delta)>limit*limit?AlsCharacterSweepMath.Normalize(delta)*limit:delta;
    }
    public THit Move(Vector3 motion,float half,bool safe=true)
    {
        if(!float.IsFinite(half)||half<_radius||!float.IsFinite(motion.X)||!float.IsFinite(motion.Y)||!float.IsFinite(motion.Z))
            throw new ArgumentException("Invalid capsule sweep.");
        Recovered=false;var hit=_world.Query(motion,half,false);
        if(hit.Penetrating&&safe&&Resolve(hit,motion,half)){Recovered=true;hit=_world.Query(motion,half,false);}
        if(!hit.Penetrating)_world.Translate(motion*hit.Time);
        return hit;
    }
    private bool Resolve(THit hit,Vector3 motion,float half)
    {
        var adjustment=Adjustment(hit);if(adjustment==Vector3.Zero)return false;
        var start=_world.Capture();
        if(!_world.Overlap(start,adjustment,_radius,half,_settings.Inflation))
        {_world.Translate(adjustment);TeleportRecoveries++;return true;}
        bool SweepOut(Vector3 delta,out THit result)
        {
            var before=_world.Position;result=_world.Query(delta,half,true);
            if(!result.Penetrating)_world.Translate(delta*result.Time);
            return _world.Position!=before;
        }
        bool moved=SweepOut(adjustment,out var next);
        if(moved)SweptRecoveries++;
        if(!moved&&next.Penetrating)
        {
            var second=Adjustment(next);var combined=adjustment+second;
            if(second!=adjustment&&combined!=Vector3.Zero){moved=SweepOut(combined,out _);if(moved)CombinedRecoveries++;}
        }
        if(!moved&&motion!=Vector3.Zero)
        {
            moved=SweepOut(adjustment+motion,out _);if(moved)AdjustedRecoveries++;
            if(!moved&&AlsCharacterSweepMath.Dot(motion,adjustment)>0)
            {moved=SweepOut(motion,out _);if(moved)OriginalRecoveries++;}
        }
        return moved;
    }
}
