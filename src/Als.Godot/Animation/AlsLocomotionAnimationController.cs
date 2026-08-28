using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

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

public sealed class AlsLocomotionAnimationController : IDisposable
{
    private const int WarmupUninitialized = 0;
    private const int WarmupInitializing = 1;
    private const int WarmupReady = 2;
    private const ulong DigestOffsetBasis = 14695981039346656037UL;
    private const ulong DigestPrime = 1099511628211UL;
    private const float QuantizationScale = 100_000f;

    private static readonly string[] PoseBoneNames =
    [
        "pelvis", "spine_03", "hand_l", "hand_r", "foot_l", "foot_r",
    ];

    private readonly AlsLocomotionGraphBuildResult _graph;
    private readonly Skeleton3D _skeleton;
    private readonly float _playRateMaximum;
    private readonly int[] _poseBoneIndices = new int[PoseBoneNames.Length];
    private AnimationNodeStateMachinePlayback? _topPlayback;
    private AnimationNodeStateMachinePlayback? _groundedPlayback;
    private int _warmupState;
    private int _disposed;
    private byte _activeP4Mode;
    private float _activeTurnBlendSeconds;
    private byte _turnBank;
    private byte _rotateBank;
    private byte _turnTargetBank;
    private byte _rotateTargetBank;
    private bool _turnBlendActive;
    private bool _rotateBlendActive;
    private double _turnBlendElapsed;
    private double _rotateBlendElapsed;
    private float _turnBlendDuration;
    private float _rotateBlendDuration;
    private float _actionBlendAmount;
    private float _actionBlendTarget;
    private float _actionBlendDuration;
    private float _actionModeBlendAmount;
    private float _actionModeBlendTarget;

    public AlsLocomotionAnimationController(
        AlsLocomotionGraphBuildResult graph,
        AlsLocomotionSettings settings)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _skeleton = graph.TargetSkeleton;
        ArgumentNullException.ThrowIfNull(settings);
        _playRateMaximum = settings.PlayRateMaximum;
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
                SetP4Parameters(_graph.Handles.P4, PreparedP4.Disabled, 0, 0, 0f, 0f, 0f, 0f);
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
        ThrowIfDisposed();
        if (Volatile.Read(ref _warmupState) != WarmupReady)
        {
            throw new InvalidOperationException("P3 locomotion animation controller must be warmed before Apply().");
        }
        var prepared = Prepare(result, deltaTime);
        var preparedP4 = PrepareP4(in p4Input);

        var requestedP4Mode = preparedP4.TurnActive ? (byte)1 :
            preparedP4.RotateActive ? (byte)2 : (byte)0;
        var p4Handles = _graph.Handles.P4;
        var stateChanged = result.AnimationState != ActiveAnimationState;
        if (stateChanged)
        {
            _topPlayback!.Travel(_graph.Handles.StateNames[(int)result.AnimationState], true);
        }
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
        var nextTurnBank = _turnBank;
        var nextRotateBank = _rotateBank;
        var nextTurnTargetBank = _turnTargetBank;
        var nextRotateTargetBank = _rotateTargetBank;
        var nextTurnBlendActive = _turnBlendActive;
        var nextRotateBlendActive = _rotateBlendActive;
        var nextTurnBlendElapsed = _turnBlendElapsed;
        var nextRotateBlendElapsed = _rotateBlendElapsed;
        var nextTurnBlendDuration = _turnBlendDuration;
        var nextRotateBlendDuration = _rotateBlendDuration;
        if (preparedP4.TurnActive &&
            p4Input.ActiveTurnAnimationId != ActiveTurnAnimationId)
        {
            nextTurnTargetBank = _activeP4Mode == 1 ? (byte)(1 - _turnBank) : _turnBank;
            nextTurnBlendDuration = preparedP4.TurnBinding.BlendSeconds;
            nextTurnBlendElapsed = 0.0;
            nextTurnBlendActive = _activeP4Mode == 1 && nextTurnBlendDuration > 0f;
            _graph.Tree.Set(
                p4Handles!.GetTurnRequestPath(nextTurnTargetBank),
                preparedP4.TurnBinding.StateName);
        }
        if (preparedP4.RotateActive &&
            p4Input.ActiveRotateAnimationId != ActiveRotateAnimationId)
        {
            nextRotateTargetBank = _activeP4Mode == 2 ? (byte)(1 - _rotateBank) : _rotateBank;
            nextRotateBlendDuration = preparedP4.RotateBinding.BlendSeconds;
            nextRotateBlendElapsed = 0.0;
            nextRotateBlendActive = _activeP4Mode == 2 && nextRotateBlendDuration > 0f;
            _graph.Tree.Set(
                p4Handles!.GetRotateRequestPath(nextRotateTargetBank),
                preparedP4.RotateBinding.StateName);
        }
        var turnParameterBank = nextTurnBlendActive ? nextTurnTargetBank : nextTurnBank;
        var rotateParameterBank = nextRotateBlendActive ? nextRotateTargetBank : nextRotateBank;
        var turnBlendAmount = (float)nextTurnBank;
        var rotateBlendAmount = (float)nextRotateBank;
        if (preparedP4.TurnActive && nextTurnBlendActive)
        {
            nextTurnBlendElapsed = Math.Min(
                nextTurnBlendElapsed + deltaTime, nextTurnBlendDuration);
            var alpha = (float)(nextTurnBlendElapsed / nextTurnBlendDuration);
            turnBlendAmount = nextTurnTargetBank == 1 ? alpha : 1f - alpha;
            if (nextTurnBlendElapsed >= nextTurnBlendDuration - 1e-6)
            {
                nextTurnBank = nextTurnTargetBank;
                nextTurnBlendActive = false;
                turnBlendAmount = nextTurnBank;
            }
        }
        if (preparedP4.RotateActive && nextRotateBlendActive)
        {
            nextRotateBlendElapsed = Math.Min(
                nextRotateBlendElapsed + deltaTime, nextRotateBlendDuration);
            var alpha = (float)(nextRotateBlendElapsed / nextRotateBlendDuration);
            rotateBlendAmount = nextRotateTargetBank == 1 ? alpha : 1f - alpha;
            if (nextRotateBlendElapsed >= nextRotateBlendDuration - 1e-6)
            {
                nextRotateBank = nextRotateTargetBank;
                nextRotateBlendActive = false;
                rotateBlendAmount = nextRotateBank;
            }
        }

