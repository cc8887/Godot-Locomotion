using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredStandingHostInput(AlsRefactoredMovementInput Movement,
    AlsRefactoredRestInput Rest, AlsRefactoredStandingMovementInput Details,
    AlsRefactoredQuickStopInput QuickStop, float FootPlanted, bool MovingSmooth, bool ActivatePivot = false);

/// <summary>Exclusive Standing graph, with a standalone or character-shared action bank.
/// Prepare/Evaluate may run on one worker; after joining it, PostUpdateActions
/// consumes Parent requests then main-thread notifies. Validate all before Commit.
/// Output ends at node68, before the character's outer Transition Slot/layers.</summary>
public sealed class AlsRefactoredStandingHost
{
    private readonly AlsRefactoredStandingHostProfile _profile;
    private readonly uint _character, _generation;
    private readonly AlsRefactoredStandingRuntime _standing;
    private readonly AlsRefactoredStopRuntime _stop;
    private readonly AlsRefactoredStopSourceRuntime _stopSources;
    private readonly AlsRefactoredMovementParentRuntime _parent;
    private readonly AlsRefactoredRestParentRuntime _restParent;
    private readonly AlsRefactoredStandingRestTraversal _restTraversal;
    private readonly AlsRefactoredStandingMovementTraversal _traversal;
    private readonly IAlsRefactoredSourcePlayers _players, _rotate;
    private readonly AlsRefactoredDirectionPose.Sampler _direction;
    private readonly AlsRefactoredMovementCacheRuntime _movement;
    private readonly AlsRefactoredMovementDetailsPose.Sampler _details;
    private readonly AlsRefactoredMovementInertialization _movementInertia;
    private readonly AlsRefactoredStandingPose.Runtime _standingPose;
    private readonly AlsRefactoredStandingInertialization _inertia;
    private readonly AlsRefactoredStandingIdleSlot _idle;
    private readonly AlsRefactoredStandingOutput _output;
    private readonly AlsMontageRuntime _bank;
    private readonly AlsTransitionQueueRuntime _queue;
    private readonly AlsRefactoredCharacterActionRuntime? _sharedActions;
    private readonly AlsRefactoredStandingObservation[] _clocks = new AlsRefactoredStandingObservation[2], _nextClocks = new AlsRefactoredStandingObservation[2];
    private readonly AlsRefactoredMovementCacheRead[] _reads = new AlsRefactoredMovementCacheRead[6];
    private readonly int[] _initialReads = new int[5];
    private readonly AlsRefactoredSourcePlayerInput[] _rotateInputs = new AlsRefactoredSourcePlayerInput[2];
    private readonly AlsPrecisePose[] _directionPose, _detailsPose, _pose;
    private readonly AlsInertialCurve[] _directionCurves, _detailsCurves, _curves;
    private AlsFrameIdentity _identity;
    private AlsPoseUpdateContext _context;
    private bool _initialize;
    private AlsRefactoredStandingHostInput _input;
    private float _machineWeight, _nextMachineWeight;
    private bool _prepared, _evaluated, _postUpdated, _hasIdle, _hasStop, _hasMovement, _poseBegun;

    public AlsFrameIdentity CommittedIdentity { get; private set; }
    internal bool Prepared=>_prepared;
    internal float CommittedMachineWeight => _machineWeight;
    public int CommittedState => _standing.CommittedState.CurrentState;
    public int State { get { Check(); return _standing.Candidate.State.CurrentState; } }
    public AlsRefactoredRestState RestState { get { Check(); return _restParent.Candidate; } }
    public AlsRefactoredMovementState MovementState { get { Check(); return _parent.MovementCandidate; } }
    public ReadOnlySpan<string> BoneNames => _profile.Pose.BoneNames;
    public ReadOnlySpan<string> CurveNames => _output.CurveNames;
    public ReadOnlySpan<AlsPrecisePose> Pose { get { RequirePose(); return _pose; } }
    public ReadOnlySpan<AlsInertialCurve> Curves { get { RequirePose(); return _curves; } }
    public ReadOnlySpan<AlsRefactoredDirectionCacheUpdate> CacheUpdates { get { Check(); return _traversal.CacheUpdates; } }
    public ReadOnlySpan<AlsMontageInstance> CommittedMontages => _bank.Committed;
    public ReadOnlySpan<AlsMontageInstance> CandidateMontages { get { Check(); return _bank.Candidate; } }
    public AlsMontageFrame MontageFrame { get { Check(); return _bank.Frame; } }
    public int QuickStopDispatchCount { get; private set; }
    public int PivotDispatchCount { get; private set; }
    public bool PivotActive { get { Check(); return _parent.Candidate.PivotActive; } }
    public int MovementDetailsState { get { Check(); return _hasMovement ? _traversal.DetailsMachine.Candidate.State.CurrentState : -1; } }
    internal AlsGroundedMachineUpdate? DirectionUpdate { get { Check(); return _hasMovement ? _traversal.Movement.Direction.Candidate : null; } }
    internal IAlsRefactoredSourcePlayers? MovementPlayers { get { Check(); return _hasMovement ? _players : null; } }

