using System.Collections.Immutable;

namespace GodotAls.Core.Locomotion;

// One instance is owned by the actual Main or Linked Provider. Phase calls and
// the character transaction use the same node histories. Scoped pose memory is
// kept by the evaluation scope, as in FCachedPoseThreadContext.
public sealed class AlsPoseCacheLifecycle
{
    private AlsPoseCacheNodeHistory[] _committed,_candidate;
    private object? _frame;
    private bool _failed;
    public bool Pending=>_frame is not null;
    public ImmutableArray<AlsPoseCacheNodeHistory> History=>_committed.ToImmutableArray();
    public ImmutableArray<AlsPoseCacheNodeHistory> Prepared
        =>Pending?_candidate.ToImmutableArray():throw new InvalidOperationException("No pose-cache frame.");
    public AlsPoseCacheLifecycle(params int[] nodes)
    {
        if(nodes.Length==0||nodes.Any(n=>n<0)||nodes.Distinct().Count()!=nodes.Length)
            throw new ArgumentException("Invalid owner cache nodes.");
        _committed=nodes.Select(n=>new AlsPoseCacheNodeHistory(n,default,default,default,0)).ToArray();
        _candidate=new AlsPoseCacheNodeHistory[nodes.Length];
    }
    private int Index(int node)
    {
        for(var i=0;i<_committed.Length;i++)if(_committed[i].Node==node)return i;
        throw new InvalidOperationException("Cache node belongs to another owner.");
    }
    private void Idle()
    {if(Pending)throw new InvalidOperationException("Cache phases require an idle owner.");}
    public bool Initialize(int node,AlsGraphTraversalCounter counter,Action source)
    {
        Idle();ArgumentNullException.ThrowIfNull(source);
        var i=Index(node);if(!AlsPoseCacheHistory.TryInitialize(ref _committed[i],counter))return false;
        // The original SaveCachedPose UpdateCounter is never synchronized by
        // Update/PostGraphUpdate. Do not infer relevance from player history.
        source();return true;
    }
    public bool CacheBones(int node,AlsGraphTraversalCounter counter,Action source)
    {
        Idle();ArgumentNullException.ThrowIfNull(source);
        var i=Index(node);if(!AlsPoseCacheHistory.TryCacheBones(ref _committed[i],counter))return false;
        source();return true;
    }
    public void Begin(object frame)
    {ArgumentNullException.ThrowIfNull(frame);Idle();_committed.CopyTo(_candidate,0);_frame=frame;_failed=false;}
    public void Validate(object frame)
    {if(!ReferenceEquals(frame,_frame)||!Pending||_failed)throw new InvalidOperationException("Foreign or closed cache owner frame.");}
    public bool CacheBones(object frame,int node,AlsGraphTraversalCounter counter,Action source)
    {
        Validate(frame);ArgumentNullException.ThrowIfNull(source);
        var i=Index(node);if(!AlsPoseCacheHistory.TryCacheBones(ref _candidate[i],counter))return false;
        try{source();return true;}catch{_failed=true;throw;}
    }
    public void PostUpdate(object frame,ReadOnlySpan<(int Node,float Weight)> updates)
    {
        Validate(frame);
        for(var i=0;i<_candidate.Length;i++)_candidate[i]=_candidate[i] with{GlobalWeight=0};
        foreach(var (node,weight) in updates)
        {
            if(!float.IsFinite(weight)||weight<0)throw new ArgumentException("Invalid cache global weight.");
            var i=Index(node);_candidate[i]=_candidate[i] with{GlobalWeight=weight};
        }
    }
    public void RecordWeight(int node,float weight)
    {
        if(!Pending||!float.IsFinite(weight)||weight<0)throw new InvalidOperationException("Cache weight needs its current owner frame.");
        var i=Index(node);_candidate[i]=_candidate[i] with{GlobalWeight=weight};
    }
    public bool EvaluationMatches(object frame,int node,AlsGraphTraversalCounter counter)
    {Validate(frame);if(!counter.HasUpdated)throw new ArgumentException("Invalid cache evaluation counter.");return _candidate[Index(node)].Evaluation.MatchesAll(counter);}
    public void SynchronizeEvaluation(object frame,int node,AlsGraphTraversalCounter counter)
    {Validate(frame);if(!counter.HasUpdated)throw new ArgumentException("Invalid cache evaluation counter.");var i=Index(node);_candidate[i]=_candidate[i] with{Evaluation=counter};}
    public void Commit(object frame)
    {Validate(frame);(_committed,_candidate)=(_candidate,_committed);_frame=null;}
    public void Cancel(){_frame=null;_failed=false;}
}
