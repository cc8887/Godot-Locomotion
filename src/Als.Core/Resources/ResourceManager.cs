using GodotAls.Core.Diagnostics;

namespace GodotAls.Core.Resources;

/// <summary>Synchronous, dependency-aware resource ownership. Loaders return
/// immutable data; one manager serializes construction and shares completed loads.</summary>
public sealed class ResourceManager : IDisposable
{
    private sealed record Definition(Type Type, Func<ResourceLoadContext, object> Load, Action<object>? Release);
    internal sealed class Entry(ResourceId id, long revision, object value, Action<object>? release,
        Entry[] dependencies, long bytes)
    {
        internal readonly ResourceId Id = id;
        internal readonly long Revision = revision;
        internal readonly object Value = value;
        internal readonly Action<object>? Destroy = release;
        internal readonly Entry[] Dependencies = dependencies;
        internal readonly long Bytes = bytes;
        internal int References = 1;
    }

    private readonly object _gate = new();
    private readonly Dictionary<ResourceId, Definition> _definitions = [];
    private readonly Dictionary<ResourceId, Entry> _cache = [];
    private readonly Dictionary<ResourceId, HashSet<ResourceId>> _dependents = [];
    private readonly Stack<ResourceId> _loading = [];
    private bool _disposed;
    private long _nextRevision, _requests, _hits, _loads, _failures, _invalidations, _retainedBytes;
    private int _liveVersions;
    public string Name { get; }

    public ResourceManager(string name = "resources")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public void Register<T>(ResourceId id, Func<ResourceLoadContext, T> load, Action<T>? release = null)
    {
        var definition = Define(load, release);
        lock (_gate)
        {
            RequireMutable(id);
            if (!_definitions.TryAdd(id, definition)) throw new ArgumentException("Resource already registered: " + id);
        }
    }

    public void Register<T>(ResourceId id, IResourceProvider provider, IResourceLoader<T> loader, Action<T>? release = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(loader);
        Register(id, context =>
        {
            var content = provider.Read(id);
            context.ReportBytes(content.Length);
            return loader.Load(context, content);
        }, release);
    }

    public bool IsRegistered(ResourceId id)
    {
        lock (_gate) { RequireAlive(); return _definitions.ContainsKey(id); }
    }

    public ResourceLease<T> Acquire<T>(ResourceId id)
    {
        lock (_gate)
        {
            RequireAlive();
            var entry = Find<T>(id);
            entry.References++;
            return new(this, entry);
        }
    }

    /// <summary>Loads a candidate before publication. Failure keeps the current
    /// definition and cache valid; existing leases retain the previous version.</summary>
    public void Replace<T>(ResourceId id, Func<ResourceLoadContext, T> load, Action<T>? release = null)
    {
        var definition = Define(load, release);
        lock (_gate)
        {
            RequireMutable(id);
            if (!_definitions.TryGetValue(id, out var old) || old.Type != typeof(T))
                throw new ArgumentException("Missing resource or incompatible replacement type: " + id);
            var candidate = Build(id, definition);
            var removed = RemoveCachedClosure(id);
            _definitions[id] = definition;
            Store(candidate);
            Emit(id, "replace", candidate.Revision, candidate.Bytes);
            ReleaseAll(removed);
        }
    }

    public void Invalidate(ResourceId id)
    {
        lock (_gate) { RequireMutable(id); ReleaseAll(RemoveCachedClosure(id)); }
    }

    public ResourceStatistics GetStatistics()
    {
        lock (_gate) return new(_requests, _hits, _loads, _failures, _invalidations,
            _cache.Count, _liveVersions, _retainedBytes);
    }

    internal T ReadDependency<T>(ResourceLoadContext context, ResourceId id)
    {
        lock (_gate)
        {
            RequireAlive();
            if (context.Dependencies.TryGetValue(id, out var pinned))
            {
                if (_definitions[id].Type != typeof(T)) throw new ArgumentException("Dependency type differs: " + id);
                return (T)pinned.Value;
            }
            var entry = Find<T>(id);
            if (ContainsLoadingDependency(entry)) throw new InvalidOperationException("Cyclic resource dependency: " + id);
            entry.References++;
            context.Dependencies.Add(id, entry);
            return (T)entry.Value;
        }
    }

    internal void Release(Entry entry)
    {
        lock (_gate) ReleaseCore(entry);
    }

