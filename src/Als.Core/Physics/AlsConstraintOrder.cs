namespace GodotAls.Core.Physics;

// Input order within each container is its current island-edge array order.
// AddOrder is the persistent insertion key, NOT the asset index or frame index.
public readonly record struct AlsConstraintEdge(int Container, int Body0, int Body1, ulong AddOrder);

// Native island levelization for an explicitly supplied graph snapshot. It does
// not discover islands, retain edges or invent insertion identities. The owner
// supplies those transactionally; all work buffers are allocated at construction.
public sealed class AlsConstraintOrder
{
    private readonly bool[] _dynamic;
    private readonly int[] _levels, _roots, _edgeCounts, _queue, _edgeLevels, _order;
    public int Count { get; private set; }
    public int BodyLevelAt(int body) => _levels[body];
    public int EdgeLevelAt(int edge) => (uint)edge < Count ? _edgeLevels[edge] : throw new ArgumentOutOfRangeException(nameof(edge));
    public int OrderedIndexAt(int index) => (uint)index < Count ? _order[index] : throw new ArgumentOutOfRangeException(nameof(index));

    public AlsConstraintOrder(ReadOnlySpan<bool> dynamicBodies, int capacity)
    {
        if (dynamicBodies.IsEmpty || capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _dynamic = dynamicBodies.ToArray(); _levels = new int[_dynamic.Length];
        _roots = new int[_dynamic.Length]; _edgeCounts = new int[_dynamic.Length]; _queue = new int[_dynamic.Length];
        _edgeLevels = new int[capacity]; _order = new int[capacity];
    }

    public void Build(ReadOnlySpan<AlsConstraintEdge> edges)
    {
        if (edges.Length > _order.Length) throw new ArgumentException("Constraint graph capacity exceeded.");
        var container = -1;
        foreach (var e in edges)
        {
            if (e.Container < 0 || e.Container < container || (uint)e.Body0 >= _dynamic.Length || (uint)e.Body1 >= _dynamic.Length ||
                e.Body0 == e.Body1 || !_dynamic[e.Body0] && !_dynamic[e.Body1])
                throw new ArgumentException("Edges must be grouped by nonnegative container and connect distinct bodies with a dynamic endpoint.");
            container = e.Container;
        }
        // Validation above precedes publication or any scratch mutation.
        Array.Fill(_levels, -1); Array.Clear(_edgeCounts); Array.Clear(_edgeLevels);
        foreach (var e in edges) { _levels[e.Body0] = 0; _levels[e.Body1] = 0; }
        for (var i = 0; i < _roots.Length; i++) _roots[i] = i;
        foreach (var e in edges) if (_dynamic[e.Body0] && _dynamic[e.Body1]) _roots[Root(e.Body1)] = Root(e.Body0);
        foreach (var e in edges) _edgeCounts[Root(_dynamic[e.Body0] ? e.Body0 : e.Body1)]++;
        var queued = 0;
        for (var i = 0; i < edges.Length; i++)
        {
            var e = edges[i]; _order[i] = i;
            // Native AssignIslandLevels leaves a one-edge component at level 0.
            if (_edgeCounts[Root(_dynamic[e.Body0] ? e.Body0 : e.Body1)] < 2) continue;
            var supported = !_dynamic[e.Body0] ? e.Body1 : !_dynamic[e.Body1] ? e.Body0 : -1;
            if (supported >= 0 && _levels[supported] == 0)
            { _edgeLevels[i] = 1; _levels[supported] = 1; _queue[queued++] = supported; }
        }
        for (var q = 0; q < queued; q++)
        {
            var body = _queue[q];
            for (var i = 0; i < edges.Length; i++)
            {
                var e = edges[i];
                if (_edgeLevels[i] != 0 || e.Body0 != body && e.Body1 != body) continue;
                _edgeLevels[i] = _levels[body] + 1;
                var other = e.Body0 == body ? e.Body1 : e.Body0;
                if (_dynamic[other] && _levels[other] == 0)
                { _levels[other] = _levels[body] + 1; _queue[queued++] = other; }
            }
        }
        // Stable insertion sort preserves equal keys and needs no comparer allocation.
        for (var i = 1; i < edges.Length; i++)
        {
            var index = _order[i]; var j = i;
            while (j > 0 && Less(index, _order[j - 1], edges)) { _order[j] = _order[j - 1]; j--; }
            _order[j] = index;
        }
        Count = edges.Length;
    }
    private int Root(int body)
    {
        while (_roots[body] != body) { _roots[body] = _roots[_roots[body]]; body = _roots[body]; }
        return body;
    }
    private bool Less(int a, int b, ReadOnlySpan<AlsConstraintEdge> edges) =>
        edges[a].Container != edges[b].Container ? edges[a].Container < edges[b].Container :
        _edgeLevels[a] != _edgeLevels[b] ? _edgeLevels[a] < _edgeLevels[b] : edges[a].AddOrder < edges[b].AddOrder;
}
