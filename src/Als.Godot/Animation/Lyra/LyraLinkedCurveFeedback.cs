namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraNamedCurveSample(string Name, LyraCurveSample Sample);

// CopyCurveValues reads the enclosing Main's completed curve map, not the
// input of the layer currently being evaluated. Keep the controls that are
// read by the original callbacks even when no locomotion sequence authors them.
internal sealed class LyraLinkedCurveFeedback
{
    private readonly int _sourceCount;
    private readonly Dictionary<string,int> _ids;
    private readonly LyraCurveSample[] _current;
    private readonly LyraCurveSample[] _next;
    private readonly bool[] _seen;
    private object? _candidate;
    public LyraLinkedCurveFeedback(LyraSourceCurveBank curves)
    {
        _sourceCount=curves.Names.Length;
        var names=curves.Names.ToArray().Concat(new[]{"DisableLeftHandPoseOverride","DisableLHandIK","DisableRHandIK","applyHipfireOverridePose",
            "DisableHandIKRetargeting","DisableLegIK","ScaleDownWeaponR"})
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _ids=names.Select((n,i)=>(n,i)).ToDictionary(v=>v.n,v=>v.i,StringComparer.OrdinalIgnoreCase);
        _current=new LyraCurveSample[names.Length];
        _next=new LyraCurveSample[names.Length];_seen=new bool[names.Length];
    }
    public float Value(string name)
    {
        var sample=_current[_ids[name]];return sample.Present?sample.Value:0;
    }
    internal LyraCurveSample[] CommittedHistory()=>_current.ToArray();
    public void Stage(object candidate,ReadOnlySpan<LyraCurveSample> sourceCurves,ReadOnlySpan<LyraNamedCurveSample> controls=default)
    {
        if(_candidate is not null || sourceCurves.Length!=_sourceCount)throw new InvalidOperationException("Invalid or duplicate linked curve copy.");
        foreach(var value in sourceCurves)if(!float.IsFinite(value.Value))throw new ArgumentException("Nonfinite Main curve.");
        Array.Clear(_next);Array.Clear(_seen);sourceCurves.CopyTo(_next);
        foreach(var control in controls)
        {
            if(!_ids.TryGetValue(control.Name,out var id) || id<_sourceCount || _seen[id] || !float.IsFinite(control.Sample.Value))
                throw new ArgumentException("Unknown, duplicate or nonfinite Main control curve.");
            _seen[id]=true;_next[id]=control.Sample;
        }
        _candidate=candidate;
    }
    public void Validate(object candidate,bool expected)
    {if(expected?!ReferenceEquals(candidate,_candidate):_candidate is not null)throw new InvalidOperationException("Linked curve copy belongs to another frame.");}
    public void Commit(object candidate,bool expected)
    {Validate(candidate,expected);if(expected)_next.CopyTo(_current,0);Cancel();}
    public void Cancel(){_candidate=null;}
}
