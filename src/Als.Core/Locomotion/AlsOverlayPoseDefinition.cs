using System.Numerics;

namespace GodotAls.Core.Locomotion;

public enum AlsOverlayPoseKind { Source, Root, StateRoot, Machine, Inertialization, TwoWay, MultiWay, LocalAdditive, MeshAdditive, BlendList, ModifyCurve }
public enum AlsOverlayValueKind { Constant, BasePoseN, BasePoseClf, Forward, Backward, Left, Right, AccelerationX, AccelerationY, AccelerationZ, LandPrediction, OverrideState, Aiming, Curve }
public readonly record struct AlsOverlayValue(AlsOverlayValueKind Kind, float Constant = 0, string Curve = "");
public readonly record struct AlsOverlayAlphaPolicy(float Scale, float Bias, bool Clamp, float Minimum, float Maximum,
    bool Interpolate, float Increasing, float Decreasing, bool MapRange = false,
    float InputMin = 0, float InputMax = 1, float OutputMin = 0, float OutputMax = 1);

// Exposed graph inputs retain their UE domains. Vector components are converted
// from double to float at the linked animation pin, not before the frame bridge.
public readonly record struct AlsOverlayPoseInput(float BasePoseN, float BasePoseClf, Vector4 Velocity,
    double AccelerationX, double AccelerationY, double AccelerationZ, double LandPrediction,
    int OverrideState, bool Aiming)
{
    public float Read(AlsOverlayValue value) => value.Kind switch
    {
        AlsOverlayValueKind.Constant => value.Constant,
        AlsOverlayValueKind.BasePoseN => BasePoseN, AlsOverlayValueKind.BasePoseClf => BasePoseClf,
        AlsOverlayValueKind.Forward => Velocity.X, AlsOverlayValueKind.Backward => Velocity.Y,
        AlsOverlayValueKind.Left => Velocity.Z, AlsOverlayValueKind.Right => Velocity.W,
        AlsOverlayValueKind.AccelerationX => (float)AccelerationX, AlsOverlayValueKind.AccelerationY => (float)AccelerationY,
        AlsOverlayValueKind.AccelerationZ => (float)AccelerationZ, AlsOverlayValueKind.LandPrediction => (float)LandPrediction,
        AlsOverlayValueKind.OverrideState => OverrideState, AlsOverlayValueKind.Aiming => Aiming ? 1 : 0,
        _ => throw new ArgumentException("Curve input requires the committed curve bank.")
    };
    public void Validate()
    {
        if (!float.IsFinite(BasePoseN) || !float.IsFinite(BasePoseClf) || !float.IsFinite(Velocity.LengthSquared()) ||
            !double.IsFinite(AccelerationX) || !double.IsFinite(AccelerationY) || !double.IsFinite(AccelerationZ) || !double.IsFinite(LandPrediction) ||
            !float.IsFinite((float)AccelerationX) || !float.IsFinite((float)AccelerationY) || !float.IsFinite((float)AccelerationZ) || !float.IsFinite((float)LandPrediction))
            throw new ArgumentException("Invalid Overlay pose inputs.");
    }
}

public sealed class AlsOverlayPoseNode
{
    private readonly int[] _inputs;
    private readonly AlsOverlayValue[] _values;
    private readonly float[] _times;
    private readonly string[] _curves;
    public int Index { get; }
    public string Path { get; }
    public AlsOverlayPoseKind Kind { get; }
    public int Source { get; }
    public int Machine { get; }
    public AlsOverlayAlphaPolicy Alpha { get; }
    public bool InertialTransition { get; }
    public bool ResetChild { get; }
    public AlsTransitionBlend Blend { get; }
    public ReadOnlySpan<int> Inputs => _inputs;
    public ReadOnlySpan<AlsOverlayValue> Values => _values;
    public ReadOnlySpan<float> BlendTimes => _times;
    public ReadOnlySpan<string> CurveNames => _curves;
    public AlsOverlayPoseNode(int index, string path, AlsOverlayPoseKind kind, int[] inputs, AlsOverlayValue[] values,
        AlsOverlayAlphaPolicy alpha = default, int source = -1, int machine = -1, float[]? times = null,
        bool inertial = false, bool resetChild = false, AlsTransitionBlend blend = AlsTransitionBlend.Linear, string[]? curves = null)
    {
        Index = index; Path = path; Kind = kind; Source = source; Machine = machine; Alpha = alpha;
        InertialTransition = inertial; ResetChild = resetChild; Blend = blend;
        _inputs = (int[])inputs.Clone(); _values = (AlsOverlayValue[])values.Clone();
        _times = times is null ? [] : (float[])times.Clone(); _curves = curves is null ? [] : (string[])curves.Clone();
    }
}

