namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRigDoubleRange(double A, double B)
{
    public float Interpolate(double amount) => (float)(A + (B - A) * amount);
}

public sealed record AlsRefactoredLegRigDefinition(float PoleDistance, float PoleHalfLife,
    float OffsetFrequency, float OffsetDamping, float OffsetTargetVelocity, float MaximumStretch,
    AlsRigDoubleRange MinimumPelvisDistance, AlsFootRotationInterval Swing1,
    AlsRigDoubleRange Swing2Minimum, AlsRigDoubleRange Swing2Maximum,
    AlsFootRotationInterval Twist, float RotationHalfLife, float SecondaryAxisWeight);

public readonly record struct AlsRigLegBones(int Pelvis, int Thigh, int Calf, int Foot,
    AlsDoubleVector PrimaryAxis, AlsDoubleVector SecondaryAxis);

public readonly record struct AlsRefactoredLegRigState(AlsFootOffsetLocationState Location,
    AlsFootPoleState Pole, AlsRigVectorDamperState SmoothedPole, AlsFootOffsetRotationState Rotation)
{
    public static AlsRefactoredLegRigState Initial => new(default, AlsFootPoleState.Initial, default, default);
}

public readonly record struct AlsRefactoredLegRigInput(float DeltaTime, AlsDoubleVector TargetLocation,
    AlsQuaternion TargetRotation, float OffsetZ, AlsDoubleVector OffsetNormal, float PelvisOffset,
    float LegLength, double MovingAmount, float Weight)
{
    public AlsFootContactTarget Contact { get; init; }
}

// The authored ApplyFootIk function, operating on the caller's CANDIDATE native
// component pose after the pelvis write. A skipped outer branch must not call
// this function; Weight=0 still evaluates its springs, pole and ankle history.
public static class AlsRefactoredLegRig
{
    public static AlsRefactoredLegRigState Evaluate(in AlsRefactoredLegRigState previous,
        AlsRefactoredLegRigDefinition definition, in AlsRigLegBones bones, in AlsRefactoredLegRigInput input,
        ReadOnlySpan<AlsPrecisePose> reference, Span<AlsPrecisePose> candidate, ReadOnlySpan<int> parents,
        Span<AlsPrecisePose> localScratch, Span<bool> changedScratch)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (reference.Length != candidate.Length || reference.Overlaps(candidate) ||
            reference.Overlaps(localScratch) || !double.IsFinite(input.MovingAmount) ||
            !float.IsFinite(input.Weight) || !float.IsFinite(input.Contact.Weight) || input.Contact.Weight is < 0 or > 1)
            throw new ArgumentException("Invalid rig candidate/reference inputs.");
        AlsRigHierarchyWrites.Validate(candidate, parents, localScratch, changedScratch);
        if (bones.Pelvis < 0 || bones.Thigh <= bones.Pelvis || bones.Calf <= bones.Thigh ||
            bones.Foot <= bones.Calf || bones.Foot >= candidate.Length)
            throw new ArgumentException("Invalid rig leg indices.");
        var location = AlsFootOffsetLocationModel.Evaluate(previous.Location, new(input.DeltaTime,
            candidate[bones.Pelvis].Position.Z, candidate[bones.Thigh].Position, input.TargetLocation,
            input.OffsetZ, input.PelvisOffset, input.LegLength, definition.MinimumPelvisDistance.Interpolate(input.MovingAmount),
            definition.MaximumStretch, definition.OffsetFrequency, definition.OffsetDamping, definition.OffsetTargetVelocity));
        if (input.Contact.Weight > 0)
        {
            if (!float.IsFinite(input.Contact.Weight) || input.Contact.Weight > 1 || !input.Contact.Location.IsFinite)
                throw new ArgumentException("Invalid pinned contact.");
            var target = location.FootLocation * (1 - input.Contact.Weight) + input.Contact.Location * input.Contact.Weight;
            var thigh = candidate[bones.Thigh].Position;
            var leg = target - thigh; var maximum = input.LegLength * definition.MaximumStretch;
            var constrained = leg.LengthSquared > (double)maximum * maximum;
            if (constrained) leg *= maximum / System.Math.Sqrt(leg.LengthSquared);
            location = location with { FootLocation = thigh + leg, ContactApplied = true, ContactConstrained = constrained };
        }

        // These pure nodes are first consumed by IK, before IK changes the chain.
        // Success is not a graph branch: the native pole unit retains old outputs.
        var pole = AlsFootPoleModel.Evaluate(previous.Pole, candidate[bones.Thigh].Position,
            candidate[bones.Calf].Position, candidate[bones.Foot].Position);
        var smooth = AlsFootPoleModel.Smooth(previous.SmoothedPole,
            pole.ItemBLocation + pole.Direction * definition.PoleDistance, input.DeltaTime, definition.PoleHalfLife);
        var ik = AlsRigTwoBoneIk.Solve(new(candidate[bones.Thigh], candidate[bones.Calf], candidate[bones.Foot],
            reference[bones.Thigh], reference[bones.Calf], reference[bones.Foot],
            new(location.FootLocation, input.TargetRotation, AlsDoubleVector.One),
            bones.PrimaryAxis, bones.SecondaryAxis, definition.SecondaryAxisWeight, smooth.Current,
            false, false, AlsPrecisePose.Identity, false, .75f, 1.25f, input.Weight, 0, 0));
        AlsRigTwoBoneIk.Apply(ik, candidate, parents, bones.Thigh, bones.Calf, bones.Foot, true, localScratch, changedScratch);

