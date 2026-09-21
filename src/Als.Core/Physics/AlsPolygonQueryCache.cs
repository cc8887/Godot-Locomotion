using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// One sequential query owner's persistent GJK state. Keys use registry slots
// in original ascending order. Geometry/scale changes MUST advance revision;
// resolved pair margin changes are detected separately. An omitted query does
// not imply destruction: the midphase owner must explicitly Release a pair.
public sealed class AlsPolygonQueryCache
{
    private sealed class Entry
    {
        internal readonly AlsGjkCache Committed = new(), Proposed = new();
        internal AlsContactPairKey Key, ProposedKey;
        internal float Margin0, Margin1, ProposedMargin0, ProposedMargin1;
        internal bool Exists, ProposedExists, Touched;
    }
    private readonly int _shapeCapacity;
    private readonly Entry?[] _entries;
    private readonly int[] _touched;
    private readonly AlsConvexManifoldWorkspace _workspace = new();
    private int _count;
    private bool _staged, _faulted;
    public bool Pending { get; private set; }
    public long CompletedSteps { get; private set; }
    public int CachedPairs { get; private set; }

    public AlsPolygonQueryCache(int shapeCapacity)
    {
        if(shapeCapacity<1)throw new ArgumentOutOfRangeException(nameof(shapeCapacity));
        _shapeCapacity=shapeCapacity;
        var capacity=checked(shapeCapacity*(shapeCapacity-1)/2);
        _entries=new Entry?[capacity];_touched=new int[capacity];
    }
    public void PrepareStep()
    {
        if(Pending)throw new InvalidOperationException("Finish the pending polygon query step first.");
        if(CompletedSteps==long.MaxValue)throw new InvalidOperationException("Polygon query epoch exhausted.");
        Pending=true;_staged=_faulted=false;_count=0;
    }
    public AlsConvexManifoldResult Query<TA,TB>(AlsContactPairKey key,in TA a,in TB b,in AlsPrecisePose shape1To0,
        Span<AlsDetectedContact> destination,double cullDistance,double gjkEpsilon,double epaEpsilon,
        float minimumFaceSearchDistance,float planeNormalEpsilon,bool forceEdgeZeroCull=false,bool warmStart=true)
        where TA:struct,IAlsPolygonShape where TB:struct,IAlsPolygonShape
    {
        Writable();
        var slot=Slot(key);a.Validate();b.Validate();
        var entry=Touch(slot);
        if(!entry.ProposedExists||entry.ProposedKey!=key||entry.ProposedMargin0!=a.Margin||entry.ProposedMargin1!=b.Margin)
            entry.Proposed.Reset();
        entry.ProposedKey=key;entry.ProposedMargin0=a.Margin;entry.ProposedMargin1=b.Margin;entry.ProposedExists=true;
        try
        {
            return AlsPolygonManifold.Build(a,b,shape1To0,entry.Proposed,_workspace,destination,cullDistance,
                gjkEpsilon,epaEpsilon,minimumFaceSearchDistance,planeNormalEpsilon,forceEdgeZeroCull,warmStart);
        }
        catch { _faulted=true;throw; }
    }
    // Transactional constraint destruction. A stale key cannot remove a new
    // shape/body occupying the same slots. No implicit pruning policy here.
    public void Release(AlsContactPairKey key)
    {
        Writable();var slot=Slot(key);
        if(_entries[slot] is not { } entry)return;
        var exists=entry.Touched?entry.ProposedExists:entry.Exists;
        var current=entry.Touched?entry.ProposedKey:entry.Key;
        if(!exists||current!=key)return;
        entry=Touch(slot);entry.ProposedExists=false;entry.Proposed.Reset();
    }
    public void StageCommit()
    {
        if(!Pending||_faulted)throw new InvalidOperationException("Prepare successful polygon queries before staging.");
        _staged=true;
    }
    public void PublishCommit()
    {
        // StageCommit is the fallible gate. Owner calls publication only once.
        if(!Pending||!_staged)throw new InvalidOperationException("Stage polygon queries before publication.");
        for(var i=0;i<_count;i++)
        {
            var entry=_entries[_touched[i]]!;
            CachedPairs+=(entry.ProposedExists?1:0)-(entry.Exists?1:0);
            entry.Committed.CopyFrom(entry.Proposed);entry.Key=entry.ProposedKey;
            entry.Margin0=entry.ProposedMargin0;entry.Margin1=entry.ProposedMargin1;
            entry.Exists=entry.ProposedExists;entry.Touched=false;
        }
        CompletedSteps++;Pending=_staged=false;_count=0;
    }
    public void Abort()
    {
        if(!Pending)return;
        for(var i=0;i<_count;i++)_entries[_touched[i]]!.Touched=false;
        Pending=_staged=false;_count=0;
    }
    public void Reset()
    {
        if(Pending)throw new InvalidOperationException("Abort polygon queries before reset.");
        foreach(var entry in _entries)if(entry is not null)
        {entry.Committed.Reset();entry.Proposed.Reset();entry.Exists=entry.ProposedExists=entry.Touched=false;}
        CompletedSteps=0;CachedPairs=0;
    }
    // Copying prevents diagnostic callers from mutating published cache state.
    public bool CopyCommitted(AlsContactPairKey key,AlsGjkCache destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var entry=_entries[Slot(key)];
        if(entry is null||!entry.Exists||entry.Key!=key){destination.Reset();return false;}
        destination.CopyFrom(entry.Committed);return true;
    }
    private Entry Touch(int slot)
    {
        var entry=_entries[slot]??=new Entry();
        if(!entry.Touched)
        {
            entry.Proposed.CopyFrom(entry.Committed);entry.ProposedKey=entry.Key;
            entry.ProposedMargin0=entry.Margin0;entry.ProposedMargin1=entry.Margin1;
            entry.ProposedExists=entry.Exists;entry.Touched=true;_touched[_count++]=slot;
        }
        return entry;
    }
    private void Writable()
    {if(!Pending||_staged||_faulted)throw new InvalidOperationException("Polygon queries require an unfaulted, unstaged pending step.");}
    private int Slot(AlsContactPairKey key)
    {
        var a=key.Shape0.Shape;var b=key.Shape1.Shape;
        if(a>=b||b>=_shapeCapacity||(key.Shape0.Body==key.Shape1.Body&&key.Shape0.Generation==key.Shape1.Generation))
            throw new ArgumentException("Polygon query key must retain distinct bodies and ascending registry slots.");
        return checked((int)((long)a*(2L*_shapeCapacity-a-1)/2+b-a-1));
    }
}