    private Entry Find<T>(ResourceId id)
    {
        _requests++;
        if (!_definitions.TryGetValue(id, out var definition) || definition.Type != typeof(T))
            throw new ArgumentException("Missing resource or incompatible type: " + id);
        if (_loading.Contains(id)) throw new InvalidOperationException("Cyclic resource dependency: " + id);
        if (_cache.TryGetValue(id, out var cached))
        {
            _hits++;
            Emit(id, "hit", cached.Revision, cached.Bytes);
            return cached;
        }
        var entry = Build(id, definition);
        Store(entry);
        return entry;
    }

    private Entry Build(ResourceId id, Definition definition)
    {
        using var trace = AlsPerformanceTrace.Begin("resource.load", id.Value);
        var context = new ResourceLoadContext(this);
        _loading.Push(id);
        try
        {
            var value = definition.Load(context) ?? throw new InvalidOperationException("Resource loader returned null: " + id);
            var entry = new Entry(id, ++_nextRevision, value, definition.Release,
                context.Dependencies.Values.ToArray(), context.ByteCount);
            _loads++;
            _liveVersions++;
            _retainedBytes += entry.Bytes;
            Emit(id, "load", entry.Revision, entry.Bytes);
            trace?.Complete(1, entry.Bytes);
            return entry;
        }
        catch
        {
            _failures++;
            ReleaseAll(context.Dependencies.Values);
            Emit(id, "failure", 0, context.ByteCount);
            throw;
        }
        finally { context.Close(); _loading.Pop(); }
    }

    private void Store(Entry entry)
    {
        _cache.Add(entry.Id, entry);
        foreach (var dependency in entry.Dependencies)
        {
            if (!_dependents.TryGetValue(dependency.Id, out var owners)) _dependents.Add(dependency.Id, owners = []);
            owners.Add(entry.Id);
        }
    }

    private List<Entry> RemoveCachedClosure(ResourceId id)
    {
        var affected = new HashSet<ResourceId>();
        var pending = new Stack<ResourceId>();
        pending.Push(id);
        while (pending.TryPop(out var next))
        {
            if (!affected.Add(next)) continue;
            if (_dependents.TryGetValue(next, out var owners)) foreach (var owner in owners) pending.Push(owner);
        }
        var removed = new List<Entry>();
        foreach (var resource in affected)
        {
            if (!_cache.Remove(resource, out var entry)) continue;
            removed.Add(entry);
            foreach (var dependency in entry.Dependencies)
                if (_dependents.TryGetValue(dependency.Id, out var owners))
                {
                    owners.Remove(resource);
                    if (owners.Count == 0) _dependents.Remove(dependency.Id);
                }
            _invalidations++;
            Emit(resource, "invalidate", entry.Revision, entry.Bytes);
        }
        return removed;
    }

    private bool ContainsLoadingDependency(Entry entry)
    {
        var visited = new HashSet<Entry>();
        var pending = new Stack<Entry>();
        pending.Push(entry);
        while (pending.TryPop(out var next))
        {
            if (!visited.Add(next)) continue;
            if (_loading.Contains(next.Id)) return true;
            foreach (var dependency in next.Dependencies) pending.Push(dependency);
        }
        return false;
    }

    private void ReleaseCore(Entry entry)
    {
        if (--entry.References != 0) return;
        _liveVersions--;
        _retainedBytes -= entry.Bytes;
        Emit(entry.Id, "release", entry.Revision, entry.Bytes);
        try { entry.Destroy?.Invoke(entry.Value); }
        finally { ReleaseAll(entry.Dependencies); }
    }

    private void ReleaseAll(IEnumerable<Entry> entries)
    {
        List<Exception>? failures = null;
        foreach (var entry in entries)
            try { ReleaseCore(entry); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is not null) throw new AggregateException("Resource release failed after ownership was updated.", failures);
    }

    private static Definition Define<T>(Func<ResourceLoadContext, T> load, Action<T>? release)
    {
        ArgumentNullException.ThrowIfNull(load);
        return new(typeof(T), context => load(context)!, release is null ? null : value => release((T)value));
    }

    private void RequireMutable(ResourceId id)
    {
        RequireAlive();
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        if (_loading.Count != 0) throw new InvalidOperationException("Resource definitions cannot change inside a loader.");
    }

    private void RequireAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
    private void Emit(ResourceId id, string operation, long revision, long bytes)
        => AlsPerformanceTrace.Log.ResourceCache(Name, id.Value, operation, revision, bytes);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_loading.Count != 0) throw new InvalidOperationException("Cannot dispose resources during a loader.");
            _disposed = true;
            var entries = _cache.Values.ToArray();
            _cache.Clear(); _definitions.Clear(); _dependents.Clear();
            ReleaseAll(entries);
        }
    }
}
