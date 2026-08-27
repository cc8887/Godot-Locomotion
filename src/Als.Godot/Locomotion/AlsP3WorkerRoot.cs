using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

public partial class AlsP3WorkerRoot : Node
{
    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterState _state = null!;
    private AlsAnimationLibraryBuildResult? _library;
    private AlsLocomotionGraphBuildResult? _graph;
    private AlsLocomotionAnimationController? _controller;
    private Node3D? _visualRoot;
    private AlsRuntimeState _runtimeState;
    private AlsFrameResult _result;
    private int _disposed;

    internal void Configure(AlsP3RuntimeContext context, AlsP3CharacterState state)
    {
        _context = context;
        _state = state;
        ProcessMode = ProcessModeEnum.Disabled;

        try
        {
            _library = AlsAnimationLibraryBuilder.Build(context.AnimationSet, context.Profile);
            _visualRoot = _library.Root as Node3D
                ?? throw new InvalidOperationException("P3 visual library root must be a Node3D.");
            AddChild(_library.Root);
            _graph = AlsLocomotionGraphBuilder.Build(_library, context.Profile, context.AnimationSet);
            _controller = new AlsLocomotionAnimationController(_graph, context.Settings);
            _controller.Warmup();

            // Thread ownership is assigned only after the complete visual rig is built and warmed.
            ProcessThreadGroupOrder = 1;
            ProcessThreadGroup = context.Mode == AlsHarnessMode.Parallel
                ? ProcessThreadGroupEnum.SubThread
                : ProcessThreadGroupEnum.MainThread;
        }
        catch
        {
            DisposeRuntime();
            throw;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _state.Active) == 0 ||
            Volatile.Read(ref _state.WorkerFrozen) != 0 ||
            Volatile.Read(ref _state.WorkerSuspended) != 0)
        {
            return;
        }

        var frameId = Volatile.Read(ref _state.PublishedFrameId);
        if (frameId <= 0)
        {
            return;
        }

        var identity = new AlsFrameIdentity(
            frameId,
            _state.Handle.CharacterId,
            _state.Handle.Generation);
        if (!_state.Exchange.TryReadInput(identity, out var input))
        {
            return;
        }

        try
        {
            var isMain = System.Environment.CurrentManagedThreadId == _context.MainManagedThreadId;
            if ((_context.Mode == AlsHarnessMode.Single) != isMain)
            {
                Interlocked.Increment(ref _context.AffinityViolations);
            }
            if (!isMain)
            {
                Volatile.Write(ref _state.ObservedOffMainThread, 1);
            }

            AlsLocomotionModel.Evaluate(input, ref _runtimeState, ref _result, _context.Settings);
            _visualRoot!.GlobalTransform = ToGodot(input.CharacterTransform);
            _controller!.Apply(_result, input.DeltaTime);
            var poseDigest = _controller.ComputePoseDigest(frameId);
            _state.PublishResult(_result, poseDigest, _result.Identity.FrameId, frameId);
        }
        catch (Exception exception)
        {
            _state.RecordFailure("worker_evaluate", identity, exception);
        }
    }

    public void DisposeRuntime()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _controller?.Dispose();
        _controller = null;
        _graph?.Dispose();
        _graph = null;
        _library?.Dispose();
        _library = null;
        _visualRoot = null;
    }

    private static Transform3D ToGodot(in System.Numerics.Matrix4x4 value) => new(
        new Basis(
            new Vector3(value.M11, value.M12, value.M13),
            new Vector3(value.M21, value.M22, value.M23),
            new Vector3(value.M31, value.M32, value.M33)),
        new Vector3(value.M41, value.M42, value.M43));
}
