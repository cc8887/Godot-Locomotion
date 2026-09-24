using System.Diagnostics;
using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

public partial class AlsP3Character : Node3D
{
    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterState _state = null!;
    private AlsCharacterMotor _motor = null!;
    private AlsP3WorkerRoot _worker = null!;
    private AlsP3CommitStage _commit = null!;
    internal AlsOverlayPropRuntime? Props { get; private set; }
    internal GodotAls.Physics.AlsCharacterBodyHistory? BodyHistory { get; private set; }
    internal GodotAls.Physics.AlsCharacterRagdollSimulation? RagdollSimulation { get; private set; }
    private Node? _requestedRagdollEnvironment;
    private int _physicsDriven;
    private bool _ragdollExitRequested, _getUpAccepted, _getUpNotifySeen;
    private long _getUpRequestId;
    internal bool GettingUp => _motor.GetUpInputBlocked;
    internal GodotAls.Physics.AlsRagdollRecoveryFrame? LastRagdollRecovery { get; private set; }
    internal void RequestRagdollToggle(Node environment)
    {
        EnsureMainThread(); EnsureConfigured(); ThrowIfDisposed();
        if (!BodyHistoryActive) return;
        if (RagdollSimulation is null) RequestRagdoll(environment);
        else _ragdollExitRequested = true;
    }
    internal void ConsumeRagdollExit()
    {
        if (!_ragdollExitRequested || PublishedFrameId != RuntimeCommittedFrameId || RagdollSimulation is not { } simulation) return;
        var decision = simulation.DecideExit(_motor.RagdollGrounded);
        var actor = _motor.GlobalTransform;
        actor.Basis = new Basis(Vector3.Up, (float)(-decision.ActorYawDegrees * (Math.PI / 180)));
        var restoredRoot = AlsP3Presentation.Compose(actor, _context.PresentationTransform);
        restoredRoot.Origin += Vector3.Up * _state.MotorInput.MeshHeightOffset;
        var recovery = simulation.PrepareRecovery(Diagnostics.Identity, _worker.RecoverySkeletonWorld(restoredRoot), decision.Grounded);
        if (!simulation.IsRecoveryCurrent(recovery)) throw new InvalidOperationException("Ragdoll recovery candidate expired.");
        var definition = decision.FacingUpward ? _context.MovementGraph!.GetUpBackDefinitionId : _context.MovementGraph!.GetUpFrontDefinitionId;
        var policy = _context.MovementGraph.ActionPolicies.Single(p => p.DefinitionId == definition);
        var request = decision.PlayGetUp ? new AlsActionRequest(checked(RuntimeCommittedFrameId + 1), AlsActionCommand.Start,
            definition, policy.StartSectionId, 100, Handle.Generation) : AlsActionRequest.None;
        var velocity = decision.FallingVelocityCm;
        _worker.InstallRagdollRecovery(restoredRoot, recovery.Snapshot);
        _motor.RestoreFromRagdoll(actor, decision.Grounded, new((float)(velocity.Y * .01), (float)(velocity.Z * .01), (float)(-velocity.X * .01)));
        simulation.Dispose(); RagdollSimulation = null;
        Volatile.Write(ref _physicsDriven, 0);
        _state.HasCommittedTargetYaw = 0;
        BodyHistory!.ResetHistory();
        _motor.RecoveryRequest = request; _motor.GetUpInputBlocked = decision.PlayGetUp;
        _getUpRequestId = request.RequestId; _getUpAccepted = _getUpNotifySeen = false;
        LastRagdollRecovery = recovery; _ragdollExitRequested = false;
    }
    internal void ObserveGetUp()
    {
        if (!_motor.GetUpInputBlocked) return;
        var result = Diagnostics.Result;
        var terminal = false;
        for (var i = 0; i < result.ActionOutcomes.Count; i++)
        {
            var outcome = result.ActionOutcomes[i];
            if (outcome.RequestId != _getUpRequestId) continue;
            _motor.RecoveryRequest = AlsActionRequest.None;
            if (outcome.ResultCode == AlsActionResultCode.Accepted) _getUpAccepted = true;
            else terminal = true;
        }
        var action = FullMovementDiagnostics.MovementNotifies.Action;
        if (action == AlsTimelineAction.GettingUp) _getUpNotifySeen = true;
        if (terminal || _getUpNotifySeen && action != AlsTimelineAction.GettingUp ||
            _getUpAccepted && result.ActionPlayback.Active == 0)
        { _motor.GetUpInputBlocked = false; _motor.RecoveryRequest = AlsActionRequest.None; }
    }
    private void ClearGetUpForLifecycle()
    {
        // Gameplay retirement abandons the request even before its first Gather.
        // Scheduling-only suspension deliberately retains it and the input lock.
        _motor.GetUpInputBlocked = false;
        _motor.RecoveryRequest = AlsActionRequest.None;
        _getUpRequestId = 0;
        _getUpAccepted = _getUpNotifySeen = false;
    }
    internal bool PhysicsDriven => Volatile.Read(ref _physicsDriven) != 0;
    internal void RequestRagdoll(Node environment)
    {
        EnsureMainThread(); EnsureConfigured(); ThrowIfDisposed();
        if (BodyHistoryActive && RagdollSimulation is null) _requestedRagdollEnvironment = environment;
    }
    internal void ConsumeRagdollRequest()
    {
        if (_requestedRagdollEnvironment is not { } environment || PublishedFrameId != RuntimeCommittedFrameId) return;
        BeginRagdoll(environment); _requestedRagdollEnvironment = null;
    }
    internal void BeginRagdoll(Node environment)
    {
        EnsureMainThread(); EnsureConfigured(); ThrowIfDisposed();
        if (!BodyHistoryActive || RagdollSimulation is not null || !_worker.UsesRefactoredFeet ||
            PublishedFrameId != RuntimeCommittedFrameId)
            throw new InvalidOperationException("Ragdoll activation requires an idle committed complete character.");
        var simulation = GodotAls.Physics.AlsCharacterRagdollSimulation.Create(this, environment, this, _context);
        _motor.CollisionLayer = 0; _motor.CollisionMask = 0;
        _motor.Velocity = Vector3.Zero;
        RagdollSimulation = simulation;
        _motor.GetUpInputBlocked = false; _motor.RecoveryRequest = AlsActionRequest.None;
        _ragdollExitRequested = false;
        Volatile.Write(ref _physicsDriven, 1);
    }
    internal void FollowRagdollPelvis() => _motor.FollowRagdoll(RagdollSimulation!.PelvisPosition);
    internal void PresentRagdoll() => _worker.PresentRagdoll(RagdollSimulation!);
    internal Skeleton3D PhysicalDisplaySkeleton => _worker.PhysicalDisplaySkeleton;
    internal bool BodyHistoryActive => _configured && Volatile.Read(ref _disposed) == 0 &&
        Volatile.Read(ref _state.Active) != 0 && Volatile.Read(ref _state.VisualReady) != 0;
    private AlsFrameInput _stagedReplacementMotorInput;
    private bool _hasStagedReplacementMotorInput;
    private bool _configured;
    private int _disposed;
    internal AlsCommittedAnimationLifecycle CommittedAnimation { get; private set; } = null!;
    internal long AnimationLifecycleRevision { get; private set; }
    private bool _animationDeactivated;
    private long _discardedCompletedFrame = -1;
    private AlsFootIkPoseSample _resumeFootPose;
    private AlsRefactoredAnimationFeedback _resumeRefactoredFeedback;

