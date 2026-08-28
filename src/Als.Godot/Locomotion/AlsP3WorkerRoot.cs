using System.Diagnostics;
using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Math;
using GodotAls.Core.Pose;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public partial class AlsP3WorkerRoot : Node3D
{
    private const string P4ProfilePath = "res://assets/config/p4_pose_profile.json";

    private AlsP3RuntimeContext _context = null!;
    private AlsP3CharacterState _state = null!;
    private AlsAnimationLibraryBuildResult? _library;
    private AlsLocomotionGraphBuildResult? _graph;
    private AlsLocomotionAnimationController? _controller;
    private AlsComponentPoseModifier? _poseModifier;
    private AlsPoseAnimationProfile? _poseProfile;
    private AlsTurnRotateSettings _turnRotateSettings;
    private int[] _p4CurveAnimationIds = [];
    private AlsCurveSampler[] _p4CurveSamplers = [];
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
            _poseProfile = AlsPoseProfileCompiler.Compile(
                File.ReadAllText(ProjectSettings.GlobalizePath(P4ProfilePath)),
                context.AnimationSet);
            _turnRotateSettings = CompileTurnRotateSettings(
                context.AnimationSet,
                _poseProfile);
            CompileCurveSamplers(context.AnimationSet, _poseProfile);
            _library = AlsAnimationLibraryBuilder.Build(
                context.AnimationSet,
                context.Profile,
                _poseProfile);
            _visualRoot = _library.Root as Node3D
                ?? throw new InvalidOperationException("P3 visual library root must be a Node3D.");
            AddChild(_library.Root);
            _graph = AlsLocomotionGraphBuilder.Build(
                _library,
                context.Profile,
                _poseProfile,
                context.AnimationSet);
            _controller = new AlsLocomotionAnimationController(_graph, context.Settings);
            _controller.Warmup();
            _skeleton = _graph.TargetSkeleton;
            _poseModifier = new AlsComponentPoseModifier(
                _skeleton,
                _visualRoot,
                _library,
                context.AnimationSet,
                _poseProfile);
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
                EvaluateModels(in input);
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
                var p4AnimationInput = CreateP4AnimationInput(in _result);
                _controller!.Apply(in _result, in p4AnimationInput, input.DeltaTime);
                var modifierInput = AlsPoseModifierInput.FromResult(in _result);
                var modifierOutput = default(AlsPoseModifierOutput);
                if (!_poseModifier!.TryApply(
                        in modifierInput,
                        ref modifierOutput,
                        out var modifierReason))
                {
                    _result.P4ReasonCode = modifierReason;
                    throw new InvalidOperationException(
                        $"P4 component pose modifier failed: {modifierReason}");
                }
                // Frame-result ticks are deterministic work units so they can participate in
                // exact single/parallel digests. Wall-clock evidence stays in Measurement.
                _result.P4ModifierElapsedTicks = modifierOutput.DeterministicElapsedTicks;
                _result.P4ReasonCode = AlsP4ReasonCode.None;
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
                Exception? restoreException = null;
                try
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
                }
                catch (Exception secondaryException)
                {
                    restoreException = secondaryException;
                }
                finally
                {
                    _state.RecordFailure(
                        "worker_evaluate",
                        identity,
                        restoreException is null
                            ? exception
                            : new AggregateException(
                                "Worker evaluation and pose restoration both failed.",
                                exception,
                                restoreException));
                }
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

        _poseModifier?.Dispose();
        _poseModifier = null;
        _controller?.Dispose();
        _controller = null;
        _graph?.Dispose();
        _graph = null;
        _library?.Dispose();
        _library = null;
        _visualRoot = null;
        _skeleton = null;
        _poseProfile = null;
        _p4CurveAnimationIds = [];
        _p4CurveSamplers = [];
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

    private void EvaluateModels(in AlsFrameInput input)
    {
        var nextState = _runtimeState;
        var nextResult = _result;
        AlsLocomotionModel.Evaluate(
            input,
            ref nextState,
            ref nextResult,
            _context.Settings);

        var viewSettings = AlsViewPoseSettings.CreateDefault();
        if (!AlsViewPoseModel.TryEvaluate(
                in viewSettings,
                in input,
                in nextState,
                out var viewState,
                out var view,
                out var viewReason))
        {
            nextResult.P4ReasonCode = viewReason;
            throw new InvalidOperationException($"P4 view pose evaluation failed: {viewReason}");
        }
        if (!AlsTurnRotateModel.TrySelectAndAdvance(
                in _turnRotateSettings,
                in input,
                in view,
                in viewState,
                out var turnRotateState,
                out var selection,
                out var selectionReason))
        {
            nextResult.P4ReasonCode = selectionReason;
            throw new InvalidOperationException(
                $"P4 Turn/Rotate selection failed: {selectionReason}");
        }

        nextResult.AimRelativeYaw = view.AimRelativeYaw;
        nextResult.AimRelativePitch = view.AimRelativePitch;
        nextResult.HeadWeight = view.HeadWeight;
        nextResult.SpineWeight = view.SpineWeight;
        nextResult.UpperBodyWeight = view.UpperBodyWeight;
        nextResult.SpineResidualYaw = view.SpineResidualYaw;
        nextResult.P4ReasonCode = AlsP4ReasonCode.None;
        if (selection.Active == 1)
        {
            if (!TrySampleSelectionCurve(
                    in selection,
                    out var previousCurve,
                    out var currentCurve))
            {
                nextResult.P4ReasonCode = AlsP4ReasonCode.NonFiniteCurve;
                throw new InvalidOperationException(
                    "P4 Turn/Rotate curve lookup failed.");
            }
            if (!AlsTurnRotateModel.TryFinalizeYaw(
                    in selection,
                    previousCurve,
                    currentCurve,
                    out var turnRotate,
                    out var curveReason))
            {
                nextResult.P4ReasonCode = curveReason;
                throw new InvalidOperationException(
                    $"P4 Turn/Rotate curve evaluation failed: {curveReason}");
            }
            nextResult.TargetYaw = AlsMath.NormalizeAngleRadians(
                input.CharacterYaw + turnRotate.YawDelta);
            if (selection.YawSource == AlsYawSource.TurnInPlace)
            {
                nextResult.TurnAnimationId = selection.AnimationId;
                nextResult.TurnCurveId = selection.CurveId;
                nextResult.TurnPhase = selection.CurrentPhase;
                nextResult.TurnPlayRate = selection.PhasePlayRate;
                nextResult.TurnNominalDegrees = selection.NominalDegrees;
                nextResult.TurnDirection = selection.Direction;
                nextResult.TurnActive = 1;
                nextResult.TurnYawDelta = turnRotate.YawDelta;
            }
            else
            {
                nextResult.RotateAnimationId = selection.AnimationId;
                nextResult.RotateCurveId = selection.CurveId;
                nextResult.RotatePhase = selection.CurrentPhase;
                nextResult.RotatePlayRate = selection.PhasePlayRate;
                nextResult.RotateDirection = selection.Direction;
                nextResult.RotateActive = 1;
                nextResult.RotateYawDelta = turnRotate.YawDelta;
            }
        }

        _runtimeState = turnRotateState;
        _result = nextResult;
    }

    private bool TrySampleSelectionCurve(
        in AlsTurnRotateSelection selection,
        out float previous,
        out float current)
    {
        previous = 0f;
        current = 0f;
        for (var index = 0; index < _p4CurveAnimationIds.Length; index++)
        {
            if (_p4CurveAnimationIds[index] != selection.AnimationId)
            {
                continue;
            }
            var sampler = _p4CurveSamplers[index];
            return sampler.TrySample(selection.CurveId, selection.PreviousPhase, out previous) &&
                sampler.TrySample(selection.CurveId, selection.CurrentPhase, out current);
        }
        return false;
    }

    private static AlsP4AnimationInput CreateP4AnimationInput(in AlsFrameResult result)
    {
        if (result.TurnActive == 1)
        {
            return AlsP4AnimationInput.Turn(
                result.TurnAnimationId,
                result.TurnPlayRate,
                result.TurnPhase,
                0f,
                0f,
                0f,
                0f);
        }
        if (result.RotateActive == 1)
        {
            return AlsP4AnimationInput.Rotate(
                result.RotateAnimationId,
                result.RotatePlayRate,
                result.RotatePhase,
                0f,
                0f,
                0f,
                0f);
        }
        return AlsP4AnimationInput.Disabled;
    }

    private void CompileCurveSamplers(
        AlsAnimationSetDefinition animationSet,
        AlsPoseAnimationProfile profile)
    {
        var turns = profile.Turns;
        var rotates = profile.Rotates;
        _p4CurveAnimationIds = new int[turns.Length + rotates.Length];
        _p4CurveSamplers = new AlsCurveSampler[_p4CurveAnimationIds.Length];
        var index = 0;
        foreach (var turn in turns)
        {
            _p4CurveAnimationIds[index] = turn.AnimationId;
            _p4CurveSamplers[index++] = new AlsCurveSampler(
                animationSet.Animations[turn.AnimationId].Curves);
        }
        foreach (var rotate in rotates)
        {
            _p4CurveAnimationIds[index] = rotate.AnimationId;
            _p4CurveSamplers[index++] = new AlsCurveSampler(
                animationSet.Animations[rotate.AnimationId].Curves);
        }
    }

    private static AlsTurnRotateSettings CompileTurnRotateSettings(
        AlsAnimationSetDefinition animationSet,
        AlsPoseAnimationProfile profile)
    {
        var reference = AlsTurnRotateSettings.CreateReference();
        return reference with
        {
            StandingTurn90Left = Turn(AlsPoseStance.Standing, -1, 90),
            StandingTurn90Right = Turn(AlsPoseStance.Standing, 1, 90),
            StandingTurn180Left = Turn(AlsPoseStance.Standing, -1, 180),
            StandingTurn180Right = Turn(AlsPoseStance.Standing, 1, 180),
            CrouchingTurn90Left = Turn(AlsPoseStance.Crouching, -1, 90),
            CrouchingTurn90Right = Turn(AlsPoseStance.Crouching, 1, 90),
            CrouchingTurn180Left = Turn(AlsPoseStance.Crouching, -1, 180),
            CrouchingTurn180Right = Turn(AlsPoseStance.Crouching, 1, 180),
            StandingRotateLeft = Rotate(AlsPoseStance.Standing, -1),
            StandingRotateRight = Rotate(AlsPoseStance.Standing, 1),
            CrouchingRotateLeft = Rotate(AlsPoseStance.Crouching, -1),
            CrouchingRotateRight = Rotate(AlsPoseStance.Crouching, 1),
        };

        AlsTurnClipSettings Turn(AlsPoseStance stance, sbyte direction, short degrees)
        {
            foreach (var value in profile.Turns)
            {
                if (value.Stance == stance && value.Direction == direction &&
                    value.NominalDegrees == degrees)
                {
                    return new AlsTurnClipSettings(
                        value.AnimationId,
                        value.CurveId,
                        animationSet.Animations[value.AnimationId].PlayLength,
                        value.BasePlayRate,
                        value.BlendSeconds,
                        value.ScaleAngle);
                }
            }
            throw new InvalidOperationException(
                $"P4 Turn profile combination is missing: {stance}/{direction}/{degrees}");
        }

        AlsRotateClipSettings Rotate(AlsPoseStance stance, sbyte direction)
        {
            foreach (var value in profile.Rotates)
            {
                if (value.Stance == stance && value.Direction == direction)
                {
                    return new AlsRotateClipSettings(
                        value.AnimationId,
                        value.CurveId,
                        animationSet.Animations[value.AnimationId].PlayLength);
                }
            }
            throw new InvalidOperationException(
                $"P4 Rotate profile combination is missing: {stance}/{direction}");
        }
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
