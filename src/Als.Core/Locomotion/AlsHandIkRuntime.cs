using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsHandIkNode(int Index, string Hand, string Effector, string AlphaProperty);
public sealed class AlsHandIkDefinition
{
    private readonly AlsHandIkNode[] _nodes;
    public ReadOnlySpan<AlsHandIkNode> Nodes => _nodes;
    public AlsHandIkDefinition(AlsHandIkNode[] nodes)
    {
        if (nodes.Length != 2 || nodes[0].Hand != "hand_l" || nodes[1].Hand != "hand_r" ||
            nodes[0].Index < 0 || nodes[1].Index < 0 || nodes[0].Index == nodes[1].Index ||
            nodes.Any(n => string.IsNullOrWhiteSpace(n.Effector))) throw new ArgumentException("Invalid original hand IK order.");
        foreach (var node in nodes) _ = default(AlsLayeringInput).GetValue(node.AlphaProperty);
        _nodes = (AlsHandIkNode[])nodes.Clone();
    }
}

// Original two hand controllers, evaluated left then right on the same FCSPose.
// Bone controls preserve curves and never update clocks or dispatch events.
public sealed class AlsHandIkRuntime
{
    private readonly AlsHandIkDefinition _definition;
    private readonly int[] _parents, _effectors;
    private readonly int[][] _chains;
    private readonly float[] _alphas = new float[2];
    private readonly float[] _committedAlphas = new float[2];
    private readonly AlsComponentPose _component;
    private readonly AlsLocalPose[] _output;
    private readonly AlsInertialCurve[] _curves;
    private readonly double _units;
    private AlsFrameIdentity _identity;
    private bool _prepared, _evaluated, _unvisited;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public AlsFrameIdentity CommittedPoseIdentity { get; private set; }
    public int EvaluatedHands { get; private set; }
    public float LeftAlpha => (_prepared ? _alphas : _committedAlphas)[0];
    public float RightAlpha => (_prepared ? _alphas : _committedAlphas)[1];
    public ReadOnlySpan<AlsLocalPose> Pose => _evaluated ? _output : throw new InvalidOperationException("Hand IK has not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _evaluated ? _curves : throw new InvalidOperationException("Hand IK has not evaluated.");
    public AlsHandIkRuntime(AlsHandIkDefinition definition, ReadOnlySpan<string> bones, ReadOnlySpan<int> parents,
        int curveCount, double unitsPerCentimeter = .01)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (bones.IsEmpty || bones.Length != parents.Length || curveCount < 0 || !double.IsFinite(unitsPerCentimeter) || unitsPerCentimeter <= 0)
            throw new ArgumentException("Invalid hand IK layout or units.");
        var names = bones.ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new ArgumentException("Duplicate hand IK bone names.");
        for (var i = 0; i < parents.Length; i++) if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Hand IK requires parent-first bones.");
        _definition = definition; _parents = parents.ToArray(); _units = unitsPerCentimeter;
        _effectors = new int[2]; _chains = new int[2][];
        for (var i = 0; i < 2; i++)
        {
            var node = definition.Nodes[i]; var end = Find(node.Hand);
            var lower = _parents[end]; var upper = lower < 0 ? -1 : _parents[lower];
            if (upper < 0) throw new ArgumentException("Incomplete original arm chain.");
            _chains[i] = [upper, lower, end]; _effectors[i] = Find(node.Effector);
        }
        _component = new(parents); _output = new AlsLocalPose[bones.Length]; _curves = new AlsInertialCurve[curveCount];
        int Find(string name)
        {
            var index = Array.FindIndex(names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : throw new ArgumentException("Missing hand IK bone: " + name);
        }
    }
    public void Prepare(in AlsLayeringInput input, bool updateSource = true)
    {
        if (_prepared || input.Identity.SlotGeneration == 0 || CommittedIdentity != default &&
            (input.Identity.CharacterId != CommittedIdentity.CharacterId || input.Identity.SlotGeneration != CommittedIdentity.SlotGeneration ||
                input.Identity.FrameId <= CommittedIdentity.FrameId)) throw new InvalidOperationException("Invalid hand IK candidate.");
        _committedAlphas.CopyTo(_alphas, 0);
        for (var hand = 0; updateSource && hand < 2; hand++)
        {
            var value = input.GetValue(_definition.Nodes[hand].AlphaProperty);
            if (!double.IsFinite(value) || !float.IsFinite((float)value)) throw new ArgumentException("Invalid hand IK alpha.");
            _alphas[hand] = System.Math.Clamp((float)value, 0, 1);
        }
        _identity = input.Identity; _prepared = true; _evaluated = false; _unvisited = !updateSource; EvaluatedHands = 0;
    }
    public void Evaluate(ReadOnlySpan<AlsLocalPose> source, ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (!_prepared || _unvisited || _evaluated) throw new InvalidOperationException("Hand IK requires one visited evaluation.");
        try
        {
            if (curves.Length != _curves.Length) throw new ArgumentException("Hand IK curve layout differs.");
            foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Nonfinite hand IK curve.");
            _component.Begin(source);
            for (var hand = 0; hand < 2; hand++)
            {
                if (_alphas[hand] <= AlsPoseBlender.WeightThreshold) continue;
                Solve(hand); EvaluatedHands++;
            }
            _component.Export(_output); curves.CopyTo(_curves); _evaluated = true;
        }
        catch { Cancel(); throw; }
    }
    private void Solve(int hand)
    {
        var chain = _chains[hand];
        // Preserve native query order. The first hand can cause the second
        // hand's target ancestors to be cached before their transforms change.
        _ = _component.Local(chain[2]); _ = _component.Local(chain[1]); _ = _component.Local(chain[0]);
        var lower = _component.Component(chain[1]); var upper = _component.Component(chain[0]);
        var end = _component.Component(chain[2]); var target = _component.Component(_effectors[hand]);
        // Joint target is hand + ParentBoneSpace + zero offset: current elbow.
        var pole = _component.Component(_parents[chain[2]]).Position;
        var r = ToNative(upper); var j = ToNative(lower); var e = ToNative(end);
        AlsTwoBoneIk.Solve(ref r, ref j, ref e, ToNative(pole), ToNative(target.Position));
        Span<AlsPrecisePose> solved = stackalloc AlsPrecisePose[3];
        solved[0] = upper with { Position = FromNative(r.Position), Rotation = FromNative(r.Rotation) };
        solved[1] = lower with { Position = FromNative(j.Position), Rotation = FromNative(j.Rotation) };
        solved[2] = end with { Position = FromNative(e.Position), Rotation = target.Rotation };
        _component.Apply(chain, solved, _alphas[hand]);
    }
    private AlsDoubleVector ToNative(AlsDoubleVector v) => new(-v.Z / _units, v.X / _units, v.Y / _units);
    private AlsDoubleVector FromNative(AlsDoubleVector v) => new(v.Y * _units, v.Z * _units, -v.X * _units);
    private AlsPrecisePose ToNative(AlsPrecisePose pose) => new(ToNative(pose.Position),
        new(pose.Rotation.Z, -pose.Rotation.X, -pose.Rotation.Y, pose.Rotation.W), AlsDoubleVector.One);
    private static AlsQuaternion FromNative(AlsQuaternion q) => new(-q.Y, -q.Z, q.X, q.W);
    public void ValidateCommit(AlsFrameIdentity identity)
    { if (!_prepared || !(_evaluated || _unvisited) || identity != _identity) throw new InvalidOperationException("Hand IK has no complete candidate."); }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); CommittedIdentity = identity;
        if (_evaluated) CommittedPoseIdentity = identity;
        _alphas.CopyTo(_committedAlphas, 0); Cancel();
    }
    public void Cancel() { _prepared = _evaluated = false; }
}
