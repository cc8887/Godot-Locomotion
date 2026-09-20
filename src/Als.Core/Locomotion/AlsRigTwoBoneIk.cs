using M = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRigTwoBoneIkInput(AlsPrecisePose Root, AlsPrecisePose Joint, AlsPrecisePose End,
    AlsPrecisePose InitialRoot, AlsPrecisePose InitialJoint, AlsPrecisePose InitialEnd,
    AlsPrecisePose Effector, AlsDoubleVector PrimaryAxis, AlsDoubleVector SecondaryAxis,
    float SecondaryAxisWeight, AlsDoubleVector Pole, bool HasPoleSpace, bool PoleIsDirection, AlsPrecisePose PoleSpace,
    bool EnableStretch, float StretchStartRatio, float StretchMaximumRatio, float Weight, float LengthA, float LengthB);

public readonly record struct AlsRigTwoBoneIkResult(bool Applied, AlsPrecisePose Root, AlsPrecisePose Joint, AlsPrecisePose End);

// FRigUnit_TwoBoneIKSimplePerItem for a valid three-bone foot chain. Inputs and
// outputs are native UE component poses, before renderer-axis conversion.
public static class AlsRigTwoBoneIk
{
    public static AlsRigTwoBoneIkResult Solve(in AlsRigTwoBoneIkInput input)
    {
        Validate(input);
        var unchanged = new AlsRigTwoBoneIkResult(false, input.Root, input.Joint, input.End);
        if (input.Weight <= 1e-8f) return unchanged;
        var pole = input.Pole;
        if (input.HasPoleSpace)
        {
            pole = pole.Rotate(input.PoleSpace.Rotation);
            if (!input.PoleIsDirection) pole += input.PoleSpace.Position;
        }
        var a = input.Root;
        // Native starts B with A's rotation and scale, replacing ONLY location.
        var b = a with { Position = input.Joint.Position };
        var c = input.Effector;
        var lengthA = input.LengthA < 1e-8f ? Length(input.InitialRoot, input.InitialJoint, a.Scale) : input.LengthA;
        var lengthB = input.LengthB < 1e-8f ? Length(input.InitialJoint, input.InitialEnd, b.Scale) : input.LengthB;
        if (lengthA < 1e-8f || lengthB < 1e-8f) return unchanged;
        var (joint, end) = AlsTwoBoneIk.SolvePositions(a.Position, pole, c.Position, lengthA, lengthB,
            input.EnableStretch, input.StretchStartRatio, input.StretchMaximumRatio);
        b = b with { Position = joint }; c = c with { Position = end };
        var fullyExtended = false;
        var axis = input.PrimaryAxis.Rotate(a.Rotation);
        var target1 = b.Position - a.Position;
        if (!target1.NearlyZero(1e-4f) && !axis.NearlyZero(1e-4f))
        {
            target1 = AlsTwoBoneIk.SafeNormal(target1);
            var direction = AlsTwoBoneIk.SafeNormal(c.Position - a.Position);
            fullyExtended = M.Abs(AlsDoubleVector.Dot(direction, target1) - 1) < 1e-4f;
            a = a with { Rotation = (AlsTwoBoneIk.Between(axis, target1) * a.Rotation).Normalized() };
            var target2 = fullyExtended ? pole - a.Position : joint - (c.Position + a.Position) * .5;
            a = AlignSecondary(a, target1, target2, input.SecondaryAxis, input.SecondaryAxisWeight);
        }
        axis = input.PrimaryAxis.Rotate(b.Rotation);
        target1 = c.Position - joint;
        if (!target1.NearlyZero(1e-4f) && !axis.NearlyZero(1e-4f))
        {
            target1 = AlsTwoBoneIk.SafeNormal(target1);
            b = b with { Rotation = (AlsTwoBoneIk.Between(axis, target1) * b.Rotation).Normalized() };
            var target2 = fullyExtended ? pole - joint : joint - (c.Position + a.Position) * .5;
            b = AlignSecondary(b, target1, target2, input.SecondaryAxis, input.SecondaryAxisWeight);
        }
        // Blend component rotations first, then reconstruct positions using
        // the SOLVED local offsets. This is not a local-pose alpha blend.
        if (input.Weight < 1f - 1e-8f)
        {
            var positionB = InversePosition(a, b.Position);
            var positionC = InversePosition(b, c.Position);
            a = a with { Rotation = Slerp(input.Root.Rotation, a.Rotation, input.Weight) };
            b = b with { Rotation = Slerp(input.Joint.Rotation, b.Rotation, input.Weight) };
            c = c with { Rotation = Slerp(input.End.Rotation, c.Rotation, input.Weight) };
            b = b with { Position = TransformPosition(a, positionB) };
            c = c with { Position = TransformPosition(b, positionC) };
        }
        return new(true, a, b, c);
    }

