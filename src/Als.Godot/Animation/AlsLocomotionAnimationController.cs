using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Animation;
using GodotAls.Core.Events;
using GodotAls.Core.Math;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public readonly record struct AlsP4AnimationInput(
    int ActiveTurnAnimationId,
    int ActiveRotateAnimationId,
    float TurnPlayRate,
    float RotatePlayRate,
    float TurnPhase,
    float RotatePhase,
    float AimDownPhase,
    float AimForwardPhase,
    float AimUpPhase,
    float AimDownWeight,
    float AimForwardWeight,
    float AimUpWeight)
{
    public static readonly AlsP4AnimationInput Disabled = new(
        -1, -1, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

    public static AlsP4AnimationInput Turn(
        int animationId,
        float playRate,
        float phase,
        float aimPhase,
        float aimDownWeight,
        float aimForwardWeight,
        float aimUpWeight) => new(
            animationId, -1, playRate, 1f, phase, 0f,
            aimPhase, aimPhase, aimPhase,
            aimDownWeight, aimForwardWeight, aimUpWeight);

    public static AlsP4AnimationInput Rotate(
        int animationId,
        float playRate,
        float phase,
        float aimPhase,
        float aimDownWeight,
        float aimForwardWeight,
        float aimUpWeight) => new(
            -1, animationId, 1f, playRate, 0f, phase,
            aimPhase, aimPhase, aimPhase,
            aimDownWeight, aimForwardWeight, aimUpWeight);
}

public readonly record struct AlsPreparedAnimationFrame(
    long OwnerId,
    long Revision,
    AlsAnimationState AnimationState,
    AlsStance Stance,
    int BaseAnimationIdA,
    int BaseAnimationIdB,
    int BaseAnimationIdC,
    float BaseWeightA,
    float BaseWeightB,
    float BaseWeightC,
    float BasePhaseNormalized,
    int TurnAnimationIdA,
    int TurnAnimationIdB,
    float TurnPhaseA,
    float TurnPhaseB,
    float TurnBlendAmount,
    int RotateAnimationIdA,
    int RotateAnimationIdB,
    float RotatePhaseA,
    float RotatePhaseB,
    float RotateBlendAmount,
    float ActionModeBlendAmount,
    float ActionBlendAmount)
{
    public int PreviousBaseAnimationIdA { get; init; } = -1;
    public int PreviousBaseAnimationIdB { get; init; } = -1;
    public int PreviousBaseAnimationIdC { get; init; } = -1;
    public float PreviousBaseWeightA { get; init; }
    public float PreviousBaseWeightB { get; init; }
    public float PreviousBaseWeightC { get; init; }
    public float PreviousBasePhaseNormalized { get; init; }
    public float BaseTransitionAlpha { get; init; } = 1f;
}

public readonly record struct AlsFootCurveSample(
    float LeftIkWeight,
    float RightIkWeight,
    float LeftLockCurve,
    float RightLockCurve);

internal readonly record struct AlsP4BankTransactionDiagnostics(
    byte Bank,
    byte TargetBank,
    bool BlendActive,
    double BlendElapsed,
    float BlendAmount,
    int BankAAnimationId,
    int BankBAnimationId,
    float BankAPlayRate,
    float BankAPhase,
    float BankBPlayRate,
    float BankBPhase,
    bool Pending,
    int PendingAnimationId,
    float PendingPlayRate,
    float PendingPhase);

internal readonly record struct AlsAnimationTransactionDiagnostics(
    AlsAnimationState AnimationState,
    AlsStance Stance,
    long ManualAdvanceCount,
    int ActiveTurnAnimationId,
    int ActiveRotateAnimationId,
    byte ActiveP4Mode,
    float ActionBlendAmount,
    float ActionModeBlendAmount,
    double BaseStateElapsed,
    AlsP4BankTransactionDiagnostics TurnBank,
    AlsP4BankTransactionDiagnostics RotateBank,
    AlsDirectionFeedbackState DirectionFeedback,
    AlsStandingTurnSlotInput StandingTurnSlot)
{
    public AlsFrameIdentity FullMovementIdentity { get; init; }
    public AlsAnimationInputFeedback FullMovementFeedback { get; init; }
    public AlsFootIkPropertyState NativeFeet { get; init; }
    public AlsRefactoredFootRigState RefactoredRig { get; init; }
    public AlsBasedFootLockFrameState BasedFeet { get; init; }
    public AlsBinaryBlendState RootSelector { get; init; }
    public AlsFrameIdentity RootIdentity { get; init; }
    public AlsRagdollFrameDiagnostics Ragdoll { get; init; }
}

internal readonly record struct AlsPreparedAnimationCommit(
    long OwnerId,
    long Revision,
    byte HasBaseTransitionPlayback,
    double CurrentPlayPosition,
    double FadingPlayPosition,
    double FadePosition);

public sealed class AlsLocomotionAnimationController : IDisposable
{
    private AlsProductionMovementRuntime? _fullMovement;
    internal bool UsesCompleteMovement => _fullMovement is not null;
    internal bool UsesLayeredPose => _fullMovement?.UsesLayeredPose == true;
    internal bool UsesNativeFootIk => _fullMovement?.UsesNativeFootIk == true;
    internal bool UsesRefactoredFeet => _fullMovement?.UsesRefactoredFeet == true;
    internal System.Numerics.Vector3 CandidateNativePelvisOffset => _fullMovement?.CandidatePelvisOffset ?? default;
    internal float CandidateNativeLeftLock => _fullMovement?.CandidateLeftLock ?? 0;
    internal float CandidateNativeRightLock => _fullMovement?.CandidateRightLock ?? 0;
    internal AlsFootIkPropertyState CandidateNativeFeet => _fullMovement?.CandidateFeet ?? default;
    internal AlsBasedFootLockDiagnostics CandidateBasedFeet => _fullMovement?.CandidateBasedFeet ?? default;
    internal AlsBasedFootLockFrameTrace? CommittedBasedTrace => _fullMovement?.CommittedBasedTrace;
    internal AlsCharacterRotationFeedback PendingCharacterRotationFeedback => _fullMovement?.CandidateRotationFeedback ?? default;
    internal AlsRefactoredAnimationFeedback PendingRefactoredFeedback => _fullMovement?.CandidateRefactoredFeedback ?? default;
    internal bool PendingPresentation => _fullMovement?.CandidatePresentationPending == true;
    internal AlsFullMovementDiagnostics FullMovementDiagnostics => _fullMovement?.Diagnostics ?? default;
    internal AlsStandingCycleState StandingCycleState => _fullMovement?.Base.Grounded.CommittedStanding.State ?? _committedPrepared.Cycle.State;
    internal AlsTransitionStackState StandingTransitions => _fullMovement?.Base.Grounded.CommittedStanding.Transitions ?? _committedPrepared.Cycle.Transitions;
    internal AlsCycleSyncFrame StandingSync => _fullMovement?.Base.CommittedSources ?? _committedPrepared.Cycle.Sync;
    internal AlsCycleDetailFrame StandingDetail => _fullMovement?.Base.Grounded.CommittedStanding.Detail ?? _committedPrepared.Cycle.Detail;
    internal AlsBinaryBlendState StandingSprintBlend => _fullMovement?.Base.Grounded.CommittedStanding.SprintBlend ?? _committedPrepared.Cycle.SprintBlend;
    internal float StandingSprintMask => _fullMovement?.Base.Grounded.CommittedStanding.SprintMask ?? _committedPrepared.Cycle.SprintMask;
    internal AlsStandingMovementInput StandingMovementInput => _fullMovement?.Base.Grounded.CommittedStanding.Movement ?? _committedPrepared.Cycle.Movement;
    internal AlsP5SourceEventState SourceEventState => _fullMovement?.Base.Movement.CommittedEventState ?? _sourceEvents;
    internal AlsLocomotionTimingPolicy TimingPolicy => _fullMovement is not null ? AlsLocomotionTimingPolicy.CompleteMovementGraph : _graph.StandingCycle is null
        ? AlsLocomotionTimingPolicy.Model : AlsLocomotionTimingPolicy.StandingSourceGraph;
    private const int WarmupUninitialized = 0;
    private const int WarmupInitializing = 1;
    private const int WarmupReady = 2;
    private const ulong DigestOffsetBasis = 14695981039346656037UL;
    private const ulong DigestPrime = 1099511628211UL;
    private const float QuantizationScale = 100_000f;
    private const float HipHemisphereForwardThreshold = 0.34202015f;
    private const double HipVariantBlendSeconds = 0.2;
    private const double MaximumPoseDirectionRadiansPerSecond = 10.0;
    private static long _nextOwnerId;

    private static readonly string[] PoseBoneNames =
    [
        "pelvis", "spine_03", "hand_l", "hand_r", "foot_l", "foot_r",
    ];

    private readonly AlsLocomotionGraphBuildResult _graph;
    private readonly Skeleton3D _skeleton;
    private readonly float _playRateMaximum;
    private readonly System.Numerics.Vector3 _animatedStandingSpeeds;
    private readonly Vector2[][] _standingBlendRings;
    private readonly Vector2[] _crouchingBlendRing;
    private readonly long _ownerId = Interlocked.Increment(ref _nextOwnerId);
    private readonly int[] _poseBoneIndices = new int[PoseBoneNames.Length];
    private AnimationNodeStateMachinePlayback? _topPlayback;
    private AnimationNodeStateMachinePlayback? _groundedPlayback;
    private int _warmupState;
    private int _disposed;
    private byte _activeP4Mode;
    private float _activeTurnBlendSeconds;
    private P4BlendChannelState _turnChannel;
    private P4BlendChannelState _rotateChannel;
    private float _actionBlendAmount;
    private float _actionBlendTarget;
    private float _actionBlendDuration;
    private float _actionModeBlendAmount;
    private float _actionModeBlendTarget;
    private AlsFootCurveBinding[] _footCurveBindingsByAnimation = [];
    private AlsCurveSampler?[] _footCurveSamplersByAnimation = [];
    private byte[] _hasFootCurveBinding = [];
    private float[] _animationPlayLengths = [];
    private BaseCurveBlendResolver? _standingBaseCurves;
    private BaseCurveBlendResolver? _crouchingBaseCurves;
    private float _groundedIkWeight;
    private float _jumpStartIkWeight;
    private float _fallLoopIkWeight;
    private float _landRecoveryIkWeight;
    private long _preparedRevision;
    private byte _hasPreparedFrame;
    private bool _sourceTimingPending;
    private bool _sourceEventsPending;
    private AlsP5SourceEventState _sourceEvents;
    private AlsP5SourceEventState _pendingSourceEvents;
    private AlsEventBuffer _pendingSourceEventBuffer;
    private PreparedApply _pendingPrepared;
    private PreparedP4 _pendingPreparedP4;
    private P4BlendChannelUpdate _pendingTurnUpdate;
    private P4BlendChannelUpdate _pendingRotateUpdate;
    private AlsP4AnimationInput _pendingP4Input;
    private double _pendingDeltaTime;
    private byte _pendingRequestedP4Mode;
    private float _pendingActionBlendAmount;
    private float _pendingActionBlendTarget;
    private float _pendingActionBlendDuration;
    private float _pendingActionModeBlendAmount;
    private float _pendingActionModeBlendTarget;
    private AlsPreparedAnimationFrame _pendingDecision;
    private byte _preparedTransactionState;
    private BaseCurveDecision _committedBaseDecision;
    private BaseCurveDecision _baseTransitionPrevious;
    private BaseCurveDecision _pendingBasePrevious;
    private BaseCurveDecision _pendingBaseDecision;
    private double _baseTransitionElapsed;
    private double _pendingBaseTransitionElapsed;
    private byte _baseTransitionActive;
    private byte _pendingBaseTransitionActive;
    private AlsAnimationState _baseTransitionTargetState;
    private AlsStance _baseTransitionTargetStance;
    private AlsAnimationState _pendingBaseTransitionTargetState;
    private AlsStance _pendingBaseTransitionTargetStance;
    private AlsAnimationState _baseTransitionPreviousState;
    private AlsStance _baseTransitionPreviousStance;
    private AlsAnimationState _pendingBaseTransitionPreviousState;
    private AlsStance _pendingBaseTransitionPreviousStance;
    private PreparedApply _baseTransitionPreviousPrepared;
    private PreparedApply _pendingBaseTransitionPreviousPrepared;
    private double _baseTransitionCurrentPlayPosition;
    private double _baseTransitionFadingPlayPosition;
    private double _baseTransitionFadePosition;
    private double _baseStateElapsed;
    private double _pendingBaseStateElapsed;
    private PreparedApply _committedPrepared;
    private PreparedP4 _committedPreparedP4;

    public AlsLocomotionAnimationController(
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _skeleton = graph.TargetSkeleton;
        ArgumentNullException.ThrowIfNull(settings);
        _playRateMaximum = settings.PlayRateMaximum;
        _animatedStandingSpeeds = new(settings.AnimatedWalkSpeed, settings.AnimatedRunSpeed, settings.AnimatedSprintSpeed);
        _standingBlendRings = graph.Handles.StandingGaitRadii
            .Select(radius => BuildBlendRing(graph.Handles.BaseCurves.StandingSamples, radius)).ToArray();
        _crouchingBlendRing = BuildBlendRing(
            graph.Handles.BaseCurves.CrouchingSamples, graph.Handles.CrouchingRadius);
    }

    public AlsLocomotionAnimationController(
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings,
        AlsPoseAnimationProfile poseProfile,
        AlsAnimationSetDefinition animationSet)
        : this(graph, settings)
    {
        ArgumentNullException.ThrowIfNull(poseProfile);
        if (!poseProfile.IsRuntimeComplete)
        {
            throw new InvalidOperationException(
                "Runtime animation controller requires a complete three-parameter pose profile.");
        }
        ArgumentNullException.ThrowIfNull(animationSet);
        var curveProfile = poseProfile.FootCurves;
        _groundedIkWeight = curveProfile.GroundedIkWeight;
        _jumpStartIkWeight = curveProfile.JumpStartIkWeight;
        _fallLoopIkWeight = curveProfile.FallLoopIkWeight;
        _landRecoveryIkWeight = curveProfile.LandRecoveryIkWeight;
        _footCurveBindingsByAnimation = new AlsFootCurveBinding[animationSet.Animations.Length];
        _footCurveSamplersByAnimation = new AlsCurveSampler?[animationSet.Animations.Length];
        _hasFootCurveBinding = new byte[animationSet.Animations.Length];
        _animationPlayLengths = new float[animationSet.Animations.Length];
        for (var index = 0; index < animationSet.Animations.Length; index++)
        {
            _animationPlayLengths[index] = animationSet.Animations[index].PlayLength;
        }
        var bindings = curveProfile.Bindings;
        for (var index = 0; index < bindings.Length; index++)
        {
            var binding = bindings[index];
            var animationId = binding.AnimationId;
            if ((uint)animationId >= (uint)animationSet.Animations.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(poseProfile), "Foot curve binding animation ID is invalid.");
            }
            if (_hasFootCurveBinding[animationId] != 0)
            {
                throw new ArgumentException(
                    "Foot curve bindings contain a duplicate animation ID.", nameof(poseProfile));
            }
            _footCurveBindingsByAnimation[animationId] = binding;
            _footCurveSamplersByAnimation[animationId] = new AlsCurveSampler(
                animationSet.Animations[animationId].Curves);
            _hasFootCurveBinding[animationId] = 1;
        }
        _standingBaseCurves = new BaseCurveBlendResolver(
            graph.Handles.BaseCurves.StandingSamples);
        _crouchingBaseCurves = new BaseCurveBlendResolver(
            graph.Handles.BaseCurves.CrouchingSamples);
    }

    public AlsAnimationState ActiveAnimationState { get; private set; } = AlsAnimationState.Grounded;

    public AlsStance ActiveStance { get; private set; } = AlsStance.Standing;

    public long ManualAdvanceCount { get; private set; }

    internal long GraphAdvanceCount { get; private set; }

    public int ActiveTurnAnimationId { get; private set; } = -1;

    public int ActiveRotateAnimationId { get; private set; } = -1;

    internal AlsAnimationTransactionDiagnostics CaptureTransactionDiagnostics() => new(
        ActiveAnimationState,
        ActiveStance,
        ManualAdvanceCount,
        ActiveTurnAnimationId,
        ActiveRotateAnimationId,
        _activeP4Mode,
        _actionBlendAmount,
        _actionModeBlendAmount,
        _baseStateElapsed,
        CaptureBankDiagnostics(in _turnChannel),
        CaptureBankDiagnostics(in _rotateChannel),
        _committedPrepared.Cycle.Detail.Standing.Feedback,
        _committedPrepared.Cycle.Detail.Standing.TurnSlot)
        { FullMovementIdentity = _fullMovement?.Base.CommittedIdentity ?? default,
            FullMovementFeedback = _fullMovement?.CommittedFeedback ?? default,
            NativeFeet = _fullMovement?.CommittedFeet ?? default,
            RefactoredRig = _fullMovement?.CommittedRefactoredRig ?? default,
            BasedFeet = _fullMovement?.CommittedBasedFeet ?? default,
            RootSelector = _fullMovement?.CommittedRoot ?? default,
            RootIdentity = _fullMovement?.CommittedRootIdentity ?? default,
            Ragdoll = _fullMovement?.CommittedRagdoll ?? default };

    internal void EnableCompleteMovement(AlsMovementGraphDefinition definition, AlsAnimationLibraryBuildResult library,
        AlsAnimationSetDefinition set, AlsPoseAnimationProfile pose,uint character,uint generation)
    {
        ThrowIfDisposed();
        if (_warmupState != WarmupReady || _fullMovement is not null || ManualAdvanceCount != 0 || _hasPreparedFrame != 0)
            throw new InvalidOperationException("Complete movement must be configured once before the first frame.");
        _fullMovement = new(definition, library, _graph.StandingCycle ?? throw new InvalidOperationException("Standing graph is missing."),
            set, pose, _animatedStandingSpeeds,character,generation);
        _graph.Tree.Active = false;
    }

    public void Warmup()
    {
        ThrowIfDisposed();
        var previousState = Interlocked.CompareExchange(
            ref _warmupState,
            WarmupInitializing,
            WarmupUninitialized);
        if (previousState == WarmupReady)
        {
            return;
        }
        if (previousState == WarmupInitializing)
        {
            throw new InvalidOperationException(
                "P3 locomotion animation controller Warmup() is already initializing.");
        }

        try
        {
            if (!GodotObject.IsInstanceValid(_graph.Tree) ||
                !GodotObject.IsInstanceValid(_skeleton))
            {
                throw new ObjectDisposedException(nameof(AlsLocomotionGraphBuildResult));
            }

            _graph.Tree.Active = true;
            var topPlayback = GetPlayback(_graph.Handles.TopPlaybackPath, "top");
            var groundedPlayback = GetPlayback(_graph.Handles.GroundedPlaybackPath, "Grounded");
            var poseBoneIndices = new int[PoseBoneNames.Length];
            for (var index = 0; index < PoseBoneNames.Length; index++)
            {
                poseBoneIndices[index] = _skeleton.FindBone(PoseBoneNames[index]);
                if (poseBoneIndices[index] < 0)
                {
                    throw new InvalidOperationException(
                        $"P3 graph pose digest bone is missing: {PoseBoneNames[index]}");
                }
            }

            topPlayback.Start(
                _graph.Handles.StateNames[(int)AlsAnimationState.Grounded], true);
            groundedPlayback.Start(
                _graph.Handles.StanceNames[(int)AlsStance.Standing], true);
            SetParameters(
                _graph.Handles.GroundedStanding,
                Vector2.Zero,
                1f,
                Vector2.Zero,
                0f,
                0f);
            _committedPrepared = new PreparedApply(
                _graph.Handles.GroundedStanding,
                Vector2.Zero,
                1f,
                Vector2.Zero,
                0f,
                0f);
            _committedPreparedP4 = PreparedP4.Disabled;
            _committedBaseDecision = _standingBaseCurves is null
                ? BaseCurveDecision.Empty(0f)
                : _standingBaseCurves.Resolve(Vector2.Zero, 0f);
            if (_graph.Handles.P4 is not null)
            {
                _turnChannel = P4BlendChannelState.Initial(
                    _graph.Handles.P4.InitialTurnBinding);
                _rotateChannel = P4BlendChannelState.Initial(
                    _graph.Handles.P4.InitialRotateBinding);
                var turnUpdate = P4BlendChannelUpdate.Idle(_turnChannel);
                var rotateUpdate = P4BlendChannelUpdate.Idle(_rotateChannel);
                SetP4Parameters(
                    _graph.Handles.P4,
                    PreparedP4.Disabled,
                    turnUpdate,
                    rotateUpdate,
                    0f,
                    0f);
            }
            _graph.Tree.Advance(0.0);

            _topPlayback = topPlayback;
            _groundedPlayback = groundedPlayback;
            poseBoneIndices.CopyTo(_poseBoneIndices, 0);
            Volatile.Write(ref _warmupState, WarmupReady);
        }
        catch
        {
            _topPlayback = null;
            _groundedPlayback = null;
            if (GodotObject.IsInstanceValid(_graph.Tree))
            {
                _graph.Tree.Active = false;
            }
            Volatile.Write(ref _warmupState, WarmupUninitialized);
            throw;
        }
    }

    public AlsEventBuffer Apply(in AlsFrameResult result, double deltaTime)
    {
        var p4 = AlsP4AnimationInput.Disabled;
        return Apply(in result, in p4, deltaTime);
    }

    public AlsEventBuffer Apply(
        in AlsFrameResult result,
        in AlsP4AnimationInput p4Input,
        double deltaTime,
        AlsStandingMovementInput? movementInput = null)
    {
        var prepared = PrepareFrame(in result, in p4Input, deltaTime, movementInput);
        try
        {
            var eventResult = result;
            CompleteSourceEvents(in prepared, ref eventResult);
            ApplyPrepared(in prepared);
            CommitPrepared(in prepared);
            return eventResult.TypedEvents;
        }
        catch
        {
            if (_preparedTransactionState == 2)
            {
                RollbackPrepared(in prepared);
            }
            else if (_preparedTransactionState == 1)
            {
                DiscardPrepared(in prepared);
            }
            throw;
        }
    }

    internal AlsPreparedAnimationFrame PrepareFrame(in AlsFrameResult result, in AlsP4AnimationInput p4Input,
        in AlsFrameInput input, bool cancelForRuntimeFailure = false)
    {
        if (input.Identity != result.Identity) throw new ArgumentException("Animation input/result identity mismatch.", nameof(input));
        if (_fullMovement is not null)
        {
            ThrowIfDisposed();
            if (_hasPreparedFrame != 0) throw new InvalidOperationException("A movement candidate is already pending.");
            _fullMovement.Prepare(input, result, cancelForRuntimeFailure);
            return RegisterMovementCandidate(input, result);
        }
        return PrepareFrame(result, p4Input, input.DeltaTime, _graph.StandingCycle?.CreateMovementInput(input));
    }

    internal AlsPreparedAnimationFrame PrepareFootQueries(in AlsFrameInput input, in AlsFrameResult result,
        in AlsLocalPose component, out AlsFootRigQueries queries, bool cancelForRuntimeFailure = false)
    {
        ThrowIfDisposed();
        if (!UsesRefactoredFeet || _hasPreparedFrame != 0)
            throw new InvalidOperationException("Split movement preparation requires an idle Refactored owner.");
        queries = _fullMovement!.PrepareFootQueries(input, result, component, cancelForRuntimeFailure);
        return RegisterMovementCandidate(input, result);
    }

    internal void ResumeFootQueries(in AlsPreparedAnimationFrame prepared, in AlsFootRigObservations observations)
    {
        ThrowIfDisposed(); ValidatePrepared(prepared);
        if (!UsesRefactoredFeet) throw new InvalidOperationException("No split movement owner.");
        _fullMovement!.ResumeFootQueries(observations);
    }

    private AlsPreparedAnimationFrame RegisterMovementCandidate(in AlsFrameInput input, in AlsFrameResult result)
    {
        _pendingDecision = default(AlsPreparedAnimationFrame) with { OwnerId = _ownerId, Revision = ++_preparedRevision,
            AnimationState = _fullMovement!.AnimationState, Stance = result.ActualStance,
            BaseAnimationIdA = -1, BaseAnimationIdB = -1, BaseAnimationIdC = -1,
            TurnAnimationIdA = -1, TurnAnimationIdB = -1, RotateAnimationIdA = -1, RotateAnimationIdB = -1 };
        _pendingDeltaTime = input.DeltaTime; _sourceTimingPending = _sourceEventsPending = true;
        _hasPreparedFrame = _preparedTransactionState = 1;
        return _pendingDecision;
    }

    public AlsPreparedAnimationFrame PrepareFrame(
        in AlsFrameResult result,
        in AlsP4AnimationInput p4Input,
        double deltaTime,
        AlsStandingMovementInput? movementInput = null)
    {
        ThrowIfDisposed();
        if (_fullMovement is not null)
            throw new InvalidOperationException("Complete movement requires the full frame input; legacy preparation cannot advance its sources.");
        if (Volatile.Read(ref _warmupState) != WarmupReady)
        {
            throw new InvalidOperationException(
                "P3 locomotion animation controller must be warmed before PrepareFrame().");
        }
        if (_preparedTransactionState == 2)
        {
            throw new InvalidOperationException(
                "Applied animation frame must be committed or rolled back before preparing another frame.");
        }
        var preparedP4 = PrepareP4(in p4Input);
        // Standing owns Rotate pose and source time; the legacy channel remains for other branches.
        var sourceBranch = _graph.StandingCycle is not null && result.AnimationState == AlsAnimationState.Grounded &&
            result.ActualStance == AlsStance.Standing;
        if (sourceBranch) preparedP4 = preparedP4 with { RotateActive = false };

        var requestedP4Mode = preparedP4.TurnActive ? (byte)1 :
            preparedP4.RotateActive ? (byte)2 : (byte)0;
        var turnBranchVisible = _actionBlendAmount > 0f && _actionModeBlendAmount < 1f;
        var rotateBranchVisible = _actionBlendAmount > 0f && _actionModeBlendAmount > 0f;
        var nextActionBlendAmount = _actionBlendAmount;
        var nextActionBlendTarget = _actionBlendTarget;
        var nextActionBlendDuration = _actionBlendDuration;
        var nextActionModeBlendAmount = _actionModeBlendAmount;
        var nextActionModeBlendTarget = _actionModeBlendTarget;
        if (requestedP4Mode != _activeP4Mode)
        {
            nextActionBlendTarget = requestedP4Mode == 0 ? 0f : 1f;
            nextActionBlendDuration = requestedP4Mode switch
            {
                1 => preparedP4.TurnBinding.BlendSeconds,
                2 => preparedP4.RotateBinding.BlendSeconds,
                _ when _activeP4Mode == 1 => _activeTurnBlendSeconds,
                _ => 0.08f,
            };
            nextActionModeBlendTarget = requestedP4Mode == 2 ? 1f : 0f;
            if (_activeP4Mode == 0 || requestedP4Mode == 0)
            {
                nextActionModeBlendAmount = nextActionModeBlendTarget;
            }
        }
        nextActionBlendAmount = MoveBlend(
            nextActionBlendAmount, nextActionBlendTarget, nextActionBlendDuration, deltaTime);
        nextActionModeBlendAmount = MoveBlend(
            nextActionModeBlendAmount, nextActionModeBlendTarget, 0.08f, deltaTime);
        var turnUpdate = UpdateP4BlendChannel(
            _turnChannel,
            preparedP4.TurnActive,
            turnBranchVisible,
            preparedP4.TurnBinding,
            preparedP4.TurnPlayRate,
            preparedP4.TurnPhase,
            deltaTime);
        var rotateUpdate = UpdateP4BlendChannel(
            _rotateChannel,
            preparedP4.RotateActive,
            rotateBranchVisible,
            preparedP4.RotateBinding,
            preparedP4.RotatePlayRate,
            preparedP4.RotatePhase,
            deltaTime);

        var turnSlot = new AlsStandingTurnSlotInput(turnUpdate.State.BankA.AnimationId, turnUpdate.State.BankB.AnimationId,
            turnUpdate.State.BankAPhase, turnUpdate.State.BankBPhase, turnUpdate.State.BlendAmount,
            nextActionBlendAmount * (1 - nextActionModeBlendAmount));
        var prepared = Prepare(result, deltaTime, movementInput, turnSlot);

        var baseDecision = PrepareBaseCurveDecision(
            result.AnimationState,
            result.ActualStance,
            prepared.Blend,
            prepared.Phase,
            deltaTime,
            out var nextBaseStateElapsed);
        PrepareBaseTransition(
            result.AnimationState,
            result.ActualStance,
            in baseDecision,
            deltaTime,
            out var previousBaseDecision,
            out var baseTransitionAlpha);
        prepared = prepared with { Phase = baseDecision.PhaseNormalized };

        var revision = checked(_preparedRevision + 1);
        var decision = new AlsPreparedAnimationFrame(
            _ownerId,
            revision,
            result.AnimationState,
            result.ActualStance,
            baseDecision.AnimationIdA,
            baseDecision.AnimationIdB,
            baseDecision.AnimationIdC,
            baseDecision.WeightA,
            baseDecision.WeightB,
            baseDecision.WeightC,
            baseDecision.PhaseNormalized,
            turnUpdate.State.BankA.AnimationId,
            turnUpdate.State.BankB.AnimationId,
            turnUpdate.State.BankAPhase,
            turnUpdate.State.BankBPhase,
            turnUpdate.State.BlendAmount,
            rotateUpdate.State.BankA.AnimationId,
            rotateUpdate.State.BankB.AnimationId,
            rotateUpdate.State.BankAPhase,
            rotateUpdate.State.BankBPhase,
            rotateUpdate.State.BlendAmount,
            nextActionModeBlendAmount,
            prepared.CycleEnabled ? 0 : nextActionBlendAmount)
        {
            PreviousBaseAnimationIdA = previousBaseDecision.AnimationIdA,
            PreviousBaseAnimationIdB = previousBaseDecision.AnimationIdB,
            PreviousBaseAnimationIdC = previousBaseDecision.AnimationIdC,
            PreviousBaseWeightA = previousBaseDecision.WeightA,
            PreviousBaseWeightB = previousBaseDecision.WeightB,
            PreviousBaseWeightC = previousBaseDecision.WeightC,
            PreviousBasePhaseNormalized = previousBaseDecision.PhaseNormalized,
            BaseTransitionAlpha = baseTransitionAlpha,
        };
        if (_graph.StandingCycle is not null)
        {
            var sourceSync = prepared.Cycle.Sync;
            ReadOnlySpan<AlsP5SourceNotifyTick> sourceTicks = sourceSync.NotifyTicks;
            var count = prepared.CycleEnabled ? sourceSync.NotifyTickCount : 0;
            if (!AlsP5Runtime.TryPrepareSourceEvents(_graph.StandingCycle.SourceBindings, result.Identity, (float)deltaTime,
                    sourceTicks[..count], prepared.CycleEnabled ? 1 : 1 - nextActionBlendAmount, _sourceEvents, out _pendingSourceEvents,
                    out _pendingSourceEventBuffer, out var eventFailure))
                throw new InvalidOperationException($"P5 source event prepare failed: {eventFailure}");
        }
        _pendingPrepared = prepared;
        _pendingPreparedP4 = preparedP4;
        _pendingTurnUpdate = turnUpdate;
        _pendingRotateUpdate = rotateUpdate;
        _pendingP4Input = p4Input;
        _pendingDeltaTime = deltaTime;
        _pendingRequestedP4Mode = requestedP4Mode;
        _pendingActionBlendAmount = nextActionBlendAmount;
        _pendingActionBlendTarget = nextActionBlendTarget;
        _pendingActionBlendDuration = nextActionBlendDuration;
        _pendingActionModeBlendAmount = nextActionModeBlendAmount;
        _pendingActionModeBlendTarget = nextActionModeBlendTarget;
        _pendingDecision = decision;
        _pendingBaseDecision = baseDecision;
        _pendingBaseStateElapsed = nextBaseStateElapsed;
        _preparedRevision = revision;
        _hasPreparedFrame = 1;
        _sourceTimingPending = AlsLocomotionModel.HasPendingSourceTiming(result);
        _sourceEventsPending = _graph.StandingCycle is not null;
        _preparedTransactionState = 1;
        return decision;
    }

    public AlsFootCurveSample SampleFootCurves(
        in AlsPreparedAnimationFrame prepared)
    {
        ThrowIfDisposed();
        ValidatePrepared(in prepared);
        if (_fullMovement is not null) return _fullMovement.FootCurves();
        var ikWeight = prepared.AnimationState switch
        {
            AlsAnimationState.Grounded => _groundedIkWeight,
            AlsAnimationState.JumpStart => _jumpStartIkWeight,
            AlsAnimationState.FallLoop => _fallLoopIkWeight,
            AlsAnimationState.LandRecovery => _landRecoveryIkWeight,
            _ => 0f,
        };
        SampleBaseCurves(in prepared, out var baseLeft, out var baseRight);
        SampleActionBank(
            prepared.TurnAnimationIdA, prepared.TurnPhaseA,
            out var turnLeftA, out var turnRightA);
        SampleActionBank(
            prepared.TurnAnimationIdB, prepared.TurnPhaseB,
            out var turnLeftB, out var turnRightB);
        SampleActionBank(
            prepared.RotateAnimationIdA, prepared.RotatePhaseA,
            out var rotateLeftA, out var rotateRightA);
        SampleActionBank(
            prepared.RotateAnimationIdB, prepared.RotatePhaseB,
            out var rotateLeftB, out var rotateRightB);
        var turnLeft = Lerp(turnLeftA, turnLeftB, prepared.TurnBlendAmount);
        var turnRight = Lerp(turnRightA, turnRightB, prepared.TurnBlendAmount);
        var rotateLeft = Lerp(rotateLeftA, rotateLeftB, prepared.RotateBlendAmount);
        var rotateRight = Lerp(rotateRightA, rotateRightB, prepared.RotateBlendAmount);
        var actionLeft = Lerp(turnLeft, rotateLeft, prepared.ActionModeBlendAmount);
        var actionRight = Lerp(turnRight, rotateRight, prepared.ActionModeBlendAmount);
        var leftLock = Lerp(baseLeft, actionLeft, prepared.ActionBlendAmount);
        var rightLock = Lerp(baseRight, actionRight, prepared.ActionBlendAmount);
        return new AlsFootCurveSample(
            ikWeight,
            ikWeight,
            Math.Clamp(leftLock, 0f, 1f),
            Math.Clamp(rightLock, 0f, 1f));
    }

    internal void CompleteSourceTiming(in AlsPreparedAnimationFrame prepared,
        ref AlsRuntimeState state, ref AlsFrameResult result)
    {
        ValidatePrepared(in prepared);
        if (_fullMovement is not null)
        {
            if (!_sourceTimingPending) throw new InvalidOperationException("Movement timing was already completed.");
            _fullMovement.CompleteTiming(ref state, ref result); _sourceTimingPending = false; return;
        }
        if (!_pendingPrepared.CycleEnabled)
        {
            if (!float.IsFinite(result.Stride) || !float.IsFinite(result.PlayRate) || !float.IsFinite(result.AnimationPhase))
                throw new InvalidOperationException("Non-source branch cannot publish unresolved timing.");
            return;
        }
        if (!_sourceTimingPending)
            throw new InvalidOperationException("The prepared source timing was already completed.");
        var cycle = _pendingPrepared.Cycle.State;
        AlsLocomotionModel.CompleteStandingSourceTiming(_pendingPrepared.SourceIdentity,
            new(cycle.Stride, cycle.PlayRate, cycle.Phase), ref state, ref result);
        _sourceTimingPending = false;
    }

    internal void CompleteSourceRotation(in AlsPreparedAnimationFrame prepared, in AlsFrameInput input,
        ref AlsRuntimeState state, ref AlsFrameResult result)
    {
        ValidatePrepared(in prepared);
        if (_fullMovement is not null) { _fullMovement.CompleteRotation(input, ref state, ref result); return; }
        if (!_pendingPrepared.CycleEnabled || result.RotateActive == 0) return;
        if (input.Identity != result.Identity || result.Identity != _pendingPrepared.SourceIdentity)
            throw new InvalidOperationException("Standing rotation feedback identity differs.");
        var sources = _graph.StandingCycle!.SourceBindings.Sources;
        var sync = _pendingPrepared.Cycle.Sync;
        var phase = 0f;
        var yaw = 0f;
        for (var i = 0; i < sync.PlayerCount; i++)
        {
            var history = sync.Players[i];
            var binding = sources.Players[history.PlayerId];
            if (binding.LoopInput != (result.RotateDirection < 0 ? AlsSourceLoopInput.RotateLeft : AlsSourceLoopInput.RotateRight)) continue;
            var sample = sources.Samples[binding.SampleStart];
            if (sample.AnimationId != result.RotateAnimationId || history.SampleCount != 1)
                throw new InvalidOperationException("Standing rotation and P4 selection disagree.");
            var sampler = _footCurveSamplersByAnimation[sample.AnimationId]!;
            if (!sampler.TrySample(result.RotateCurveId, history.DeltaPrevious, out var previous) ||
                !sampler.TrySample(result.RotateCurveId, history.Time, out var current))
                throw new InvalidOperationException("Standing rotation feedback curve is missing.");
            phase = history.Time;
            // Preserve the current P4 yaw integration contract, using only the shared source interval.
            yaw = (previous + current) * .5f * history.Delta;
            break;
        }
        if (!float.IsFinite(yaw)) throw new InvalidOperationException("Standing rotation feedback is non-finite.");
        result.RotatePhase = phase;
        result.RotateYawDelta = yaw;
        result.TargetYaw = AlsMath.NormalizeAngleRadians(input.CharacterYaw + yaw);
        state.RotateInPlace = state.RotateInPlace with { Phase = phase };
    }

    internal void CompleteSourceEvents(in AlsPreparedAnimationFrame prepared, ref AlsFrameResult result)
    {
        ValidatePrepared(in prepared);
        if (_fullMovement is not null)
        {
            if (!_sourceEventsPending) throw new InvalidOperationException("Movement events were already completed.");
            _fullMovement.CompleteEvents(ref result); _sourceEventsPending = false; return;
        }
        if (_graph.StandingCycle is null) return;
        if (!_sourceEventsPending || result.Identity != _pendingSourceEvents.Identity || result.TypedEvents.Count != 0)
            throw new InvalidOperationException("P5 source events are stale, already copied, or overlap another event producer.");
        result.TypedEvents = _pendingSourceEventBuffer;
        _sourceEventsPending = false;
    }

    public void ApplyPrepared(in AlsPreparedAnimationFrame prepared)
    {
        ThrowIfDisposed();
        ValidatePrepared(in prepared);
        if (_sourceTimingPending || _sourceEventsPending)
            throw new InvalidOperationException("Complete source timing before applying the candidate pose.");
        if (_fullMovement is not null)
        {
            try { _fullMovement.Apply(); GraphAdvanceCount++; _preparedTransactionState = 2; }
            catch { try { _fullMovement.Discard(); } finally { ClearPreparedTransaction(); } throw; }
            return;
        }
        var stateChanged = prepared.AnimationState != ActiveAnimationState;
        try
        {
            if (stateChanged)
            {
                _topPlayback!.Travel(
                    _graph.Handles.StateNames[(int)prepared.AnimationState], true);
            }

            var stanceChanged = prepared.Stance != ActiveStance;
            if (prepared.AnimationState == AlsAnimationState.Grounded &&
                (stateChanged || stanceChanged))
            {
                _groundedPlayback!.Travel(
                    _graph.Handles.StanceNames[(int)prepared.Stance], true);
            }

            SetPreparedParameters(in _pendingPrepared);
            if (_graph.Handles.P4 is not null)
            {
                SetP4Parameters(
                    _graph.Handles.P4,
                    _pendingPreparedP4,
                    _pendingTurnUpdate,
                    _pendingRotateUpdate,
                    _pendingActionModeBlendAmount,
                    _pendingPrepared.CycleEnabled ? 0 : _pendingActionBlendAmount);
            }

            _graph.Tree.Advance(_pendingDeltaTime);
            GraphAdvanceCount++;
            _preparedTransactionState = 2;
        }
        catch
        {
            try
            {
                RestoreCommittedGraph();
            }
            finally
            {
                ClearPreparedTransaction();
            }
            throw;
        }
    }

    public void CommitPrepared(in AlsPreparedAnimationFrame prepared)
    {
        var commit = PrepareCommit(in prepared);
        if (!TryFinalizePreparedCommit(in commit))
        {
            throw new InvalidOperationException(
                "Prepared animation commit is stale, foreign or not awaiting commit.");
        }
    }

    internal AlsPreparedAnimationCommit PrepareCommit(
        in AlsPreparedAnimationFrame prepared)
    {
        ThrowIfDisposed();
        ValidateAppliedPrepared(in prepared);

        if (_sourceTimingPending || _sourceEventsPending)
            throw new InvalidOperationException("Unresolved source timing cannot be committed.");

        if (_pendingBaseTransitionActive == 0)
        {
            return new AlsPreparedAnimationCommit(
                prepared.OwnerId,
                prepared.Revision,
                0,
                0.0,
                0.0,
                0.0);
        }

        var playback = _pendingBaseTransitionPreviousState == AlsAnimationState.Grounded &&
            _pendingBaseTransitionTargetState == AlsAnimationState.Grounded
            ? _groundedPlayback!
            : _topPlayback!;
        return new AlsPreparedAnimationCommit(
            prepared.OwnerId,
            prepared.Revision,
            1,
            playback.GetCurrentPlayPosition(),
            playback.GetFadingFromPlayPosition(),
            playback.GetFadingPosition());
    }

    internal bool TryFinalizePreparedCommit(in AlsPreparedAnimationCommit commit)
    {
        if (commit.OwnerId != _ownerId ||
            _sourceTimingPending ||
            _sourceEventsPending ||
            _hasPreparedFrame != 1 ||
            _preparedTransactionState != 2 ||
            commit.OwnerId != _pendingDecision.OwnerId ||
            commit.Revision != _pendingDecision.Revision)
        {
            return false;
        }
        if (_fullMovement is not null)
        {
            _fullMovement.Commit(); ManualAdvanceCount++;
            ActiveAnimationState = _pendingDecision.AnimationState; ActiveStance = _pendingDecision.Stance;
            ClearPreparedTransaction(); return true;
        }
        ManualAdvanceCount++;
        ActiveAnimationState = _pendingDecision.AnimationState;
        ActiveStance = _pendingDecision.Stance;
        ActiveTurnAnimationId = _pendingP4Input.ActiveTurnAnimationId;
        ActiveRotateAnimationId = _pendingP4Input.ActiveRotateAnimationId;
        _activeP4Mode = _pendingRequestedP4Mode;
        _turnChannel = _pendingTurnUpdate.State;
        _rotateChannel = _pendingRotateUpdate.State;
        _actionBlendAmount = _pendingActionBlendAmount;
        _actionBlendTarget = _pendingActionBlendTarget;
        _actionBlendDuration = _pendingActionBlendDuration;
        _actionModeBlendAmount = _pendingActionModeBlendAmount;
        _actionModeBlendTarget = _pendingActionModeBlendTarget;
        _baseStateElapsed = _pendingBaseStateElapsed;
        _committedPrepared = _pendingPrepared;
        _committedPreparedP4 = _pendingPreparedP4;
        _committedBaseDecision = _pendingBaseDecision;
        _baseTransitionPrevious = _pendingBasePrevious;
        _baseTransitionElapsed = _pendingBaseTransitionElapsed;
        _baseTransitionActive = _pendingBaseTransitionActive;
        _baseTransitionTargetState = _pendingBaseTransitionTargetState;
        _baseTransitionTargetStance = _pendingBaseTransitionTargetStance;
        _baseTransitionPreviousState = _pendingBaseTransitionPreviousState;
        _baseTransitionPreviousStance = _pendingBaseTransitionPreviousStance;
        _baseTransitionPreviousPrepared = _pendingBaseTransitionPreviousPrepared;
        if (commit.HasBaseTransitionPlayback != 0)
        {
            _baseTransitionCurrentPlayPosition = commit.CurrentPlayPosition;
            _baseTransitionFadingPlayPosition = commit.FadingPlayPosition;
            _baseTransitionFadePosition = commit.FadePosition;
        }
        if (_pendingPreparedP4.TurnActive)
        {
            _activeTurnBlendSeconds = _pendingPreparedP4.TurnBinding.BlendSeconds;
        }
        _graph.StandingCycle?.CommitPose(_pendingPrepared.CycleEnabled);
        if (_graph.StandingCycle is not null) _sourceEvents = _pendingSourceEvents;
        ClearPreparedTransaction();
        return true;
    }

    public void RollbackPrepared(in AlsPreparedAnimationFrame prepared)
    {
        ThrowIfDisposed();
        ValidateAppliedPrepared(in prepared);
        if (_fullMovement is not null)
        {
            try { _fullMovement.Discard(); } finally { ClearPreparedTransaction(); }
            return;
        }
        try
        {
            RestoreCommittedGraph();
        }
        finally
        {
            ClearPreparedTransaction();
        }
    }

    public void DiscardPrepared(in AlsPreparedAnimationFrame prepared)
    {
        ThrowIfDisposed();
        ValidatePrepared(in prepared);
        _fullMovement?.Discard();
        ClearPreparedTransaction();
    }

    private void ClearPreparedTransaction()
    {
        _sourceEventsPending = false;
        _hasPreparedFrame = 0;
        _sourceTimingPending = false;
        _preparedTransactionState = 0;
    }
    internal void ClearAnimationOwnershipForLifecycle(in AlsActionRequest abandonedInput)
    {
        ThrowIfDisposed();
        if (_hasPreparedFrame != 0) throw new InvalidOperationException("Animation candidate is still prepared.");
        _fullMovement?.ClearAnimationOwnershipForLifecycle(abandonedInput);
    }

    private void ValidatePrepared(in AlsPreparedAnimationFrame prepared)
    {
        if (prepared.OwnerId != _ownerId ||
            _hasPreparedFrame != 1 ||
            _preparedTransactionState != 1 ||
            prepared != _pendingDecision)
        {
            throw new InvalidOperationException(
                "Prepared animation frame is stale, foreign or already applied.");
        }
    }

    private void ValidateAppliedPrepared(in AlsPreparedAnimationFrame prepared)
    {
        if (prepared.OwnerId != _ownerId ||
            _hasPreparedFrame != 1 ||
            _preparedTransactionState != 2 ||
            prepared != _pendingDecision)
        {
            throw new InvalidOperationException(
                "Prepared animation frame is stale, foreign or not awaiting commit.");
        }
    }

    private void PrepareBaseTransition(
        AlsAnimationState state,
        AlsStance stance,
        in BaseCurveDecision target,
        double deltaTime,
        out BaseCurveDecision previous,
        out float alpha)
    {
        var changed = state != ActiveAnimationState ||
            (state == AlsAnimationState.Grounded && stance != ActiveStance);
        var continuing = _baseTransitionActive != 0 &&
            state == _baseTransitionTargetState &&
            (state != AlsAnimationState.Grounded || stance == _baseTransitionTargetStance);

        if (continuing)
        {
            previous = AdvanceBaseDecision(
                in _baseTransitionPrevious,
                _baseTransitionPreviousState,
                _baseTransitionPreviousPrepared.EffectivePlayRate,
                deltaTime);
            _pendingBaseTransitionPreviousState = _baseTransitionPreviousState;
            _pendingBaseTransitionPreviousStance = _baseTransitionPreviousStance;
            _pendingBaseTransitionPreviousPrepared = _baseTransitionPreviousPrepared;
            _pendingBaseTransitionElapsed = _baseTransitionElapsed + deltaTime;
        }
        else if (changed)
        {
            previous = AdvanceBaseDecision(
                in _committedBaseDecision,
                ActiveAnimationState,
                _committedPrepared.EffectivePlayRate,
                deltaTime);
            _pendingBaseTransitionPreviousState = ActiveAnimationState;
            _pendingBaseTransitionPreviousStance = ActiveStance;
            _pendingBaseTransitionPreviousPrepared = _committedPrepared;
            _pendingBaseTransitionElapsed = deltaTime;
        }
        else
        {
            previous = target;
            _pendingBaseTransitionPreviousState = state;
            _pendingBaseTransitionPreviousStance = stance;
            _pendingBaseTransitionPreviousPrepared = _pendingPrepared;
            _pendingBaseTransitionElapsed = AlsLocomotionGraphHandles.StateTransitionSeconds;
        }

        alpha = changed || continuing
            ? Math.Clamp(
                (float)(_pendingBaseTransitionElapsed /
                    AlsLocomotionGraphHandles.StateTransitionSeconds),
                0f,
                1f)
            : 1f;
        _pendingBasePrevious = previous;
        _pendingBaseTransitionActive = alpha < 1f ? (byte)1 : (byte)0;
        _pendingBaseTransitionTargetState = state;
        _pendingBaseTransitionTargetStance = stance;
    }

    private static BaseCurveDecision AdvanceBaseDecision(
        in BaseCurveDecision decision,
        AlsAnimationState state,
        float effectivePlayRate,
        double deltaTime)
    {
        var phaseDelta = (float)(
            deltaTime * effectivePlayRate /
            AlsLocomotionGraphHandles.BaseTimelineSeconds);
        var phase = decision.PhaseNormalized + phaseDelta;
        phase = state is AlsAnimationState.Grounded or AlsAnimationState.FallLoop
            ? phase - MathF.Floor(phase)
            : Math.Clamp(phase, 0f, 1f);
        return decision with { PhaseNormalized = phase };
    }

    private BaseCurveDecision PrepareBaseCurveDecision(
        AlsAnimationState state,
        AlsStance stance,
        Vector2 blend,
        float groundedPhase,
        double deltaTime,
        out double nextStateElapsed)
    {
        if (state == AlsAnimationState.Grounded)
        {
            nextStateElapsed = 0.0;
            if (stance == AlsStance.Standing && _graph.StandingCycle is not null)
                return BaseCurveDecision.Empty(groundedPhase);
            var resolver = stance == AlsStance.Standing
                ? _standingBaseCurves
                : _crouchingBaseCurves;
            return resolver is null
                ? BaseCurveDecision.Empty(groundedPhase)
                : resolver.Resolve(blend, groundedPhase);
        }

        nextStateElapsed = state == ActiveAnimationState
            ? _baseStateElapsed + deltaTime
            : deltaTime;
        var animationId = state switch
        {
            AlsAnimationState.JumpStart => _graph.Handles.BaseCurves.JumpStartAnimationId,
            AlsAnimationState.FallLoop => _graph.Handles.BaseCurves.FallLoopAnimationId,
            AlsAnimationState.LandRecovery => _graph.Handles.BaseCurves.LandRecoveryAnimationId,
            _ => -1,
        };
        var elapsed = state == AlsAnimationState.FallLoop
            ? nextStateElapsed % AlsLocomotionGraphHandles.BaseTimelineSeconds
            : Math.Min(nextStateElapsed, AlsLocomotionGraphHandles.BaseTimelineSeconds);
        var phase = (float)(elapsed / AlsLocomotionGraphHandles.BaseTimelineSeconds);
        return new BaseCurveDecision(animationId, -1, -1, 1f, 0f, 0f, phase);
    }

    private void SampleActionBank(
        int animationId,
        float phase,
        out float left,
        out float right)
    {
        SampleAnimationCurves(animationId, phase, out left, out right);
    }

    private static float Lerp(float from, float to, float amount) =>
        from + ((to - from) * amount);

    private void SampleBaseCurves(
        in AlsPreparedAnimationFrame prepared,
        out float left,
        out float right)
    {
        SampleBaseCurves(
            prepared.BaseAnimationIdA,
            prepared.BaseAnimationIdB,
            prepared.BaseAnimationIdC,
            prepared.BaseWeightA,
            prepared.BaseWeightB,
            prepared.BaseWeightC,
            prepared.BasePhaseNormalized,
            out var targetLeft,
            out var targetRight);
        if (_pendingPrepared.CycleEnabled && _graph.StandingCycle is { } targetCycle)
        {
            targetLeft = targetCycle.Sample(_pendingPrepared.Cycle, "FootLock_L");
            targetRight = targetCycle.Sample(_pendingPrepared.Cycle, "FootLock_R");
        }
        if (prepared.BaseTransitionAlpha >= 1f)
        {
            left = targetLeft;
            right = targetRight;
            return;
        }
        SampleBaseCurves(
            prepared.PreviousBaseAnimationIdA,
            prepared.PreviousBaseAnimationIdB,
            prepared.PreviousBaseAnimationIdC,
            prepared.PreviousBaseWeightA,
            prepared.PreviousBaseWeightB,
            prepared.PreviousBaseWeightC,
            prepared.PreviousBasePhaseNormalized,
            out var previousLeft,
            out var previousRight);
        if (_pendingBaseTransitionPreviousPrepared.CycleEnabled && _graph.StandingCycle is { } previousCycle)
        {
            previousLeft = previousCycle.Sample(_pendingBaseTransitionPreviousPrepared.Cycle, "FootLock_L");
            previousRight = previousCycle.Sample(_pendingBaseTransitionPreviousPrepared.Cycle, "FootLock_R");
        }
        left = Lerp(previousLeft, targetLeft, prepared.BaseTransitionAlpha);
        right = Lerp(previousRight, targetRight, prepared.BaseTransitionAlpha);
    }

    private void SampleBaseCurves(
        int animationIdA,
        int animationIdB,
        int animationIdC,
        float weightA,
        float weightB,
        float weightC,
        float phase,
        out float left,
        out float right)
    {
        SampleAnimationCurvesNormalized(
            animationIdA, phase,
            out var leftA, out var rightA);
        SampleAnimationCurvesNormalized(
            animationIdB, phase,
            out var leftB, out var rightB);
        SampleAnimationCurvesNormalized(
            animationIdC, phase,
            out var leftC, out var rightC);
        left = (leftA * weightA) + (leftB * weightB) + (leftC * weightC);
        right = (rightA * weightA) + (rightB * weightB) + (rightC * weightC);
    }

    private void SampleAnimationCurvesNormalized(
        int animationId,
        float phaseNormalized,
        out float left,
        out float right)
    {
        var timeSeconds = (uint)animationId < (uint)_animationPlayLengths.Length
            ? phaseNormalized * _animationPlayLengths[animationId]
            : 0f;
        SampleAnimationCurves(animationId, timeSeconds, out left, out right);
    }

    private void SampleAnimationCurves(
        int animationId,
        float timeSeconds,
        out float left,
        out float right)
    {
        left = 0f;
        right = 0f;
        if ((uint)animationId >= (uint)_hasFootCurveBinding.Length ||
            _hasFootCurveBinding[animationId] == 0)
        {
            return;
        }
        var binding = _footCurveBindingsByAnimation[animationId];
        var sampler = _footCurveSamplersByAnimation[animationId]!;
        left = binding.LeftLockCurveId >= 0 &&
               sampler.TrySample(binding.LeftLockCurveId, timeSeconds, out var sampledLeft)
            ? Math.Clamp(sampledLeft, 0f, 1f)
            : binding.LeftLockDefault;
        right = binding.RightLockCurveId >= 0 &&
                sampler.TrySample(binding.RightLockCurveId, timeSeconds, out var sampledRight)
            ? Math.Clamp(sampledRight, 0f, 1f)
            : binding.RightLockDefault;
    }

    public ulong ComputePoseDigest(long frameId)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _warmupState) != WarmupReady)
        {
            throw new InvalidOperationException(
                "P3 locomotion animation controller must be warmed before pose digest computation.");
        }

        var digest = DigestOffsetBasis;
        Append(ref digest, unchecked((ulong)frameId));
        for (var index = 0; index < _poseBoneIndices.Length; index++)
        {
            var boneIndex = _poseBoneIndices[index];
            var position = _skeleton.GetBonePosePosition(boneIndex);
            var rotation = AlsPoseDigest.CanonicalizeRotation(
                _skeleton.GetBonePoseRotation(boneIndex));
            var scale = _skeleton.GetBonePoseScale(boneIndex);
            Append(ref digest, position);
            Append(ref digest, rotation);
            Append(ref digest, scale);
        }
        return digest;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _topPlayback = null;
        _groundedPlayback = null;
        _fullMovement?.Dispose();
    }

    private AnimationNodeStateMachinePlayback GetPlayback(StringName path, string label)
    {
        var playback = _graph.Tree.Get(path).As<AnimationNodeStateMachinePlayback>();
        return playback ?? throw new InvalidOperationException(
            $"P3 animation graph did not expose the {label} state-machine playback resource.");
    }

    private static float MoveBlend(float current, float target, float duration, double deltaTime)
    {
        if (current == target || duration <= 0f)
        {
            return target;
        }
        var step = (float)(deltaTime / duration);
        return current < target
            ? MathF.Min(current + step, target)
            : MathF.Max(current - step, target);
    }

    private static P4BlendChannelUpdate UpdateP4BlendChannel(
        P4BlendChannelState state,
        bool active,
        bool branchVisible,
        in AlsP4ClipBinding binding,
        float playRate,
        float phase,
        double deltaTime)
    {
        if (!active)
        {
            return P4BlendChannelUpdate.Idle(state with { Pending = false });
        }

        var next = state;
        var requestBinding = false;
        var requestBank = state.Bank;

        if (!branchVisible)
        {
            var currentBinding = next.GetBinding(next.Bank);
            requestBinding = !SameBinding(currentBinding, binding);
            requestBank = next.Bank;
            next = next
                .WithBinding(next.Bank, binding)
                .WithParameters(next.Bank, playRate, phase) with
            {
                TargetBank = next.Bank,
                BlendActive = false,
                BlendElapsed = 0.0,
                BlendDuration = binding.BlendSeconds,
                BlendAmount = next.Bank,
                Pending = false,
            };
        }
        else if (next.BlendActive)
        {
            var targetBinding = next.GetBinding(next.TargetBank);
            if (SameBinding(targetBinding, binding))
            {
                next = next
                    .WithParameters(next.TargetBank, playRate, phase) with
                {
                    Pending = false,
                };
            }
            else
            {
                next = next with
                {
                    Pending = true,
                    PendingBinding = binding,
                    PendingPlayRate = playRate,
                    PendingPhase = phase,
                };
            }
        }
        else if (SameBinding(next.GetBinding(next.Bank), binding))
        {
            next = next
                .WithParameters(next.Bank, playRate, phase) with
            {
                Pending = false,
            };
        }
        else
        {
            StartP4Blend(
                ref next,
                in binding,
                playRate,
                phase,
                ref requestBinding,
                ref requestBank);
        }

        var remainingDelta = deltaTime;
        for (var transition = 0;
             transition < 2 && next.BlendActive && remainingDelta >= 0.0;
             transition++)
        {
            var remainingBlend = Math.Max(0.0, next.BlendDuration - next.BlendElapsed);
            var consumed = Math.Min(remainingDelta, remainingBlend);
            var elapsed = Math.Min(next.BlendElapsed + consumed, next.BlendDuration);
            var alpha = (float)(elapsed / next.BlendDuration);
            next = next with
            {
                BlendElapsed = elapsed,
                BlendAmount = next.TargetBank == 1 ? alpha : 1f - alpha,
            };
            remainingDelta -= consumed;
            if (elapsed < next.BlendDuration - 1e-6)
            {
                break;
            }

            next = next with
            {
                Bank = next.TargetBank,
                BlendActive = false,
                BlendElapsed = next.BlendDuration,
                BlendAmount = next.TargetBank,
            };
            if (!next.Pending)
            {
                break;
            }

            var pendingBinding = next.PendingBinding;
            var pendingPlayRate = next.PendingPlayRate;
            var pendingPhase = next.PendingPhase;
            next = next with { Pending = false };
            StartP4Blend(
                ref next,
                in pendingBinding,
                pendingPlayRate,
                pendingPhase,
                ref requestBinding,
                ref requestBank);
            if (remainingDelta <= 0.0)
            {
                break;
            }
        }

        return new P4BlendChannelUpdate(
            next,
            requestBinding,
            requestBank);
    }

    private static void StartP4Blend(
        ref P4BlendChannelState state,
        in AlsP4ClipBinding binding,
        float playRate,
        float phase,
        ref bool requestBinding,
        ref byte requestBank)
    {
        var targetBank = (byte)(1 - state.Bank);
        requestBinding = !SameBinding(state.GetBinding(targetBank), binding);
        requestBank = targetBank;
        state = state
            .WithBinding(targetBank, binding)
            .WithParameters(targetBank, playRate, phase) with
        {
            TargetBank = targetBank,
            BlendActive = binding.BlendSeconds > 0f,
            BlendElapsed = 0.0,
            BlendDuration = binding.BlendSeconds,
            Pending = false,
        };
        if (binding.BlendSeconds <= 0f)
        {
            state = state with
            {
                Bank = targetBank,
                BlendAmount = targetBank,
            };
        }
    }

    private static bool SameBinding(
        in AlsP4ClipBinding left,
        in AlsP4ClipBinding right) => left.StateName == right.StateName;

    private static AlsP4BankTransactionDiagnostics CaptureBankDiagnostics(
        in P4BlendChannelState state) => new(
            state.Bank,
            state.TargetBank,
            state.BlendActive,
            state.BlendElapsed,
            state.BlendAmount,
            state.BankA.AnimationId,
            state.BankB.AnimationId,
            state.BankAPlayRate,
            state.BankAPhase,
            state.BankBPlayRate,
            state.BankBPhase,
            state.Pending,
            state.PendingBinding.AnimationId,
            state.PendingPlayRate,
            state.PendingPhase);

    private AlsLocomotionGraphParameterSet GetParameters(
        AlsAnimationState state,
        AlsStance stance) => state switch
        {
            AlsAnimationState.Grounded => stance == AlsStance.Standing
                ? _graph.Handles.GroundedStanding
                : _graph.Handles.GroundedCrouching,
            AlsAnimationState.JumpStart => _graph.Handles.JumpStart,
            AlsAnimationState.FallLoop => _graph.Handles.FallLoop,
            AlsAnimationState.LandRecovery => _graph.Handles.LandRecovery,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private void SetParameters(
        AlsLocomotionGraphParameterSet parameters,
        Vector2 blend,
        float playRate,
        Vector2 lean,
        float leanAmount,
        float phase)
    {
        if (parameters.BlendPositionPath is not null)
        {
            _graph.Tree.Set(
                parameters.BlendPositionPath,
                blend);
        }
        _graph.Tree.Set(parameters.LeanPositionPath, lean);
        _graph.Tree.Set(parameters.LeanAmountPath, leanAmount);
        _graph.Tree.Set(parameters.PlayRatePath, playRate);
        if (parameters.PhasePath is not null)
        {
            _graph.Tree.Set(parameters.PhasePath, Math.Clamp(phase, 0f, 1f));
        }
    }

    private void SetPreparedParameters(in PreparedApply prepared)
    {
        if (prepared.CycleEnabled) _graph.StandingCycle!.Apply(_graph.Tree, prepared.Cycle);
        SetParameters(
        prepared.Parameters,
        prepared.Blend,
        prepared.EffectivePlayRate,
        prepared.Lean,
        prepared.LeanAmount,
        prepared.Phase);
    }

    private void RestoreCommittedGraph()
    {
        // Also restore the derived pose when the committed branch has not entered Cycle yet.
        _graph.StandingCycle?.RestorePose();
        if (_baseTransitionActive != 0)
        {
            var transitionElapsed = _baseTransitionFadePosition;
            _topPlayback!.Start(
                _graph.Handles.StateNames[(int)_baseTransitionPreviousState], true);
            _groundedPlayback!.Start(
                _graph.Handles.StanceNames[(int)_baseTransitionPreviousStance], true);
            var sourcePrepared = _baseTransitionPreviousPrepared with
            {
                Phase = RewindBasePhase(
                    _baseTransitionFadingPlayPosition,
                    _baseTransitionPreviousState,
                    _baseTransitionPreviousPrepared.EffectivePlayRate,
                    transitionElapsed),
            };
            SetPreparedParameters(in sourcePrepared);
            RestoreCommittedP4Parameters();
            _graph.Tree.Advance(0.0);

            if (_baseTransitionTargetState != _baseTransitionPreviousState)
            {
                _topPlayback.Travel(
                    _graph.Handles.StateNames[(int)_baseTransitionTargetState], true);
            }
            if (_baseTransitionTargetState == AlsAnimationState.Grounded &&
                _baseTransitionTargetStance != _baseTransitionPreviousStance)
            {
                _groundedPlayback.Travel(
                    _graph.Handles.StanceNames[(int)_baseTransitionTargetStance], true);
            }
            var targetStartPrepared = _committedPrepared with
            {
                Phase = RewindBasePhase(
                    _baseTransitionCurrentPlayPosition,
                    _baseTransitionTargetState,
                    _committedPrepared.EffectivePlayRate,
                    transitionElapsed),
            };
            SetPreparedParameters(in targetStartPrepared);
            RestoreCommittedP4Parameters();
            _graph.Tree.Advance(0.0);

            var targetPrepared = _committedPrepared with
            {
                Phase = (float)_baseTransitionCurrentPlayPosition,
            };
            SetPreparedParameters(in targetPrepared);
            RestoreCommittedP4Parameters();
            _graph.Tree.Advance(transitionElapsed);
            return;
        }

        _topPlayback!.Start(
            _graph.Handles.StateNames[(int)ActiveAnimationState], true);
        _groundedPlayback!.Start(
            _graph.Handles.StanceNames[(int)ActiveStance], true);
        SetPreparedParameters(in _committedPrepared);
        RestoreCommittedP4Parameters();
        _graph.Tree.Advance(0.0);
    }

    private static float RewindBasePhase(
        double phase,
        AlsAnimationState state,
        float effectivePlayRate,
        double elapsed)
    {
        var value = (float)(phase -
            (elapsed * effectivePlayRate /
             AlsLocomotionGraphHandles.BaseTimelineSeconds));
        if (state is AlsAnimationState.Grounded or AlsAnimationState.FallLoop)
        {
            return value - MathF.Floor(value);
        }
        return Math.Clamp(value, 0f, 1f);
    }

    private void RestoreCommittedP4Parameters()
    {
        var handles = _graph.Handles.P4;
        if (handles is null)
        {
            return;
        }
        for (byte bank = 0; bank < 2; bank++)
        {
            _graph.Tree.Set(
                handles.GetTurnRequestPath(bank),
                _turnChannel.GetBinding(bank).StateName);
            _graph.Tree.Set(
                handles.GetRotateRequestPath(bank),
                _rotateChannel.GetBinding(bank).StateName);
        }
        var turn = P4BlendChannelUpdate.Idle(_turnChannel);
        var rotate = P4BlendChannelUpdate.Idle(_rotateChannel);
        SetP4Parameters(
            handles,
            _committedPreparedP4,
            turn,
            rotate,
            _actionModeBlendAmount,
            _committedPrepared.CycleEnabled ? 0 : _actionBlendAmount);
    }

    private void SetP4Parameters(
        AlsP4GraphHandles handles,
        in PreparedP4 prepared,
        in P4BlendChannelUpdate turn,
        in P4BlendChannelUpdate rotate,
        float actionModeBlendAmount,
        float actionBlendAmount)
    {
        if (turn.RequestBinding)
        {
            _graph.Tree.Set(
                handles.GetTurnRequestPath(turn.RequestBank),
                turn.State.GetBinding(turn.RequestBank).StateName);
        }
        if (rotate.RequestBinding)
        {
            _graph.Tree.Set(
                handles.GetRotateRequestPath(rotate.RequestBank),
                rotate.State.GetBinding(rotate.RequestBank).StateName);
        }
        for (byte bank = 0; bank < 2; bank++)
        {
            var binding = turn.State.GetBinding(bank);
            _graph.Tree.Set(
                binding.GetPlayRatePath(bank), turn.State.GetPlayRate(bank));
            _graph.Tree.Set(
                binding.GetPhasePath(bank), turn.State.GetPhase(bank));
            binding = rotate.State.GetBinding(bank);
            _graph.Tree.Set(
                binding.GetPlayRatePath(bank), rotate.State.GetPlayRate(bank));
            _graph.Tree.Set(
                binding.GetPhasePath(bank), rotate.State.GetPhase(bank));
        }
        _graph.Tree.Set(handles.TurnBlendPath, turn.State.BlendAmount);
        _graph.Tree.Set(handles.RotateBlendPath, rotate.State.BlendAmount);
        _graph.Tree.Set(handles.ActionModeBlendPath, actionModeBlendAmount);
        _graph.Tree.Set(handles.ActionBlendPath, actionBlendAmount);
        _graph.Tree.Set(handles.AimDownPhasePath, prepared.AimDownPhase);
        _graph.Tree.Set(handles.AimDownWeightPath, prepared.AimDownWeight);
        _graph.Tree.Set(handles.AimForwardPhasePath, prepared.AimForwardPhase);
        _graph.Tree.Set(handles.AimForwardWeightPath, prepared.AimForwardWeight);
        _graph.Tree.Set(handles.AimUpPhasePath, prepared.AimUpPhase);
        _graph.Tree.Set(handles.AimUpWeightPath, prepared.AimUpWeight);
    }

    private Vector2 MapBlendPosition(in AlsFrameResult result, float hipBias,
        double deltaTime, out Vector2 direction)
    {
        direction = Vector2.Zero;
        var right = (double)result.BlendCoordinates.X;
        var forward = (double)result.BlendCoordinates.Y;
        var magnitude = Math.Sqrt((right * right) + (forward * forward));
        if (magnitude <= 1e-6)
        {
            return Vector2.Zero;
        }

        // LF/LB and RF/RB are lateral cycles with opposite hip orientations,
        // not four diagonal directions. Select their hemisphere before projection.
        if (result.ActualStance == AlsStance.Standing && result.ActualGait != AlsGait.Sprinting)
        {
            forward += Math.Abs(right) * hipBias;
            magnitude = Math.Sqrt((right * right) + (forward * forward));
        }

        var ring = result.ActualStance == AlsStance.Crouching
            ? _crouchingBlendRing
            : _standingBlendRings[(int)result.ActualGait];
        if (ring.Length == 1) return ring[0] * result.Stride;
        direction = new Vector2(
            (float)(right / magnitude), (float)(forward / magnitude));
        if (result.AnimationState == AlsAnimationState.Grounded &&
            result.ActualStance == AlsStance.Standing && deltaTime > 0.0 &&
            _committedPrepared.Direction.LengthSquared() > 0.5f)
        {
            // Bound pose-direction changes, then project back onto this gait's polygon.
            // Interpolating coordinates directly would mix slower gait rings on the chord.
            var previousAngle = _committedPrepared.Direction.Angle();
            var difference = Mathf.Wrap(direction.Angle() - previousAngle, -Mathf.Pi, Mathf.Pi);
            var maximumStep = (float)Math.Min(deltaTime * MaximumPoseDirectionRadiansPerSecond, Math.PI);
            direction = Vector2.FromAngle(previousAngle + Math.Clamp(difference, -maximumStep, maximumStep));
        }
        // A circular radius can leave the selected gait polygon and blend in faster clips.
        for (var index = 0; index < ring.Length; index++)
        {
            var start = ring[index];
            var edge = ring[(index + 1) % ring.Length] - start;
            var denominator = direction.Cross(edge);
            if (MathF.Abs(denominator) <= 1e-8f) continue;
            var distance = start.Cross(edge) / denominator;
            var along = start.Cross(direction) / denominator;
            if (distance >= 0f && along >= -1e-6f && along <= 1f + 1e-6f)
                // Preserve the idle center as speed approaches zero, including direction reversals.
                return start.Lerp(start + edge, Math.Clamp(along, 0f, 1f)) * result.Stride;
        }
        throw new InvalidOperationException("Locomotion direction has no selected gait polygon intersection.");
    }

    private static Vector2[] BuildBlendRing(AlsLocomotionAnimationSample[] samples, float radius)
    {
        var ring = samples.Select(value => new Vector2(value.X, value.Y))
            .Where(value => MathF.Abs(value.Length() - radius) <= 1e-4f)
            .OrderBy(value => MathF.Atan2(value.Y, value.X)).ToArray();
        if (ring.Length == 0) throw new InvalidOperationException("Locomotion gait ring is empty.");
        return ring;
    }

    private static Vector2 Clamp(Vector2 value, Vector2 minimum, Vector2 maximum) => new(
        Math.Clamp(value.X, minimum.X, maximum.X),
        Math.Clamp(value.Y, minimum.Y, maximum.Y));

    private PreparedApply Prepare(in AlsFrameResult result, double deltaTime, AlsStandingMovementInput? movementInput,
        AlsStandingTurnSlotInput turnSlot)
    {
        if (!double.IsFinite(deltaTime) || deltaTime < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        }
        if ((uint)result.AnimationState > (uint)AlsAnimationState.LandRecovery)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "Animation state is invalid.");
        }
        if ((uint)result.ActualStance > (uint)AlsStance.Crouching)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "Stance is invalid.");
        }
        if ((uint)result.ActualGait > (uint)AlsGait.Sprinting)
        {
            throw new ArgumentOutOfRangeException(nameof(result), "Gait is invalid.");
        }
        var pendingSourceTiming = AlsLocomotionModel.HasPendingSourceTiming(result);
        var sourceBranch = _graph.StandingCycle is not null && result.AnimationState == AlsAnimationState.Grounded &&
            result.ActualStance == AlsStance.Standing;
        if (pendingSourceTiming && !sourceBranch)
            throw new ArgumentException("Pending source timing requires the standing source graph.", nameof(result));
        if (!float.IsFinite(result.BlendCoordinates.X) ||
            !float.IsFinite(result.BlendCoordinates.Y) ||
            (!pendingSourceTiming && (!float.IsFinite(result.Stride) ||
            result.Stride < 0f || (!sourceBranch && result.Stride > 1f) ||
            !float.IsFinite(result.PlayRate) ||
            result.PlayRate < 0f || (!sourceBranch && result.PlayRate == 0f) ||
            result.PlayRate > _playRateMaximum ||
            !float.IsFinite(result.AnimationPhase))) ||
            !float.IsFinite(result.Lean.X) ||
            !float.IsFinite(result.Lean.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(result),
                $"P3 animation parameters must be finite and within the fixed settings range; " +
                $"playRateMaximum={_playRateMaximum:R}.");
        }
        if (result.AnimationState == AlsAnimationState.LandRecovery &&
            result.ResolvedLocomotionState != AlsLocomotionState.Grounded)
        {
            throw new ArgumentException(
                "LandRecovery requires a physically Grounded locomotion result.", nameof(result));
        }

        var parameters = GetParameters(result.AnimationState, result.ActualStance);
        if (_graph.StandingCycle is { } standingCycle && result.AnimationState == AlsAnimationState.Grounded &&
            result.ActualStance == AlsStance.Standing)
        {
            if (movementInput is not { } movement)
                throw new ArgumentException("Standing source graph requires explicit character movement input.", nameof(movementInput));
            var cycle = standingCycle.Prepare(_committedPrepared.Cycle, result, (float)deltaTime, _animatedStandingSpeeds, movement,
                includeDetail: true, turnSlot: turnSlot);
            var cycleLean = Clamp(new Vector2(result.Lean.X, result.Lean.Y), parameters.LeanMinimum, parameters.LeanMaximum);
            return new PreparedApply(parameters, Vector2.Zero, 0, cycleLean,
                cycleLean.LengthSquared() > 1e-12f ? 1f : 0f, cycle.State.Phase,
                CycleEnabled: true, Cycle: cycle, SourceIdentity: result.Identity);
        }
        var backwardHips = _committedPrepared.BackwardHips;
        var hipBias = _committedPrepared.HipBias;
        if (result.AnimationState == AlsAnimationState.Grounded &&
            result.ActualStance == AlsStance.Standing && result.ActualGait != AlsGait.Sprinting)
        {
            var length = result.BlendCoordinates.Length();
            if (length > 1e-6f)
            {
                var forwardFraction = result.BlendCoordinates.Y / length;
                // Retain the selected hip variant in the lateral band (70..110 degrees).
                if (forwardFraction < -HipHemisphereForwardThreshold) backwardHips = true;
                else if (forwardFraction > HipHemisphereForwardThreshold) backwardHips = false;
                hipBias = Mathf.MoveToward(hipBias, backwardHips ? -1f : 1f,
                    (float)Math.Min(deltaTime * 2.0 / HipVariantBlendSeconds, 2.0));
            }
        }
        var blend = MapBlendPosition(result, hipBias, deltaTime, out var direction);
        if (!float.IsFinite(blend.X) || !float.IsFinite(blend.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(result), "Derived blend position is not finite.");
        }
        blend = Clamp(blend, parameters.BlendMinimum, parameters.BlendMaximum);

        var effectivePlayRateValue = result.AnimationState == AlsAnimationState.Grounded
            ? (double)result.PlayRate * result.Stride
            : 1d;
        if (!double.IsFinite(effectivePlayRateValue) ||
            effectivePlayRateValue < 0d ||
            effectivePlayRateValue > _playRateMaximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(result),
                "Derived animation time scale is outside the fixed settings range.");
        }
        var effectivePlayRate = (float)effectivePlayRateValue;
        if (!float.IsFinite(effectivePlayRate))
        {
            throw new ArgumentOutOfRangeException(
                nameof(result),
                "Derived animation time scale is not finite.");
        }

        var lean = Clamp(
            new Vector2(result.Lean.X, result.Lean.Y),
            parameters.LeanMinimum,
            parameters.LeanMaximum);
        var leanLengthSquared = ((double)lean.X * lean.X) + ((double)lean.Y * lean.Y);
        var leanAmount = leanLengthSquared > 1e-12d ? 1f : 0f;
        var phase = Math.Clamp(result.AnimationPhase, 0f, 1f);
        return new PreparedApply(
            parameters,
            blend,
            effectivePlayRate,
            lean,
            leanAmount,
            phase,
            backwardHips,
            hipBias,
            direction,
            Cycle: _graph.StandingCycle is { } inactiveCycle
                ? inactiveCycle.AdvanceInactiveFeedback(_committedPrepared.Cycle, (float)deltaTime) : _committedPrepared.Cycle);
    }

    private PreparedP4 PrepareP4(in AlsP4AnimationInput input)
    {
        var turnActive = input.ActiveTurnAnimationId >= 0;
        var rotateActive = input.ActiveRotateAnimationId >= 0;
        if (turnActive && rotateActive)
        {
            throw new ArgumentException("P4 Turn and Rotate inputs are mutually exclusive.", nameof(input));
        }
        if (input.ActiveTurnAnimationId < -1 || input.ActiveRotateAnimationId < -1 ||
            !IsPositiveRate(input.TurnPlayRate) ||
            !IsPositiveRate(input.RotatePlayRate) ||
            !IsNonNegativeFinite(input.TurnPhase) ||
            !IsNonNegativeFinite(input.RotatePhase) ||
            !IsUnit(input.AimDownPhase) ||
            !IsUnit(input.AimForwardPhase) ||
            !IsUnit(input.AimUpPhase) ||
            !IsUnit(input.AimDownWeight) ||
            !IsUnit(input.AimForwardWeight) ||
            !IsUnit(input.AimUpWeight))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), "P4 rates, phases, weights and inactive IDs are outside their fixed range.");
        }

        var handles = _graph.Handles.P4;
        if (handles is null)
        {
            if (turnActive || rotateActive)
            {
                throw new InvalidOperationException("This animation graph was built without a P4 profile.");
            }
            return PreparedP4.Disabled;
        }

        var turnBinding = default(AlsP4ClipBinding);
        if (turnActive &&
            (!handles.TryGetTurnBinding(input.ActiveTurnAnimationId, out turnBinding) ||
             input.TurnPhase > turnBinding.DurationSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                $"Unknown P4 Turn ID or phase outside [0,duration]: {input.ActiveTurnAnimationId}");
        }
        var rotateBinding = default(AlsP4ClipBinding);
        if (rotateActive &&
            (!handles.TryGetRotateBinding(input.ActiveRotateAnimationId, out rotateBinding) ||
             input.RotatePhase >= rotateBinding.DurationSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(input),
                $"Unknown P4 Rotate ID or phase outside [0,duration): {input.ActiveRotateAnimationId}");
        }

        return new PreparedP4(
            turnActive,
            rotateActive,
            turnBinding,
            rotateBinding,
            input.TurnPlayRate,
            input.RotatePlayRate,
            input.TurnPhase,
            input.RotatePhase,
            input.AimDownPhase,
            input.AimForwardPhase,
            input.AimUpPhase,
            input.AimDownWeight,
            input.AimForwardWeight,
            input.AimUpWeight);

        bool IsPositiveRate(float value) =>
            float.IsFinite(value) && value > 0f && value <= _playRateMaximum;
        static bool IsNonNegativeFinite(float value) =>
            float.IsFinite(value) && value >= 0f;
        static bool IsUnit(float value) =>
            float.IsFinite(value) && value >= 0f && value <= 1f;
    }

    private static void Append(ref ulong digest, Vector3 value)
    {
        Append(ref digest, Quantize(value.X));
        Append(ref digest, Quantize(value.Y));
        Append(ref digest, Quantize(value.Z));
    }

    private static void Append(ref ulong digest, Quaternion value)
    {
        Append(ref digest, Quantize(value.X));
        Append(ref digest, Quantize(value.Y));
        Append(ref digest, Quantize(value.Z));
        Append(ref digest, Quantize(value.W));
    }

    private static ulong Quantize(float value) =>
        unchecked((ulong)(long)MathF.Round(
            value * QuantizationScale,
            MidpointRounding.AwayFromZero));

    private static void Append(ref ulong digest, ulong value)
    {
        for (var shift = 0; shift < 64; shift += 8)
        {
            digest ^= (byte)(value >> shift);
            digest *= DigestPrime;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private readonly record struct P4BlendChannelState(
        byte Bank,
        byte TargetBank,
        bool BlendActive,
        double BlendElapsed,
        float BlendDuration,
        float BlendAmount,
        AlsP4ClipBinding BankA,
        AlsP4ClipBinding BankB,
        float BankAPlayRate,
        float BankAPhase,
        float BankBPlayRate,
        float BankBPhase,
        bool Pending,
        AlsP4ClipBinding PendingBinding,
        float PendingPlayRate,
        float PendingPhase)
    {
        public static P4BlendChannelState Initial(in AlsP4ClipBinding binding) =>
            new(0, 0, false, 0.0, binding.BlendSeconds, 0f,
                binding, binding, 1f, 0f, 1f, 0f, false, default, 1f, 0f);

        public AlsP4ClipBinding GetBinding(byte bank) => bank == 0 ? BankA : BankB;

        public P4BlendChannelState WithBinding(byte bank, in AlsP4ClipBinding binding) =>
            bank == 0 ? this with { BankA = binding } : this with { BankB = binding };

        public P4BlendChannelState WithParameters(byte bank, float playRate, float phase) =>
            bank == 0
                ? this with { BankAPlayRate = playRate, BankAPhase = phase }
                : this with { BankBPlayRate = playRate, BankBPhase = phase };

        public float GetPlayRate(byte bank) => bank == 0 ? BankAPlayRate : BankBPlayRate;

        public float GetPhase(byte bank) => bank == 0 ? BankAPhase : BankBPhase;
    }

    private readonly record struct P4BlendChannelUpdate(
        P4BlendChannelState State,
        bool RequestBinding,
        byte RequestBank)
    {
        public static P4BlendChannelUpdate Idle(in P4BlendChannelState state) =>
            new(state, false, state.Bank);
    }

    private readonly record struct PreparedApply(
        AlsLocomotionGraphParameterSet Parameters,
        Vector2 Blend,
        float EffectivePlayRate,
        Vector2 Lean,
        float LeanAmount,
        float Phase,
        bool BackwardHips = false,
        float HipBias = 1f,
        Vector2 Direction = default,
        bool CycleEnabled = false,
        AlsStandingCycleFrame Cycle = default,
        AlsFrameIdentity SourceIdentity = default);

    private readonly record struct BaseCurveDecision(
        int AnimationIdA,
        int AnimationIdB,
        int AnimationIdC,
        float WeightA,
        float WeightB,
        float WeightC,
        float PhaseNormalized)
    {
        public static BaseCurveDecision Empty(float phase) =>
            new(-1, -1, -1, 0f, 0f, 0f, phase);
    }

    private sealed class BaseCurveBlendResolver
    {
        private const float CoordinateEpsilon = 1e-5f;
        private readonly AlsLocomotionAnimationSample[] _samples;
        private readonly int[] _triangles;

        public BaseCurveBlendResolver(AlsLocomotionAnimationSample[] samples)
        {
            _samples = samples?.ToArray() ?? throw new ArgumentNullException(nameof(samples));
            var points = new Vector2[_samples.Length];
            for (var index = 0; index < _samples.Length; index++)
            {
                points[index] = new Vector2(_samples[index].X, _samples[index].Y);
            }
            _triangles = Geometry2D.TriangulateDelaunay(points);
            if (_triangles.Length < 3)
            {
                throw new ArgumentException(
                    "Base animation curve samples do not form a blend-space triangle.",
                    nameof(samples));
            }
        }

        public BaseCurveDecision Resolve(Vector2 point, float phase)
        {
            for (var index = 0; index < _samples.Length; index++)
            {
                var deltaX = point.X - _samples[index].X;
                var deltaY = point.Y - _samples[index].Y;
                if ((deltaX * deltaX) + (deltaY * deltaY) <=
                    CoordinateEpsilon * CoordinateEpsilon)
                {
                    return Single(index, phase);
                }
            }

            for (var triangle = 0; triangle < _triangles.Length; triangle += 3)
            {
                var a = _triangles[triangle];
                var b = _triangles[triangle + 1];
                var c = _triangles[triangle + 2];
                if (TryBarycentric(point, a, b, c, out var wa, out var wb, out var wc) &&
                    wa >= -CoordinateEpsilon &&
                    wb >= -CoordinateEpsilon &&
                    wc >= -CoordinateEpsilon)
                {
                    wa = MathF.Max(0f, wa);
                    wb = MathF.Max(0f, wb);
                    wc = MathF.Max(0f, wc);
                    var total = wa + wb + wc;
                    return Triangle(a, b, c, wa / total, wb / total, wc / total, phase);
                }
            }

            var closestDistance = float.PositiveInfinity;
            var closestA = 0;
            var closestB = 0;
            var closestAmount = 0f;
            for (var triangle = 0; triangle < _triangles.Length; triangle += 3)
            {
                CheckEdge(_triangles[triangle], _triangles[triangle + 1]);
                CheckEdge(_triangles[triangle + 1], _triangles[triangle + 2]);
                CheckEdge(_triangles[triangle + 2], _triangles[triangle]);
            }
            return new BaseCurveDecision(
                _samples[closestA].AnimationId,
                _samples[closestB].AnimationId,
                -1,
                1f - closestAmount,
                closestAmount,
                0f,
                phase);

            void CheckEdge(int a, int b)
            {
                var start = new Vector2(_samples[a].X, _samples[a].Y);
                var end = new Vector2(_samples[b].X, _samples[b].Y);
                var edge = end - start;
                var lengthSquared = edge.LengthSquared();
                var amount = lengthSquared > 0f
                    ? Math.Clamp((point - start).Dot(edge) / lengthSquared, 0f, 1f)
                    : 0f;
                var projected = start + (edge * amount);
                var distance = point.DistanceSquaredTo(projected);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestA = a;
                    closestB = b;
                    closestAmount = amount;
                }
            }
        }

        private BaseCurveDecision Single(int index, float phase) => new(
            _samples[index].AnimationId, -1, -1, 1f, 0f, 0f, phase);

        private BaseCurveDecision Triangle(
            int a,
            int b,
            int c,
            float wa,
            float wb,
            float wc,
            float phase) => new(
                _samples[a].AnimationId,
                _samples[b].AnimationId,
                _samples[c].AnimationId,
                wa,
                wb,
                wc,
                phase);

        private bool TryBarycentric(
            Vector2 point,
            int a,
            int b,
            int c,
            out float wa,
            out float wb,
            out float wc)
        {
            var pa = new Vector2(_samples[a].X, _samples[a].Y);
            var pb = new Vector2(_samples[b].X, _samples[b].Y);
            var pc = new Vector2(_samples[c].X, _samples[c].Y);
            var denominator = ((pb.Y - pc.Y) * (pa.X - pc.X)) +
                              ((pc.X - pb.X) * (pa.Y - pc.Y));
            if (MathF.Abs(denominator) <= CoordinateEpsilon)
            {
                wa = wb = wc = 0f;
                return false;
            }
            wa = (((pb.Y - pc.Y) * (point.X - pc.X)) +
                  ((pc.X - pb.X) * (point.Y - pc.Y))) / denominator;
            wb = (((pc.Y - pa.Y) * (point.X - pc.X)) +
                  ((pa.X - pc.X) * (point.Y - pc.Y))) / denominator;
            wc = 1f - wa - wb;
            return true;
        }
    }

    private readonly record struct PreparedP4(
        bool TurnActive,
        bool RotateActive,
        AlsP4ClipBinding TurnBinding,
        AlsP4ClipBinding RotateBinding,
        float TurnPlayRate,
        float RotatePlayRate,
        float TurnPhase,
        float RotatePhase,
        float AimDownPhase,
        float AimForwardPhase,
        float AimUpPhase,
        float AimDownWeight,
        float AimForwardWeight,
        float AimUpWeight)
    {
        public static readonly PreparedP4 Disabled = new(
            false, false, default, default, 1f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
    }
}
