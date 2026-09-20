using System.Numerics;

namespace GodotAls.Core.Locomotion;

public static class AlsStandingCyclePose
{
    public const int ClipCount = 26;
    public const int ScratchPoseCount = 12;

    public static void Compose(ReadOnlySpan<AlsLocalPose> clips, ReadOnlySpan<bool> profileBones,
        Vector4 cornerWeights, ReadOnlySpan<int> sampleOrder, Vector4 velocityBlend,
        in AlsTransitionStackState transitions, float movingWeight, float sprintWeight,
        Span<AlsLocalPose> scratch, Span<AlsLocalPose> output, float sprintMask = 0, ReadOnlySpan<int> directionInputs = default)
    {
        var bones = output.Length;
        if (bones == 0 || clips.Length != ClipCount * bones || profileBones.Length != bones ||
            scratch.Length != ScratchPoseCount * bones || sampleOrder.Length is < 1 or > 4 ||
            (uint)transitions.CurrentState >= 6)
            throw new ArgumentException("Invalid standing pose buffers or state.");
        // Standalone callers use the formal V4 mapping; production supplies compiled cache roles.
        if (directionInputs.IsEmpty) directionInputs = [0, 1, 2, 4, 0, 1, 3, 5, 0, 1, 2, 5,
            0, 1, 3, 4, 0, 1, 3, 4, 0, 1, 2, 5];
        if (directionInputs.Length != 24) throw new ArgumentException("Invalid direction input mapping.");
        foreach (var input in directionInputs) if ((uint)input >= 6) throw new ArgumentException("Invalid direction input role.");

        for (var direction = 0; direction < 6; direction++)
        for (var bone = 0; bone < bones; bone++)
        {
            var pose = AlsPoseBlender.Scale(clips[(1 + direction * 4 + sampleOrder[0]) * bones + bone], cornerWeights[sampleOrder[0]]);
            for (var i = 1; i < sampleOrder.Length; i++)
                pose = AlsPoseBlender.Accumulate(pose, clips[(1 + direction * 4 + sampleOrder[i]) * bones + bone], cornerWeights[sampleOrder[i]]);
            scratch[direction * bones + bone] = AlsPoseBlender.Normalize(pose);
        }

        for (var bone = 0; bone < bones; bone++)
        {
            var forward = scratch[bone];
            var sprint = sprintWeight <= AlsPoseBlender.WeightThreshold ? forward :
                sprintWeight >= 1 - AlsPoseBlender.WeightThreshold ? clips[25 * bones + bone] :
                AlsPoseBlender.Blend(forward, clips[25 * bones + bone], sprintWeight);
            scratch[bone] = sprintMask <= AlsPoseBlender.WeightThreshold ? sprint :
                sprintMask >= 1 - AlsPoseBlender.WeightThreshold ? forward : AlsPoseBlender.Blend(sprint, forward, sprintMask);
        }

        ComposeDirections(scratch[..(6 * bones)], clips[..bones], profileBones, velocityBlend, transitions,
            movingWeight, scratch[(6 * bones)..], output, directionInputs);
    }