public sealed class AlsOverlayPoseDefinition
{
    private readonly AlsOverlayPoseNode[] _nodes;
    private readonly AlsOverlayPoseNode?[] _lookup;
    public AlsOverlayStateGraph States { get; }
    public string BindingDigest => States.BindingDigest;
    public int RootIndex { get; }
    public int Capacity => _lookup.Length;
    public ReadOnlySpan<AlsOverlayPoseNode> Nodes => _nodes;
    public AlsOverlayPoseDefinition(AlsOverlayStateGraph states, int capacity, int root, AlsOverlayPoseNode[] nodes)
    {
        ArgumentNullException.ThrowIfNull(states); States = states;
        if (capacity <= 0 || (uint)root >= capacity || nodes.Length == 0) throw new ArgumentException("Invalid Overlay graph layout.");
        _nodes = (AlsOverlayPoseNode[])nodes.Clone(); _lookup = new AlsOverlayPoseNode[capacity]; RootIndex = root;
        foreach (var node in nodes)
        {
            if ((uint)node.Index >= capacity || _lookup[node.Index] is not null || (uint)node.Kind > 10 || string.IsNullOrEmpty(node.Path))
                throw new ArgumentException("Duplicate or invalid Overlay node.");
            _lookup[node.Index] = node;
        }
        if (Node(root).Kind != AlsOverlayPoseKind.Root) throw new ArgumentException("Missing Overlay root.");
        var visited = new HashSet<int>(); var active = new HashSet<int>(); Visit(root);
        if (visited.Count != nodes.Length) throw new ArgumentException("Unreachable Overlay pose node.");
        void Visit(int index)
        {
            if (visited.Contains(index)) return;
            if (!active.Add(index)) throw new ArgumentException("Cyclic Overlay pose graph.");
            var node = Node(index);
            foreach (var input in node.Inputs) Visit(input);
            active.Remove(index); visited.Add(index);
        }
        foreach (var node in nodes)
        {
            var count = node.Inputs.Length; var values = node.Values.Length;
            var valid = node.Kind switch
            {
                AlsOverlayPoseKind.Source => count == 0 && values == 0 && node.Source >= 0,
                AlsOverlayPoseKind.Machine => node.Machine is >= 0 and < 5 && values == 0 &&
                    count == states.Machines[node.Machine].States.Length - (node.Machine == 0 ? 1 : 0),
                AlsOverlayPoseKind.Root or AlsOverlayPoseKind.StateRoot or AlsOverlayPoseKind.Inertialization => count == 1 && values == 0,
                AlsOverlayPoseKind.TwoWay or AlsOverlayPoseKind.LocalAdditive or AlsOverlayPoseKind.MeshAdditive => count == 2 && values == 1,
                AlsOverlayPoseKind.MultiWay => count is 2 or 4 && values == count,
                AlsOverlayPoseKind.BlendList => count is 2 or 4 && values == 1 && node.BlendTimes.Length == count,
                AlsOverlayPoseKind.ModifyCurve => count == 1 && node.CurveNames.Length > 0 && values == node.CurveNames.Length + 1,
                _ => false
            };
            if (!valid) throw new ArgumentException("Invalid Overlay pose operation.");
            foreach (var value in node.Values)
                if ((uint)value.Kind > 13 || !float.IsFinite(value.Constant) || value.Kind == AlsOverlayValueKind.Curve && string.IsNullOrEmpty(value.Curve))
                    throw new ArgumentException("Invalid Overlay exposed value.");
            foreach (var time in node.BlendTimes) if (!float.IsFinite(time) || time < 0) throw new ArgumentException("Invalid Overlay blend time.");
        }
    }
    public AlsOverlayPoseNode Node(int index) => (uint)index < _lookup.Length && _lookup[index] is { } node ? node :
        throw new ArgumentException("Unknown Overlay pose node: " + index);
}
