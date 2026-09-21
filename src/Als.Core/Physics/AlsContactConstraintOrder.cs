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
    public int Count => _pending ? _nextCount : throw new InvalidOperationException("Prepare the constraint order before solving.");
    public int ContactSlotAt(int index) => _nextContacts[index];

    public AlsContactConstraintOrder(AlsJointIsland island, int capacity)
    {
        _island = island;
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
        foreach (var slot in active) _present[slot] = true;
        _contacts.AsSpan(0, _count).CopyTo(_incoming); _nextCount = _count;
        // Native edge removal uses swap removal. Keep the previous container
        // order for surviving edges; append new identities in detection order.
        for (var i = 0; i < _nextCount;)
        {
            var slot = _incoming[i];
            if (!_present[slot] || _identity[slot] != identity[slot]) _incoming[i] = _incoming[--_nextCount];
            else { _retained[slot] = true; i++; }
        }
        foreach (var slot in active) if (!_retained[slot])
        { _nextKeys[slot] = _stagedKey; _stagedKey = checked(_stagedKey + 1); _incoming[_nextCount++] = slot; }
        for (var i = 0; i < _nextCount; i++)
        {
            var slot = _incoming[i]; _edges[i] = new(0, body0[slot], body1[slot], _nextKeys[slot]);
        }
        var count = _nextCount;
        foreach (var j in _joints)
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
    }
    public void Abort() { _pending = false; _nextCount = 0; }
    public void Reset()
    {
        _count = _nextCount = 0; _nextKey = 0; _pending = false;
        for (var i = 0; i < _joints.Length; i++) _joints[i] = i;
    }
}
