using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace GodotAls.Tools.PerformanceTrace;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                Console.WriteLine("capture --godot EXE [--project DIR] [--output DIR] [--frames 180] [--timeout-seconds 180] [--profile PROFILES] [--dotnet EXE] [-- GODOT_ARGS]");
                Console.WriteLine("analyze --input FILE.nettrace --output DIR");
                Console.WriteLine("compare --baseline summary.json --current summary.json --output FILE.json");
                return 0;
            }
            var options = Parse(args[1..], out var extra);
            switch (args[0])
            {
                case "capture":
                    CheckOptions(options, "godot", "project", "output", "frames", "timeout-seconds", "profile", "dotnet");
                    return await Capture(options, extra);
                case "analyze":
                    CheckOptions(options, "input", "output");
                    if (extra.Length != 0) throw new ArgumentException("Unexpected extra arguments.");
                    return PrintReport(TraceAnalyzer.Analyze(Required(options, "input"), Required(options, "output")));
                case "compare":
                    CheckOptions(options, "baseline", "current", "output");
                    if (extra.Length != 0) throw new ArgumentException("Unexpected extra arguments.");
                    return Compare(options);
                default:
                    throw new ArgumentException("Unknown command: " + args[0]);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }

    private static async Task<int> Capture(Dictionary<string, string> options, string[] extra)
    {
        var project = Path.GetFullPath(options.GetValueOrDefault("project", "."));
        var godot = Path.GetFullPath(Required(options, "godot"));
        var output = Path.GetFullPath(options.GetValueOrDefault("output",
            Path.Combine(project, "artifacts", "performance", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture))));
        var frames = Positive(options.GetValueOrDefault("frames", "180"));
        var timeout = Positive(options.GetValueOrDefault("timeout-seconds", "180"));
        var dotnet = options.GetValueOrDefault("dotnet", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet");
        var profile = options.GetValueOrDefault("profile", "dotnet-common,dotnet-sampled-thread-time");
        if (!File.Exists(godot) || !File.Exists(Path.Combine(project, "project.godot")))
            throw new ArgumentException("Use the actual Godot .NET engine executable and a runnable project directory.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Capture output must be empty; existing evidence is preserved.");
        Directory.CreateDirectory(output);
        var arguments = new List<string>
        {
            "tool", "run", "dotnet-trace", "--", "collect", "--profile", profile,
            "--providers", TraceFormat.Provider + ":1:4", "--output", Path.Combine(output, "startup.nettrace"),
            "--stopping-event-provider-name", TraceFormat.Provider,
            "--stopping-event-event-name", "Milestone",
            "--stopping-event-payload-filter", "name:character.first_visual_commit",
            "--show-child-io", "--", godot, "--headless", "--path", project,
            "--fixed-fps", "60", "--max-fps", "60", "--quit-after", frames.ToString(CultureInfo.InvariantCulture),
            "res://scenes/demo/als_demo.tscn"
        };
        arguments.AddRange(extra);
        var packPath = Path.Combine(project, "assets", "config", "als_resource_pack.json");
        using var pack = File.Exists(packPath) ? JsonDocument.Parse(File.ReadAllText(packPath)) : null;
        var assemblies = new[] { "GodotALS.dll", "Als.Core.dll", "Als.Import.dll" }
            .Select(name => Path.Combine(project, ".godot", "mono", "temp", "bin", "Debug", name))
            .Where(File.Exists).Select(path =>
            {
                var file = new FileInfo(path);
                using var stream = File.OpenRead(path);
                return new { path, file.Length, file.LastWriteTimeUtc, sha256 = Convert.ToHexString(SHA256.HashData(stream)) };
            }).ToArray();
        var environment = new[] { "DOTNET_ROOT", "DOTNET_ROLL_FORWARD", "ALS_RESOURCE_PACK_PATH",
            "ALS_RAW_SOURCE_GENERATED_DIR", "ALS_VERIFY_RESOURCE_PACK_HASHES" }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable);
        TraceFormat.Write(Path.Combine(output, "run.json"), new
        {
            schemaVersion = 1, startedUtc = DateTime.UtcNow, project, godot, frames, profile, dotnet, arguments,
            collectorVersion = "9.0.661903", parserVersion = "3.2.8",
            gitCommit = await ReadCommand("git", ["rev-parse", "HEAD"], project),
            gitStatus = await ReadCommand("git", ["status", "--porcelain"], project),
            dotnetVersion = await ReadCommand(dotnet, ["--version"], project),
            godotVersion = await ReadCommand(godot, ["--version"], project),
            activePack = pack?.RootElement, assemblies, environment
        });
        Console.WriteLine("Capturing startup: " + output);
        using var stdout = new FileStream(Path.Combine(output, "collector.stdout.log"), FileMode.CreateNew);
        using var stderr = new FileStream(Path.Combine(output, "collector.stderr.log"), FileMode.CreateNew);
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = project, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start collector.");
        var watch = Stopwatch.StartNew();
        var copyOut = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var copyErr = process.StandardError.BaseStream.CopyToAsync(stderr);
        var timedOut = false;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        await Task.WhenAll(copyOut, copyErr);
        watch.Stop();
        TraceFormat.Write(Path.Combine(output, "result.json"), new
        {
            process.ExitCode, timedOut, wallMs = watch.Elapsed.TotalMilliseconds, finishedUtc = DateTime.UtcNow
        });
        if (timedOut || process.ExitCode != 0)
        {
            Console.Error.WriteLine($"Collector/child failed (exit={process.ExitCode}, timeout={timedOut}); logs retained in {output}.");
            return 1;
        }
        var report = TraceAnalyzer.Analyze(Path.Combine(output, "startup.nettrace"), output, requireStartupMilestones: true);
        TraceFormat.Write(Path.Combine(output, "validation.json"), new { valid = report.Errors.Count == 0, report.Errors });
        return PrintReport(report);
    }

    private static int PrintReport(TraceReport report)
    {
        Console.WriteLine($"Spans={report.Spans.Count}, thread samples={report.ThreadSampleCount}, lost events={report.LostEvents}, errors={report.Errors.Count}");
        foreach (var phase in report.Phases.Take(12))
            Console.WriteLine($"{phase.Phase} {phase.WorkerRole}: self={phase.SelfMs:F3} ms, inclusive={phase.InclusiveMs:F3} ms, count={phase.Count}");
        foreach (var error in report.Errors) Console.Error.WriteLine(error);
        return report.Errors.Count == 0 ? 0 : 2;
    }

    private static int Compare(Dictionary<string, string> options)
    {
        TraceReport Load(string path) => JsonSerializer.Deserialize<TraceReport>(File.ReadAllText(path), TraceFormat.Json)
            ?? throw new InvalidDataException("Invalid summary: " + path);
        var baselinePath = Path.GetFullPath(Required(options, "baseline"));
        var currentPath = Path.GetFullPath(Required(options, "current"));
        var baseline = Load(baselinePath);
        var current = Load(currentPath);
        if (baseline.Errors.Count != 0 || current.Errors.Count != 0 ||
            baseline.SchemaVersion != current.SchemaVersion || baseline.Provider != current.Provider)
            throw new InvalidDataException("Comparison requires valid summaries with the same schema/provider.");
        var oldPhases = baseline.Phases.ToDictionary(p => (p.Phase, p.WorkerRole));
        var newPhases = current.Phases.ToDictionary(p => (p.Phase, p.WorkerRole));
        var differences = oldPhases.Keys.Union(newPhases.Keys).Select(key =>
        {
            var before = oldPhases.GetValueOrDefault(key);
            var after = newPhases.GetValueOrDefault(key);
            return new { phase = key.Phase, workerRole = key.WorkerRole, before, after,
                selfDeltaMs = (after?.SelfMs ?? 0) - (before?.SelfMs ?? 0),
                inclusiveDeltaMs = (after?.InclusiveMs ?? 0) - (before?.InclusiveMs ?? 0) };
        }).OrderByDescending(d => Math.Abs(d.selfDeltaMs)).ToArray();
        TraceFormat.Write(Path.GetFullPath(Required(options, "output")), new
        {
            schemaVersion = 1, baseline = baselinePath, current = currentPath,
            interpretation = "Observed deltas only; compare run.json for pack, build, profile and cache conditions. No pass threshold is inferred.",
            differences
        });
        return 0;
    }

    private static async Task<string> ReadCommand(string executable, string[] arguments, string directory)
    {
        var start = new ProcessStartInfo(executable)
        { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to inspect run metadata.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? (await stdout).Trim() : (await stderr).Trim();
    }

    private static Dictionary<string, string> Parse(string[] args, out string[] extra)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        extra = [];
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--") { extra = args[(i + 1)..]; break; }
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length)
                throw new ArgumentException("Expected --option value: " + args[i]);
            if (!options.TryAdd(args[i][2..], args[++i])) throw new ArgumentException("Duplicate option.");
        }
        return options;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException("Missing --" + name);
    private static int Positive(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
        ? value : throw new ArgumentException("Expected a positive integer: " + text);
    private static void CheckOptions(Dictionary<string, string> options, params string[] names)
    {
        foreach (var name in options.Keys)
            if (!names.Contains(name, StringComparer.Ordinal)) throw new ArgumentException("Unknown option: --" + name);
    }
}
