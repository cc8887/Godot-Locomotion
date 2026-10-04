using System.Collections.Immutable;

namespace GodotAls.Core.Animation;

public sealed record AlsLinkedLayerFunction(string Name, string Group, bool Implemented = true);
public sealed record AlsLinkedLayerClass(string Name, ImmutableArray<AlsLinkedLayerFunction> Functions,
    bool ReceiveNotifies = false, bool PropagateNotifies = false);
public sealed record AlsLinkedLayerCallSite(string Node, string Function, string? DefaultClass = null,
    bool HasInterface = true, bool ReceiveNotifies = false, bool PropagateNotifies = false);
public enum AlsLinkedLayerTargetKind { Self, External, Unbound }
public sealed record AlsLinkedLayerTarget(AlsLinkedLayerTargetKind Kind, string? Class, string Group,
    long Instance, bool ReceiveNotifies, bool PropagateNotifies);

// Ordinary UAnimInstance::PerformLinkedLayerOverlayOperation ownership policy.
// This describes binding identities, not graph execution, Sync groups or poses.
// The optional UE SharedLinkedAnimLayers subsystem requires a separate policy.
public sealed class AlsLinkedLayerBindings
{
    private readonly string _mainClass;
    private readonly ImmutableArray<AlsLinkedLayerCallSite> _calls;
    private readonly Dictionary<string, AlsLinkedLayerClass> _classes;
    private readonly Dictionary<string, int> _nodes;
    private ImmutableArray<AlsLinkedLayerTarget> _targets;
    private long _lastInstance;
    private AlsLinkedLayerBindingCandidate? _pending;

