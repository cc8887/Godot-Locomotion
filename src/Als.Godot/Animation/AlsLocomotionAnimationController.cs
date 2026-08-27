using Godot;
using GodotAls.Core.Contracts;

namespace GodotAls.Animation;

public sealed class AlsLocomotionAnimationController : IDisposable
{
    private const ulong DigestOffsetBasis = 14695981039346656037UL;
    private const ulong DigestPrime = 1099511628211UL;
    private const float QuantizationScale = 100_000f;

    private static readonly string[] PoseBoneNames =
    [
        "pelvis", "spine_03", "hand_l", "hand_r", "foot_l", "foot_r",
    ];

    private readonly AlsLocomotionGraphBuildResult _graph;
    private readonly Skeleton3D _skeleton;
    private readonly int[] _poseBoneIndices = new int[PoseBoneNames.Length];
    private AnimationNodeStateMachinePlayback? _topPlayback;
    private AnimationNodeStateMachinePlayback? _groundedPlayback;
    private int _warmed;
    private int _disposed;

    public AlsLocomotionAnimationController(
        AlsLocomotionGraphBuildResult graph,
        Skeleton3D skeleton)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
    }

    public AlsAnimationState ActiveAnimationState { get; private set; } = AlsAnimationState.Grounded;

    public AlsStance ActiveStance { get; private set; } = AlsStance.Standing;

    public long ManualAdvanceCount { get; private set; }

    public void Warmup()
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref _warmed, 1) != 0)
        {
            return;
        }
        if (!GodotObject.IsInstanceValid(_graph.Tree) ||
            !GodotObject.IsInstanceValid(_skeleton))
        {
            throw new ObjectDisposedException(nameof(AlsLocomotionGraphBuildResult));
        }

        try
        {
            _graph.Tree.Active = true;
            _topPlayback = GetPlayback(_graph.Handles.TopPlaybackPath, "top");
            _groundedPlayback = GetPlayback(_graph.Handles.GroundedPlaybackPath, "Grounded");
            for (var index = 0; index < PoseBoneNames.Length; index++)
            {
                _poseBoneIndices[index] = _skeleton.FindBone(PoseBoneNames[index]);
                if (_poseBoneIndices[index] < 0)
                {
                    throw new InvalidOperationException(
                        $"P3 graph pose digest bone is missing: {PoseBoneNames[index]}");
                }
            }

            _topPlayback.Start(
                _graph.Handles.StateNames[(int)AlsAnimationState.Grounded], true);
            _groundedPlayback.Start(
                _graph.Handles.StanceNames[(int)AlsStance.Standing], true);
            SetParameters(_graph.Handles.GroundedStanding, Vector2.Zero, 1f, Vector2.Zero, 0f);
            _graph.Tree.Advance(0.0);
        }
        catch
        {
            Interlocked.Exchange(ref _warmed, 0);
            _topPlayback = null;
            _groundedPlayback = null;
            throw;
        }
    }

    public void Apply(in AlsFrameResult result, double deltaTime)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _warmed) == 0)
        {
            throw new InvalidOperationException("P3 locomotion animation controller must be warmed before Apply().");
        }
        Validate(result, deltaTime);

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

        var parameters = GetParameters(result.AnimationState, result.ActualStance);
        var blend = MapBlendPosition(result);
        var lean = new Vector2(result.Lean.X, result.Lean.Y);
        var effectivePlayRate = result.AnimationState == AlsAnimationState.Grounded
            ? result.PlayRate * result.Stride
            : 1f;
        SetParameters(parameters, blend, effectivePlayRate, lean, result.AnimationPhase);

        _graph.Tree.Advance(deltaTime);
        ManualAdvanceCount++;
        ActiveAnimationState = result.AnimationState;
        ActiveStance = result.ActualStance;
    }

    public ulong ComputePoseDigest(long frameId)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _warmed) == 0)
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
            var rotation = _skeleton.GetBonePoseRotation(boneIndex).Normalized();
            if (rotation.W < 0f)
            {
                rotation = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
            }
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
        float phase)
    {
        if (parameters.BlendPositionPath is not null)
        {
            _graph.Tree.Set(
                parameters.BlendPositionPath,
                Clamp(blend, parameters.BlendMinimum, parameters.BlendMaximum));
        }
        _graph.Tree.Set(
            parameters.LeanPositionPath,
            Clamp(lean, parameters.LeanMinimum, parameters.LeanMaximum));
        _graph.Tree.Set(parameters.LeanAmountPath, lean.LengthSquared() > 1e-12f ? 1f : 0f);
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

    private static void Validate(in AlsFrameResult result, double deltaTime)
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
        if (!float.IsFinite(result.BlendCoordinates.X) ||
            !float.IsFinite(result.BlendCoordinates.Y) ||
            !float.IsFinite(result.Stride) ||
            result.Stride < 0f || result.Stride > 1f ||
            !float.IsFinite(result.PlayRate) ||
            result.PlayRate <= 0f ||
            !float.IsFinite(result.Lean.X) ||
            !float.IsFinite(result.Lean.Y) ||
            !float.IsFinite(result.AnimationPhase))
        {
            throw new ArgumentException("P3 animation parameters must be finite and valid.", nameof(result));
        }
        if (result.AnimationState == AlsAnimationState.LandRecovery &&
            result.ResolvedLocomotionState != AlsLocomotionState.Grounded)
        {
            throw new ArgumentException(
                "LandRecovery requires a physically Grounded locomotion result.", nameof(result));
        }
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
}
