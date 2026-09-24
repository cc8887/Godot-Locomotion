using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public interface IAlsRefactoredLookPoseSampler
{
    void Evaluate(float pitch,float normalizedTime,Span<AlsPrecisePose> output);
}

public readonly record struct AlsRefactoredHeadFrame(AlsAnimationGraphFrame Graph,AlsRefactoredHeadState Head,
    AlsGraphTraversalCounter LastHeadUpdate,bool Visited,bool HeadUpdated,bool Initialized,float Alpha);

/// <summary>Candidate-owned Head callback chain. The parent updates View/Spine first,
/// then the upstream base graph updates before Prepare. Its pose/curves enter Evaluate.
/// The enclosing owner commits all of those candidates together.</summary>
public sealed class AlsRefactoredHeadRuntime
{
    private readonly uint _character,_generation;
    private readonly AlsRefactoredHeadSettings _settings;
    private readonly IAlsRefactoredLookPoseSampler _sampler;
    private readonly int[] _parents;
    private readonly AlsPrecisePose[] _additive;
    private readonly AlsQuaternion[] _scratch;
    private bool _pending,_ready,_evaluating;
    private AlsRefactoredHeadFrame _candidate;
    public AlsRefactoredHeadFrame Committed { get; private set; }
    public AlsRefactoredHeadFrame Candidate=>_ready?_candidate:throw new InvalidOperationException("Head candidate is not ready.");

    public AlsRefactoredHeadRuntime(uint character,uint generation,AlsRefactoredHeadSettings settings,
        ReadOnlySpan<int> parents,IAlsRefactoredLookPoseSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);
        if(generation==0||parents.IsEmpty)throw new ArgumentException("Invalid Head owner/layout.");
        for(var i=0;i<parents.Length;i++)if(parents[i]<-1||parents[i]>=i)throw new ArgumentException("Invalid Head hierarchy.");
        ReadOnlySpan<float> times=[settings.PitchHalfLife,settings.YawHalfLife,settings.SwitchSidesHalfLife,
            settings.FirstPersonPitchHalfLife,settings.FirstPersonYawHalfLife];
        foreach(var time in times)if(!float.IsFinite(time)||time<0)throw new ArgumentException("Invalid Head settings.");
        _character=character;_generation=generation;_settings=settings;_sampler=sampler;
        _parents=parents.ToArray();_additive=new AlsPrecisePose[parents.Length];_scratch=new AlsQuaternion[parents.Length*2];
        Committed=new(default,AlsRefactoredHeadState.Initial,default,false,false,false,0);
    }
    public void Prepare(in AlsAnimationGraphFrame graph,in AlsRefactoredViewInput input,in AlsRefactoredViewState view,bool visited=true)
    {
        if(_pending||_evaluating)throw new InvalidOperationException("Commit or cancel Head before preparing another frame.");
        graph.Validate(graph.Identity);
        if(graph.Identity.CharacterId!=_character||graph.Identity.SlotGeneration!=_generation||
            Committed.Graph.Identity!=default&&graph.Identity.FrameId<=Committed.Graph.Identity.FrameId||
            !float.IsFinite(view.HeadBlendAmount)||view.HeadBlendAmount is <0 or >1)
            throw new ArgumentException("Head frame owner, history or alpha differs.");
        _pending=true;_ready=false;
        var head=Committed.Head;var counter=Committed.LastHeadUpdate;
        if(!graph.Initialization.MatchesCounter(Committed.Graph.Initialization))counter=default;
        var updated=visited&&view.HeadBlendAmount>AlsPoseBlender.WeightThreshold;
        var initialized=false;
        if(updated)
        {
            initialized=!counter.HasUpdated||!counter.WasSynchronizedCounter(graph.Update);
            if(initialized)head=AlsRefactoredViewModel.InitializeHead(head);
            head=AlsRefactoredViewModel.RefreshHead(input,view,head,_settings);
            counter=graph.Update;
        }
        _candidate=new(graph,head,counter,visited,updated,initialized,view.HeadBlendAmount);_ready=true;
    }
    public void Evaluate(ReadOnlySpan<AlsPrecisePose> basis,ReadOnlySpan<AlsInertialCurve> sourceCurves,
        Span<AlsPrecisePose> output,Span<AlsInertialCurve> curves)
    {
        if(!_ready||_evaluating||!_candidate.Visited||basis.Length!=_parents.Length||output.Length!=basis.Length||curves.Length!=sourceCurves.Length)
            throw new InvalidOperationException("Head evaluation frame or layout differs.");
        foreach(var pose in basis)pose.Validate();
        foreach(var curve in sourceCurves)if(curve.Present&&!float.IsFinite(curve.Value))throw new ArgumentException("Non-finite Head base curve.");
        _evaluating=true;
        try
        {
            if(_candidate.HeadUpdated)
            {
                _sampler.Evaluate(_candidate.Head.Pitch,_candidate.Head.YawAmount,_additive);
                foreach(var pose in _additive)pose.Validate();
                AlsPrecisePoseBlender.MeshApply(basis,_additive,_parents,_scratch,output,_candidate.Alpha);
            }
            else basis.CopyTo(output);
            sourceCurves.CopyTo(curves);
        }
        catch {_ready=false;throw;}
        finally {_evaluating=false;}
    }
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if(!_ready||_evaluating||_candidate.Graph.Identity!=identity)throw new InvalidOperationException("Invalid Head commit.");
    }
    public void Commit(AlsFrameIdentity identity){ValidateCommit(identity);Committed=_candidate;Cancel();}
    public void Cancel()
    {
        if(_evaluating)throw new InvalidOperationException("Cannot cancel an evaluating Head frame.");
        _candidate=default;_pending=_ready=false;
    }
}