    public AlsLinkedLayerBindings(string mainClass, IEnumerable<AlsLinkedLayerCallSite> calls,
        IEnumerable<AlsLinkedLayerClass> classes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mainClass);
        _mainClass = mainClass;
        _calls = calls.ToImmutableArray();
        _classes = new(StringComparer.Ordinal);
        _nodes = new(StringComparer.Ordinal);
        foreach (var definition in classes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
            var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var function in definition.Functions)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(function.Name);
                ArgumentNullException.ThrowIfNull(function.Group);
                if (!functions.Add(function.Name)) throw new ArgumentException("Duplicate layer function.");
            }
            _classes.Add(definition.Name, definition);
        }
        foreach (var call in _calls)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Node);
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Function);
            _nodes.Add(call.Node, _nodes.Count);
            if (call.DefaultClass is { } defaultClass && !_classes.ContainsKey(defaultClass))
                throw new ArgumentException("Unknown default layer class: " + defaultClass);
        }
        _targets = _calls.Select(InitialTarget).ToImmutableArray();
    }

    public ImmutableArray<AlsLinkedLayerTarget> Targets => _targets;
    public AlsLinkedLayerTarget Target(string node) => _targets[_nodes[node]];

    public AlsLinkedLayerBindingCandidate PrepareLink(string? className) => Prepare(className, false);
    public AlsLinkedLayerBindingCandidate PrepareUnlink(string? className)
    {
        if (className is not null) ArgumentException.ThrowIfNullOrWhiteSpace(className);
        return Prepare(className, true);
    }

    private AlsLinkedLayerTarget InitialTarget(AlsLinkedLayerCallSite call) =>
        !call.HasInterface || call.DefaultClass is null
            ? new(AlsLinkedLayerTargetKind.Self, _mainClass, "", 0,
                _classes.TryGetValue(_mainClass, out var main) && main.ReceiveNotifies,
                _classes.TryGetValue(_mainClass, out main) && main.PropagateNotifies)
            : new(AlsLinkedLayerTargetKind.Unbound, null, "", 0, false, false);

    private AlsLinkedLayerBindingCandidate Prepare(string? className, bool unlink)
    {
        if (_pending is not null) throw new InvalidOperationException("Layer binding operation already pending.");
        if (className is not null && !_classes.ContainsKey(className))
            throw new ArgumentException("Unknown layer class: " + className);

        // UE builds two nested TMaps in first-insertion order: class, then group.
        // Unlink still groups by the function on the requested class, even when
        // its selector retains another class or restores a default class.
        var buckets = new List<ClassBucket>();
        for (var index = 0; index < _calls.Length; index++)
        {
            var call = _calls[index];
            var evaluate = className ?? call.DefaultClass;
            var group = "";
            if (evaluate is not null)
            {
                var function = _classes[evaluate].Functions.FirstOrDefault(f =>
                    string.Equals(f.Name, call.Function, StringComparison.OrdinalIgnoreCase));
                if (function is null || !function.Implemented) continue;
                group = string.Equals(function.Group, "None", StringComparison.OrdinalIgnoreCase) ? "" : function.Group;
            }
            var selected = !unlink ? className ?? call.DefaultClass :
                _targets[index].Class == className ? call.DefaultClass : _targets[index].Class;
            var bucket = buckets.FirstOrDefault(b => b.Class == selected);
            if (bucket is null) { bucket = new(selected); buckets.Add(bucket); }
            var nodes = bucket.Groups.FirstOrDefault(g => string.Equals(g.Name, group, StringComparison.OrdinalIgnoreCase));
            if (nodes is null) { nodes = new(group); bucket.Groups.Add(nodes); }
            nodes.Nodes.Add(index);
        }

        var targets = _targets.ToArray();
        var nextInstance = _lastInstance;
        void Retire(int index)
        {
            var previous = targets[index];
            if (previous.Kind != AlsLinkedLayerTargetKind.External) return;
            // Ordinary LinkedAnimGraph teardown marks its target as garbage.
            // GetTargetInstance uses IsValid, so every call to that instance
            // becomes unbound, including functions excluded from this overlay.
            for (var other = 0; other < targets.Length; other++)
                if (targets[other].Kind == AlsLinkedLayerTargetKind.External && targets[other].Instance == previous.Instance)
                    targets[other] = new(AlsLinkedLayerTargetKind.Unbound, null, "", 0, false, false);
        }
        foreach (var bucket in buckets)
        foreach (var group in bucket.Groups)
        {
            if (bucket.Class is null || bucket.Class == _mainClass)
            {
                foreach (var index in group.Nodes)
                {
                    var call = _calls[index];
                    Retire(index);
                    // SetLinkedLayerInstance(null) uses self when either the
                    // interface or default class is absent. Otherwise its base
                    // ReinitializeLinkedAnimInstance creates a fresh default
                    // instance for this node, even when that class matches.
                    targets[index] = !call.HasInterface || call.DefaultClass is null ? InitialTarget(call) :
                        new(AlsLinkedLayerTargetKind.External, call.DefaultClass, "", checked(++nextInstance),
                            call.ReceiveNotifies, call.PropagateNotifies);
                }
            }
            else if (group.Name.Length == 0)
            {
                foreach (var index in group.Nodes)
                    if (targets[index].Class != bucket.Class)
                    {
                        var call = _calls[index];
                        Retire(index);
                        targets[index] = new(AlsLinkedLayerTargetKind.External, bucket.Class, "", checked(++nextInstance),
                            call.ReceiveNotifies, call.PropagateNotifies);
                    }
            }
            else if (targets[group.Nodes[0]].Class != bucket.Class)
            {
                // Grouped instances begin with their class CDO flags and OR in
                // all call-site flags; ungrouped instances use node flags only.
                var definition = _classes[bucket.Class];
                var target = new AlsLinkedLayerTarget(AlsLinkedLayerTargetKind.External, bucket.Class, group.Name,
                    checked(++nextInstance), definition.ReceiveNotifies || group.Nodes.Any(i => _calls[i].ReceiveNotifies),
                    definition.PropagateNotifies || group.Nodes.Any(i => _calls[i].PropagateNotifies));
                foreach (var index in group.Nodes) { Retire(index); targets[index] = target; }
            }
        }
        return _pending = new(this, targets.ToImmutableArray(), nextInstance);
    }

    public void Validate(AlsLinkedLayerBindingCandidate candidate)
    {
        if (!ReferenceEquals(candidate, _pending) || !ReferenceEquals(candidate.Owner, this))
            throw new InvalidOperationException("Foreign or stale layer binding candidate.");
    }
    public void Commit(AlsLinkedLayerBindingCandidate candidate)
    {
        Validate(candidate);
        _targets = candidate.Targets;
        _lastInstance = candidate.LastInstance;
        _pending = null;
    }
    public void Cancel(AlsLinkedLayerBindingCandidate candidate) { Validate(candidate); _pending = null; }

    private sealed class ClassBucket(string? className)
    {
        public string? Class { get; } = className;
        public List<GroupBucket> Groups { get; } = [];
    }
    private sealed class GroupBucket(string name)
    {
        public string Name { get; } = name;
        public List<int> Nodes { get; } = [];
    }
}

public sealed class AlsLinkedLayerBindingCandidate
{
    internal AlsLinkedLayerBindingCandidate(AlsLinkedLayerBindings owner,
        ImmutableArray<AlsLinkedLayerTarget> targets, long lastInstance)
    { Owner = owner; Targets = targets; LastInstance = lastInstance; }
    internal AlsLinkedLayerBindings Owner { get; }
    internal long LastInstance { get; }
    public ImmutableArray<AlsLinkedLayerTarget> Targets { get; }
}
