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
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 0;
    }

    public AlsSlotHandle Handle => _state.Handle;

    public long PublishedFrameId => Volatile.Read(ref _state.PublishedFrameId);

    public bool WorkerObservedOffMainThread =>
        Volatile.Read(ref _state.ObservedOffMainThread) != 0;

    public bool IsPoseFrozen => Volatile.Read(ref _state.WorkerFrozen) != 0;

    public int FailureDiagnosticCount =>
        Volatile.Read(ref _state.FailureDiagnosticPublished);

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
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commandSource);
        EnsureMainThread();
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
        _state = new AlsP3CharacterState(handle);
        try
        {
            _motor = new AlsCharacterMotor { Name = "Motor" };
            AddChild(_motor);
            _motor.Configure(context.MotorSettings, commandSource);

            _worker = new AlsP3WorkerRoot { Name = "VisualWorker" };
            AddChild(_worker);
            _worker.Configure(context, _state);

            _commit = new AlsP3CommitStage { Name = "Commit" };
            _commit.Configure(context, _state);
            AddChild(_commit);
            _configured = true;
        }
        catch
        {
            DisposeRuntime();
            throw;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_configured || Volatile.Read(ref _disposed) != 0 ||
            Volatile.Read(ref _state.Active) == 0)
        {
            return;
        }

        try
        {
            var frameId = Volatile.Read(ref _state.PublishedFrameId) + 1;
            var input = _motor.Step(
                frameId,
                checked((int)_state.Handle.CharacterId),
                checked((int)_state.Handle.Generation),
                checked((float)delta),
                _state.HasCommittedTargetYaw,
                _state.CommittedTargetYaw);
            _state.CommandFrameId = frameId;
            _state.MotorSnapshotFrameId = input.Identity.FrameId;
            _state.Exchange.PublishInput(input);
            Volatile.Write(ref _state.PublishedFrameId, frameId);
        }
        catch (Exception exception)
        {
            _state.RecordFailure(
                "motor_step",
                HandleIdentity(Math.Max(0, Volatile.Read(ref _state.PublishedFrameId) + 1)),
                exception);
        }
    }

    public void SetActive(bool active)
    {
        EnsureConfigured();
        EnsureMainThread();
        Volatile.Write(ref _state.Active, active ? 1 : 0);
        _motor.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _motor.CollisionLayer = active ? 1u : 0u;
        _motor.CollisionMask = active ? _context.MotorSettings.CollisionMask : 0u;
        _worker.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        _commit.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
    }

    public void ResumeAt(long completedFrameId)
    {
        EnsureConfigured();
        EnsureMainThread();
        ArgumentOutOfRangeException.ThrowIfNegative(completedFrameId);
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_worker is not null)
        {
            _worker.DisposeRuntime();
        }
    }

    public override void _ExitTree()
    {
        if (GodotThread.IsMainThread())
        {
            DisposeRuntime();
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
}
