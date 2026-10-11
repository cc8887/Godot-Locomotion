using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Diagnostics;

[EventSource(Name = ProviderName)]
public sealed class AlsPerformanceTrace : EventSource
{
    public const string ProviderName = "GodotAls-Performance";
    public static AlsPerformanceTrace Log { get; } = new();

    public static class Keywords
    {
        public const EventKeywords Startup = (EventKeywords)1;
    }

    [ThreadStatic] private static Scope? _current;
    private static long _nextId;
    private static int _runtimeInfoEmitted;

    private AlsPerformanceTrace() { }

    [NonEvent]
    public static Scope? Begin(string phase, string assetPath = "", int assetId = -1,
        long characterId = -1, long generation = -1, string workerRole = "")
    {
        if (!Log.IsEnabled(EventLevel.Informational, Keywords.Startup)) return null;
        if (Interlocked.Exchange(ref _runtimeInfoEmitted, 1) == 0)
            Log.RuntimeInfo(RuntimeInformation.FrameworkDescription, Environment.Version.ToString(),
                RuntimeInformation.ProcessArchitecture.ToString(), System.Runtime.GCSettings.IsServerGC ? 1 : 0);
        return new Scope(phase, assetPath, assetId, characterId, generation, workerRole);
    }

    [NonEvent]
    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        if (command.Command == EventCommand.Enable) Volatile.Write(ref _runtimeInfoEmitted, 0);
    }

    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Startup)]
    public void ScopeBegin(long spanId, long parentId, string phase, string assetPath,
        int assetId, long characterId, long generation, string workerRole)
    {
        if (IsEnabled()) WriteEvent(1, spanId, parentId, phase, assetPath,
            assetId, characterId, generation, workerRole);
    }

    [Event(2, Level = EventLevel.Informational, Keywords = Keywords.Startup)]
    public void ScopeEnd(long spanId, long elapsedTicks, long frequency,
        long managedAllocatedBytes, int completed, long itemCount, long byteCount)
    {
        if (IsEnabled()) WriteEvent(2, spanId, elapsedTicks, frequency,
            managedAllocatedBytes, completed, itemCount, byteCount);
    }

    [Event(3, Level = EventLevel.Informational, Keywords = Keywords.Startup)]
    public void Milestone(string name, long characterId, long generation, long frameId)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Startup))
            WriteEvent(3, name, characterId, generation, frameId);
    }

    [Event(4, Level = EventLevel.Informational, Keywords = Keywords.Startup)]
    public void RuntimeInfo(string framework, string version, string architecture, int serverGc)
    {
        if (IsEnabled()) WriteEvent(4, framework, version, architecture, serverGc);
    }

    [Event(5, Level = EventLevel.Informational, Keywords = Keywords.Startup)]
    public void ResourceCache(string manager, string resourceId, string operation, long revision, long byteCount)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Startup))
            WriteEvent(5, manager, resourceId, operation, revision, byteCount);
    }

    // Synchronous scopes inherit ownership on the current thread; worker callbacks
    // supply their own identity rather than carrying a scene-construction scope.
    public sealed class Scope : IDisposable
    {
        private readonly Scope? _parent;
        private readonly long _id;
        private readonly long _started;
        private readonly long _allocated;
        private readonly int _threadId;
        private readonly long _characterId;
        private readonly long _generation;
        private readonly string _workerRole;
        private readonly int _assetId;
        private readonly string _assetPath;
        private bool _completed;
        private bool _disposed;
        private long _itemCount;
        private long _byteCount;

        internal Scope(string phase, string assetPath, int assetId,
            long characterId, long generation, string workerRole)
        {
            _parent = _current;
            _id = Interlocked.Increment(ref _nextId);
            _threadId = Environment.CurrentManagedThreadId;
            _characterId = characterId < 0 ? _parent?._characterId ?? -1 : characterId;
            _generation = generation < 0 ? _parent?._generation ?? -1 : generation;
            _workerRole = workerRole.Length == 0 ? _parent?._workerRole ?? "" : workerRole;
            _assetId = assetId < 0 ? _parent?._assetId ?? -1 : assetId;
            _assetPath = assetPath.Length == 0 ? _parent?._assetPath ?? "" : assetPath;
            Log.ScopeBegin(_id, _parent?._id ?? 0, phase, _assetPath,
                _assetId, _characterId, _generation, _workerRole);
            _current = this;
            _allocated = GC.GetAllocatedBytesForCurrentThread();
            _started = Stopwatch.GetTimestamp();
        }

        public void Complete(long itemCount = 0, long byteCount = 0)
        {
            _completed = true;
            _itemCount = itemCount;
            _byteCount = byteCount;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var elapsed = Stopwatch.GetTimestamp() - _started;
            var sameThread = Environment.CurrentManagedThreadId == _threadId;
            var allocated = sameThread ? GC.GetAllocatedBytesForCurrentThread() - _allocated : -1;
            if (sameThread) _current = _parent;
            Log.ScopeEnd(_id, elapsed, Stopwatch.Frequency, allocated,
                _completed ? 1 : 0, _itemCount, _byteCount);
        }
    }
}
