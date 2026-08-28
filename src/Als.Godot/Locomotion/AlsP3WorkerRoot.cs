using System.Diagnostics;
using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

public partial class AlsP3WorkerRoot : Node3D
{
    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterState _state = null!;
    private AlsAnimationLibraryBuildResult? _library;
    private AlsLocomotionGraphBuildResult? _graph;
    private AlsLocomotionAnimationController? _controller;
    private Node3D? _visualRoot;
    private Skeleton3D? _skeleton;
    private Vector3[] _posePositions = [];
    private Quaternion[] _poseRotations = [];
    private Vector3[] _poseScales = [];
    private Transform3D _capturedRootTransform;
    private ulong _capturedFullPoseDigest;
    private ulong _capturedRootDigest;
    private AlsRuntimeState _runtimeState = AlsRuntimeState.CreateDefault();
    private AlsFrameResult _result;
    private int _disposed;

    internal void Configure(
        AlsP3RuntimeContext context,
        AlsP3CharacterState state,
        in Transform3D initialLogicalTransform)
    {
        AlsRuntimeState.ValidateP4Defaults(in _runtimeState);
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
            _skeleton = _graph.TargetSkeleton;
            var boneCount = _skeleton.GetBoneCount();
            _posePositions = new Vector3[boneCount];
            _poseRotations = new Quaternion[boneCount];
            _poseScales = new Vector3[boneCount];

            var correctedRoot = AlsP3Presentation.Compose(
                initialLogicalTransform,
                context.PresentationTransform);
            AlsP3Presentation.ThrowIfNonFinite(correctedRoot);
            _visualRoot.GlobalTransform = correctedRoot;
            AlsP3Presentation.ThrowIfNonFinite(_visualRoot.GlobalTransform);
            CapturePose();
            PublishVisualRootVisibility(0);

            // Thread ownership is assigned only after the complete visual rig is built and warmed.
            ProcessThreadGroupOrder = 1;
            ProcessThreadGroup = context.Mode == AlsHarnessMode.Parallel
                ? ProcessThreadGroupEnum.SubThread
                : ProcessThreadGroupEnum.MainThread;
        }
        catch
        {
            TryDisposeRuntime();
            throw;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_state.TryEnterWorker())
        {
            return;
        }
        try
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
            var measurement = _context.Measurement;
            var measurementIndex = -1;
            var measure = measurement is not null &&
                measurement.TryGetMeasurementIndex(identity, out measurementIndex);
            var productionElapsedTicks = 0L;
            var allocatedBeforeExchange = measure
                ? GC.GetAllocatedBytesForCurrentThread()
                : 0L;
            var productionSegmentStartedAt = measure
                ? Stopwatch.GetTimestamp()
                : 0L;
            var hasInput = _state.Exchange.TryReadInput(identity, out var input);
            if (measure)
            {
                productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
            }
            if (!hasInput)
            {
                return;
            }
            if (measure)
            {
                measurement!.AddExchangeAllocations(
                    GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeExchange);
            }

