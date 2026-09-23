namespace GodotAls.Core.Physics;

// Transactional ordering for the fixed body/shape topology of AlsWorldContacts.
// Contact edges exist while they have active manifold points. Container 0 is
// contacts and container 1 is joints, matching the native registration priority.
internal sealed class AlsContactConstraintOrder
{
    private readonly AlsJointIsland _island;
    private readonly AlsConstraintOrder _graph;
    private readonly AlsConstraintEdge[] _edges;
    private readonly int[] _incoming;
    private readonly bool[] _present, _retained;
    private int[] _contacts, _nextContacts, _joints, _nextJoints;
    private ulong[] _keys, _nextKeys;
    private AlsContactPairKey[] _identity, _nextIdentity;
    private int _count, _nextCount;
    private ulong _nextKey, _stagedKey;
    private bool _pending;
    private readonly int _capacity, _edgeCapacity;
    private int[] _nodeEdges, _nextNodeEdges, _nodeCounts, _nextNodeCounts;
    private readonly int[] _queue;
    private readonly bool[] _visitedEdges, _visitedBodies;
    private bool _initialized;
    public int Count => _pending ? _nextCount : throw new InvalidOperationException("Prepare the constraint order before solving.");
    public int ContactSlotAt(int index) => _nextContacts[index];
    public int BodyLevelAt(int body) => _pending ? _graph.BodyLevelAt(body) : throw new InvalidOperationException("Prepare graph levels first.");

    public AlsContactConstraintOrder(AlsJointIsland island, int capacity)
    {
        _island = island;
        _capacity = capacity; _edgeCapacity = checked(capacity + island.JointCount);
        _nodeEdges = new int[checked(island.BodyCount * _edgeCapacity)]; _nextNodeEdges = new int[_nodeEdges.Length];
        _nodeCounts = new int[island.BodyCount]; _nextNodeCounts = new int[island.BodyCount];
        _queue = new int[island.BodyCount]; _visitedBodies = new bool[island.BodyCount]; _visitedEdges = new bool[_edgeCapacity];
        var dynamic = new bool[island.BodyCount];
        for (var i = 0; i < dynamic.Length; i++) dynamic[i] = island.BodyDefinitionAt(i).InverseMass.Mass > 0;
        _graph = new(dynamic, checked(capacity + island.JointCount)); _edges = new AlsConstraintEdge[capacity + island.JointCount];
        _incoming = new int[capacity]; _present = new bool[capacity]; _retained = new bool[capacity];
        _contacts = new int[capacity]; _nextContacts = new int[capacity];
        _keys = new ulong[capacity]; _nextKeys = new ulong[capacity];
        _identity = new AlsContactPairKey[capacity]; _nextIdentity = new AlsContactPairKey[capacity];
        _joints = new int[island.JointCount]; _nextJoints = new int[island.JointCount]; Reset();
    }

