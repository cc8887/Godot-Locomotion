using System.Numerics;
using System.Runtime.CompilerServices;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCachedDirectionUpdate(bool Present, float Weight, int State, bool Active);
[InlineArray(6)] public struct AlsCycleCachedUpdates { private AlsCachedDirectionUpdate _element; }

public static class AlsCycleCacheWeights
{
    /// <summary>Resolve SaveCachedPose update winners, not the weights used to blend poses.
    /// State traversal is oldest transition first, From then To, with each state visited once.</summary>
    public static AlsCycleCachedUpdates Resolve(in AlsTransitionStackState transitions, Vector4 velocity, float graphWeight,
        ReadOnlySpan<Vector4> stateWeights = default)
        => Resolve(transitions, transitions, velocity, graphWeight, stateWeights);

    /// <summary>Update unfinished transitions before cleanup, then visit the sole
    /// evaluated state only if it was not already updated in that traversal.</summary>
    public static AlsCycleCachedUpdates Resolve(in AlsTransitionStackState beforeCleanup, in AlsTransitionStackState evaluated,
        Vector4 velocity, float graphWeight, ReadOnlySpan<Vector4> stateWeights = default)
    {
        if ((uint)evaluated.CurrentState >= 6 || beforeCleanup.CurrentState != evaluated.CurrentState ||
            !float.IsFinite(graphWeight) || graphWeight is < 0 or > 1 ||
            !float.IsFinite(velocity.LengthSquared()) || velocity.X < 0 || velocity.Y < 0 || velocity.Z < 0 || velocity.W < 0)
            throw new ArgumentException("Invalid Cycle cache update input.");
        if (!stateWeights.IsEmpty && stateWeights.Length != 6) throw new ArgumentException("Invalid direction input layout.");
        var result = default(AlsCycleCachedUpdates);
        var total = velocity.X + velocity.Y + velocity.Z + velocity.W;
        if (total <= AlsPoseBlender.WeightThreshold && stateWeights.IsEmpty) return result;
        velocity = total > AlsPoseBlender.WeightThreshold ? velocity / total : Vector4.Zero;
        var visited = 0;
        for (var i = 0; i < beforeCleanup.Count; i++)
        {
            var edge = beforeCleanup.GetTransition(i);
            if (edge.Complete) continue;
            Visit(edge.From, beforeCleanup, velocity, stateWeights, graphWeight, ref visited, ref result);
            Visit(edge.To, beforeCleanup, velocity, stateWeights, graphWeight, ref visited, ref result);
        }
        if (evaluated.Count == 0)
            Visit(evaluated.CurrentState, evaluated, velocity, stateWeights, graphWeight, ref visited, ref result);
        return result;
    }

    private static void Visit(int state, in AlsTransitionStackState transitions, Vector4 velocity, ReadOnlySpan<Vector4> stateWeights, float graphWeight,
        ref int visited, ref AlsCycleCachedUpdates result)
    {
        if ((uint)state >= 6) throw new ArgumentException("Invalid Cycle cache state.");
        if ((visited & (1 << state)) != 0) return;
        visited |= 1 << state;
        if (!stateWeights.IsEmpty) velocity = stateWeights[state];
        var weight = graphWeight * AlsTransitionStack.Weight(transitions, state);
        for (var direction = 0; direction < 6; direction++)
        {
            var local = AlsStandingCycle.DirectionWeight((AlsCycleDirection)state, direction, velocity);
            if (local <= AlsPoseBlender.WeightThreshold) continue;
            var contribution = weight * local;
            var previous = result[direction];
            // A zero global weight still carries an update when the local MultiWayBlend pin is relevant.
            if (!previous.Present || contribution > previous.Weight)
                result[direction] = new(true, contribution, state, state == transitions.CurrentState);
        }
    }
}
