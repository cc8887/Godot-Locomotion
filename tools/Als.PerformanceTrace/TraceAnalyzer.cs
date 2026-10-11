using System.Globalization;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace GodotAls.Tools.PerformanceTrace;

internal static class TraceAnalyzer
{
    private sealed record ThreadSample(int ProcessId, int ThreadId, double TimeMs, string[] Frames);

    public static TraceReport Analyze(string input, string output, bool requireStartupMilestones = false)
    {
        input = Path.GetFullPath(input);
        output = Path.GetFullPath(output);
        if (!File.Exists(input)) throw new FileNotFoundException("Trace file is missing.", input);
        Directory.CreateDirectory(output);
        if (File.Exists(Path.Combine(output, "summary.json")) || File.Exists(Path.Combine(output, "events.etlx")))
            throw new IOException("Analysis already exists; use a fresh output directory.");
        string etlx;
        try
        {
            etlx = TraceLog.CreateFromEventPipeDataFile(input, Path.Combine(output, "events.etlx"));
        }
        catch (Exception exception)
        {
            TraceFormat.Write(Path.Combine(output, "parse-failure.json"), new
            { schemaVersion = 1, input, error = exception.ToString() });
            throw;
        }
        using var log = new TraceLog(etlx);
        var spans = new List<TraceSpan>();
        var milestones = new List<TraceMilestone>();
        var runtimes = new List<RuntimeIdentity>();
        var cacheEvents = new List<ResourceCacheEvent>();
        var byId = new Dictionary<(int, long), TraceSpan>();
        var errors = new List<string>();
        var runtimeEvents = new Dictionary<(string Provider, string Event), long>();
        var samples = new List<ThreadSample>();
        var endMs = 0d;
        foreach (var data in log.Events)
        {
            endMs = Math.Max(endMs, data.TimeStampRelativeMSec);
            if (data.ProviderName == TraceFormat.Provider)
            {
                switch ((int)data.ID)
                {
                    case 0:
                        errors.Add("EventSource: " + data.PayloadString(0));
                        break;
                    case 1:
                        var span = new TraceSpan
                        {
                            Id = Number(data, "spanId"), ParentId = Number(data, "parentId"),
                            Phase = Text(data, "phase"), AssetPath = Text(data, "assetPath"),
                            AssetId = (int)Number(data, "assetId"),
                            CharacterId = Number(data, "characterId"), Generation = Number(data, "generation"),
                            WorkerRole = Text(data, "workerRole"), ProcessId = data.ProcessID,
                            ThreadId = data.ThreadID, StartMs = data.TimeStampRelativeMSec
                        };
                        if (!byId.TryAdd((span.ProcessId, span.Id), span))
                        {
                            errors.Add($"Duplicate span {span.Id} in process {span.ProcessId}.");
                            break;
                        }
                        spans.Add(span);
                        break;
                    case 2:
                        var id = Number(data, "spanId");
                        if (!byId.TryGetValue((data.ProcessID, id), out var ended))
                        {
                            errors.Add($"End without begin: {id}.");
                            break;
                        }
                        if (ended.HasEnd) errors.Add($"Duplicate end: {id}.");
                        var frequency = Number(data, "frequency");
                        if (frequency <= 0) throw new InvalidDataException("Invalid Stopwatch frequency.");
                        ended.HasEnd = true;
                        ended.EndMs = data.TimeStampRelativeMSec;
                        ended.DurationMs = Number(data, "elapsedTicks") * 1000d / frequency;
                        ended.ManagedAllocatedBytes = Number(data, "managedAllocatedBytes");
                        ended.Completed = Number(data, "completed") == 1;
                        ended.ItemCount = Number(data, "itemCount");
                        ended.ByteCount = Number(data, "byteCount");
                        if (data.ThreadID != ended.ThreadId || ended.ManagedAllocatedBytes < 0)
                            errors.Add($"Span crossed threads: {id}.");
                        if (!ended.Completed) errors.Add($"Incomplete operation: {ended.Phase} ({id}).");
                        break;
                    case 3:
                        milestones.Add(new(Text(data, "name"), data.ProcessID, data.ThreadID,
                            data.TimeStampRelativeMSec, Number(data, "characterId"),
                            Number(data, "generation"), Number(data, "frameId")));
                        break;
                    case 4:
                        runtimes.Add(new(data.ProcessID, Text(data, "framework"), Text(data, "version"),
                            Text(data, "architecture"), Number(data, "serverGc") == 1));
                        break;
                    case 5:
                        cacheEvents.Add(new(data.ProcessID, data.ThreadID, data.TimeStampRelativeMSec,
                            Text(data, "manager"), Text(data, "resourceId"), Text(data, "operation"),
                            Number(data, "revision"), Number(data, "byteCount")));
                        break;
                }
            }
            else if (data.ProviderName == "Microsoft-DotNETCore-SampleProfiler")
            {
                var frames = new List<string>();
                for (var stack = data.CallStack(); stack is not null; stack = stack.Caller)
                {
                    var address = stack.CodeAddress;
                    var method = address.FullMethodName;
                    frames.Add(string.IsNullOrEmpty(method)
                        ? $"{address.ModuleFile?.Name ?? "native/unknown"}!0x{address.Address:x}" : method);
                }
                samples.Add(new(data.ProcessID, data.ThreadID, data.TimeStampRelativeMSec, frames.ToArray()));
            }
            else if (data.ProviderName.StartsWith("Microsoft-Windows-DotNETRuntime", StringComparison.Ordinal))
            {
                var key = (data.ProviderName, data.EventName);
                runtimeEvents[key] = runtimeEvents.GetValueOrDefault(key) + 1;
            }
        }
        if (spans.Count == 0) errors.Add("No GodotAls startup spans; verify provider and startup capture.");
        if (requireStartupMilestones)
        {
            if (runtimes.Count == 0) errors.Add("Missing actual game runtime identity.");
            if (!milestones.Any(m => m.Name == "demo.configured")) errors.Add("Missing demo.configured milestone.");
            if (!milestones.Any(m => m.Name == "character.first_visual_commit"))
                errors.Add("Missing first visible committed pose; increase --frames or inspect the runtime log.");
        }
        foreach (var span in spans.Where(s => !s.HasEnd)) errors.Add($"Missing end: {span.Phase} ({span.Id}).");
        if (log.EventsLost != 0) errors.Add($"Lost events: {log.EventsLost}.");
        ComputeSelfTimes(spans, errors);
        var stats = spans.GroupBy(s => (s.Phase, s.WorkerRole)).Select(g => new PhaseStats(
            g.Key.Phase, g.Key.WorkerRole, g.Count(), g.Sum(s => s.DurationMs),
            g.Sum(s => s.SelfMs), g.Max(s => s.DurationMs), g.Sum(s => s.ManagedAllocatedBytes),
            g.Sum(s => s.ItemCount), g.Sum(s => s.ByteCount))).OrderByDescending(p => p.SelfMs).ToArray();
        var report = new TraceReport(1, TraceFormat.Provider, input, endMs, log.EventsLost,
            samples.Count, samples.Count(s => s.Frames.Length == 0), errors, spans, milestones, stats,
            runtimeEvents.Select(p => new RuntimeEventCount(p.Key.Provider, p.Key.Event, p.Value))
                .OrderBy(p => p.Provider).ThenBy(p => p.Event).ToArray(), runtimes, BuildHotspots(samples, spans))
        { ResourceCacheEvents = cacheEvents.ToArray() };
        TraceFormat.Write(Path.Combine(output, "summary.json"), report);
        WriteCsv(output, report);
        WriteTimeline(output, report);
        return report;
    }

