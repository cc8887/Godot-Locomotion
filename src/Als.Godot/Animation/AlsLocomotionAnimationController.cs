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
    private AnimationNodeStateMachinePlayback? _turnPlayback;
    private AnimationNodeStateMachinePlayback? _rotatePlayback;
    private int _warmupState;
    private int _disposed;
    private byte _activeP4Mode;

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
            var turnPlayback = _graph.Handles.P4 is null
                ? null
                : GetPlayback(_graph.Handles.P4.TurnPlaybackPath, "P4Turn");
            var rotatePlayback = _graph.Handles.P4 is null
                ? null
                : GetPlayback(_graph.Handles.P4.RotatePlaybackPath, "P4Rotate");
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
            if (_graph.Handles.P4 is not null)
            {
                turnPlayback!.Start(_graph.Handles.P4.InitialTurnBinding.StateName, true);
                rotatePlayback!.Start(_graph.Handles.P4.InitialRotateBinding.StateName, true);
            }
            SetParameters(
                _graph.Handles.GroundedStanding,
                Vector2.Zero,
                1f,
                Vector2.Zero,
                0f,
                0f);
            if (_graph.Handles.P4 is not null)
            {
                SetP4Parameters(_graph.Handles.P4, PreparedP4.Disabled);
            }
            _graph.Tree.Advance(0.0);

            _topPlayback = topPlayback;
            _groundedPlayback = groundedPlayback;
            _turnPlayback = turnPlayback;
            _rotatePlayback = rotatePlayback;
            poseBoneIndices.CopyTo(_poseBoneIndices, 0);
            Volatile.Write(ref _warmupState, WarmupReady);
        }
        catch
        {
            _topPlayback = null;
            _groundedPlayback = null;
            _turnPlayback = null;
            _rotatePlayback = null;
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
        var stateChanged = result.AnimationState != ActiveAnimationState;
        if (requestedP4Mode != _activeP4Mode ||
            (requestedP4Mode == 0 && stateChanged))
        {
            var stateName = requestedP4Mode switch
            {
                1 => _graph.Handles.P4!.TurnStateName,
                2 => _graph.Handles.P4!.RotateStateName,
                _ => _graph.Handles.StateNames[(int)result.AnimationState],
            };
            if (requestedP4Mode == 0)
            {
                _topPlayback!.Travel(stateName, true);
            }
            else
            {
                _topPlayback!.Start(stateName, true);
            }
        }
        if (preparedP4.TurnActive &&
            p4Input.ActiveTurnAnimationId != ActiveTurnAnimationId)
        {
            _turnPlayback!.Start(preparedP4.TurnBinding.StateName, true);
        }
        if (preparedP4.RotateActive &&
            p4Input.ActiveRotateAnimationId != ActiveRotateAnimationId)
        {
            _rotatePlayback!.Start(preparedP4.RotateBinding.StateName, true);
        }

        var stanceChanged = result.ActualStance != ActiveStance;
        if (result.AnimationState == AlsAnimationState.Grounded &&
            ((requestedP4Mode == 0 && stateChanged) || stanceChanged))
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
            SetP4Parameters(_graph.Handles.P4, preparedP4);
        }

        _graph.Tree.Advance(deltaTime);
        ManualAdvanceCount++;
        ActiveAnimationState = result.AnimationState;
        ActiveStance = result.ActualStance;
        ActiveTurnAnimationId = p4Input.ActiveTurnAnimationId;
        ActiveRotateAnimationId = p4Input.ActiveRotateAnimationId;
        _activeP4Mode = requestedP4Mode;
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
        _turnPlayback = null;
        _rotatePlayback = null;
    }

    private AnimationNodeStateMachinePlayback GetPlayback(StringName path, string label)
    {
        var playback = _graph.Tree.Get(path).As<AnimationNodeStateMachinePlayback>();
        return playback ?? throw new InvalidOperationException(
            $"P3 animation graph did not expose the {label} state-machine playback resource.");
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

    private void SetP4Parameters(AlsP4GraphHandles handles, in PreparedP4 prepared)
    {
        if (prepared.TurnActive)
        {
            _graph.Tree.Set(prepared.TurnBinding.PlayRatePath, prepared.TurnPlayRate);
            _graph.Tree.Set(prepared.TurnBinding.PhasePath, prepared.TurnPhase);
        }
        if (prepared.RotateActive)
        {
            _graph.Tree.Set(prepared.RotateBinding.PlayRatePath, prepared.RotatePlayRate);
            _graph.Tree.Set(prepared.RotateBinding.PhasePath, prepared.RotatePhase);
        }
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