    // The frame owner supplies its candidate component array and reusable local
    // scratch. Unrelated bones are untouched. No Godot/hierarchy objects or heap
    // allocations are used, and caller-owned committed poses remain separate.
    public static void Apply(in AlsRigTwoBoneIkResult result, Span<AlsPrecisePose> components,
        ReadOnlySpan<int> parents, int root, int joint, int end, bool propagate, Span<AlsPrecisePose> localScratch,
        Span<bool> changedScratch)
    {
        if (components.Length != parents.Length || localScratch.Length < components.Length || changedScratch.Length < components.Length ||
            root < 0 || joint <= root || end <= joint || end >= components.Length || components.Overlaps(localScratch))
            throw new ArgumentException("Invalid rig IK hierarchy buffers.");
        for (var i = 0; i < parents.Length; i++)
            if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Rig hierarchy must be parent-first.");
        if (!result.Applied) return;
        result.Root.Validate(); result.Joint.Validate(); result.End.Validate();
        changedScratch[..components.Length].Clear();
        if (propagate)
        {
            // Capture old local transforms before any parent is modified.
            for (var i = 0; i < components.Length; i++)
            {
                var parent = parents[i];
                localScratch[i] = parent < 0 ? components[i] : AlsPrecisePose.Relative(components[i], components[parent]);
            }
        }
        for (var i = 0; i < components.Length; i++)
        {
            if (i == root) { components[i] = result.Root; changedScratch[i] = true; }
            else if (i == joint) { components[i] = result.Joint; changedScratch[i] = true; }
            else if (i == end) { components[i] = result.End; changedScratch[i] = true; }
            else if (propagate && parents[i] >= 0 && changedScratch[parents[i]])
            {
                components[i] = AlsPrecisePose.Compose(localScratch[i], components[parents[i]]).Normalized();
                changedScratch[i] = true;
            }
        }
    }

    private static float Length(AlsPrecisePose initial, AlsPrecisePose next, AlsDoubleVector scale)
    {
        var ratio = initial.Scale.LengthSquared > 1e-8f ?
            new AlsDoubleVector(scale.X / initial.Scale.X, scale.Y / initial.Scale.Y, scale.Z / initial.Scale.Z) : AlsDoubleVector.One;
        var length = (float)M.Sqrt(((initial.Position - next.Position) * ratio).LengthSquared);
        if (!float.IsFinite(length)) throw new ArgumentException("Invalid rig reference scale/length.");
        return length;
    }

    private static AlsPrecisePose AlignSecondary(AlsPrecisePose pose, AlsDoubleVector primaryTarget,
        AlsDoubleVector target, AlsDoubleVector secondaryAxis, float weight)
    {
        if (weight <= 1e-8f) return pose;
        var axis = secondaryAxis.Rotate(pose.Rotation);
        if (target.NearlyZero(1e-4f) || axis.NearlyZero(1e-4f)) return pose;
        target = AlsTwoBoneIk.SafeNormal(target - primaryTarget * AlsDoubleVector.Dot(target, primaryTarget));
        var rotation = AlsTwoBoneIk.Between(axis, target);
        if (M.Abs(weight - 1f) > 1e-8f)
        {
            var rotationAxis = new AlsDoubleVector(rotation.X, rotation.Y, rotation.Z);
            rotationAxis = rotationAxis.LengthSquared < 1e-8f ? new(1, 0, 0) :
                rotationAxis * (1 / M.Sqrt(rotationAxis.LengthSquared));
            var angle = (float)(2 * M.Acos(M.Clamp(rotation.W, -1, 1)));
            var half = (double)(angle * M.Clamp(weight, 0, 1)) * .5;
            var sine = M.Sin(half);
            rotation = new(rotationAxis.X * sine, rotationAxis.Y * sine, rotationAxis.Z * sine, M.Cos(half));
        }
        return pose with { Rotation = (rotation * pose.Rotation).Normalized() };
    }

    internal static AlsQuaternion Slerp(AlsQuaternion a, AlsQuaternion b, float weight)
    {
        var cosine = AlsQuaternion.Dot(a, b); var sign = cosine >= 0 ? 1 : -1;
        cosine *= sign;
        var w0 = 1d - weight; var w1 = (double)weight * sign;
        if (cosine < .9999)
        {
            var angle = M.Acos(M.Clamp(cosine, -1, 1)); var inverse = 1 / M.Sin(angle);
            w0 = M.Sin(w0 * angle) * inverse; w1 = M.Sin(w1 * angle) * inverse;
        }
        return (a * w0 + b * w1).Normalized();
    }
    private static AlsDoubleVector TransformPosition(AlsPrecisePose pose, AlsDoubleVector position) =>
        (position * pose.Scale).Rotate(pose.Rotation) + pose.Position;
    private static AlsDoubleVector InversePosition(AlsPrecisePose pose, AlsDoubleVector position) =>
        (position - pose.Position).Rotate(pose.Rotation.Conjugate()) *
        new AlsDoubleVector(Reciprocal(pose.Scale.X), Reciprocal(pose.Scale.Y), Reciprocal(pose.Scale.Z));
    private static double Reciprocal(double value) => M.Abs(value) <= 1e-8f ? 0 : 1 / value;

    private static void Validate(in AlsRigTwoBoneIkInput input)
    {
        input.Root.Validate(); input.Joint.Validate(); input.End.Validate();
        input.InitialRoot.Validate(); input.InitialJoint.Validate(); input.InitialEnd.Validate(); input.Effector.Validate();
        if (input.HasPoleSpace) input.PoleSpace.Validate();
        if (!input.PrimaryAxis.IsFinite || !input.SecondaryAxis.IsFinite || !input.Pole.IsFinite ||
            !float.IsFinite(input.Weight) || !float.IsFinite(input.SecondaryAxisWeight) || !float.IsFinite(input.LengthA) ||
            !float.IsFinite(input.LengthB) || !float.IsFinite(input.StretchStartRatio) || !float.IsFinite(input.StretchMaximumRatio))
            throw new ArgumentException("Invalid rig IK input.");
    }
}