    internal static void ComputeSelfTimes(List<TraceSpan> spans, List<string> errors)
    {
        var byId = spans.ToDictionary(s => (s.ProcessId, s.Id));
        foreach (var child in spans.Where(s => s.ParentId != 0))
            if (!byId.TryGetValue((child.ProcessId, child.ParentId), out var parent) || parent.ThreadId != child.ThreadId)
                errors.Add($"Invalid parent: {child.Id} -> {child.ParentId}.");
        var children = spans.Where(s => s.ParentId != 0).ToLookup(s => (s.ProcessId, s.ParentId));
        foreach (var span in spans)
        {
            var previousEnd = span.StartMs;
            var covered = 0d;
            foreach (var child in children[(span.ProcessId, span.Id)].OrderBy(c => c.StartMs))
            {
                if (child.StartMs < previousEnd || child.EndMs > span.EndMs)
                    errors.Add($"Overlapping or out-of-parent synchronous span: {child.Id}.");
                covered += child.DurationMs;
                previousEnd = child.EndMs;
            }
            span.SelfMs = Math.Max(0, span.DurationMs - covered);
        }
    }

    private static SampleHotspot[] BuildHotspots(List<ThreadSample> samples, List<TraceSpan> spans)
    {
        var threads = spans.GroupBy(s => (s.ProcessId, s.ThreadId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.StartMs).ToArray());
        var counts = new Dictionary<(string Phase, string Role, string Method), (int Inclusive, int Leaf)>();
        foreach (var sample in samples)
        {
            var owner = threads.GetValueOrDefault((sample.ProcessId, sample.ThreadId))?.FirstOrDefault(s =>
                s.StartMs <= sample.TimeMs && sample.TimeMs <= s.EndMs);
            var phase = owner?.Phase ?? "outside_startup_scopes";
            var role = owner?.WorkerRole ?? "";
            foreach (var method in sample.Frames.Distinct(StringComparer.Ordinal))
            {
                var key = (phase, role, method);
                var count = counts.GetValueOrDefault(key);
                counts[key] = (count.Inclusive + 1, count.Leaf + (sample.Frames[0] == method ? 1 : 0));
            }
        }
        return counts.Select(p => new SampleHotspot(p.Key.Phase, p.Key.Role, p.Key.Method,
            p.Value.Inclusive, p.Value.Leaf)).OrderByDescending(p => p.InclusiveSamples).ToArray();
    }