            var poseCaptured = false;
            try
            {
                PublishVisualRootVisibility(frameId);
                var isMain = System.Environment.CurrentManagedThreadId == _context.MainManagedThreadId;
                if ((_context.Mode == AlsHarnessMode.Single) != isMain)
                {
                    Interlocked.Increment(ref _context.AffinityViolations);
                }
                if (!isMain)
                {
                    Volatile.Write(ref _state.ObservedOffMainThread, 1);
                }

                var allocatedBeforeModel = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                AlsLocomotionModel.Evaluate(
                    input,
                    ref _runtimeState,
                    ref _result,
                    _context.Settings);
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddModelAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeModel);
                }

                var allocatedBeforeSkeleton = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                CapturePose();
                poseCaptured = true;
                var correctedRoot = AlsP3Presentation.Compose(
                    input.CharacterTransform,
                    _context.PresentationTransform);
                AlsP3Presentation.ThrowIfNonFinite(correctedRoot);
                _visualRoot!.GlobalTransform = correctedRoot;
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddSkeletonAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeSkeleton);
                }

                var allocatedBeforeController = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                _controller!.Apply(_result, input.DeltaTime);
                var appliedRoot = _visualRoot.GlobalTransform;
                AlsP3Presentation.ThrowIfNonFinite(appliedRoot);
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddControllerAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeController);
                }

                allocatedBeforeSkeleton = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                var poseDigest = _controller.ComputePoseDigest(frameId);
                var fullPoseDigest = ComputeFullPoseDigest();
                var rootDigest = AlsP3Presentation.ComputeDigest(appliedRoot);
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddSkeletonAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeSkeleton);
                }

                allocatedBeforeExchange = measure
                    ? GC.GetAllocatedBytesForCurrentThread()
                    : 0L;
                productionSegmentStartedAt = measure
                    ? Stopwatch.GetTimestamp()
                    : 0L;
                var candidate = new AlsP3VisualCommitCandidate(
                    _result.Identity,
                    AlsP3Presentation.Capture(appliedRoot),
                    poseDigest,
                    fullPoseDigest,
                    rootDigest);
                _state.PublishResult(
                    _result,
                    candidate,
                    _result.Identity.FrameId,
                    frameId);
                if (measure)
                {
                    productionElapsedTicks += Stopwatch.GetTimestamp() - productionSegmentStartedAt;
                    measurement!.AddExchangeAllocations(
                        GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeExchange);
                    measurement.RecordWorkerAdvance(
                        measurementIndex,
                        productionElapsedTicks);
                }
            }
            catch (Exception exception)
            {
                if (poseCaptured)
                {
                    RestorePose();
                    var restoredFullPoseDigest = ComputeFullPoseDigest();
                    var restoredRootDigest = AlsP3Presentation.ComputeDigest(
                        _visualRoot!.GlobalTransform);
                    _state.RecordRollback(
                        restoredFullPoseDigest,
                        restoredRootDigest,
                        restoredFullPoseDigest == _capturedFullPoseDigest &&
                        restoredRootDigest == _capturedRootDigest);
                }
                _state.RecordFailure("worker_evaluate", identity, exception);
            }
        }
        finally
        {
            _state.ExitWorker();
        }
    }

    internal bool TryDisposeRuntime()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return true;
        }
        if (!_state.IsWorkerAdmissionClosed || _state.WorkerInFlightCount != 0)
        {
            Volatile.Write(ref _disposed, 0);
            return false;
        }

        _controller?.Dispose();
        _controller = null;
        _graph?.Dispose();
        _graph = null;
        _library?.Dispose();
        _library = null;
        _visualRoot = null;
        _skeleton = null;
        _posePositions = [];
        _poseRotations = [];
        _poseScales = [];
        return true;
    }

    private void CapturePose()
    {
        _capturedRootTransform = _visualRoot!.GlobalTransform;
        for (var index = 0; index < _posePositions.Length; index++)
        {
            _posePositions[index] = _skeleton!.GetBonePosePosition(index);
            _poseRotations[index] = _skeleton.GetBonePoseRotation(index);
            _poseScales[index] = _skeleton.GetBonePoseScale(index);
        }
        _capturedFullPoseDigest = ComputeFullPoseDigest();
        _capturedRootDigest = AlsP3Presentation.ComputeDigest(_capturedRootTransform);
    }

    private void PublishVisualRootVisibility(long observationFrameId)
    {
        var snapshot = AlsP3VisualRootVisibilityObservation.EncodeWorker(
            observationFrameId,
            _visualRoot!.IsVisibleInTree());
        Volatile.Write(ref _state.VisualRootVisibilitySnapshot, snapshot);
    }

    private void RestorePose()
    {
        _visualRoot!.GlobalTransform = _capturedRootTransform;
        for (var index = 0; index < _posePositions.Length; index++)
        {
            _skeleton!.SetBonePosePosition(index, _posePositions[index]);
            _skeleton.SetBonePoseRotation(index, _poseRotations[index]);
            _skeleton.SetBonePoseScale(index, _poseScales[index]);
        }
    }

    private ulong ComputeFullPoseDigest()
    {
        var digest = 14695981039346656037UL;
        for (var index = 0; index < _posePositions.Length; index++)
        {
            Append(ref digest, _skeleton!.GetBonePosePosition(index));
            Append(ref digest, _skeleton.GetBonePoseRotation(index));
            Append(ref digest, _skeleton.GetBonePoseScale(index));
        }
        return digest;
    }

    private static void Append(ref ulong digest, Vector3 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
    }

    private static void Append(ref ulong digest, Quaternion value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
        Append(ref digest, value.W);
    }

    private static void Append(ref ulong digest, float value)
    {
        const ulong prime = 1099511628211UL;
        var quantized = checked((int)MathF.Round(value * 100_000f, MidpointRounding.AwayFromZero));
        for (var shift = 0; shift < 32; shift += 8)
        {
            digest ^= (byte)(quantized >> shift);
            digest *= prime;
        }
    }

}
