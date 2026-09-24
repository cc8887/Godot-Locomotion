using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Transactional input filter and explicit-time BlendSpace evaluator.
/// This owner does not replace marker/player clocks; the caller supplies evaluator time.</summary>
public sealed class AlsRefactoredBlendEvaluatorRuntime
{
    private readonly AlsRefactoredBlendPoseSource.Sampler _sampler;
    private readonly Vector2 _windows;
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private AlsRefactoredBlendFilterState _committed,_candidate;
    private int _cache=-1,_candidateCache=-1,_startCache=-1;
    private long _committedFrame=-1,_frame;
    private float _time;
    private bool _prepared,_evaluated;
    public Vector2 CommittedInput=>_committed.Output;
    public Vector2 CandidateInput=>_prepared?_candidate.Output:throw new InvalidOperationException("No prepared blend frame.");
    public ReadOnlySpan<AlsPrecisePose> Pose=>_evaluated?_pose:throw new InvalidOperationException("No evaluated blend frame.");
    public ReadOnlySpan<AlsInertialCurve> Curves=>_evaluated?_curves:throw new InvalidOperationException("No evaluated blend frame.");
    public AlsRefactoredBlendEvaluatorRuntime(AlsRefactoredBlendPoseSource source)
    {
        _sampler=source.CreateSampler();_windows=source.FilterWindows;
        _pose=new AlsPrecisePose[source.BoneNames.Length];_curves=new AlsInertialCurve[source.CurveNames.Length];
    }
    public void Prepare(long frame,Vector2 input,float delta,float normalizedTime,bool reinitialize=false)
    {
        if(_prepared||frame<=_committedFrame||!float.IsFinite(normalizedTime))throw new ArgumentException("Invalid blend frame identity/time.");
        var previous=reinitialize?default:_committed;
        var next=AlsRefactoredBlendFilter.Advance(previous,input,delta,_windows);
        _candidate=next;_startCache=reinitialize?-1:_cache;_time=normalizedTime;_frame=frame;_prepared=true;_evaluated=false;
    }
    public void Evaluate(long frame)
    {
        if(!_prepared||frame!=_frame)throw new ArgumentException("Foreign blend evaluation frame.");
        _evaluated=false;
        var p=_candidate.Output;
        // Repeated Evaluate starts at the same candidate traversal cache. Sampling
        // does not advance the filter or a playback clock.
        _candidateCache=_sampler.Evaluate(new(p.X,p.Y),_time,_startCache,_pose,_curves);
        _evaluated=true;
    }
    public void ValidateCommit(long frame)
    {
        if(!_prepared||!_evaluated||frame!=_frame)throw new ArgumentException("Blend frame is not ready to commit.");
    }
    public void Commit(long frame)
    {
        ValidateCommit(frame);_committed=_candidate;_cache=_candidateCache;_committedFrame=frame;Cancel();
    }
    public void Cancel(){_prepared=false;_evaluated=false;}
}
