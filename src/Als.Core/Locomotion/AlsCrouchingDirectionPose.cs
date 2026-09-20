using System.Numerics;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsCrouchingDirectionPoseRow(int Forward, int Backward, int Left, int Right, int YawAxis)
{
    public int Cache(int axis) => axis switch { 0 => Forward, 1 => Backward, 2 => Left, 3 => Right, _ => throw new ArgumentOutOfRangeException(nameof(axis)) };
}

public static class AlsCrouchingDirectionPose
{
    public static void Compose(ReadOnlySpan<AlsCrouchingDirectionPoseRow> rows, AlsGroundedMachineDefinition machine,
        in AlsGroundedMachineState state, ReadOnlySpan<AlsLocalPose> caches, ReadOnlySpan<AlsLocalPose> rest,
        ReadOnlySpan<bool> profileBones, Vector4 velocity, Span<AlsLocalPose> scratch, Span<AlsLocalPose> output)
    {
        Validate(rows, state, velocity);
        var bones = rest.Length;
        if (bones == 0 || caches.Length != 6 * bones || scratch.Length != 6 * bones || output.Length != bones ||
            profileBones.Length != bones || machine.Kind != state.Kind || machine.States.Length != 6)
            throw new ArgumentException("Invalid Crouching direction pose buffers.");
        for (var i = 0; i < state.Transitions.Count; i++)
        {
            var id = state.GetActiveEdge(i);
            if ((uint)id >= machine.Edges.Length || machine.Edges[id].BlendProfile != AlsGroundedBlendProfile.ChangeDirection)
                throw new ArgumentException("Unsupported Crouching direction blend profile.");
        }
        velocity = Weights(velocity);
        for (var index = 0; index < rows.Length; index++)
        for (var bone = 0; bone < bones; bone++)
        {
            var pose = default(AlsLocalPose); var first = true;
            // Native MultiWayBlend traverses the authored F/B/L/R pins without weight sorting.
            for (var axis = 0; axis < 4; axis++)
            {
                var weight = velocity[axis];
                if (weight <= AlsPoseBlender.WeightThreshold) continue;
                var source = caches[rows[index].Cache(axis) * bones + bone];
                pose = first ? AlsPoseBlender.Scale(source, weight) : AlsPoseBlender.Accumulate(pose, source, weight);
                first = false;
            }
            scratch[index * bones + bone] = first ? rest[bone] : AlsPoseBlender.Normalize(pose);
        }
        var stack = state.Transitions;
        var initial = stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From;
        for (var bone = 0; bone < bones; bone++)
        {
            var pose = scratch[initial * bones + bone];
            for (var i = 0; i < stack.Count; i++)
            {
                var edge = stack.GetTransition(i);
                var weights = AlsGroundedPoseBlend.WeightFactor(edge.Alpha, 2, profileBones[bone]);
                pose = AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(pose, weights.Y), scratch[edge.To * bones + bone], weights.X);
            }
            output[bone] = stack.Count == 0 ? pose : AlsPoseBlender.Normalize(pose);
        }
    }

    public static float Curve(ReadOnlySpan<AlsCrouchingDirectionPoseRow> rows, in AlsGroundedMachineState state,
        ReadOnlySpan<float> caches, Vector4 velocity, Vector4 yaw, bool yawOffset)
    {
        Validate(rows, state, velocity);
        if (caches.Length != 6 || !Finite(yaw)) throw new ArgumentException("Invalid Crouching direction curve inputs.");
        foreach (var value in caches) if (!float.IsFinite(value)) throw new ArgumentException("Non-finite cached curve.");
        velocity = Weights(velocity);
        Span<float> values = stackalloc float[6];
        values.Clear();
        for (var index = 0; index < rows.Length; index++)
        {
            if (yawOffset) { values[index] = yaw[rows[index].YawAxis]; continue; }
            for (var axis = 0; axis < 4; axis++)
                if (velocity[axis] > AlsPoseBlender.WeightThreshold) values[index] += caches[rows[index].Cache(axis)] * velocity[axis];
        }
        var stack = state.Transitions;
        var result = values[stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From];
        for (var i = 0; i < stack.Count; i++)
        {
            var edge = stack.GetTransition(i);
            result = result * (1 - edge.Alpha) + values[edge.To] * edge.Alpha;
        }
        return result;
    }

    public static AlsInertialCurve CurveWithPresence(ReadOnlySpan<AlsCrouchingDirectionPoseRow> rows,
        in AlsGroundedMachineState state, ReadOnlySpan<AlsInertialCurve> caches, Vector4 velocity, Vector4 yaw, bool yawOffset)
    {
        Validate(rows, state, velocity);
        if (caches.Length != 6 || !Finite(yaw)) throw new ArgumentException("Invalid Crouching direction curves.");
        velocity = Weights(velocity);
        Span<AlsInertialCurve> values = stackalloc AlsInertialCurve[6];
        values.Clear();
        for (var index = 0; index < rows.Length; index++)
        {
            for (var axis = 0; axis < 4; axis++)
                values[index] = AlsStandingCycleCurves.Accumulate(values[index], caches[rows[index].Cache(axis)], velocity[axis]);
            if (yawOffset) values[index] = AlsStandingCycleCurves.ModifyBlend(values[index], yaw[rows[index].YawAxis], 1);
        }
        var stack = state.Transitions;
        var result = values[stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From];
        for (var i = 0; i < stack.Count; i++)
        {
            var edge = stack.GetTransition(i);
            result = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(result, 1 - edge.Alpha), values[edge.To], edge.Alpha);
        }
        return result;
    }

    private static Vector4 Weights(Vector4 velocity)
    {
        var total = velocity.X + velocity.Y + velocity.Z + velocity.W;
        return total > AlsPoseBlender.WeightThreshold ? velocity / total : Vector4.Zero;
    }
    private static bool Finite(Vector4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
    private static void Validate(ReadOnlySpan<AlsCrouchingDirectionPoseRow> rows, in AlsGroundedMachineState state, Vector4 velocity)
    {
        if (rows.Length != 6 || !state.HasUpdated || state.Kind != AlsGroundedMachineKind.CrouchingDirection ||
            (uint)state.CurrentState >= 6 || !Finite(velocity) || velocity.X < 0 || velocity.Y < 0 || velocity.Z < 0 || velocity.W < 0 ||
            !float.IsFinite(velocity.X + velocity.Y + velocity.Z + velocity.W))
            throw new ArgumentException("Invalid Crouching direction pose state/input.");
        foreach (var row in rows)
        {
            if ((uint)row.YawAxis > 3) throw new ArgumentException("Invalid Crouching yaw input.");
            for (var axis = 0; axis < 4; axis++) if ((uint)row.Cache(axis) >= 6) throw new ArgumentException("Invalid Crouching cache input.");
        }
    }

    public static void Compose(ReadOnlySpan<AlsCrouchingDirectionPoseRow> rows, AlsGroundedMachineDefinition machine,
        in AlsGroundedMachineState state, ReadOnlySpan<AlsPrecisePose> caches, ReadOnlySpan<AlsPrecisePose> rest,
        ReadOnlySpan<bool> profileBones, Vector4 velocity, Span<AlsPrecisePose> scratch, Span<AlsPrecisePose> output)
    {
        Validate(rows, state, velocity);
        var bones = rest.Length;
        if (bones == 0 || caches.Length != 6 * bones || scratch.Length != 6 * bones || output.Length != bones ||
            profileBones.Length != bones || machine.Kind != state.Kind || machine.States.Length != 6)
            throw new ArgumentException("Invalid Crouching direction pose buffers.");
        for (var i = 0; i < state.Transitions.Count; i++)
        {
            var id = state.GetActiveEdge(i);
            if ((uint)id >= machine.Edges.Length || machine.Edges[id].BlendProfile != AlsGroundedBlendProfile.ChangeDirection)
                throw new ArgumentException("Unsupported Crouching direction blend profile.");
        }
        velocity = Weights(velocity);
        for (var index = 0; index < rows.Length; index++)
        for (var bone = 0; bone < bones; bone++)
        {
            var pose = default(AlsPrecisePose); var first = true;
            // Native MultiWayBlend traverses the authored F/B/L/R pins without weight sorting.
            for (var axis = 0; axis < 4; axis++)
            {
                var weight = velocity[axis];
                if (weight <= AlsPoseBlender.WeightThreshold) continue;
                var source = caches[rows[index].Cache(axis) * bones + bone];
                pose = first ? AlsPrecisePoseBlender.Scale(source, weight) : AlsPrecisePoseBlender.Accumulate(pose, source, weight);
                first = false;
            }
            scratch[index * bones + bone] = first ? rest[bone] : AlsPrecisePoseBlender.Normalize(pose);
        }
        var stack = state.Transitions;
        var initial = stack.Count == 0 ? stack.CurrentState : stack.GetTransition(0).From;
        for (var bone = 0; bone < bones; bone++)
        {
            var pose = scratch[initial * bones + bone];
            for (var i = 0; i < stack.Count; i++)
            {
                var edge = stack.GetTransition(i);
                var weights = AlsGroundedPoseBlend.WeightFactor(edge.Alpha, 2, profileBones[bone]);
                pose = AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(pose, weights.Y), scratch[edge.To * bones + bone], weights.X);
            }
            output[bone] = stack.Count == 0 ? pose : AlsPrecisePoseBlender.Normalize(pose);
        }
    }
}