    internal AlsRefactoredStandingHost(AlsRefactoredStandingHostProfile profile, uint character, uint generation,
        AlsRefactoredCharacterActionRuntime? sharedActions = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        _profile = profile; _character = character; _generation = generation;
        _standing = new(profile.Standing); _stop = new(profile.Stop.Resources); _stopSources = new(profile.Stop);
        _parent = sharedActions?.MovementParent??new(profile.Details.Callbacks, profile.MovementSettings);
        _traversal = new(profile.Catalog, profile.Standing, profile.Details, profile.Direction, 0);
        _players = sharedActions?.Sources?.CreateView(AlsRefactoredLocomotionSourceOwner.StandingMovement) ?? new AlsRefactoredSourcePlayerRuntime(profile.Catalog, profile.Sync, profile.Triangles, profile.Details.Players.Bind(0,
            new Dictionary<string, int> { ["Movement"] = 0, ["Run Start"] = 1, ["First Pivot"] = 2, ["Second Pivot"] = 3 }));
        _rotate = sharedActions?.Sources?.CreateView(AlsRefactoredLocomotionSourceOwner.StandingRotate) ?? new AlsRefactoredSourcePlayerRuntime(profile.Catalog, profile.Sync, profile.Triangles, profile.Standing.RotatePlayers.Bind(0));
        _traversal.DetailsSources.Registration = _players;
        _traversal.Movement.Sources.Registration = _players;
        _direction = profile.DirectionPose.CreateSampler();
        _movement = new(profile.Movement, profile.DirectionPose.BoneNames, profile.DirectionPose.CurveNames);
        _details = profile.Pose.Movement.CreateSampler(); _movementInertia = new(profile.Catalog, profile.Pose.Movement);
        _standingPose = profile.Pose.CreateRuntime(_rotate, 0); _inertia = new(profile.Catalog, profile.Pose);
        _output = new(profile.Catalog, profile.Pose, _inertia);
        _sharedActions = sharedActions;
        _bank = sharedActions?.Bank ?? new([], sequences: profile.Assets);
        _queue = sharedActions?.Queue ?? new(_bank, character, generation);
        _restParent = sharedActions?.RestParent??new(profile.Montages.Settings, profile.Montages, _bank, _queue, profile.Callbacks);
        _restTraversal = new(profile.RestGraph, profile.Callbacks);
        _idle = new(profile.Catalog, profile.RestGraph, profile.Pose.Rest, profile.MontagePose);
        _directionPose = new AlsPrecisePose[BoneNames.Length]; _detailsPose = new AlsPrecisePose[BoneNames.Length]; _pose = new AlsPrecisePose[BoneNames.Length];
        _directionCurves = new AlsInertialCurve[profile.DirectionPose.CurveNames.Length];
        _detailsCurves = new AlsInertialCurve[profile.Pose.Movement.CurveNames.Length]; _curves = new AlsInertialCurve[CurveNames.Length];
        for (var i = 0; i < 2; i++) _clocks[i] = new(profile.Standing.RotatePlayers.Players[i].PropertyIndex, 0, 0, false);
    }