        // Read AFTER IK, including its partial component-space weighting.
        var rotation = AlsFootOffsetRotationModel.Evaluate(previous.Rotation, new(input.DeltaTime,
            reference[bones.Calf].Rotation, reference[bones.Foot].Rotation,
            candidate[bones.Calf].Rotation, candidate[bones.Foot].Rotation, input.TargetRotation, input.OffsetNormal,
            definition.Swing1, new(definition.Swing2Minimum.Interpolate(input.MovingAmount), definition.Swing2Maximum.Interpolate(input.MovingAmount)),
            definition.Twist, definition.RotationHalfLife));
        AlsRigHierarchyWrites.SetRotation(candidate, parents, bones.Foot, rotation.FootRotation, input.Weight, localScratch, changedScratch);
        return new(location, pole, smooth, rotation);
    }
}

// Current Global SetRotation / SetTranslation with propagation enabled. Used by
// the graph executor; callers provide separate candidate and scratch buffers.
public static class AlsRigHierarchyWrites
{
    public static void SetRotation(Span<AlsPrecisePose> poses, ReadOnlySpan<int> parents, int bone,
        AlsQuaternion rotation, float weight, Span<AlsPrecisePose> localScratch, Span<bool> changedScratch)
    {
        Validate(poses, parents, localScratch, changedScratch);
        if ((uint)bone >= poses.Length || !float.IsFinite(weight)) throw new ArgumentException("Invalid rig write.");
        // SetTransform uses strict < SMALL_NUMBER; TwoBoneIK uses <=.
        if (weight < 1e-8f) return;
        var target = poses[bone] with { Rotation = rotation };
        target.Validate();
        if (weight < 1f - 1e-8f)
            target = target with { Rotation = AlsRigTwoBoneIk.Slerp(poses[bone].Rotation, rotation, weight) };
        SetGlobal(poses, parents, bone, target, localScratch, changedScratch);
    }

    public static void SetGlobal(Span<AlsPrecisePose> poses, ReadOnlySpan<int> parents, int bone,
        AlsPrecisePose target, Span<AlsPrecisePose> localScratch, Span<bool> changedScratch)
    {
        Validate(poses, parents, localScratch, changedScratch);
        if ((uint)bone >= poses.Length) throw new ArgumentException("Invalid rig bone.");
        target.Validate();
        // SetTranslation/SetRotation read the current global transform before
        // writing it. UE therefore takes the clean-cache early return in
        // URigHierarchy::SetTransform / FRigComputedTransform::Equals. Even a
        // tiny skipped rotation matters to the propagated ball/toe position.
        if (NativeEquals(poses[bone], target)) return;
        for (var i = 0; i < poses.Length; i++)
            localScratch[i] = parents[i] < 0 ? poses[i] : AlsPrecisePose.Relative(poses[i], poses[parents[i]]);
        changedScratch[..poses.Length].Clear();
        poses[bone] = target; changedScratch[bone] = true;
        for (var i = bone + 1; i < poses.Length; i++)
            if (parents[i] >= 0 && changedScratch[parents[i]])
            {
                poses[i] = AlsPrecisePose.Compose(localScratch[i], poses[parents[i]]).Normalized();
                changedScratch[i] = true;
            }
    }

    private static bool NativeEquals(AlsPrecisePose a, AlsPrecisePose b)
    {
        const float tolerance = 1e-4f; // native component centimeters, unit scale/quaternion components
        var p = a.Position - b.Position; var s = a.Scale - b.Scale;
        if (!p.NearlyZero(tolerance) || !s.NearlyZero(tolerance)) return false;
        var x = a.Rotation; var y = b.Rotation;
        static bool Near(double a, double b) => System.Math.Abs(a - b) <= tolerance;
        return Near(x.X,y.X) && Near(x.Y,y.Y) && Near(x.Z,y.Z) && Near(x.W,y.W) ||
            Near(x.X,-y.X) && Near(x.Y,-y.Y) && Near(x.Z,-y.Z) && Near(x.W,-y.W);
    }

    internal static void Validate(Span<AlsPrecisePose> poses, ReadOnlySpan<int> parents,
        Span<AlsPrecisePose> localScratch, Span<bool> changedScratch)
    {
        if (poses.Length != parents.Length || localScratch.Length < poses.Length || changedScratch.Length < poses.Length ||
            poses.Overlaps(localScratch)) throw new ArgumentException("Invalid rig hierarchy buffers.");
        for (var i = 0; i < parents.Length; i++)
            if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Rig hierarchy must be parent-first.");
    }
}