    public AlsP3Character()
    {
        Visible = false;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = AlsP3FrameStages.Gather;
    }

    public AlsSlotHandle Handle => _state.Handle;

    public long PublishedFrameId => Volatile.Read(ref _state.PublishedFrameId);

    public bool WorkerObservedOffMainThread =>
        Volatile.Read(ref _state.ObservedOffMainThread) != 0;

    public bool IsPoseFrozen => Volatile.Read(ref _state.WorkerFrozen) != 0;

    public int FailureDiagnosticCount =>
        Volatile.Read(ref _state.FailureDiagnosticCount);

    internal long ResultPublishedFrameId => _state.ResultPublishedFrameId;
    internal int AnimationRecoveryAttempts => _state.AnimationRecovery.FailedAttempts;
    internal AlsP3SplitFootDiagnostics SplitFootDiagnostics => _worker.SplitFootDiagnostics;

    internal AlsP4ReasonCode LastFailureReasonCode =>
        (AlsP4ReasonCode)Volatile.Read(ref _state.LastFailureReasonCode);

    public Node3D MovementAnchor
    {
        get
        {
            EnsureMainThread();
            EnsureConfigured();
            ThrowIfDisposed();
            if (!GodotObject.IsInstanceValid(_motor) ||
                _motor.IsQueuedForDeletion() || !_motor.IsInsideTree())
            {
                throw new ObjectDisposedException(nameof(AlsP3Character));
            }
            return _motor;
        }
    }

    internal AlsP3RuntimeDiagnostics RuntimeDiagnostics =>
        _state.CaptureRuntimeDiagnostics();

    internal AlsP4LifecyclePublicationDiagnostics LifecyclePublicationDiagnostics
    {
        get
        {
            EnsureMainThread();
            EnsureConfigured();
            return _state.CaptureLifecyclePublicationDiagnostics();
        }
    }

    internal AlsP3WorkerTransactionRollbackDiagnostics WorkerTransactionRollbackDiagnostics =>
        _state.WorkerTransactionRollbackDiagnostics;