    public void Prepare(in AlsPoseUpdateContext context, in AlsRefactoredStandingHostInput input,
        AlsGraphTraversalCounter initialization, bool initializeInstance = false)
    {
        if (_prepared) throw new InvalidOperationException("Standing frame already prepared.");
        if (context.Identity.CharacterId != _character || context.Identity.SlotGeneration != _generation ||
            context.UpdateCounter is not { HasUpdated: true } || !context.HasSharedContext ||
            context.Delta != input.Movement.Delta || context.Delta != input.Rest.Delta ||
            _sharedActions is null&&(input.Rest.Stance != AlsRefactoredRestStance.Standing || input.QuickStop.Crouching) ||
            input.QuickStop.VelocityDirection != (input.Rest.Rotation == AlsRefactoredRestRotation.VelocityDirection) ||
            !float.IsFinite(input.FootPlanted)) throw new ArgumentException("Invalid Standing host input/context.");
        _identity = context.Identity; _input = input; _context = context; _initialize = initializeInstance;
        var frame = _identity.FrameId;
        try
        {
            _players.BeginRegistration(initializeInstance); _rotate.BeginRegistration(initializeInstance);
            if (_sharedActions is null) { _queue.Begin(_identity); _bank.Begin(_identity, context.Delta); }
            else _sharedActions.ValidateUpdate(context);
            if(_sharedActions is not null)_sharedActions.PrepareParents(context,input,initializeInstance);
            else
            {
                _parent.Prepare(_identity, input.Movement, initializeInstance); _parent.RefreshGrounded(frame);
                if (input.ActivatePivot) _parent.ActivatePivot(frame);
                _restParent.Prepare(_identity, input.Rest, initializeInstance);
            }
            var source = AlsRefactoredStandingInertialization.SourceContext(context);
            _restTraversal.Begin(source, _restParent, initializeInstance);
            var rotate = _restParent.Candidate.Rotate;
            _standing.Prepare(frame, new(input.MovingSmooth, rotate.Left, rotate.Right), _clocks, context.Delta,
                context.Weight, initializeInstance, context.UpdateCounter);
            _profile.Actions.QueueStanding(_queue, _identity, _standing);
            var update = _standing.Candidate;
            var initialized = 0;
            for (var i = 0; i < update.InitializationCount; i++) initialized |= 1 << update.GetInitialization(i);
            var readCount = 0; var initialCount = 0; var rotateCount = 0;
            if ((initialized & 2) != 0) _initialReads[initialCount++] = 55;
            for (var i = 0; i < update.UpdateCount; i++)
            {
                var state = update.GetUpdate(i);
                var path = source.WithWeight(state.Weight).WithState(65, state.State, state.InertializationSync);
                var reset = (initialized & (1 << state.State)) != 0;
                switch (state.State)
                {
                    case 0:
                        _hasIdle = true; _restTraversal.BeginIdle(frame);
                        _idle.Prepare(_bank.Frame, path, initializeInstance || reset);
                        _restTraversal.CompleteIdle(frame);
                        break;
                    case 1: _reads[readCount++] = new(55, 66, path); break;
                    case 2:
                        _hasStop = true; _stop.Prepare(frame, input.FootPlanted, context.Delta, state.Weight, reset, context.UpdateCounter);
                        _profile.Actions.QueueStopState(_queue, _bank, _identity, _stop);
                        // These bindings are sampled BEFORE the deferred cache66 callbacks.
                        _stopSources.Prepare(_stop, path, _parent.MovementCandidate.VelocityBlend, _parent.Candidate.HipsDirection, initializeInstance);
                        _stopSources.CacheReads.CopyTo(_reads.AsSpan(readCount)); readCount += _stopSources.CacheReads.Length;
                        _stopSources.CacheInitializationReads.CopyTo(_initialReads.AsSpan(initialCount)); initialCount += _stopSources.CacheInitializationReads.Length;
                        break;
                    default:
                        _rotateInputs[rotateCount++] = _profile.Standing.RotatePlayers.Input(0, state.State - 3,
                            rotate.PlayRate, rotate.Left, rotate.Right, state.Weight, reset) with { RequestedInertialization = path.InertializationSync };
                        _rotate.Register(_rotateInputs[rotateCount - 1], path);
                        break;
                }
            }
            _restTraversal.Complete(frame);
            _nextMachineWeight = context.Weight;
            // GetInstanceMachineWeight reads the proxy's previous buffer. The
            // linked host owns this value; it is not a constant Parent input.
            var detailsInput = input.Details with { StandingMachineWeight = initializeInstance ? 0 : _machineWeight };
            _traversal.Prepare(source, _reads.AsSpan(0, readCount), _initialReads.AsSpan(0, initialCount), initialization,
                _parent, detailsInput, initializeInstance);
            _rotate.Prepare(frame, _rotateInputs.AsSpan(0, rotateCount), context.Delta, initializeInstance);
            _hasMovement = _traversal.HasMovement;
            // Sync membership belongs to the animation frame, not the lifetime
            // of the cached Movement branch. Empty frames retire prior groups
            // while the source runtime retains each hidden player's own state.
            _players.Prepare(frame, _hasMovement ? _traversal.Movement.SourceInputs : [], context.Delta, initializeInstance);
            // Capture Parent values at this graph's visit. A later linked graph
            // may refresh the shared Parent before the outer Sync tick finishes.
            if (_hasMovement)
                _movement.Prepare(frame, input.Details.UnweightedRunningAmount, _parent.MovementCandidate.Lean, context.Delta, _traversal.Movement.InitializeMovement);
            _prepared = true;
            if (!_players.Deferred) CompleteSources();
        }
        catch { Cancel(); throw; }
    }

