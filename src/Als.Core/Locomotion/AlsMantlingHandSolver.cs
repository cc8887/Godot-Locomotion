using GodotAls.Core.Actions;

namespace GodotAls.Core.Locomotion;

// A pose control inside the character transaction. Scratch has no time/history;
// discarded frames cannot retain anchors or affect the next evaluation.
public sealed class AlsMantlingHandSolver
{
    private readonly AlsComponentPose _component;
    private readonly int[][] _chains;
    private readonly AlsTwistBoneReconstruction? _twists;
    public double MaximumResidualCm { get; private set; }
    public double MaximumRequestedCorrectionCm { get; private set; }
    public double MaximumReachExcessCm { get; private set; }
    public AlsMantlingHandSolver(ReadOnlySpan<string> names, ReadOnlySpan<int> parents,
        ReadOnlySpan<AlsPrecisePose> reference = default)
    {
        if (names.Length != parents.Length || names.IsEmpty) throw new ArgumentException("Invalid mantle arm layout.");
        var bones = names.ToArray(); _chains = new int[2][];
        for (var i = 0; i < parents.Length; i++)
            if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Mantle arms require parent-first bones.");
        for (var hand = 0; hand < 2; hand++)
        {
            var suffix = hand == 0 ? "_l" : "_r";
            var chain = new[] { Find("upperarm" + suffix), Find("lowerarm" + suffix), Find("hand" + suffix) };
            if (parents[chain[2]] != chain[1] || parents[chain[1]] != chain[0]) throw new ArgumentException("Incomplete mantle arm chain.");
            _chains[hand] = chain;
        }
        _component = new(parents);
        if (!reference.IsEmpty) _twists = new(names, parents, reference);
        int Find(string name) => Array.FindIndex(bones, n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is var index && index >= 0
            ? index : throw new ArgumentException("Missing mantle arm bone: " + name);
    }
    public void Evaluate(ReadOnlySpan<AlsLocalPose> source, Span<AlsLocalPose> output,
        in AlsLocalPose fbxComponentToWorld, in AlsPrecisePose ledgeToWorld,
        in AlsMantlingHandContact left, in AlsMantlingHandContact right, float montageWeight)
    {
        if (source.Length != output.Length || !float.IsFinite(montageWeight) || montageWeight is < 0 or > 1)
            throw new ArgumentException("Invalid mantle hand evaluation.");
        ledgeToWorld.Validate(); _component.Begin(source); MaximumResidualCm = 0;
        MaximumRequestedCorrectionCm = MaximumReachExcessCm = 0;
        var world = AlsFootIkCoordinates.FbxComponentToNativeWorld(fbxComponentToWorld);
        var ledge = ledgeToWorld;
        Solve(0, left); Solve(1, right); _component.Export(output);
        _twists?.Adjust(source, output);
        void Solve(int hand, AlsMantlingHandContact contact)
        {
            if (!float.IsFinite(contact.Weight) || contact.Weight is < 0 or > 1) throw new ArgumentException("Invalid mantle hand weight.");
            // The contact curve owns approach/support/release. Multiplying a
            // planted hand by the body's montage blend lets it slide through
            // the ledge when height matching enters mid-support.
            var alpha = montageWeight > AlsPoseBlender.WeightThreshold ? contact.Weight : 0;
            if (alpha <= AlsPoseBlender.WeightThreshold) return;
            if(!float.IsFinite(contact.TransitionLiftCm)||contact.TransitionLiftCm is <0 or >4)
                throw new ArgumentException("Invalid mantle transition clearance.");
            // The union of the top and front half-spaces is not convex. A
            // straight contact ramp can cut the ledge corner, so take a small
            // authored lift in its normal direction, zero at both endpoints.
            var anchor=contact.LedgeAnchor with {Position=contact.LedgeAnchor.Position+
                new AlsDoubleVector(0,0,4*contact.TransitionLiftCm*(1-alpha))};
            var desired = AlsPrecisePose.Relative(AlsPrecisePose.Compose(anchor, ledge), world);
            var chain = _chains[hand]; var upper = Native(_component.Component(chain[0]));
            var lower = Native(_component.Component(chain[1])); var end = Native(_component.Component(chain[2]));
            // Blend the effector in component space before solving. Blending
            // local arm joints after IK makes a release wrist travel on an arc
            // that can enter the ledge even when both endpoints are clear.
            var target = end.Position + (desired.Position-end.Position)*alpha;
            var delta = target - end.Position;
            var correction = System.Math.Sqrt(delta.LengthSquared);
            MaximumRequestedCorrectionCm = System.Math.Max(MaximumRequestedCorrectionCm, correction);
            var reach = System.Math.Sqrt((lower.Position-upper.Position).LengthSquared) +
                System.Math.Sqrt((end.Position-lower.Position).LengthSquared);
            MaximumReachExcessCm = System.Math.Max(MaximumReachExcessCm,
                System.Math.Sqrt((target-upper.Position).LengthSquared)-reach);
            // Export repair limits certify a nominal asset. Runtime contact
            // also adapts that asset to ledge height and the composed pose.
            // Solve the actual fixed anchor with immutable limb lengths;
            // genuine reach failures remain visible in the residual.
            AlsTwoBoneIk.Solve(ref upper, ref lower, ref end, lower.Position, target);
            MaximumResidualCm = System.Math.Max(MaximumResidualCm, System.Math.Sqrt((end.Position - target).LengthSquared));
            var bias=AlsQuaternion.Dot(end.Rotation,desired.Rotation)>=0 ? 1 : -1;
            end = end with { Rotation = (end.Rotation*(1-alpha)+desired.Rotation*(bias*alpha)).Normalized() };
            Span<AlsPrecisePose> solved = stackalloc AlsPrecisePose[3];
            solved[0] = Fbx(upper); solved[1] = Fbx(lower); solved[2] = Fbx(end);
            _component.Apply(chain, solved, 1);
        }
    }
    private static AlsPrecisePose Native(in AlsPrecisePose p) => new(new(p.Position.X * 100, -p.Position.Y * 100, p.Position.Z * 100),
        new(-p.Rotation.X, p.Rotation.Y, -p.Rotation.Z, p.Rotation.W), p.Scale);
    private static AlsPrecisePose Fbx(in AlsPrecisePose p) => new(new(p.Position.X * .01, -p.Position.Y * .01, p.Position.Z * .01),
        new(-p.Rotation.X, p.Rotation.Y, -p.Rotation.Z, p.Rotation.W), p.Scale);
}
