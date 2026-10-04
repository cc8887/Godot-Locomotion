using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// The three CanPlayIdleBreak reads explicitly belong to UE's game-thread
// PreEventGraph batch. They observe Main before its current worker update and
// Montage advance, including when this linked instance has no visited root.
internal readonly record struct LyraLinkedPreUpdateState(bool MontagePlaying,bool HasVelocity,bool Jumping,
    AlsPivotMovementSnapshot Pivot,AlsStopMovementSnapshot Stop);
internal sealed record LyraLinkedPreUpdateCandidate(object Frame,long MainFrame,LyraLinkedPreUpdateState State);

internal sealed class LyraLinkedPreUpdateHost
{
    private LyraLinkedPreUpdateCandidate? _pending;
    private bool _initialized;
    internal LyraLinkedPreUpdateState State {get;private set;}
    internal LyraLinkedPreUpdateState Prepared=>_pending?.State??State;
    internal LyraLinkedPreUpdateHost(LyraMainLayerGraphCatalog graphs,string profile)
    {
        var defaults=graphs.Defaults(profile);
        bool Read(int index)=>defaults.GetProperty("K2Node_PropertyAccess_"+index).GetProperty("value").GetBoolean();
        float Number(int index)=>defaults.GetProperty("K2Node_PropertyAccess_"+index).GetProperty("value").GetSingle();
        AlsDoubleVector Vector(int index)
        {var v=defaults.GetProperty("K2Node_PropertyAccess_"+index).GetProperty("value");return new(v.GetProperty("x").GetDouble(),v.GetProperty("y").GetDouble(),v.GetProperty("z").GetDouble());}
        State=new(Read(48),Read(49),Read(50),new(Vector(25),Vector(26),Number(27)),
            new(Vector(60),Read(61),Number(62),Number(63),Number(64),Number(65)));
    }
    internal void Initialize(in AlsStopMovementSnapshot movement,bool montagePlaying,bool hasVelocity,bool jumping)
    {
        if(_initialized||_pending is not null)throw new InvalidOperationException("Linked preupdate initialization is not repeatable.");
        ValidateMovement(new(default,movement.LastUpdateVelocity,movement.GroundFriction),movement);
        // UE initializes the pre-event-graph batch before graph initialization.
        // Pivot copies (25..27) precede the cached movement component (57), so
        // they keep the CDO values on this first pass. Stop copies follow it.
        State=State with{MontagePlaying=montagePlaying,HasVelocity=hasVelocity,Jumping=jumping,Stop=movement};
        _initialized=true;
    }
    internal void Prepare(object frame,long mainFrame,in LyraLinkedPreUpdateState state)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if(!_initialized)throw new InvalidOperationException("Linked preupdate has no initialized movement component.");
        if(_pending is not null)throw new InvalidOperationException("Linked preupdate is already pending.");
        ValidateMovement(state.Pivot,state.Stop);
        _pending=new(frame,mainFrame,state);
    }
    private static void ValidateMovement(in AlsPivotMovementSnapshot pivot,in AlsStopMovementSnapshot stop)
    {
        if(!pivot.Acceleration.IsFinite||!pivot.LastUpdateVelocity.IsFinite||!float.IsFinite(pivot.GroundFriction)||
            !stop.LastUpdateVelocity.IsFinite||!float.IsFinite(stop.BrakingFriction)||!float.IsFinite(stop.GroundFriction)||
            !float.IsFinite(stop.BrakingFrictionFactor)||!float.IsFinite(stop.BrakingDecelerationWalking))
            throw new ArgumentException("Nonfinite linked movement component snapshot.");
    }
    internal void Validate(long mainFrame)
    {
        if(_pending is not null&&_pending.MainFrame!=mainFrame)
            throw new InvalidOperationException("Foreign linked preupdate commit frame.");
    }
    internal void ValidateVisit(object frame,long mainFrame)
    {
        if(_pending is null||!ReferenceEquals(frame,_pending.Frame)||_pending.MainFrame!=mainFrame)
            throw new InvalidOperationException("Linked worker visit has no matching preupdate.");
    }
    internal void Commit(long mainFrame)
    {Validate(mainFrame);if(_pending is null)return;State=_pending.State;_pending=null;}
    internal void Cancel()=>_pending=null;
}
