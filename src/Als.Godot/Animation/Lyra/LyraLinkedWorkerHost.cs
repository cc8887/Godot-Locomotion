namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraLinkedWorkerState(LyraAimWeights Weights,double TimeFalling,double RightHand,double LeftHand);
internal sealed record LyraLinkedWorkerCandidate(object Frame,long MainFrame,LyraAimWeightCandidate Weights,LyraLinkedWorkerState State);

// A linked proxy runs its worker update once at the first actual root visit.
// Unvisited proxies retain their fields. Graphs borrow this candidate; they do
// not advance a second weight/falling clock or commit worker fields themselves.
internal sealed class LyraLinkedWorkerHost
{
    private readonly LyraAimWeightHost _weights;
    private readonly bool _disableHand;
    private LyraLinkedWorkerCandidate? _pending;
    internal bool Used {get;private set;}
    internal LyraLinkedWorkerState State {get;private set;}
    internal LyraLinkedWorkerState Prepared=>_pending?.State??State;
    internal LyraLinkedWorkerHost(LyraMainLayerGraphCatalog graphs,string profile)
    {
        _weights=new(graphs,profile);var defaults=graphs.Defaults(profile);
        _disableHand=defaults.GetProperty("DisableHandIK").GetProperty("value").GetBoolean();
        double Number(string name)=>defaults.GetProperty(name).GetProperty("value").GetDouble();
        State=new(_weights.Weights,Number("TimeFalling"),Number("HandIK_Right_Alpha"),Number("HandIK_Left_Alpha"));
        if(State.TimeFalling!=0||State.RightHand!=1||State.LeftHand!=1)
            throw new NotSupportedException("Changed linked worker initialization.");
    }
    internal LyraLinkedWorkerState Visit(object frame,LyraMainUpdateCandidate main,in LyraAimWeightInput input,
        bool visited,in LyraSkeletalFeedback feedback)
    {
        if(_pending is not null)
        {
            if(!ReferenceEquals(frame,_pending.Frame)||main.Observation.Frame!=_pending.MainFrame)
                throw new InvalidOperationException("Linked worker belongs to another pending Main frame.");
            return _pending.State;
        }
        if(!visited)return State;
        if(!feedback.Finite)throw new ArgumentException("Nonfinite worker curve feedback.");
        var weights=_weights.Prepare(input);
        double hand=_disableHand?0:1;
        var state=new LyraLinkedWorkerState(weights.Weights,
            main.State.Falling?State.TimeFalling+input.Delta:main.State.Jumping?0:State.TimeFalling,
            Math.Clamp(hand-(double)feedback.Right,0,1),Math.Clamp(hand-(double)feedback.Left,0,1));
        _pending=new(frame,main.Observation.Frame,weights,state);return state;
    }
    internal void Validate(long mainFrame)
    {
        if(_pending is null)return;
        if(_pending.MainFrame!=mainFrame)throw new InvalidOperationException("Foreign linked worker commit frame.");
        _weights.Validate(_pending.Weights);
    }
    internal void Commit(long mainFrame)
    {
        Validate(mainFrame);if(_pending is null)return;
        _weights.Commit(_pending.Weights);State=_pending.State;Used=true;_pending=null;
    }
    internal void Cancel(){_weights.Cancel();_pending=null;}
}
