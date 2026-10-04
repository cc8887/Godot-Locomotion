namespace GodotAls.Core.Locomotion;

// Bone-space TwoBoneIK with the source node's default twist/stretch/relative
// rotation settings. Uses the FCSPose local blend, including cached descendants.
public sealed class AlsTwoBoneIkController
{
    private readonly AlsComponentPose _pose;
    private readonly int[] _chain;
    private readonly int _effector, _joint, _bones;
    private readonly AlsDoubleVector _jointOffset;
    private readonly bool _takeEffectorRotation;

    public AlsTwoBoneIkController(ReadOnlySpan<int> parents, int endBone, int effectorBone,
        int jointBone, AlsDoubleVector jointOffset, bool takeEffectorRotation)
    {
        for (var bone = 0; bone < parents.Length; bone++)
            if (parents[bone] < -1 || parents[bone] >= bone)
                throw new ArgumentException("TwoBoneIK requires a parent-first hierarchy.");
        if ((uint)endBone >= parents.Length || (uint)effectorBone >= parents.Length ||
            (uint)jointBone >= parents.Length || !jointOffset.IsFinite)
            throw new ArgumentException("Invalid TwoBoneIK target.");
        var lower = parents[endBone];
        var upper = lower < 0 ? -1 : parents[lower];
        if (upper < 0) throw new ArgumentException("TwoBoneIK requires a three-bone chain.");
        _chain = [upper, lower, endBone];
        _bones = parents.Length;
        _pose = new AlsComponentPose(parents);
        _effector = effectorBone; _joint = jointBone;
        _jointOffset = jointOffset; _takeEffectorRotation = takeEffectorRotation;
    }

    public void Evaluate(ReadOnlySpan<AlsPrecisePose> input, AlsDoubleVector effectorOffset,
        float alpha, Span<AlsPrecisePose> output)
    {
        if (input.Length != _bones || output.Length != _bones || input.Overlaps(output) ||
            !effectorOffset.IsFinite || !float.IsFinite(alpha) || alpha is < 0 or > 1)
            throw new ArgumentException("Invalid TwoBoneIK pose buffers or alpha.");
        if (alpha <= AlsPoseBlender.WeightThreshold)
        {
            input.CopyTo(output);
            return;
        }
        _pose.Begin(input);
        Evaluate(_pose,effectorOffset,alpha);
        _pose.Export(output);
    }
    internal void Evaluate(AlsComponentPose pose,AlsDoubleVector effectorOffset,float alpha)
    {
        if(alpha<=AlsPoseBlender.WeightThreshold)return;
        // Match the node's local reads before any component-space access.
        _ = pose.Local(_chain[2]); _ = pose.Local(_chain[1]); _ = pose.Local(_chain[0]);
        var lower = pose.Component(_chain[1]);
        var upper = pose.Component(_chain[0]);
        var end = pose.Component(_chain[2]);
        var effector = pose.Component(_effector);
        var joint = pose.Component(_joint);
        var targetPosition = effector.Position + (effector.Scale * effectorOffset).Rotate(effector.Rotation);
        var jointPosition = joint.Position + (joint.Scale * _jointOffset).Rotate(joint.Rotation);
        AlsTwoBoneIk.Solve(ref upper, ref lower, ref end, jointPosition, targetPosition);
        if (_takeEffectorRotation) end = end with { Rotation = effector.Rotation };
        Span<AlsPrecisePose> changes = stackalloc AlsPrecisePose[3];
        changes[0] = upper; changes[1] = lower; changes[2] = end;
        pose.Apply(_chain, changes, alpha);
    }
}
