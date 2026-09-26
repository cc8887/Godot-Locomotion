using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Transactional precise inertial stage for linked-graph composition.
/// Layout and filters are frozen by the caller's validated original graph.</summary>
public sealed class AlsRefactoredPoseInertia
{
    private AlsInertialization _committed, _updated, _evaluatedState;
    private readonly int[] _included;
    private readonly AlsInertialCurve[] _input, _filtered, _curves;
    private readonly AlsPrecisePose[] _pose;
    private AlsFrameIdentity _identity, _last;
    private AlsGraphTraversalCounter _counter, _nextCounter;
    private bool _prepared, _evaluated, _faulted;
    public ReadOnlySpan<AlsPrecisePose> Pose => _evaluated && !_faulted ? _pose : throw new InvalidOperationException("Inertial pose unavailable.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _evaluated && !_faulted ? _curves : throw new InvalidOperationException("Inertial curves unavailable.");
    public AlsRefactoredPoseInertia(int bones, ReadOnlySpan<string> curves, params string[] excluded)
    {
        var names=curves.ToArray();
        if(bones<=0||names.Any(string.IsNullOrWhiteSpace)||names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=names.Length||
            excluded.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=excluded.Length||excluded.Any(n=>Array.IndexOf(names,n)<0))
            throw new ArgumentException("Invalid inertial pose/curve/filter layout.");
        _included = Enumerable.Range(0,curves.Length).Except(excluded.Select(n => Array.IndexOf(names,n))).ToArray();
        _pose = new AlsPrecisePose[bones]; _curves = new AlsInertialCurve[curves.Length];
        _input = new AlsInertialCurve[_included.Length]; _filtered = new AlsInertialCurve[_included.Length];
        _committed = new(bones,_included.Length); _updated = new(bones,_included.Length); _evaluatedState = new(bones,_included.Length);
    }
    public void Prepare(in AlsPoseUpdateContext context, bool initialize = false)
    {
        if (_prepared || context.UpdateCounter is not {HasUpdated:true} || _last.SlotGeneration != 0 &&
            (context.Identity.FrameId <= _last.FrameId || context.Identity.CharacterId != _last.CharacterId || context.Identity.SlotGeneration != _last.SlotGeneration))
            throw new ArgumentException("Invalid inertial frame.");
        _updated.CopyFrom(_committed);
        if (initialize || _counter.HasUpdated && !_counter.WasSynchronizedCounter(context.UpdateCounter.Value)) _updated.Reset();
        _updated.Update(context.Delta); _identity=context.Identity; _nextCounter=context.UpdateCounter.Value;
        _prepared=true; _evaluated=_faulted=false;
    }
    public void Request(float seconds) { ValidateCommit(_identity); if (_evaluated) throw new InvalidOperationException("Late inertia request."); _updated.Request(seconds); }
    public void Evaluate(ReadOnlySpan<AlsPrecisePose> pose, ReadOnlySpan<AlsInertialCurve> curves, in AlsPrecisePose component)
    {
        ValidateCommit(_identity); _evaluated=false;
        try
        {
            if (pose.Length!=_pose.Length || curves.Length!=_curves.Length) throw new ArgumentException("Foreign inertial layout.");
            _evaluatedState.CopyFrom(_updated);
            for(var i=0;i<_included.Length;i++) _input[i]=curves[_included[i]];
            _evaluatedState.EvaluatePrecisePose(pose,_input,component,0,0,_pose,_filtered);
            curves.CopyTo(_curves); for(var i=0;i<_included.Length;i++) _curves[_included[i]]=_filtered[i];
            _evaluated=true;
        }
        catch { _faulted=true; throw; }
    }
    public void ValidateCommit(in AlsFrameIdentity identity)
    { if (!_prepared || _faulted || identity!=_identity) throw new ArgumentException("Incomplete inertial frame."); }
    public void Commit(in AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        if (_evaluated) (_committed,_evaluatedState)=(_evaluatedState,_committed); else (_committed,_updated)=(_updated,_committed);
        _counter=_nextCounter; _last=identity; Cancel();
    }
    public void Cancel() { _prepared=_evaluated=_faulted=false; }
}
