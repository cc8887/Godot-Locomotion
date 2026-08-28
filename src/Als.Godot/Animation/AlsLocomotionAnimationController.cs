using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
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
    float ActionBlendAmount);

public readonly record struct AlsFootCurveSample(
    float LeftIkWeight,
    float RightIkWeight,
    float LeftLockCurve,
    float RightLockCurve);

public sealed class AlsLocomotionAnimationController : IDisposable
{
    private const int WarmupUninitialized = 0;
    private const int WarmupInitializing = 1;
    private const int WarmupReady = 2;
    private const ulong DigestOffsetBasis = 14695981039346656037UL;
    private const ulong DigestPrime = 1099511628211UL;
    private const float QuantizationScale = 100_000f;
    private static long _nextOwnerId;

    private static readonly string[] PoseBoneNames =
    [
        "pelvis", "spine_03", "hand_l", "hand_r", "foot_l", "foot_r",
    ];

    private readonly AlsLocomotionGraphBuildResult _graph;
    private readonly Skeleton3D _skeleton;
    private readonly float _playRateMaximum;
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
    private double _baseStateElapsed;
    private double _pendingBaseStateElapsed;

    public AlsLocomotionAnimationController(
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _skeleton = graph.TargetSkeleton;
        ArgumentNullException.ThrowIfNull(settings);
        _playRateMaximum = settings.PlayRateMaximum;
    }

    public AlsLocomotionAnimationController(
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings,
        AlsPoseAnimationProfile poseProfile,
        AlsAnimationSetDefinition animationSet)
        : this(graph, settings)
    {
        ArgumentNullException.ThrowIfNull(poseProfile);
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

    public int ActiveTurnAnimationId { get; private set; } = -1;

    public int ActiveRotateAnimationId { get; private set; } = -1;

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

    public void Apply(in AlsFrameResult result, double deltaTime)
    {
        var p4 = AlsP4AnimationInput.Disabled;
        Apply(in result, in p4, deltaTime);
    }

    public void Apply(
        in AlsFrameResult result,
        in AlsP4AnimationInput p4Input,
        double deltaTime)
    {
        var prepared = PrepareFrame(in result, in p4Input, deltaTime);
        ApplyPrepared(in prepared);
    }

    public AlsPreparedAnimationFrame PrepareFrame(
        in AlsFrameResult result,
        in AlsP4AnimationInput p4Input,
        double deltaTime)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _warmupState) != WarmupReady)
        {
            throw new InvalidOperationException(
                "P3 locomotion animation controller must be warmed before PrepareFrame().");
        }
        var prepared = Prepare(result, deltaTime);
        var preparedP4 = PrepareP4(in p4Input);

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

        var baseDecision = PrepareBaseCurveDecision(
            result.AnimationState,
            result.ActualStance,
            prepared.Blend,
            prepared.Phase,
            deltaTime,
            out var nextBaseStateElapsed);

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
            nextActionBlendAmount);
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
        _pendingBaseStateElapsed = nextBaseStateElapsed;
        _preparedRevision = revision;
        _hasPreparedFrame = 1;
        return decision;
    }

    public AlsFootCurveSample SampleFootCurves(
        in AlsPreparedAnimationFrame prepared)
    {
        ThrowIfDisposed();
        ValidatePrepared(in prepared);
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

    public void ApplyPrepared(in AlsPreparedAnimationFrame prepared)
    {
        ThrowIfDisposed();
        ValidatePrepared(in prepared);
        _hasPreparedFrame = 0;
        var stateChanged = prepared.AnimationState != ActiveAnimationState;
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

        SetParameters(
            _pendingPrepared.Parameters,
            _pendingPrepared.Blend,
            _pendingPrepared.EffectivePlayRate,
            _pendingPrepared.Lean,
            _pendingPrepared.LeanAmount,
            _pendingPrepared.Phase);
        if (_graph.Handles.P4 is not null)
        {
            SetP4Parameters(
                _graph.Handles.P4,
                _pendingPreparedP4,
                _pendingTurnUpdate,
                _pendingRotateUpdate,
                _pendingActionModeBlendAmount,
                _pendingActionBlendAmount);
        }

        _graph.Tree.Advance(_pendingDeltaTime);
        ManualAdvanceCount++;
        ActiveAnimationState = prepared.AnimationState;
        ActiveStance = prepared.Stance;
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
        if (_pendingPreparedP4.TurnActive)
        {
            _activeTurnBlendSeconds = _pendingPreparedP4.TurnBinding.BlendSeconds;
        }
    }

    private void ValidatePrepared(in AlsPreparedAnimationFrame prepared)
    {
        if (prepared.OwnerId != _ownerId ||
            _hasPreparedFrame != 1 ||
            prepared != _pendingDecision)
        {
            throw new InvalidOperationException(
                "Prepared animation frame is stale, foreign or already applied.");
        }
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
        var playLength = (uint)animationId < (uint)_animationPlayLengths.Length
            ? _animationPlayLengths[animationId]
            : 0f;
        var phase = 0f;
        if (playLength > 0f)
        {
            var elapsed = state == AlsAnimationState.FallLoop
                ? nextStateElapsed % playLength
                : Math.Min(nextStateElapsed, playLength);
            phase = (float)(elapsed / playLength);
        }
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
        SampleAnimationCurvesNormalized(
            prepared.BaseAnimationIdA, prepared.BasePhaseNormalized,
            out var leftA, out var rightA);
        SampleAnimationCurvesNormalized(
            prepared.BaseAnimationIdB, prepared.BasePhaseNormalized,
            out var leftB, out var rightB);
        SampleAnimationCurvesNormalized(
            prepared.BaseAnimationIdC, prepared.BasePhaseNormalized,
            out var leftC, out var rightC);
        left = (leftA * prepared.BaseWeightA) +
               (leftB * prepared.BaseWeightB) +
               (leftC * prepared.BaseWeightC);
        right = (rightA * prepared.BaseWeightA) +
                (rightB * prepared.BaseWeightB) +
                (rightC * prepared.BaseWeightC);
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

    private Vector2 MapBlendPosition(in AlsFrameResult result)
    {
        var right = (double)result.BlendCoordinates.X;
        var forward = (double)result.BlendCoordinates.Y;
        var magnitude = Math.Sqrt((right * right) + (forward * forward));
        if (magnitude <= 1e-6)
        {
            return Vector2.Zero;
        }

        var radius = result.ActualStance == AlsStance.Crouching
            ? _graph.Handles.CrouchingRadius
            : _graph.Handles.StandingGaitRadii[(int)result.ActualGait];
        return new Vector2(
            (float)((right / magnitude) * radius),
            (float)((forward / magnitude) * radius));
    }

    private static Vector2 Clamp(Vector2 value, Vector2 minimum, Vector2 maximum) => new(
        Math.Clamp(value.X, minimum.X, maximum.X),
        Math.Clamp(value.Y, minimum.Y, maximum.Y));

    private PreparedApply Prepare(in AlsFrameResult result, double deltaTime)
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
        if (!float.IsFinite(result.BlendCoordinates.X) ||
            !float.IsFinite(result.BlendCoordinates.Y) ||
            !float.IsFinite(result.Stride) ||
            result.Stride < 0f || result.Stride > 1f ||
            !float.IsFinite(result.PlayRate) ||
            result.PlayRate <= 0f ||
            result.PlayRate > _playRateMaximum ||
            !float.IsFinite(result.Lean.X) ||
            !float.IsFinite(result.Lean.Y) ||
            !float.IsFinite(result.AnimationPhase))
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
        var blend = MapBlendPosition(result);
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
            phase);
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
        float Phase);

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
