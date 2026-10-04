namespace GodotAls.Core.Locomotion;

// FAnimNode_HandIKRetargeting. The owner supplies UE component axes/cm and
// applies the returned single-bone transform with its skeletal-control blend.
public static class AlsHandIkRetargeting
{
    public static AlsPrecisePose Retarget(in AlsPrecisePose gun,
        in AlsPrecisePose leftFk, in AlsPrecisePose rightFk,
        in AlsPrecisePose leftIk, in AlsPrecisePose rightIk, float handFkWeight)
    {
        gun.Validate(); leftFk.Validate(); rightFk.Validate(); leftIk.Validate(); rightIk.Validate();
        if (!float.IsFinite(handFkWeight)) throw new ArgumentOutOfRangeException(nameof(handFkWeight));
        AlsDoubleVector offset;
        if (handFkWeight >= 1 - AlsPoseBlender.WeightThreshold)
            offset = rightFk.Position - rightIk.Position;
        else if (handFkWeight <= AlsPoseBlender.WeightThreshold)
            offset = leftFk.Position - leftIk.Position;
        else
        {
            var fk = leftFk.Position + (rightFk.Position - leftFk.Position) * handFkWeight;
            var ik = leftIk.Position + (rightIk.Position - leftIk.Position) * handFkWeight;
            offset = fk - ik;
        }
        return offset.NearlyZero(1e-4f) ? gun : gun with { Position = gun.Position + offset };
    }
}