    internal AlsFrameInput LatestMotorInput => _state.MotorInput;
    internal long MotorIntegrationCount => _motor.IntegrationCount;
    internal GodotAls.Core.Actions.AlsMontageRootMotionRange ConsumedRootMotion => _motor.LastConsumedRootMotion;
    internal AlsRootMotionDelta RootMotionWorldDelta => _motor.LastRootMotionWorldDelta;
    internal bool UsesCompleteMovement => _worker.UsesCompleteMovement;
    internal bool UsesLayeredPose => _worker.UsesLayeredPose;
    internal GodotAls.Animation.AlsFullMovementDiagnostics FullMovementDiagnostics => _worker.FullMovementDiagnostics;
    internal int AnimationPoseBoneCount => _worker.AnimationPoseBoneCount;
    internal bool TryCopyCommittedPreciseFlail(AlsFrameIdentity identity, Span<GodotAls.Core.Locomotion.AlsPrecisePose> destination)
    {
        if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Flail handoff requires Main.");
        return identity.FrameId == RuntimeCommittedFrameId && identity.CharacterId == Handle.CharacterId &&
            identity.SlotGeneration == Handle.Generation && Volatile.Read(ref _disposed) == 0 &&
            _worker.TryCopyCommittedPreciseFlail(identity, destination);
    }
    internal bool TryCopyCommittedFlail(AlsFrameIdentity identity, Span<GodotAls.Core.Locomotion.AlsLocalPose> destination)
    {
        if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Flail handoff requires Main.");
        return identity.FrameId == RuntimeCommittedFrameId && identity.CharacterId == Handle.CharacterId &&
            identity.SlotGeneration == Handle.Generation && Volatile.Read(ref _disposed) == 0 &&
            _worker.TryCopyCommittedFlail(identity, destination);
    }
    internal void CopyCommittedAnimationPose(AlsFrameIdentity identity, Span<GodotAls.Core.Locomotion.AlsLocalPose> destination)
    {
        if (!GodotThread.IsMainThread() || identity.FrameId != RuntimeCommittedFrameId ||
            identity.CharacterId != Handle.CharacterId || identity.SlotGeneration != Handle.Generation ||
            Volatile.Read(ref _disposed) != 0)
            throw new InvalidOperationException("Animation pose handoff requires the live character's main-thread committed frame.");
        _worker.CopyCommittedAnimationPose(identity, destination);
    }
    internal GodotAls.Physics.AlsRagdollEntryFrame CopyCommittedRagdollEntry(
        AlsFrameIdentity identity, Span<AlsLocalPose> destination)
    {
        EnsureMainThread(); EnsureConfigured(); ThrowIfDisposed();
        var committed = _state.Diagnostics;
        if (identity.FrameId <= 0 || identity != committed.Identity ||
            identity.FrameId != RuntimeCommittedFrameId || identity.CharacterId != Handle.CharacterId ||
            identity.SlotGeneration != Handle.Generation || committed.FootProbeSource.Identity != identity ||
            committed.PresentationPending || Volatile.Read(ref _state.Active) == 0)
            throw new InvalidOperationException("Ragdoll entry requires one live, fully presented committed frame.");
        var character = Transform(committed.FootProbeSource.CharacterTransform);
        var skeleton = Transform(committed.FootProbeSource.SkeletonTransform);
        var v = committed.ActualVelocity; var velocity = new Vector3(v.X, v.Y, v.Z);
        // Validate all metadata before the caller's pose buffer can change.
        _ = GodotAls.Physics.AlsSceneContactSet.FromWorld(character);
        _ = GodotAls.Physics.AlsCorePhysicsPose.FromWorld(skeleton);
        if (!velocity.IsFinite()) throw new InvalidOperationException("Nonfinite committed character velocity.");
        CopyCommittedAnimationPose(identity, destination);
        return new(identity, character, skeleton, velocity);

        static Transform3D Transform(AlsP3VisualTransformSnapshot p) => new(new Basis(
            new(p.BasisX.X, p.BasisX.Y, p.BasisX.Z), new(p.BasisY.X, p.BasisY.Y, p.BasisY.Z),
            new(p.BasisZ.X, p.BasisZ.Y, p.BasisZ.Z)), new(p.Origin.X, p.Origin.Y, p.Origin.Z));
    }
    internal GodotAls.Core.Locomotion.AlsRefactoredAnimationFeedback CommittedRefactoredFeedback => _state.CommittedRefactoredFeedback;
    internal GodotAls.Core.Locomotion.AlsBasedFootLockFrameTrace? BasedFootLockTrace => _worker.CommittedBasedTrace;
    internal GodotAls.Core.Locomotion.AlsStandingCycleState StandingCycleState => _worker.StandingCycleState;
    internal GodotAls.Core.Locomotion.AlsBinaryBlendState StandingSprintBlend => _worker.StandingSprintBlend;
    internal float StandingSprintMask => _worker.StandingSprintMask;
    internal GodotAls.Animation.AlsCycleDetailFrame StandingDetail => _worker.StandingDetail;
    internal float RuntimeAnimationPhase => _worker.RuntimeAnimationPhase;
    internal GodotAls.Core.Locomotion.AlsStandingMovementInput StandingMovementInput => _worker.StandingMovementInput;
    internal GodotAls.Animation.AlsCycleSyncFrame StandingCycleSync => _worker.StandingCycleSync;
    internal GodotAls.Core.Animation.AlsP5SourceEventState SourceEventState => _worker.SourceEventState;

    internal AlsP3ResultClassificationDiagnostics ResultClassificationDiagnostics =>
        _state.CaptureResultClassification();

