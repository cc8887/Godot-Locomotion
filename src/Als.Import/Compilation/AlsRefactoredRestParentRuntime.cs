using GodotAls.Core.Contracts;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public enum AlsRefactoredRestStance { Standing, Crouching, Other }
public enum AlsRefactoredRestRotation { VelocityDirection, ViewDirection, Aiming }
public readonly record struct AlsRefactoredRestInput(float Delta, float Yaw, float YawSpeed, bool Moving, bool FirstPerson,
    AlsRefactoredRestRotation Rotation, AlsRefactoredRestStance Stance, bool TransitionsAllowed, bool PendingUpdate,
    float Scale, float LeftLock, float RightLock, AlsDoubleVector LeftTarget, AlsDoubleVector LeftLocation,
    AlsDoubleVector RightTarget, AlsDoubleVector RightLocation, bool GameWorld = true);
public sealed record AlsRefactoredRestPlayback(string Sequence, string Slot, float PlayRate, float CurvePlayRate, float BlendIn,
    float BlendOut, bool InertialBlendOut, float StartTime = 0);
public readonly record struct AlsRefactoredRestState(AlsRefactoredRotateState Rotate, float TurnDelay, float TurnPlayRate, int DynamicFrameDelay,
    AlsRefactoredRestPlayback? QueuedTurn, AlsRefactoredRestPlayback? QueuedTransition)
{
    public static AlsRefactoredRestState Initial => new(new(false,false,1),0,1,0,null,null);
}

