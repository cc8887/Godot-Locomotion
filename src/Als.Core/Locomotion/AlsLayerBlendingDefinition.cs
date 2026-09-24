namespace GodotAls.Core.Locomotion;

public enum AlsLayerPoseKind
{
    Input, Root, SaveCache, UseCache, DynamicLocalAdditive, DynamicMeshAdditive,
    ApplyLocalAdditive, ApplyMeshAdditive, TwoWayBlend, Slot, LayeredBlend,
    CurveAccumulate, CurveOverride, CurveReset, NormalizedMultiWayBlend,
}

public enum AlsLayerAlphaKind { Constant, Property, Curve }
public enum AlsLayerPropertySchema { V4, Refactored }
public enum AlsLayerCurveBlendMode { Override, BlendByWeight }
public readonly record struct AlsLayerBranchFilter(string Bone, int Depth);
public readonly record struct AlsLayerAlpha(AlsLayerAlphaKind Kind, double Value = 0, string Name = "");

// Inputs are source compiled-node identities in native pin order: Base then
// Additive; A then B; or BasePose followed by BlendPoses. UseCache points at its Save.
public sealed record AlsLayerPoseNode(int Index, string Name, AlsLayerPoseKind Kind,
    int[] Inputs, AlsLayerAlpha[] Alphas, string Label = "", bool MeshSpaceRotation = false,
    AlsLayerCurveBlendMode CurveBlendMode = AlsLayerCurveBlendMode.Override,
    AlsLayerBranchFilter[][]? Filters = null,string[]? ModifiedCurves=null,float[]? ModifiedValues=null);

public sealed class AlsLayerBlendingDefinition
{
    private readonly Dictionary<int, AlsLayerPoseNode> _byIndex;
    private readonly AlsLayerPoseNode[] _nodes;
    public string Source { get; }
    public int RootIndex { get; }
    public ReadOnlySpan<AlsLayerPoseNode> Nodes => _nodes;
    public AlsPoseCacheDefinition Caches { get; }
    public AlsLayerPropertySchema PropertySchema { get; }

    public AlsLayerBlendingDefinition(string source, int compiledPropertyCount, int rootIndex,
        IEnumerable<AlsLayerPoseNode> nodes, int[] cacheUpdateOrder, AlsLayerPropertySchema propertySchema=AlsLayerPropertySchema.V4)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        if(!Enum.IsDefined(propertySchema))throw new ArgumentException("Unknown layering property schema.");
        PropertySchema=propertySchema;
        _nodes = nodes.Select(n => n with { Inputs = (int[])n.Inputs.Clone(), Alphas = (AlsLayerAlpha[])n.Alphas.Clone(),
            Filters = n.Filters?.Select(f => (AlsLayerBranchFilter[])f.Clone()).ToArray(),
            ModifiedCurves=n.ModifiedCurves?.ToArray(),ModifiedValues=n.ModifiedValues?.ToArray() }).ToArray();
        _byIndex = _nodes.ToDictionary(n => n.Index);
        if (_nodes.Length == 0 || _nodes.Any(n => n.Index < 0 || n.Index >= compiledPropertyCount ||
                n.Inputs.Any(i => !_byIndex.ContainsKey(i))) ||
            !_byIndex.TryGetValue(rootIndex, out var root) || root.Kind != AlsLayerPoseKind.Root ||
            !_nodes.Where(n => n.Kind == AlsLayerPoseKind.SaveCache).Select(n => n.Index).Order()
                .SequenceEqual(cacheUpdateOrder.Order()))
            throw new ArgumentException("Invalid LayerBlending node or cache layout.");
        foreach (var node in _nodes)
        {
            var expectedInputs = node.Kind switch
            {
                AlsLayerPoseKind.Input => 0,
                AlsLayerPoseKind.DynamicLocalAdditive or AlsLayerPoseKind.DynamicMeshAdditive or
                    AlsLayerPoseKind.ApplyLocalAdditive or AlsLayerPoseKind.ApplyMeshAdditive or AlsLayerPoseKind.TwoWayBlend or
                    AlsLayerPoseKind.CurveAccumulate or AlsLayerPoseKind.CurveOverride => 2,
                AlsLayerPoseKind.LayeredBlend => (node.Filters?.Length ?? 0) + 1,
                AlsLayerPoseKind.NormalizedMultiWayBlend => node.Alphas.Length,
                _ => 1,
            };
            var expectedAlphas = node.Kind switch
            {
                AlsLayerPoseKind.ApplyLocalAdditive or AlsLayerPoseKind.ApplyMeshAdditive or AlsLayerPoseKind.TwoWayBlend => 1,
                AlsLayerPoseKind.LayeredBlend => expectedInputs - 1,
                AlsLayerPoseKind.NormalizedMultiWayBlend => expectedInputs,
                _ => 0,
            };
            if (node.Inputs.Length != expectedInputs || node.Alphas.Length != expectedAlphas ||
                !Enum.IsDefined(node.Kind)||
                node.Kind == AlsLayerPoseKind.UseCache && _byIndex[node.Inputs[0]].Kind != AlsLayerPoseKind.SaveCache ||
                node.Kind == AlsLayerPoseKind.LayeredBlend && (node.Filters is null || node.Filters.Length == 0) ||
                node.Alphas.Any(a => !double.IsFinite(a.Value) || (uint)a.Kind > 2 || a.Kind != AlsLayerAlphaKind.Constant && string.IsNullOrEmpty(a.Name)))
                throw new ArgumentException("Invalid LayerBlending node inputs or alpha bindings.");
            if(node.Kind==AlsLayerPoseKind.CurveReset)
            {
                if(node.ModifiedCurves is null||node.ModifiedValues is null||node.ModifiedCurves.Length==0||
                    node.ModifiedCurves.Length!=node.ModifiedValues.Length||node.ModifiedCurves.Any(string.IsNullOrWhiteSpace)||
                    node.ModifiedCurves.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=node.ModifiedCurves.Length||node.ModifiedValues.Any(v=>!float.IsFinite(v)))
                    throw new ArgumentException("Incomplete authored curve reset.");
            }
            else if(node.ModifiedCurves is not null||node.ModifiedValues is not null)throw new ArgumentException("Curve reset belongs to another node kind.");
        }
        // Include cache read -> write edges when rejecting cycles.
        var visiting = new HashSet<int>(); var visited = new HashSet<int>();
        foreach (var node in _nodes) Visit(node.Index);
        void Visit(int index)
        {
            if (visited.Contains(index)) return;
            if (!visiting.Add(index)) throw new ArgumentException("LayerBlending contains a pose cycle.");
            foreach (var input in _byIndex[index].Inputs) Visit(input);
            visiting.Remove(index); visited.Add(index);
        }
        Source = source; RootIndex = rootIndex;
        Caches = new(compiledPropertyCount, cacheUpdateOrder, _nodes.Where(n => n.Kind == AlsLayerPoseKind.UseCache)
            .Select(n => new AlsPoseCacheReadBinding(n.Index, n.Inputs[0])).ToArray());
    }

    public AlsLayerPoseNode Node(int index) => _byIndex.TryGetValue(index, out var node) ? node :
        throw new ArgumentOutOfRangeException(nameof(index));
}