    public void Prepare(AlsJointIsland island, ReadOnlySpan<int> active, int[] body0, int[] body1,
        AlsContactPairKey[] identity, Span<int> jointOrder)
    {
        if (!ReferenceEquals(island, _island)) throw new ArgumentException("Constraint order belongs to a different island.");
        if (jointOrder.Length != island.JointCount) throw new ArgumentException("One order entry per joint is required.");
        if (_pending) throw new InvalidOperationException("Constraint order already has a pending step.");
        Array.Clear(_present); Array.Clear(_retained); _stagedKey = _nextKey;
        _keys.CopyTo(_nextKeys, 0); identity.CopyTo(_nextIdentity, 0);
        _nodeEdges.CopyTo(_nextNodeEdges, 0); _nodeCounts.CopyTo(_nextNodeCounts, 0);
        foreach (var slot in active) _present[slot] = true;
        _contacts.AsSpan(0, _count).CopyTo(_incoming); _nextCount = _count;
        var removed = false;
        // Native collects expired edges in the old container order first,
        // then swap-removes each from both the island and endpoint arrays.
        for (var i = 0; i < _count; i++)
        {
            var slot = _contacts[i];
            if (!_present[slot] || _identity[slot] != identity[slot])
            {
                var index = Array.IndexOf(_incoming, slot, 0, _nextCount);
                _incoming[index] = _incoming[--_nextCount]; RemoveNodeEdge(slot); removed = true;
            }
            else _retained[slot] = true;
        }
        foreach (var slot in active) if (!_retained[slot])
        {
            _nextKeys[slot] = _stagedKey; _stagedKey = checked(_stagedKey + 1); _incoming[_nextCount++] = slot;
            AddNodeEdge(body0[slot], slot); AddNodeEdge(body1[slot], slot);
        }
        if (!_initialized)
            foreach (var j in _joints)
            {
                var joint = island.JointDefinitionAt(j);
                AddNodeEdge(joint.Parent, _capacity + j); AddNodeEdge(joint.Child, _capacity + j);
            }
        // ProcessIslandSplits repopulates containers by walking persistent node
        // adjacency whenever edges were removed, even when the island does not
        // actually split. This can change which shape supplies a body's first
        // kinematic support before levelization (multiple floor contacts).
        if (removed) RebuildContainers(body0, body1);
        for (var i = 0; i < _nextCount; i++)
        {
            var slot = _incoming[i]; _edges[i] = new(0, body0[slot], body1[slot], _nextKeys[slot]);
        }
        var count = _nextCount;
        var incomingJoints = removed ? _nextJoints : _joints;
        foreach (var j in incomingJoints)
        {
            var joint = island.JointDefinitionAt(j);
            if (island.BodyDefinitionAt(joint.Parent).InverseMass.Mass > 0 || island.BodyDefinitionAt(joint.Child).InverseMass.Mass > 0)
                _edges[count++] = new(1, joint.Parent, joint.Child, (ulong)j);
        }
        _graph.Build(_edges.AsSpan(0, count));
        var contactCount = 0; var jointCount = 0;
        for (var i = 0; i < count; i++)
        {
            var index = _graph.OrderedIndexAt(i);
            if (index < _nextCount) _nextContacts[contactCount++] = _incoming[index];
            else _nextJoints[jointCount++] = (int)_edges[index].AddOrder;
        }
        // Inactive fixed/fixed joints have no graph edge but retain a defined
        // order for the existing no-op solver rows and projection guards.
        foreach (var j in _joints)
        {
            var joint = island.JointDefinitionAt(j);
            if (island.BodyDefinitionAt(joint.Parent).InverseMass.Mass <= 0 && island.BodyDefinitionAt(joint.Child).InverseMass.Mass <= 0)
                _nextJoints[jointCount++] = j;
        }
        _nextJoints.CopyTo(jointOrder); _pending = true;
    }
    public void Commit()
    {
        if (!_pending) return;
        (_contacts, _nextContacts) = (_nextContacts, _contacts); (_joints, _nextJoints) = (_nextJoints, _joints);
        (_keys, _nextKeys) = (_nextKeys, _keys); (_identity, _nextIdentity) = (_nextIdentity, _identity);
        _count = _nextCount; _nextKey = _stagedKey; _pending = false;
        (_nodeEdges, _nextNodeEdges) = (_nextNodeEdges, _nodeEdges);
        (_nodeCounts, _nextNodeCounts) = (_nextNodeCounts, _nodeCounts); _initialized = true;
    }
    public void Abort() { _pending = false; _nextCount = 0; }
    public void Reset()
    {
        _count = _nextCount = 0; _nextKey = 0; _pending = false;
        _initialized = false; Array.Clear(_nodeCounts);
        for (var i = 0; i < _joints.Length; i++) _joints[i] = i;
    }
    private void AddNodeEdge(int body, int edge) => _nextNodeEdges[body * _edgeCapacity + _nextNodeCounts[body]++] = edge;
    private void RemoveNodeEdge(int edge)
    {
        for (var body = 0; body < _nextNodeCounts.Length; body++)
        {
            var start = body * _edgeCapacity;
            var index = Array.IndexOf(_nextNodeEdges, edge, start, _nextNodeCounts[body]);
            if (index >= 0) _nextNodeEdges[index] = _nextNodeEdges[start + --_nextNodeCounts[body]];
        }
    }
    private void RebuildContainers(int[] body0, int[] body1)
    {
        Array.Clear(_visitedBodies); Array.Clear(_visitedEdges);
        var contacts = 0; var joints = 0;
        for (var root = 0; root < _island.BodyCount; root++)
        {
            if (_visitedBodies[root] || _island.BodyDefinitionAt(root).InverseMass.Mass <= 0) continue;
            var queued = 0; _queue[queued++] = root; _visitedBodies[root] = true;
            while (queued > 0)
            {
                var body = _queue[--queued];
                for (var i = 0; i < _nextNodeCounts[body]; i++)
                {
                    var edge = _nextNodeEdges[body * _edgeCapacity + i];
                    if (_visitedEdges[edge]) continue;
                    _visitedEdges[edge] = true;
                    int a, b;
                    if (edge < _capacity) { _incoming[contacts++] = edge; a = body0[edge]; b = body1[edge]; }
                    else
                    {
                        var j = edge - _capacity; _nextJoints[joints++] = j;
                        var joint = _island.JointDefinitionAt(j); a = joint.Parent; b = joint.Child;
                    }
                    var other = a == body ? b : a;
                    if (!_visitedBodies[other] && _island.BodyDefinitionAt(other).InverseMass.Mass > 0)
                    { _visitedBodies[other] = true; _queue[queued++] = other; }
                }
            }
        }
        foreach (var j in _joints) if (!_visitedEdges[_capacity + j]) _nextJoints[joints++] = j;
        if (contacts != _nextCount || joints != _joints.Length) throw new InvalidOperationException("Incomplete rebuilt constraint graph.");
    }
}
