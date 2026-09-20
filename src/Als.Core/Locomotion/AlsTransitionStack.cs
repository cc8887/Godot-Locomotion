using System.Runtime.CompilerServices;

namespace GodotAls.Core.Locomotion;

public enum AlsTransitionBlend : byte { Linear, Cubic, HermiteCubic, Custom }

public readonly record struct AlsActiveTransition(int From, int To, float Duration, float Elapsed,
    float Remaining, float Alpha, AlsTransitionBlend Blend)
{
    public float BlendProgress { get; init; }
    public bool Complete { get; init; }
}

[InlineArray(128)]
internal struct AlsActiveTransitionBuffer { private AlsActiveTransition _element; }

public struct AlsTransitionStackState
{
    internal AlsActiveTransitionBuffer Entries;
    public int CurrentState { get; internal set; }
    public int Count { get; internal set; }
    public AlsActiveTransition Latest { get; internal set; }

    public readonly AlsActiveTransition GetTransition(int index) => (uint)index < Count
        ? Entries[index] : throw new ArgumentOutOfRangeException(nameof(index));
}

/// <summary>UE standard-transition scalar weights and lifetime; pose evaluation and events are consumers.</summary>
public static class AlsTransitionStack
{
    public const int Capacity = 128;

    public static AlsTransitionStackState Initialize(int state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state);
        return new AlsTransitionStackState { CurrentState = state };
    }

    public static AlsTransitionStackState Start(in AlsTransitionStackState previous, int target,
        float duration, AlsTransitionBlend blend)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(target);
        if (!float.IsFinite(duration) || duration < 0 || (uint)blend > (uint)AlsTransitionBlend.Custom)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (target == previous.CurrentState) return previous;
        if (previous.Count == Capacity) throw new InvalidOperationException("Active transition capacity exceeded.");
        var state = previous;
        var remainingWeight = 1 - Weight(previous, target);
        // UE only applies this inverse approximation to HermiteCubic, not Cubic/Custom.
        var factor = blend == AlsTransitionBlend.HermiteCubic
            ? remainingWeight * remainingWeight * remainingWeight * (4f / 3f) -
              remainingWeight * remainingWeight * 2f + remainingWeight * (5f / 3f)
            : remainingWeight;
        var effectiveDuration = duration * System.Math.Clamp(factor, 0, 1);
        state.Latest = new(previous.CurrentState, target, effectiveDuration, 0, effectiveDuration, 0, blend);
        state.Entries[state.Count++] = state.Latest;
        state.CurrentState = target;
        return state;
    }

    public static AlsTransitionStackState Advance(in AlsTransitionStackState previous, float delta,
        Func<float, float>? customCurve = null)
        => Advance(previous, delta, out _, customCurve);

    public static AlsTransitionStackState Advance(in AlsTransitionStackState previous, float delta,
        out AlsTransitionStackState beforeCleanup, Func<float, float>? customCurve = null)
        => AdvanceCore(previous, delta, out beforeCleanup, customCurve, null);

    // The index addresses the pre-cleanup active transition, not a state or an
    // animation asset. The owner retains each edge's curve until that edge retires.
    public static AlsTransitionStackState AdvancePerTransition(in AlsTransitionStackState previous, float delta,
        out AlsTransitionStackState beforeCleanup, Func<int, float, float> customCurves)
    {
        ArgumentNullException.ThrowIfNull(customCurves);
        return AdvanceCore(previous, delta, out beforeCleanup, null, customCurves);
    }

    private static AlsTransitionStackState AdvanceCore(in AlsTransitionStackState previous, float delta,
        out AlsTransitionStackState beforeCleanup, Func<float, float>? customCurve, Func<int, float, float>? indexedCurves)
    {
        if (!float.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        var state = previous;
        var completed = -1;
        for (var i = 0; i < state.Count; i++)
        {
            var entry = state.Entries[i];
            var elapsed = entry.Elapsed + delta;
            if (!float.IsFinite(elapsed)) throw new ArgumentOutOfRangeException(nameof(delta));
            // FAlphaBlend owns a separate incremental clock and completes when
            // its curve value reaches the endpoint, even with time remaining.
            // The state machine's pose Alpha still uses elapsed / duration.
            var remaining = entry.Remaining;
            var blendProgress = entry.Duration > 0 ? entry.BlendProgress : 1;
            var desired = Sample(1);
            if (Sample(blendProgress) != desired)
            {
                blendProgress = remaining > delta ? blendProgress + (1 - blendProgress) / remaining * delta : 1;
                blendProgress = System.Math.Clamp(blendProgress, 0, 1);
                remaining = MathF.Max(0, remaining - delta);
            }
            var complete = Sample(blendProgress) == desired;
            var progress = entry.Duration > 0 ? elapsed / entry.Duration : 0;
            var alpha = Sample(progress);
            entry = entry with { Elapsed = elapsed, Remaining = remaining, Alpha = alpha,
                BlendProgress = blendProgress, Complete = complete };
            state.Entries[i] = entry;
            if (complete) completed = i;
            float Sample(float time)
            {
                var value = entry.Blend == AlsTransitionBlend.Custom && indexedCurves is not null
                    ? indexedCurves(i, time) : Alpha(time, entry.Blend, customCurve);
                if (!float.IsFinite(value)) throw new InvalidOperationException("Transition curve returned a non-finite weight.");
                return System.Math.Clamp(value, 0, 1);
            }
        }
        if (state.Count > 0) state.Latest = state.Entries[state.Count - 1];
        beforeCleanup = state;
        // A newer completed transition supersedes every older transition, even unfinished ones.
        if (completed >= 0)
        {
            var remove = completed + 1;
            for (var i = remove; i < state.Count; i++) state.Entries[i - remove] = state.Entries[i];
            for (var i = state.Count - remove; i < state.Count; i++) state.Entries[i] = default;
            state.Count -= remove;
        }
        return state;
    }

    public static float Weight(in AlsTransitionStackState state, int stateId, float boneWeightFactor = 1)
    {
        if (!float.IsFinite(boneWeightFactor) || boneWeightFactor <= 0)
            throw new ArgumentOutOfRangeException(nameof(boneWeightFactor));
        if (state.Count == 0) return state.CurrentState == stateId ? 1 : 0;
        var weight = 0f;
        for (var i = 0; i < state.Count; i++)
        {
            var entry = state.Entries[i];
            var weights = AlsGroundedPoseBlend.WeightFactor(entry.Alpha, boneWeightFactor, boneWeightFactor != 1);
            if (i > 0) weight *= weights.Y;
            else if (entry.From == stateId) weight += weights.Y;
            if (entry.To == stateId) weight += weights.X;
        }
        return System.Math.Clamp(weight, 0, 1);
    }

    public static float Alpha(float progress, AlsTransitionBlend blend, Func<float, float>? customCurve = null)
    {
        if (!float.IsFinite(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
        var value = blend switch
        {
            AlsTransitionBlend.Linear => progress,
            AlsTransitionBlend.Cubic => -2 * progress * progress * progress + 3 * progress * progress,
            AlsTransitionBlend.HermiteCubic => progress <= 0 ? 0 : progress >= 1 ? 1 : progress * progress * (3 - 2 * progress),
            AlsTransitionBlend.Custom => customCurve is not null ? customCurve(progress) :
                throw new ArgumentException("Custom transitions require the source curve.", nameof(customCurve)),
            _ => throw new ArgumentOutOfRangeException(nameof(blend)),
        };
        if (!float.IsFinite(value)) throw new InvalidOperationException("Transition curve returned a non-finite weight.");
        return System.Math.Clamp(value, 0, 1);
    }
}