    public static void ComposeDirections(ReadOnlySpan<AlsLocalPose> directions, ReadOnlySpan<AlsLocalPose> idle,
        ReadOnlySpan<bool> profileBones, Vector4 velocityBlend, in AlsTransitionStackState transitions,
        float movingWeight, Span<AlsLocalPose> scratch, Span<AlsLocalPose> output, ReadOnlySpan<int> directionInputs,
        ReadOnlySpan<Vector4> stateWeights = default, ReadOnlySpan<AlsLocalPose> referencePose = default)
    {
        var bones = output.Length;
        if (bones == 0 || directions.Length != 6 * bones || idle.Length != bones || profileBones.Length != bones ||
            scratch.Length != 6 * bones || directionInputs.Length != 24 || (uint)transitions.CurrentState >= 6 ||
            !float.IsFinite(movingWeight) || movingWeight is < 0 or > 1)
            throw new ArgumentException("Invalid cached direction pose buffers.");
        if (!stateWeights.IsEmpty && (stateWeights.Length != 6 || referencePose.Length != bones))
            throw new ArgumentException("Invalid independent direction pose inputs.");
        foreach (var input in directionInputs) if ((uint)input >= 6) throw new ArgumentException("Invalid cached direction input.");
        velocityBlend = NormalizeVelocityWeights(velocityBlend);
        for (var state = 0; state < 6; state++)
        for (var bone = 0; bone < bones; bone++)
        {
            var accumulated = default(AlsLocalPose);
            var first = true;
            // The source MultiWayBlend visits F/B/L/R pins, not weight-sorted clips.
            for (var axis = 0; axis < 4; axis++)
            {
                var weight = stateWeights.IsEmpty ? velocityBlend[axis] : stateWeights[state][axis];
                if (weight <= AlsPoseBlender.WeightThreshold) continue;
                var source = directions[directionInputs[state * 4 + axis] * bones + bone];
                accumulated = first ? AlsPoseBlender.Scale(source, weight) : AlsPoseBlender.Accumulate(accumulated, source, weight);
                first = false;
            }
            scratch[state * bones + bone] = first && !referencePose.IsEmpty ? referencePose[bone] : AlsPoseBlender.Normalize(accumulated);
        }

        for (var bone = 0; bone < bones; bone++)
        {
            var sourceState = transitions.Count > 0 ? transitions.GetTransition(0).From : transitions.CurrentState;
            var pose = scratch[sourceState * bones + bone];
            for (var i = 0; i < transitions.Count; i++)
            {
                var transition = transitions.GetTransition(i);
                var weights = AlsGroundedPoseBlend.WeightFactor(transition.Alpha, 2, profileBones[bone]);
                pose = AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(pose, weights.Y),
                    scratch[transition.To * bones + bone], weights.X);
            }
            pose = AlsPoseBlender.Normalize(pose);
            // Temporary outer Idle policy stays explicit until the Stop state machine replaces it.
            output[bone] = AlsPoseBlender.Blend(idle[bone], pose, movingWeight);
        }
    }

    public static void ComposeDirections(ReadOnlySpan<AlsPrecisePose> directions, ReadOnlySpan<AlsPrecisePose> idle,
        ReadOnlySpan<bool> profileBones, Vector4 velocityBlend, in AlsTransitionStackState transitions,
        float movingWeight, Span<AlsPrecisePose> scratch, Span<AlsPrecisePose> output, ReadOnlySpan<int> directionInputs,
        ReadOnlySpan<Vector4> stateWeights = default, ReadOnlySpan<AlsPrecisePose> referencePose = default)
    {
        var bones = output.Length;
        if (bones == 0 || directions.Length != 6 * bones || idle.Length != bones || profileBones.Length != bones ||
            scratch.Length != 6 * bones || directionInputs.Length != 24 || (uint)transitions.CurrentState >= 6 ||
            !float.IsFinite(movingWeight) || movingWeight is < 0 or > 1)
            throw new ArgumentException("Invalid cached direction pose buffers.");
        if (!stateWeights.IsEmpty && (stateWeights.Length != 6 || referencePose.Length != bones))
            throw new ArgumentException("Invalid independent direction pose inputs.");
        foreach (var input in directionInputs) if ((uint)input >= 6) throw new ArgumentException("Invalid cached direction input.");
        velocityBlend = NormalizeVelocityWeights(velocityBlend);
        for (var state = 0; state < 6; state++)
        for (var bone = 0; bone < bones; bone++)
        {
            var accumulated = default(AlsPrecisePose);
            var first = true;
            // The source MultiWayBlend visits F/B/L/R pins, not weight-sorted clips.
            for (var axis = 0; axis < 4; axis++)
            {
                var weight = stateWeights.IsEmpty ? velocityBlend[axis] : stateWeights[state][axis];
                if (weight <= AlsPoseBlender.WeightThreshold) continue;
                var source = directions[directionInputs[state * 4 + axis] * bones + bone];
                accumulated = first ? AlsPrecisePoseBlender.Scale(source, weight) : AlsPrecisePoseBlender.Accumulate(accumulated, source, weight);
                first = false;
            }
            scratch[state * bones + bone] = first && !referencePose.IsEmpty ? referencePose[bone] : accumulated.Normalized();
        }

        for (var bone = 0; bone < bones; bone++)
        {
            var sourceState = transitions.Count > 0 ? transitions.GetTransition(0).From : transitions.CurrentState;
            var pose = scratch[sourceState * bones + bone];
            for (var i = 0; i < transitions.Count; i++)
            {
                var transition = transitions.GetTransition(i);
                var weights = AlsGroundedPoseBlend.WeightFactor(transition.Alpha, 2, profileBones[bone]);
                pose = AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(pose, weights.Y),
                    scratch[transition.To * bones + bone], weights.X);
            }
            pose = pose.Normalized();
            // Temporary outer Idle policy stays explicit until the Stop state machine replaces it.
            output[bone] = AlsPrecisePoseBlender.Blend(idle[bone], pose, movingWeight);
        }
    }

    public static Vector4 NormalizeVelocityWeights(Vector4 weights)
    {
        var total = weights.X + weights.Y + weights.Z + weights.W;
        return total > AlsPoseBlender.WeightThreshold ? weights / total : Vector4.UnitX;
    }
}
