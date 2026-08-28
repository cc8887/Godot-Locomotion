using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Exchange;

namespace GodotAls.Locomotion;

public partial class AlsP3Character : Node3D
{
    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterState _state = null!;
    private AlsCharacterMotor _motor = null!;
    private AlsP3WorkerRoot _worker = null!;
    private AlsP3CommitStage _commit = null!;
    private bool _configured;
    private int _disposed;

    public AlsP3Character()
    {
        Visible = false;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 0;
    }

    public AlsSlotHandle Handle => _state.Handle;

    public long PublishedFrameId => Volatile.Read(ref _state.PublishedFrameId);

    public bool WorkerObservedOffMainThread =>
        Volatile.Read(ref _state.ObservedOffMainThread) != 0;

    public bool IsPoseFrozen => Volatile.Read(ref _state.WorkerFrozen) != 0;

    public int FailureDiagnosticCount =>
        Volatile.Read(ref _state.FailureDiagnosticCount);

    internal long ResultPublishedFrameId => _state.ResultPublishedFrameId;

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
        Configure(context, handle, commandSource, new AlsP3ExchangeSlot());
    }

    internal void Configure(
        AlsP3RuntimeContext context,
        AlsSlotHandle handle,
        IAlsLocomotionCommandSource commandSource,
        AlsP3ExchangeSlot exchangeSlot)
    {
        EnsureMainThread();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commandSource);
        ArgumentNullException.ThrowIfNull(exchangeSlot);
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
        _state = new AlsP3CharacterState(handle, exchangeSlot);
        try
        {
            _motor = new AlsCharacterMotor { Name = "Motor" };
            AddChild(_motor);
            _motor.Configure(context.MotorSettings, commandSource);

            _worker = new AlsP3WorkerRoot { Name = "VisualWorker" };
            AddChild(_worker);
            _worker.Configure(context, _state, _motor.GlobalTransform);

            _commit = new AlsP3CommitStage { Name = "Commit" };
            _commit.Configure(context, _state, this);
            AddChild(_commit);
            _configured = true;
            Volatile.Write(ref _state.ProcessingEnabled, 1);
        }
        catch
        {
            DisposeRuntimeCore(allowActive: true);
            throw;
        }
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
            if (completedFrameId >= AlsP3VisualRootVisibilityObservation.MaximumWorkerFrameId)
            {
                throw new InvalidOperationException("P3 frame sequence reached its supported limit.");
            }
            var frameId = completedFrameId + 1;
            var input = _motor.Step(
                frameId,
                checked((int)_state.Handle.CharacterId),
                checked((int)_state.Handle.Generation),
                checked((float)delta),
                _state.HasCommittedTargetYaw,
                _state.CommittedTargetYaw);
            _state.CommandFrameId = frameId;
            _state.MotorSnapshotFrameId = input.Identity.FrameId;
            _state.MotorActualVelocity = input.ActualVelocity;
            var measurement = _context.Measurement;
            var measure = measurement is not null &&
                measurement.TryGetMeasurementIndex(input.Identity, out _);
            var allocatedBeforeExchange = measure
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            _state.Exchange.PublishInput(input);
            if (measure)
            {
                measurement!.AddExchangeAllocations(
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeExchange);
            }
            Volatile.Write(ref _state.PublishedFrameId, frameId);
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
        EnsureMainThread();
        ThrowIfDisposed();
        EnsureConfigured();
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
        }
        Volatile.Write(ref _state.Active, active ? 1 : 0);
        ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _motor.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _motor.CollisionLayer = active ? 1u : 0u;
        _motor.CollisionMask = active ? _context.MotorSettings.CollisionMask : 0u;
        _worker.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _commit.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        Volatile.Write(ref _state.ProcessingEnabled, active ? 1 : 0);
        if (!active)
        {
            ResetVisualReadyCore();
        }
        else
        {
            _state.OpenWorkerAdmission();
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
        SetActive(false);
        Volatile.Write(ref _state.GatherSuspended, 1);
        Volatile.Write(ref _state.WorkerSuspended, 1);
        Volatile.Write(ref _state.CommitSuspended, 1);
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

    internal void StartReplacementRecovery()
    {
        EnsureMainThread();
        ThrowIfDisposed();
        Volatile.Write(ref _state.GatherSuspended, 1);
        Volatile.Write(ref _state.WorkerSuspended, 0);
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
        Volatile.Write(ref _state.ProcessingEnabled, 0);
        if (_worker is not null && !_worker.TryDisposeRuntime())
        {
            Volatile.Write(ref _disposed, 0);
            throw new InvalidOperationException(
                "P3 character runtime disposal was rejected while its worker callback was in flight.");
        }

        Volatile.Write(ref _state.Active, 0);
        DisableRuntimeNodes();
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
