using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation;

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
        ThrowIfDisposed();
        if (Volatile.Read(ref _warmupState) != WarmupReady)
        {
            throw new InvalidOperationException("P3 locomotion animation controller must be warmed before Apply().");
        }
        var prepared = Prepare(result, deltaTime);

        var stateChanged = result.AnimationState != ActiveAnimationState;
        if (stateChanged)
        {
            _topPlayback!.Travel(
                _graph.Handles.StateNames[(int)result.AnimationState], true);
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

        _graph.Tree.Advance(deltaTime);
        ManualAdvanceCount++;
        ActiveAnimationState = result.AnimationState;
        ActiveStance = result.ActualStance;
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
}
