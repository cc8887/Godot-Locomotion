using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Resource parameter adaptation only; solve and transform math are shared Core.
internal static class LyraFootPlantRigMath
{
    public static AlsDoubleVector EulerZYX(AlsQuaternion q) => q.ToEulerZYXDegrees();
    public static AlsPrecisePose Inverse(AlsPrecisePose pose) => AlsPrecisePose.Inverse(pose);
    public static AlsPrecisePose Aim(AlsPrecisePose pose, LyraRigAimTarget primary, LyraRigAimTarget secondary, float weight)
    {
        static AlsRigAimTarget Convert(LyraRigAimTarget target)
        {
            if (target.Space != "None") throw new NotSupportedException("Changed FootPlant aim space.");
            return new(target.Weight, target.Axis, target.Target, target.Kind == "Location" ?
                AlsRigAimTargetKind.Location : AlsRigAimTargetKind.Direction);
        }
        return AlsRigAim.Aim(pose, Convert(primary), Convert(secondary), weight);
    }
    public static void SolveIk(ref AlsPrecisePose a, ref AlsPrecisePose b, ref AlsPrecisePose c,
        AlsDoubleVector pole, AlsDoubleVector primary, AlsDoubleVector secondary, float upperLength, float lowerLength,
        bool stretch, float start, float maximum)
    {
        var result = AlsRigTwoBoneIk.Solve(new(a, b, c, a, b, c, c, primary, secondary,
            1, pole, false, false, AlsPrecisePose.Identity, stretch, start, maximum, 1, upperLength, lowerLength));
        a = result.Root; b = result.Joint; c = result.End;
    }
}
