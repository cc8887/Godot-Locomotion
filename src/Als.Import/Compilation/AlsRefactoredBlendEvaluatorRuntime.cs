using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Transactional input filter and explicit-time BlendSpace evaluator.
/// This owner does not replace marker/player clocks; the caller supplies evaluator time.</summary>
public sealed class AlsRefactoredBlendEvaluatorRuntime
{
    private readonly AlsRefactoredBlendPoseSource _source;
    private readonly AlsRefactoredBlendPoseSource.Sampler _sampler;
    private readonly Vector2 _windows;
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private AlsRefactoredBlendFilterState _committed,_candidate;
    private readonly AlsRefactoredTimedBlendSample[] _samples = new AlsRefactoredTimedBlendSample[3];
    private int _cache=-1,_candidateCache=-1,_sampleCount;
    private long _committedFrame=-1,_frame;
    private bool _prepared,_evaluated,_evaluationAttempted;
    public Vector2 CommittedInput=>_committed.Output;
    public Vector2 CandidateInput=>_prepared?_candidate.Output:throw new InvalidOperationException("No prepared blend frame.");
    public int CandidateTriangulationCache=>_prepared?_candidateCache:throw new InvalidOperationException("No prepared blend frame.");
    public ReadOnlySpan<AlsPrecisePose> Pose=>_evaluated?_pose:throw new InvalidOperationException("No evaluated blend frame.");
    public ReadOnlySpan<AlsInertialCurve> Curves=>_evaluated?_curves:throw new InvalidOperationException("No evaluated blend frame.");
    public AlsRefactoredBlendEvaluatorRuntime(AlsRefactoredBlendPoseSource source)
    {
        _source=source;_sampler=source.CreateSampler();_windows=source.FilterWindows;
        _pose=new AlsPrecisePose[source.BoneNames.Length];_curves=new AlsInertialCurve[source.CurveNames.Length];
    }
    public void Prepare(long frame,Vector2 input,float delta,float normalizedTime,bool reinitialize=false)
    {
        if(_prepared||frame<0||frame<=_committedFrame||!float.IsFinite(normalizedTime))throw new ArgumentException("Invalid blend frame identity/time.");
        var previous=reinitialize?default:_committed;
        var next=AlsRefactoredBlendFilter.Advance(previous,input,delta,_windows);
        var count=_source.ResolveSamples(new(next.Output.X,next.Output.Y),normalizedTime,reinitialize?-1:_cache,_samples,out var cache);
        _candidate=next;_candidateCache=cache;_sampleCount=count;_frame=frame;_prepared=true;_evaluated=false;_evaluationAttempted=false;
    }
    public void Evaluate(long frame)
    {
        _evaluated=false;_evaluationAttempted=true;
        if(!_prepared||frame!=_frame)throw new ArgumentException("Foreign blend evaluation frame.");
        // Sampling consumes the frozen update result; repeated Evaluate cannot
        // advance filtering, triangulation traversal or a playback clock.
        _sampler.SampleTimes(_samples.AsSpan(0,_sampleCount),_pose,_curves);
        _evaluated=true;
    }
    public void ValidateCommit(long frame)
    {
        if(!_prepared||(_evaluationAttempted&&!_evaluated)||frame!=_frame)throw new ArgumentException("Blend frame is not ready to commit.");
    }
    public void Commit(long frame)
    {
        ValidateCommit(frame);_committed=_candidate;_cache=_candidateCache;_committedFrame=frame;Cancel();
    }
    public void Cancel(){_prepared=false;_evaluated=false;_evaluationAttempted=false;}
}