        var stanceChanged = result.ActualStance != ActiveStance;
        if (result.AnimationState == AlsAnimationState.Grounded &&
            (stateChanged || stanceChanged))
        {
            _groundedPlayback!.Travel(
                _graph.Handles.StanceNames[(int)result.ActualStance], true);
        }

        SetParameters(
            prepared.Parameters,
            prepared.Blend,
            prepared.EffectivePlayRate,
            prepared.Lean,
            prepared.LeanAmount,
            prepared.Phase);
        if (_graph.Handles.P4 is not null)
        {
            SetP4Parameters(
                _graph.Handles.P4,
                preparedP4,
                turnParameterBank,
                rotateParameterBank,
                turnBlendAmount,
                rotateBlendAmount,
                nextActionModeBlendAmount,
                nextActionBlendAmount);
        }

        _graph.Tree.Advance(deltaTime);
        ManualAdvanceCount++;
        ActiveAnimationState = result.AnimationState;
        ActiveStance = result.ActualStance;
        ActiveTurnAnimationId = p4Input.ActiveTurnAnimationId;
        ActiveRotateAnimationId = p4Input.ActiveRotateAnimationId;
        _activeP4Mode = requestedP4Mode;
        _turnBank = nextTurnBank;
        _rotateBank = nextRotateBank;
        _turnTargetBank = nextTurnTargetBank;
        _rotateTargetBank = nextRotateTargetBank;
        _turnBlendActive = nextTurnBlendActive;
        _rotateBlendActive = nextRotateBlendActive;
        _turnBlendElapsed = nextTurnBlendElapsed;
        _rotateBlendElapsed = nextRotateBlendElapsed;
        _turnBlendDuration = nextTurnBlendDuration;
        _rotateBlendDuration = nextRotateBlendDuration;
        _actionBlendAmount = nextActionBlendAmount;
        _actionBlendTarget = nextActionBlendTarget;
        _actionBlendDuration = nextActionBlendDuration;
        _actionModeBlendAmount = nextActionModeBlendAmount;
        _actionModeBlendTarget = nextActionModeBlendTarget;
        if (preparedP4.TurnActive)
        {
            _activeTurnBlendSeconds = preparedP4.TurnBinding.BlendSeconds;
        }
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
        byte turnBank,
        byte rotateBank,
        float turnBlendAmount,
        float rotateBlendAmount,
        float actionModeBlendAmount,
        float actionBlendAmount)
    {
        if (prepared.TurnActive)
        {
            _graph.Tree.Set(prepared.TurnBinding.GetPlayRatePath(turnBank), prepared.TurnPlayRate);
            _graph.Tree.Set(prepared.TurnBinding.GetPhasePath(turnBank), prepared.TurnPhase);
        }
        if (prepared.RotateActive)
        {
            _graph.Tree.Set(prepared.RotateBinding.GetPlayRatePath(rotateBank), prepared.RotatePlayRate);
            _graph.Tree.Set(prepared.RotateBinding.GetPhasePath(rotateBank), prepared.RotatePhase);
        }
        _graph.Tree.Set(handles.TurnBlendPath, turnBlendAmount);
        _graph.Tree.Set(handles.RotateBlendPath, rotateBlendAmount);
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

    private readonly record struct PreparedApply(
        AlsLocomotionGraphParameterSet Parameters,
        Vector2 Blend,
        float EffectivePlayRate,
        Vector2 Lean,
        float LeanAmount,
        float Phase);

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