    private static long Number(TraceEvent data, string name) =>
        Convert.ToInt64(data.PayloadByName(name), CultureInfo.InvariantCulture);
    private static string Text(TraceEvent data, string name) =>
        Convert.ToString(data.PayloadByName(name), CultureInfo.InvariantCulture) ?? "";

    private static string Cell(object value) => "\"" +
        (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"") + "\"";

    private static void WriteCsv(string output, TraceReport report)
    {
        using var phases = new StreamWriter(new FileStream(Path.Combine(output, "phases.csv"), FileMode.CreateNew), Encoding.UTF8);
        phases.WriteLine("phase,worker_role,count,inclusive_ms,self_ms,max_ms,managed_allocated_bytes,item_count,byte_count");
        foreach (var p in report.Phases)
            phases.WriteLine(string.Join(",", new object[] { p.Phase, p.WorkerRole, p.Count,
                p.InclusiveMs, p.SelfMs, p.MaxMs, p.ManagedAllocatedBytes, p.ItemCount, p.ByteCount }.Select(Cell)));
        using var resources = new StreamWriter(new FileStream(Path.Combine(output, "spans.csv"), FileMode.CreateNew), Encoding.UTF8);
        resources.WriteLine("id,parent_id,process_id,thread_id,phase,asset_path,asset_id,character_id,generation,worker_role,start_ms,duration_ms,self_ms,managed_allocated_bytes,completed,item_count,byte_count");
        foreach (var s in report.Spans)
            resources.WriteLine(string.Join(",", new object[] { s.Id, s.ParentId, s.ProcessId, s.ThreadId,
                s.Phase, s.AssetPath, s.AssetId, s.CharacterId, s.Generation, s.WorkerRole,
                s.StartMs, s.DurationMs, s.SelfMs, s.ManagedAllocatedBytes, s.Completed, s.ItemCount, s.ByteCount }.Select(Cell)));
        using var cache = new StreamWriter(new FileStream(Path.Combine(output, "resource-cache.csv"), FileMode.CreateNew), Encoding.UTF8);
        cache.WriteLine("process_id,thread_id,time_ms,manager,resource_id,operation,revision,byte_count");
        foreach (var entry in report.ResourceCacheEvents)
            cache.WriteLine(string.Join(",", new object[] { entry.ProcessId, entry.ThreadId, entry.TimeMs,
                entry.Manager, entry.ResourceId, entry.Operation, entry.Revision, entry.ByteCount }.Select(Cell)));
    }

    private static void WriteTimeline(string output, TraceReport report)
    {
        var events = new List<object>();
        foreach (var s in report.Spans.Where(s => s.HasEnd))
            events.Add(new { name = s.Phase, cat = s.WorkerRole, ph = "X", pid = s.ProcessId,
                tid = s.ThreadId, ts = s.StartMs * 1000, dur = (s.EndMs - s.StartMs) * 1000,
                args = new { s.Id, s.ParentId, s.AssetId, s.AssetPath, s.CharacterId, s.Generation,
                    s.DurationMs, s.ManagedAllocatedBytes, s.Completed, s.ItemCount, s.ByteCount } });
        foreach (var m in report.Milestones)
            events.Add(new { name = m.Name, ph = "i", s = "t", pid = m.ProcessId, tid = m.ThreadId,
                ts = m.TimeMs * 1000, args = new { m.CharacterId, m.Generation, m.FrameId } });
        TraceFormat.Write(Path.Combine(output, "timeline.json"), new { traceEvents = events, displayTimeUnit = "ms" });
    }
}