    internal void CommitMotorLifecycleFrame(long frameId) =>
        _motor.CommitLifecycleFrame(frameId);

    internal AlsCharacterMotorLifecycleSnapshot CaptureCommittedMotorLifecycle(
        long completedFrameId) =>
        _motor.CaptureCommittedLifecycleSnapshot(completedFrameId);

    internal void RestoreCommittedMotorLifecycle(
        in AlsCharacterMotorLifecycleSnapshot snapshot,
        long completedFrameId) =>
        _motor.RestoreCommittedLifecycleSnapshot(in snapshot, completedFrameId);

    internal AlsCharacterMotorLifecycleSnapshot CapturePublishedMotorLifecycle(
        long publishedFrameId) =>
        _motor.CapturePublishedLifecycleSnapshot(publishedFrameId);

    internal void RestorePublishedMotorLifecycle(
        in AlsCharacterMotorLifecycleSnapshot snapshot,
        long publishedFrameId,
        bool releasePlatformOnNextStep) =>
        _motor.RestorePublishedLifecycleSnapshot(
            in snapshot,
            publishedFrameId,
            releasePlatformOnNextStep);

    internal void StageReplacementMotorInput(in AlsFrameInput input)
    {
        EnsureMainThread();
        ThrowIfDisposed();
        EnsureConfigured();
        if (Volatile.Read(ref _state.Active) != 0 || _hasStagedReplacementMotorInput)
        {
            throw new InvalidOperationException(
                "Replacement Motor input can only be staged on an unused inactive character.");
        }
        _stagedReplacementMotorInput = _motor.RecaptureRefactoredPrediction(_motor.RecaptureReplacementFeet(input));
        _hasStagedReplacementMotorInput = true;
    }

    internal AlsP3WorkerLifecycleSnapshot CaptureFailureWorkerLifecycle(
        long committedFrameId) =>
        _worker.CaptureFailureLifecycleSnapshot(committedFrameId);

    internal void RestoreFailureWorkerLifecycle(
        in AlsP3WorkerLifecycleSnapshot snapshot,
        long forcedPlatformReleaseFrameId) =>
        _worker.RestoreFailureLifecycleSnapshot(
            in snapshot,
            forcedPlatformReleaseFrameId);

    internal int FailurePendingIdentityCount =>
        _state.CaptureRuntimeDiagnostics().PendingFailureIdentityCount;

    internal int FailureRetainedIdentityCount =>
        _state.CaptureRuntimeDiagnostics().RetainedFailureIdentityCount;

    internal AlsP3VisualRootVisibilityObservation VisualRootVisibilityObservation =>
        AlsP3VisualRootVisibilityObservation.Decode(
            Volatile.Read(ref _state.VisualRootVisibilitySnapshot));

    public AlsP3LifecycleDiagnostics LifecycleDiagnostics
    {
        get
        {
            EnsureMainThread();
            EnsureConfigured();
            return new AlsP3LifecycleDiagnostics(
                Volatile.Read(ref _disposed) != 0,
                Volatile.Read(ref _state.Active) != 0,
                _motor.CollisionLayer != 0 || _motor.CollisionMask != 0,
                Volatile.Read(ref _state.ProcessingEnabled) != 0,
                Visible,
                Volatile.Read(ref _state.VisualReady) != 0);
        }
    }

    public AlsP3FrameDiagnostics Diagnostics
    {
        get
        {
            EnsureMainThread();
            EnsureConfigured();
            var frameId = Volatile.Read(ref _state.CommittedFrameId);
            var diagnostics = _state.Diagnostics;
            return diagnostics.CommittedFrameId == frameId ? diagnostics : default;
        }
    }

    public void Configure(
        AlsP3RuntimeContext context,
        AlsSlotHandle handle,
        IAlsLocomotionCommandSource commandSource)
    {
        EnsureMainThread();
        ThrowIfDisposed();
        Configure(
            context,
            handle,
            commandSource,
            new AlsP3ExchangeSlot(),
            new AlsP4FootProbeExchange());
    }

    internal void Configure(
        AlsP3RuntimeContext context,
        AlsSlotHandle handle,
        IAlsLocomotionCommandSource commandSource,
        AlsP3ExchangeSlot exchangeSlot) =>
        Configure(
            context,
            handle,
            commandSource,
            exchangeSlot,
            new AlsP4FootProbeExchange());

