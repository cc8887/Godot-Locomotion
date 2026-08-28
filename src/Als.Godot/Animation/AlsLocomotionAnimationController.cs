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
    private P4BlendChannelState _turnChannel;
    private P4BlendChannelState _rotateChannel;
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
        var turnBranchVisible = _actionBlendAmount > 0f && _actionModeBlendAmount < 1f;
        var rotateBranchVisible = _actionBlendAmount > 0f && _actionModeBlendAmount > 0f;
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
                turnUpdate,
                rotateUpdate,
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
        _turnChannel = turnUpdate.State;
        _rotateChannel = rotateUpdate.State;
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
