using System.Text.Json;

namespace GodotAls.Tools.PerformanceTrace;

internal static class TraceFormat
{
    public const string Provider = "GodotAls-Performance";
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static void Write(string path, object value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, value, TraceFormat.Json);
    }
}

internal sealed class TraceSpan
{
    public long Id { get; init; }
    public long ParentId { get; init; }
    public int ProcessId { get; init; }
    public int ThreadId { get; init; }
    public string Phase { get; init; } = "";
    public string AssetPath { get; init; } = "";
    public int AssetId { get; init; }
    public long CharacterId { get; init; }
    public long Generation { get; init; }
    public string WorkerRole { get; init; } = "";
    public double StartMs { get; init; }
    public double EndMs { get; set; }
    public double DurationMs { get; set; }
    public double SelfMs { get; set; }
    public long ManagedAllocatedBytes { get; set; }
    public bool Completed { get; set; }
    public bool HasEnd { get; set; }
    public long ItemCount { get; set; }
    public long ByteCount { get; set; }
}

internal sealed record TraceMilestone(string Name, int ProcessId, int ThreadId,
    double TimeMs, long CharacterId, long Generation, long FrameId);
internal sealed record PhaseStats(string Phase, string WorkerRole, int Count,
    double InclusiveMs, double SelfMs, double MaxMs, long ManagedAllocatedBytes,
    long ItemCount, long ByteCount);
internal sealed record RuntimeEventCount(string Provider, string Event, long Count);
internal sealed record RuntimeIdentity(int ProcessId, string Framework, string Version, string Architecture, bool ServerGc);
internal sealed record SampleHotspot(string Phase, string WorkerRole, string Method,
    int InclusiveSamples, int LeafSamples);
internal sealed record ResourceCacheEvent(int ProcessId, int ThreadId, double TimeMs,
    string Manager, string ResourceId, string Operation, long Revision, long ByteCount);
internal sealed record TraceReport(int SchemaVersion, string Provider, string Source,
    double TraceDurationMs, long LostEvents, int ThreadSampleCount,
    int SamplesWithoutStacks, List<string> Errors, List<TraceSpan> Spans,
    List<TraceMilestone> Milestones, PhaseStats[] Phases,
    RuntimeEventCount[] RuntimeEvents, List<RuntimeIdentity> Runtimes, SampleHotspot[] SampleHotspots)
{
    public ResourceCacheEvent[] ResourceCacheEvents { get; init; } = [];
}
