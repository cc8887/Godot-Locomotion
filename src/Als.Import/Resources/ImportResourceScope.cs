using GodotAls.Core.Resources;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Resources;

/// <summary>A bounded synchronous import operation. Legacy text compiler APIs
/// use its current-thread adapter; ownership and dependencies live in Core.</summary>
public sealed class ImportResourceScope : IDisposable
{
    [ThreadStatic] private static ImportResourceScope? _current;
    private readonly ImportResourceScope? _parent;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<ResourceId, string> _revisions;
    private readonly bool _ownsResources;
    private bool _disposed;
    public static ImportResourceScope? Current => _current;
    public ResourceManager Resources { get; }

    public ImportResourceScope(string name, bool shareWithParent = true)
    {
        _parent = _current;
        _ownsResources = _parent is null || !shareWithParent;
        Resources = _ownsResources ? new(name) : _parent!.Resources;
        _revisions = _ownsResources ? [] : _parent!._revisions;
        _current = this;
    }

    internal void Bind<T>(ResourceId id, string revision, Func<ResourceLoadContext, T> load)
    {
        if (_revisions.TryGetValue(id, out var previous))
        {
            if (previous == revision) return;
            Resources.Replace(id, load);
        }
        else Resources.Register(id, load);
        _revisions[id] = revision;
    }

    internal T Read<T>(ResourceId id)
    {
        using var lease = Resources.Acquire<T>(id);
        return lease.Value;
    }

    internal string ExtractGraph(string text, string source, string path)
    {
        var textId = new ResourceId("native-text:" + source);
        // String equality detects same-ID replacement even when size and timestamp
        // are unchanged. This operates on the already supplied text, without I/O.
        Bind(textId, text, _ => text);
        var indexId = new ResourceId("native-index:" + source);
        Bind(indexId, "t3d-index-v1", context => new AlsNativeNestedGraph.Index(context.Read<string>(textId), source));
        var graphId = new ResourceId("native-graph:" + path);
        Bind(graphId, "t3d-slice-v1", context => context.Read<AlsNativeNestedGraph.Index>(indexId).Extract(path));
        return Read<string>(graphId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_thread != Environment.CurrentManagedThreadId || _current != this)
            throw new InvalidOperationException("Import resource scopes must close in order on their creating thread.");
        _disposed = true;
        _current = _parent;
        if (_ownsResources)
        {
            try { Resources.Dispose(); }
            finally { _revisions.Clear(); }
        }
    }
}