/// <summary>Candidate original rest Parent updates and playback requests. The
/// host executes callback traversal and consumes requests in its action bank;
/// this class never starts a montage or performs a main-thread side effect.</summary>
public sealed class AlsRefactoredRestParentRuntime
{
    public AlsRefactoredRestSettings Settings { get; }
    private readonly HashSet<AlsRefactoredStanceCallback> _bindings;
    private AlsRefactoredRestState _candidate;
    private AlsRefactoredRestInput _input;
    private AlsFrameIdentity _identity, _committedIdentity;
    private bool _prepared, _hasCommitted, _rotateUpdated, _turnUpdated, _dynamicUpdated;
    private readonly AlsRefactoredRestMontages? _sharedMontages;
    private readonly AlsMontageRuntime? _sharedBank;
    private readonly AlsTransitionQueueRuntime? _sharedTransitions;
    internal bool UsesSharedTransitions=>_sharedTransitions is not null;
    public AlsRefactoredRestState Committed { get; private set; } = AlsRefactoredRestState.Initial;
    public AlsRefactoredRestState Candidate => _prepared ? _candidate : throw new InvalidOperationException("No rest Parent candidate.");
    public AlsRefactoredRestParentRuntime(AlsRefactoredRestSettings settings, params AlsRefactoredStanceCallbacks[] callbacks)
    {
        if (callbacks.Length == 0 || callbacks.Any(c => c.CatalogDigest != settings.CatalogDigest)) throw new ArgumentException("Foreign rest callbacks.");
        Settings = settings; _bindings = callbacks.SelectMany(c => c.Nodes.ToArray()).Where(c => c.Function is
            AlsRefactoredStanceFunction.RefreshDynamicTransitions or AlsRefactoredStanceFunction.RefreshRotateInPlace or
            AlsRefactoredStanceFunction.InitializeTurnInPlace or AlsRefactoredStanceFunction.RefreshTurnInPlace).ToHashSet();
    }
    public AlsRefactoredRestParentRuntime(AlsRefactoredRestSettings settings,AlsRefactoredRestMontages montages,
        AlsMontageRuntime bank,AlsTransitionQueueRuntime transitions,params AlsRefactoredStanceCallbacks[] callbacks):this(settings,callbacks)
    {
        if(!ReferenceEquals(settings,montages.Settings))throw new ArgumentException("Foreign shared Rest settings.");
        montages.ValidateResources(bank);_sharedMontages=montages;_sharedBank=bank;_sharedTransitions=transitions;
    }
    internal void ValidateSharedTransitions(AlsRefactoredRestMontages montages,AlsMontageRuntime bank,AlsTransitionQueueRuntime transitions,in AlsFrameIdentity identity)
    {
        ValidateContext(identity,montages.Settings.CatalogDigest);
        if(!ReferenceEquals(_sharedMontages,montages)||!ReferenceEquals(_sharedBank,bank)||!ReferenceEquals(_sharedTransitions,transitions))
            throw new ArgumentException("Foreign shared Rest queue owner.");
        transitions.ValidateBank(bank,identity);
    }
    public void Prepare(in AlsFrameIdentity identity, in AlsRefactoredRestInput input, bool initializeInstance = false)
    {
        if (_prepared || identity.SlotGeneration == 0 || _hasCommitted && (identity.FrameId <= _committedIdentity.FrameId || identity.CharacterId != _committedIdentity.CharacterId || identity.SlotGeneration != _committedIdentity.SlotGeneration) ||
            !Enum.IsDefined(input.Rotation) || !Enum.IsDefined(input.Stance) || !float.IsFinite(input.Delta) || input.Delta < 0 ||
            !float.IsFinite(input.Yaw) || MathF.Abs(input.Yaw) > 180 || !float.IsFinite(input.YawSpeed) || input.YawSpeed < 0 ||
            !float.IsFinite(input.Scale) || input.Scale <= 0 || !float.IsFinite(input.LeftLock) || !float.IsFinite(input.RightLock) ||
            input.LeftLock is < 0 or > 1 || input.RightLock is < 0 or > 1 || !input.LeftTarget.IsFinite || !input.LeftLocation.IsFinite ||
            !input.RightTarget.IsFinite || !input.RightLocation.IsFinite) throw new ArgumentException("Invalid rest Parent identity/input.");
        _sharedTransitions?.ValidateBank(_sharedBank!,identity);
        _identity = identity; _input = input; _candidate = initializeInstance ? AlsRefactoredRestState.Initial : Committed;
        _rotateUpdated = _turnUpdated = _dynamicUpdated = false; _prepared = true;
    }
    public void RefreshRotateInPlace(long frame)
    {
        Check(frame);_sharedTransitions?.ValidateBank(_sharedBank!,_identity);
        if(!_input.GameWorld||_rotateUpdated)return;_rotateUpdated=true;
        var s=Settings;var i=_input;
        _candidate=_candidate with{Rotate=AlsRefactoredRestModel.Rotate(new(s.RotateYaw,s.FirstPersonYaw,s.ReferenceYawSpeed,s.RotateRate),
            _candidate.Rotate.PlayRate,i.Moving,i.Rotation==AlsRefactoredRestRotation.Aiming,i.FirstPerson,i.Yaw,i.YawSpeed,i.Delta,i.PendingUpdate)};
    }
    public void Apply(in AlsFrameIdentity identity, AlsRefactoredStanceCallback command)
    {
        Check(identity.FrameId);
        if (identity != _identity || !_bindings.Contains(command)) throw new ArgumentException("Foreign rest Parent callback.");
        _sharedTransitions?.ValidateBank(_sharedBank!,identity);
        if (command.Function == AlsRefactoredStanceFunction.InitializeTurnInPlace) { _candidate = _candidate with {TurnDelay = 0}; return; }
        if (!_input.GameWorld) return;
        var i = _input; var s = Settings;
        switch (command.Function)
        {
            case AlsRefactoredStanceFunction.RefreshRotateInPlace:
                RefreshRotateInPlace(identity.FrameId);
                break;
            case AlsRefactoredStanceFunction.RefreshTurnInPlace:
                if (_turnUpdated) return; _turnUpdated = true;
                var turn = AlsRefactoredRestModel.Turn(new(s.TurnYaw,s.TurnSpeed,s.TurnDelay,s.Turn180Yaw),_candidate.TurnDelay,
                    i.TransitionsAllowed && i.Rotation == AlsRefactoredRestRotation.ViewDirection && !i.FirstPerson,
                    i.Stance == AlsRefactoredRestStance.Crouching,i.Stance != AlsRefactoredRestStance.Other,i.Yaw,i.YawSpeed,i.Delta);
                _candidate = _candidate with {TurnDelay = turn.Delay};
                if (turn.AssetIndex < 0) return;
                var asset = s.Turns[turn.AssetIndex]; var curveRate = asset.PlayRate;
                if (asset.ScalePlayRate) curveRate *= MathF.Abs(i.Yaw/asset.AnimatedAngle);
                _candidate = _candidate with {QueuedTurn = new(asset.Sequence,i.Stance == AlsRefactoredRestStance.Crouching ? "TurnInPlaceCrouching" : "TurnInPlaceStanding",
                    asset.PlayRate,curveRate,s.TurnBlend,s.TurnBlend,true)};
                break;
            case AlsRefactoredStanceFunction.RefreshDynamicTransitions:
                if (_dynamicUpdated) return; _dynamicUpdated = true;
                if (_candidate.DynamicFrameDelay > 0) { _candidate = _candidate with {DynamicFrameDelay = _candidate.DynamicFrameDelay-1}; return; }
                if (!i.TransitionsAllowed) return;
                var selected = AlsRefactoredRestModel.Dynamic(s.DynamicDistance,i.Scale,i.LeftLock,i.RightLock,i.LeftTarget,i.LeftLocation,i.RightTarget,i.RightLocation,
                    i.Stance == AlsRefactoredRestStance.Crouching);
                if (selected < 0) return;
                var request=new AlsRefactoredRestPlayback(s.DynamicSequence(selected >= 2,selected%2 == 0),"Transition",s.DynamicRate,1,s.DynamicBlend,s.DynamicBlend,false);
                if(_sharedTransitions is not null)_sharedMontages!.QueueTransition(_sharedBank!,_sharedTransitions,identity,request);
                _candidate = _candidate with {DynamicFrameDelay = 2, QueuedTransition = _sharedTransitions is null?request:null};
                break;
        }
    }
    // Call only when the host has accepted this exact request into its candidate
    // action bank. Commit both owners together; cancellation restores the queue.
    public void AcceptPlayback(long frame, AlsRefactoredRestPlayback request, bool turn, bool stopTransitionsQueued = false)
    {
        Check(frame); ArgumentNullException.ThrowIfNull(request);
        if (stopTransitionsQueued) return;
        if (!ReferenceEquals(turn ? _candidate.QueuedTurn : _candidate.QueuedTransition,request)) throw new ArgumentException("Stale rest playback request.");
        _candidate = turn ? _candidate with {QueuedTurn = null, TurnPlayRate = request.CurvePlayRate} : _candidate with {QueuedTransition = null};
    }
    private void Check(long frame) { if (!_prepared || frame != _identity.FrameId) throw new ArgumentException("Foreign rest Parent frame."); }
    internal void ValidateContext(in AlsFrameIdentity identity, string digest)
    { Check(identity.FrameId);if(identity!=_identity||digest!=Settings.CatalogDigest)throw new ArgumentException("Foreign rest Parent context."); }
    public void ValidateCommit(long frame) => Check(frame);
    public void Commit(long frame) { Check(frame); Committed = _candidate; _committedIdentity = _identity; _hasCommitted = true; Cancel(); }
    public void Cancel() { _prepared = false; }
}