    internal void CompleteSources()
    {
        Check();
        var frame = _identity.FrameId; var context = _context; var initializeInstance = _initialize;
        var update = _standing.Candidate;
        if (_hasMovement)
        {
            _traversal.DetailsSources.CaptureSourceTimes(frame, _players);
            _movementInertia.Prepare(_traversal.DetailsContext, _traversal.DetailsMachine, _traversal.Movement, initializeInstance);
        }
        _inertia.Prepare(context, _standing, _traversal, _hasMovement ? _movementInertia : null, initializeInstance,
            _profile.Montages.StandingSlotRequest(_bank.Frame, _identity, _restTraversal));
        _clocks.CopyTo(_nextClocks, 0);
        for (var i = 0; i < 2; i++)
            if (initializeInstance || (update.ClearCachedWeightStates & (1 << (3 + i))) != 0)
                _nextClocks[i] = new(_profile.Standing.RotatePlayers.Players[i].PropertyIndex, 0, 0, false);
        foreach (var tick in _rotate.Ticks)
            foreach (var player in _rotate.Players)
                if (player.PlayerId == tick.PlayerId)
                    _nextClocks[tick.PlayerId] = new(_profile.Standing.RotatePlayers.Players[tick.PlayerId].PropertyIndex,
                        tick.Weight, player.Time, tick.Looping);
    }

    public void Evaluate(in AlsPrecisePose component, long attachParent = 0, float teleportDistance = 0)
    {
        Check();
        if (_postUpdated) throw new InvalidOperationException("Evaluate must precede post-update actions.");
        var frame = _identity.FrameId;
        try
        {
            _evaluated = false;
            if (_hasMovement)
            {
                _direction.Sample(frame, _traversal.Movement.Direction, _traversal.Movement.Sources, _players, _directionPose, _directionCurves);
                _movement.Evaluate(frame, _directionPose, _directionCurves);
                _details.Sample(frame, _traversal.DetailsMachine, _traversal.DetailsSources, _players, _movement, _detailsPose, _detailsCurves);
                _movementInertia.Evaluate(frame, _detailsPose, _detailsCurves, component, attachParent, teleportDistance);
            }
            // Recollect on repeated Evaluate too: component motion may change
            // inertial output without advancing any update/player histories.
            _standingPose.Cancel();
            _standingPose.Begin(_identity, _standing); _poseBegun = true;
            if (_standingPose.NeedsState(0)) { _idle.Evaluate(); _standingPose.CaptureIdleSlot(_idle.Pose, _idle.Curves, _restParent.Candidate.TurnPlayRate); }
            if (_standingPose.NeedsState(1)) _standingPose.CaptureMovement(_movementInertia);
            if (_standingPose.NeedsState(2)) _standingPose.CaptureStop(_stop, _stopSources, _movementInertia);
            for (var side = 0; side < 2; side++) if (_standingPose.NeedsState(side + 3))
            { _rotate.Evaluate(frame, side); _standingPose.CaptureRotate(side == 0, _restParent.Candidate.Rotate.PlayRate); }
            _standingPose.Evaluate(frame); _inertia.Evaluate(frame, _standingPose, component, attachParent, teleportDistance);
            _output.Evaluate(_identity, _pose, _curves); _evaluated = true;
        }
        catch { Cancel(); throw; }
    }