    internal void Configure(
        AlsP3RuntimeContext context,
        AlsSlotHandle handle,
        IAlsLocomotionCommandSource commandSource,
        AlsP3ExchangeSlot exchangeSlot,
        AlsP4FootProbeExchange footProbeExchange)
    {
        EnsureMainThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commandSource);
        ArgumentNullException.ThrowIfNull(exchangeSlot);
        ArgumentNullException.ThrowIfNull(footProbeExchange);
        if (!IsInsideTree())
        {
            throw new InvalidOperationException("P3 character must be in the scene tree before Configure().");
        }
        if (_configured)
        {
            throw new InvalidOperationException("P3 character is already configured.");
        }
        if (handle.Generation == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handle));
        }

        _context = context;
        CommittedAnimation = new(handle.CharacterId, handle.Generation);
        _state = new AlsP3CharacterState(handle, exchangeSlot, footProbeExchange);
        try
        {
            _motor = new AlsCharacterMotor { Name = "Motor" };
            AddChild(_motor);
            _motor.Configure(
                context.MotorSettings,
                commandSource,
                footProbeExchange,
                context.FootGatherSettings,
                context);

            _worker = new AlsP3WorkerRoot { Name = "VisualWorker" };
            AddChild(_worker);
            _worker.Configure(context, _state, _motor.GlobalTransform);
            if (context.PropProfile is not null) Props = new(this, context);
            if (_worker.UsesNativeFootIk) _motor.ConfigureNativeFeet(_worker.InitialNativeFeet, context.MovementGraph!.FootIkInput.Offset);
            _motor.ConsumeMontageRootMotion = GodotAls.Animation.AlsAnimationRuntimeOptions.Has("--montage-root-motion");
            _motor.RollingGameplay = GodotAls.Animation.AlsAnimationRuntimeOptions.Has("--rolling-gameplay");
            if (_motor.RollingGameplay && !_motor.ConsumeMontageRootMotion)
                throw new InvalidOperationException("Rolling gameplay requires montage root motion consumption.");
            if (_motor.ConsumeMontageRootMotion && !_worker.UsesRefactoredFeet)
                throw new InvalidOperationException("Root motion consumption requires the complete split animation pipeline.");

            if (AlsP3FrameStages.SplitFeet)
            {
                if (!_worker.UsesRefactoredFeet) throw new InvalidOperationException("Split foot stages require the complete movement graph.");
                var motion = new AlsP3MotionPrepareStage { Name = "MontageMotionPrepare" };
                motion.Configure(this, _worker, context.Mode); AddChild(motion);
                var prepare = new AlsP3FootPrepareStage { Name = "FootAnimationPrepare" };
                prepare.Configure(_worker, context.Mode); AddChild(prepare);
                var query = new AlsP3FootQueryStage { Name = "FootPhysicsQuery" };
                query.Configure(_worker, _motor); AddChild(query);
            }

            _commit = new AlsP3CommitStage { Name = "Commit" };
            _commit.Configure(context, _state, this);
            AddChild(_commit);
            if (_worker.UsesCompleteMovement)
            {
                BodyHistory = new() { Name = "KinematicBodyHistory" };
                BodyHistory.Configure(this, context); AddChild(BodyHistory);
            }
            _configured = true;
            Volatile.Write(ref _state.ProcessingEnabled, 1);
        }
        catch
        {
            DisposeRuntimeCore(allowActive: true);
            throw;
        }
    }

    // Called only by the earlier motion process group. Reads value state, never
    // Node transforms, input devices or physics. Pending Motor inputs retain dt.
    internal bool TryGetMotionPreparation(float delta, out AlsFrameIdentity identity, out float frameDelta)
    {
        identity = default; frameDelta = delta;
        if (!_configured || Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _state.Active) == 0) return false;
        var published = Volatile.Read(ref _state.PublishedFrameId);
        var pending = published > Volatile.Read(ref _state.CommittedFrameId) && published != _discardedCompletedFrame;
        if (pending)
        {
            identity = HandleIdentity(published);
            if (!_state.Exchange.TryReadInput(identity, out var input)) return false;
            frameDelta = input.DeltaTime; return true;
        }
        if (Volatile.Read(ref _state.GatherSuspended) != 0) return false;
        identity = HandleIdentity(published + 1);
        if (_hasStagedReplacementMotorInput) frameDelta = _stagedReplacementMotorInput.DeltaTime;
        return true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_configured || Volatile.Read(ref _disposed) != 0 ||
            Volatile.Read(ref _state.Active) == 0 ||
            Volatile.Read(ref _state.GatherSuspended) != 0)
        {
            return;
        }

        try
        {
            var completedFrameId = Volatile.Read(ref _state.PublishedFrameId);
            if (_state.AnimationRecovery.Pending) return; // Retry the captured Motor frame without another integration.
            // A split animation frame may be canceled while later process
            // groups are suspended. Keep its already-integrated motor input
            // until that frame commits; advancing again would skip the next
            // identity expected by foot/curve/source history. Frozen failure
            // handling retains the existing motor/recovery policy.
            if (_worker.UsesRefactoredFeet && Volatile.Read(ref _state.WorkerFrozen) == 0 &&
                completedFrameId > Volatile.Read(ref _state.CommittedFrameId) && completedFrameId != _discardedCompletedFrame) return;
            if (completedFrameId >= AlsP3VisualRootVisibilityObservation.MaximumWorkerFrameId)
            {
                throw new InvalidOperationException("P3 frame sequence reached its supported limit.");
            }
            var frameId = completedFrameId + 1;
            // A replacement republishes an already integrated Motor input while
            // its animation is suspended for stale-result classification. It
            // must not wait for (or consume) a second physical montage tick.
            if (!_hasStagedReplacementMotorInput && _worker.UsesRefactoredFeet && Volatile.Read(ref _state.WorkerFrozen) == 0 &&
                _worker.MotorRootMotion.Identity != HandleIdentity(frameId)) return;
            var measurement = _context.Measurement;
            var measurementIndex = -1;
            var measure = measurement is not null &&
                measurement.TryGetMeasurementIndex(
                    HandleIdentity(frameId),
                    out measurementIndex);
            if (measure)
            {
                measurement!.RecordGatherStart(
                    measurementIndex,
                    Stopwatch.GetTimestamp());
            }
            AlsFrameInput input;
            if (_hasStagedReplacementMotorInput)
            {
                input = _stagedReplacementMotorInput;
                if (input.Identity != HandleIdentity(frameId))
                {
                    throw new InvalidOperationException(
                        "Staged replacement Motor input does not match the next frame identity.");
                }
                _stagedReplacementMotorInput = default;
                _hasStagedReplacementMotorInput = false;
            }
            else if (RagdollSimulation is { } simulation)
            {
                var identity = HandleIdentity(frameId);
                input = _motor.StepPhysicsDriven(identity, checked((float)delta), _state.MotorInput,
                    new(identity, simulation.Activation.Entry.Identity, simulation.CompletedSteps, simulation.PelvisVelocity),
                    _resumeRefactoredFeedback.Pose.Identity == HandleIdentity(frameId - 1)
                        ? _resumeRefactoredFeedback : _state.CommittedRefactoredFeedback);
            }
            else
            {
                input = _motor.Step(
                    frameId,
                    checked((int)_state.Handle.CharacterId),
                    checked((int)_state.Handle.Generation),
                    checked((float)delta),
                    _state.HasCommittedTargetYaw,
                    _state.CommittedTargetYaw,
                    _state.CommittedCharacterRotationFeedback,
                    _resumeRefactoredFeedback.Pose.Identity == HandleIdentity(frameId - 1)
                        ? _resumeRefactoredFeedback : _state.CommittedRefactoredFeedback,
                    _motor.ConsumeMontageRootMotion && _worker.MotorRootMotion.Identity == HandleIdentity(frameId)
                        ? _worker.MotorRootMotion.Source : default,
                    _worker.MotorRootMotion.Delta, _state.CommittedRolling);
            }
            _state.CommandFrameId = frameId;
            _state.MotorSnapshotFrameId = input.Identity.FrameId;
            _state.MotorActualVelocity = input.ActualVelocity;
            _state.MotorInput = input;
            var allocatedBeforeExchange = measure
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            _state.Exchange.PublishInput(input);
            if (measure)
            {
                measurement!.AddExchangeAllocations(
                    measurementIndex,
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeExchange);
            }
            Volatile.Write(ref _state.PublishedFrameId, frameId);
            _discardedCompletedFrame = -1;
            _resumeRefactoredFeedback = default;
            if (measure)
            {
                measurement!.RecordGatherEnd(
                    measurementIndex,
                    Stopwatch.GetTimestamp());
            }
        }
        catch (Exception exception)
        {
            var completedFrameId = Volatile.Read(ref _state.PublishedFrameId);
            var failureFrameId = completedFrameId >=
                AlsP3VisualRootVisibilityObservation.MaximumWorkerFrameId
                    ? AlsP3VisualRootVisibilityObservation.MaximumWorkerFrameId
                    : Math.Max(0, completedFrameId + 1);
            _state.RecordFailure(
                "motor_step",
                HandleIdentity(failureFrameId),
                exception);
        }
    }

    public void SetActive(bool active)
    {
        EnsureMainThread(); ThrowIfDisposed(); EnsureConfigured();
        if (!_worker.UsesCompleteMovement) { SetSchedulingActive(active); return; }
        if (active)
        {
            if (_animationDeactivated) { CommittedAnimation.Reopen(); _animationDeactivated = false; }
            SetSchedulingActive(true);
            return;
        }
        SetSchedulingActive(false);
        if (_animationDeactivated) return;
        _worker.ClearAnimationOwnershipForLifecycle(_state.MotorInput.ActionRequest);
        ClearGetUpForLifecycle();
        var published = Volatile.Read(ref _state.PublishedFrameId);
        if (published > Volatile.Read(ref _state.CommittedFrameId) &&
            _state.ExchangeSlot.TryGetPublishedIdentity(out var resultIdentity) && resultIdentity == HandleIdentity(published))
        {
            // Worker history already advanced. Discard this uncommitted publication
            // and resume at the next input; do not integrate this Motor frame twice.
            _state.Exchange.TryConsumeResult(resultIdentity, out _);
            _discardedCompletedFrame = published;
        }
        _animationDeactivated = true; AnimationLifecycleRevision++;
        Props?.Clear();
        _context.DispatchAnimationRetirement(CommittedAnimation, AlsActionResultCode.InterruptedByLifecycle);
    }

    // Stage suspension is not a gameplay interruption: pending input retries and
    // committed animation ownership survive until scheduling resumes.
    internal void SetSchedulingActive(bool active)
    {
        EnsureMainThread();
        ThrowIfDisposed();
        EnsureConfigured();
        if (active && _animationDeactivated) throw new InvalidOperationException("Reactivate gameplay before scheduling a deactivated actor.");
        if (active && Volatile.Read(ref _state.Active) != 0)
        {
            return;
        }
        if (active)
        {
            ResetVisualReady();
        }
        else
        {
            CloseWorkerAdmissionForDeactivation();
            _requestedRagdollEnvironment = null;
            _ragdollExitRequested = false;
            BodyHistory?.ResetHistory();
            _worker.CancelSplitFootForLifecycle();
            // Closed-admission checkpoint for same-generation resume. This is
            // animation history, not permission to dispatch an uncommitted result.
            var checkpoint = _state.VisualCommitCandidate;
            if (checkpoint.Identity.SlotGeneration != 0)
            {
                _resumeFootPose = checkpoint.FootProbeSource.NativeFootPose;
                _resumeRefactoredFeedback = checkpoint.RefactoredFeedback;
            }
        }
        Volatile.Write(ref _state.Active, active ? 1 : 0);
        ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _motor.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _motor.CollisionLayer = active && RagdollSimulation is null ? 1u : 0u;
        _motor.CollisionMask = active && RagdollSimulation is null ? _context.MotorSettings.CollisionMask : 0u;
        _worker.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _commit.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        Volatile.Write(ref _state.ProcessingEnabled, active ? 1 : 0);
        if (!active)
        {
            _state.FootProbeExchange.Clear();
            ResetVisualReadyCore();
        }
        else
        {
            _state.OpenWorkerAdmission();
            _state.FootProbeExchange.CopyNative(_resumeFootPose);
        }
    }

    internal void ResetVisualReady()
    {
        EnsureMainThread();
        ThrowIfDisposed();
        EnsureConfigured();
        if (!_state.IsWorkerAdmissionClosed)
        {
            throw new InvalidOperationException(
                "P3 visual readiness can only be reset with Worker admission closed.");
        }
        ResetVisualReadyCore();
    }

    internal void ShowCommittedVisual(AlsFrameIdentity identity)
    {
        EnsureMainThread();
        ThrowIfDisposed();
        EnsureConfigured();
        if (Volatile.Read(ref _state.Active) == 0 ||
            identity.CharacterId != _state.Handle.CharacterId ||
            identity.SlotGeneration != _state.Handle.Generation ||
            Volatile.Read(ref _state.CommittedFrameId) != identity.FrameId ||
            Volatile.Read(ref _state.VisualReady) != 1)
        {
            throw new InvalidOperationException(
                "P3 character cannot reveal a visual without its active committed identity.");
        }

        Visible = true;
    }

    private void ResetVisualReadyCore()
    {
        Visible = false;
        _state.ResetLifecyclePublication();
        Volatile.Write(ref _state.VisualReady, 0);
        Volatile.Write(
            ref _state.VisualRootVisibilitySnapshot,
            AlsP3VisualRootVisibilityObservation.MainThreadKnownHiddenValue);
    }

    private void CloseWorkerAdmissionForDeactivation()
    {
        if (Volatile.Read(ref _state.Active) == 0)
        {
            if (!_state.IsWorkerAdmissionClosed)
            {
                throw new InvalidOperationException(
                    "Inactive P3 character unexpectedly retained open Worker admission.");
            }
            return;
        }
        if (!_state.TryCloseWorkerAdmission())
        {
            throw new InvalidOperationException(
                "P3 character can only be deactivated at an idle Worker boundary.");
        }
    }

    public void ResumeAt(long completedFrameId)
    {
        EnsureMainThread();
        ThrowIfDisposed();
        EnsureConfigured();
        ArgumentOutOfRangeException.ThrowIfNegative(completedFrameId);
        if (completedFrameId >
            AlsP3VisualRootVisibilityObservation.MaximumResumableCompletedFrameId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedFrameId),
                completedFrameId,
                "P3 completed frame is too large to resume safely.");
        }
        if (Volatile.Read(ref _state.Active) != 0 || Volatile.Read(ref _state.PublishedFrameId) != 0)
        {
            throw new InvalidOperationException("Only an unused inactive P3 character can resume a frame sequence.");
        }
        Volatile.Write(ref _state.PublishedFrameId, completedFrameId);
        Volatile.Write(ref _state.CommittedFrameId, completedFrameId);
        _state.FootProbeExchange.Clear();
    }

    public AlsFrameIdentity HandleIdentity(long frameId)
    {
        EnsureConfigured();
        return new AlsFrameIdentity(frameId, _state.Handle.CharacterId, _state.Handle.Generation);
    }

    public void DisposeRuntime()
    {
        EnsureMainThread();
        EnsureConfigured();
        if (Volatile.Read(ref _state.Active) != 0)
        {
            throw new InvalidOperationException(
                "P3 character must be inactive before runtime disposal.");
        }

        DisposeRuntimeCore(allowActive: false);
    }

    internal void BeginReplacementRequest(long completedFrameId)
    {
        EnsureMainThread();
        ThrowIfDisposed();
        EnsureConfigured();
        if (Volatile.Read(ref _state.Active) == 0 ||
            Volatile.Read(ref _state.PublishedFrameId) != completedFrameId ||
            Volatile.Read(ref _state.CommittedFrameId) != completedFrameId)
        {
            throw new InvalidOperationException(
                "P3 replacement must begin at an active fully committed frame boundary.");
        }
        Volatile.Write(ref _state.CommitSuspended, 1);
    }

    internal void RetireForReplacement()
    {
        EnsureMainThread();
        ThrowIfDisposed();
        SetSchedulingActive(false);
        ClearGetUpForLifecycle();
        Volatile.Write(ref _state.GatherSuspended, 1);
        Volatile.Write(ref _state.WorkerSuspended, 1);
        Volatile.Write(ref _state.CommitSuspended, 1);
        _context.DispatchAnimationRetirement(CommittedAnimation, AlsActionResultCode.InterruptedByGeneration);
    }

    internal void StartReplacementClassification(long completedFrameId)
    {
        ResumeAt(completedFrameId);
        Volatile.Write(ref _state.GatherSuspended, 0);
        Volatile.Write(ref _state.WorkerSuspended, 1);
        Volatile.Write(ref _state.CommitSuspended, 0);
        ResetVisualReady();
        SetActive(true);
    }

    internal void StartReplacementWithoutClassification(long completedFrameId)
    {
        ResumeAt(completedFrameId);
        Volatile.Write(ref _state.GatherSuspended, 0);
        Volatile.Write(ref _state.WorkerSuspended, 1);
        Volatile.Write(ref _state.CommitSuspended, 1);
        ResetVisualReady();
        SetActive(true);
    }

    internal void StartReplacementRecovery()
    {
        EnsureMainThread();
        ThrowIfDisposed();
        Volatile.Write(ref _state.GatherSuspended, 1);
        Volatile.Write(ref _state.WorkerSuspended, 0);
        Volatile.Write(ref _state.CommitSuspended, 0);
    }

    internal void CompleteReplacementRecovery()
    {
        EnsureMainThread();
        ThrowIfDisposed();
        Volatile.Write(ref _state.GatherSuspended, 0);
        Volatile.Write(ref _state.WorkerSuspended, 0);
    }

    internal long RuntimeCommittedFrameId => Volatile.Read(ref _state.CommittedFrameId);

    internal int WorkerInFlight => _state.WorkerInFlightCount;

    public override void _ExitTree()
    {
        if (GodotThread.IsMainThread() && _configured)
        {
            DisposeRuntimeCore(allowActive: true);
        }
    }

    private void DisposeRuntimeCore(bool allowActive)
    {
        EnsureMainThread();
        if (!allowActive && Volatile.Read(ref _state.Active) != 0)
        {
            throw new InvalidOperationException(
                "P3 character must be inactive before runtime disposal.");
        }
        if (!_state.IsWorkerAdmissionClosed && !_state.TryCloseWorkerAdmission())
        {
            throw new InvalidOperationException(
                "P3 character runtime disposal was rejected while its Worker callback was in flight.");
        }
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        ResetVisualReadyCore();
        _state.FootProbeExchange.Clear();
        Volatile.Write(ref _state.ProcessingEnabled, 0);
        if (_worker is not null && !_worker.TryDisposeRuntime())
        {
            Volatile.Write(ref _disposed, 0);
            throw new InvalidOperationException(
                "P3 character runtime disposal was rejected while its worker callback was in flight.");
        }

        Volatile.Write(ref _state.Active, 0);
        DisableRuntimeNodes();
        BodyHistory?.ResetHistory();
        BodyHistory?.SetPhysicsProcess(false);
        RagdollSimulation?.Dispose(); RagdollSimulation = null;
        _requestedRagdollEnvironment = null; Volatile.Write(ref _physicsDriven, 0);
        ClearGetUpForLifecycle();
        Props?.Dispose(); Props = null;
        _context.DispatchAnimationRetirement(CommittedAnimation, AlsActionResultCode.InterruptedByLifecycle);
    }

    private void DisableRuntimeNodes()
    {
        Volatile.Write(ref _state.ProcessingEnabled, 0);
        ProcessMode = ProcessModeEnum.Disabled;
        if (_motor is not null)
        {
            _motor.ProcessMode = ProcessModeEnum.Disabled;
            _motor.CollisionLayer = 0;
            _motor.CollisionMask = 0;
        }
        if (_worker is not null)
        {
            _worker.ProcessMode = ProcessModeEnum.Disabled;
        }
        if (_commit is not null)
        {
            _commit.ProcessMode = ProcessModeEnum.Disabled;
        }
    }

    private void EnsureConfigured()
    {
        if (!_configured)
        {
            throw new InvalidOperationException("P3 character must be configured first.");
        }
    }

    private static void EnsureMainThread()
    {
        if (!GodotThread.IsMainThread())
        {
            throw new InvalidOperationException("P3 character lifecycle is restricted to Godot's main thread.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(AlsP3Character));
        }
    }
}