    public void PostUpdateActions()
    {
        if (_sharedActions is not null) throw new InvalidOperationException("Shared actions are consumed by the character coordinator.");
        PostUpdateSharedActions();
    }
    internal void PostUpdateSharedActions()
    {
        Check();
        if (_postUpdated) throw new InvalidOperationException("Standing actions already consumed.");
        try
        {
            if(_sharedActions is null)_profile.Montages.PostUpdate(_bank, _restParent, _queue, _identity);
            QuickStopDispatchCount = _profile.QuickStop.Dispatch(_queue, _bank, _identity, _standing, _input.QuickStop);
            PivotDispatchCount = _hasMovement ? _profile.PivotNotify.Dispatch(_traversal.Movement.Direction, _parent, _identity) : 0;
            _postUpdated = true;
        }
        catch { Cancel(); throw; }
    }

    public void ValidateCommit(in AlsFrameIdentity identity)
    {
        Check(); if (identity != _identity || !_postUpdated) throw new ArgumentException("Incomplete or foreign Standing host frame.");
        var frame = identity.FrameId;
        _bank.ValidateCommit(identity); _queue.ValidateCommit(identity);
        _restParent.ValidateCommit(frame); _restTraversal.ValidateCommit(frame);
        _parent.ValidateCommit(frame); _standing.ValidateCommit(frame); _traversal.ValidateCommit(frame);
        _rotate.ValidateCommit(frame); _players.ValidateCommit(frame); _inertia.ValidateCommit(frame);
        if (_hasIdle) _idle.ValidateCommit(identity);
        if (_hasStop) { _stop.ValidateCommit(frame); _stopSources.ValidateCommit(frame); }
        if (_hasMovement) { _movement.ValidateCommit(frame); _movementInertia.ValidateCommit(frame); }
        if (_poseBegun) _standingPose.ValidateCommit(frame);
    }
    public void Commit(in AlsFrameIdentity identity)
    {
        if (_sharedActions is not null) throw new InvalidOperationException("Shared Standing commits through the character coordinator.");
        CommitShared(identity);
    }
    internal void CommitShared(in AlsFrameIdentity identity)
    {
        ValidateCommit(identity); var frame = identity.FrameId;
        _inertia.Commit(frame); if (_poseBegun) _standingPose.Commit(frame);
        if (_hasIdle) _idle.Commit(identity);
        _restTraversal.Commit(frame); _traversal.Commit(frame);
        if (_hasStop) { _stopSources.Commit(frame); _stop.Commit(frame); }
        if (_hasMovement) { _movementInertia.Commit(frame); _movement.Commit(frame); }
        _players.Commit(frame); _rotate.Commit(frame); _standing.Commit(frame);
        if(_sharedActions is null){_parent.Commit(frame);_restParent.Commit(frame);}
        if (_sharedActions is null) { _queue.Commit(identity); _bank.Commit(identity); }
        _nextClocks.CopyTo(_clocks, 0);
        _machineWeight = _nextMachineWeight;
        CommittedIdentity = identity; CancelGraph();
    }
    internal void CommitUnvisitedMachineWeight() => _machineWeight = 0;
    public void Cancel()
    {
        if (_sharedActions is not null) _sharedActions.Discard();
        else CancelGraph();
    }
    internal void CancelGraph()
    {
        _inertia.Cancel(); _standingPose.Cancel(); _idle.Cancel(); _restTraversal.Cancel(); _traversal.Cancel();
        _stopSources.Cancel(); _stop.Cancel(); _movementInertia.Cancel(); _movement.Cancel(); _players.Cancel();
        _rotate.Cancel(); _standing.Cancel();
        if(_sharedActions is null){_parent.Cancel();_restParent.Cancel();}
        if (_sharedActions is null) { _queue.Discard(); _bank.Discard(); }
        _prepared = _evaluated = _postUpdated = _hasIdle = _hasStop = _hasMovement = _poseBegun = false;
        QuickStopDispatchCount = PivotDispatchCount = 0;
    }
    private void Check() { if (!_prepared) throw new InvalidOperationException("No Standing host candidate."); }
    private void RequirePose() { Check(); if (!_evaluated) throw new InvalidOperationException("Standing host pose was not evaluated."); }
}
