# Als.PerformanceTrace

Standalone .NET 8 command-line collection and analysis for `GodotAls-Performance`.
Run from the repository root with the SDK selected by `global.json` and runnable Godot assets.

```sh
dotnet tool restore
dotnet build GodotALS.csproj -p:Optimize=true
dotnet run --project tools/Als.PerformanceTrace -c Release -- capture --godot <absolute-engine-path>
dotnet run --project tools/Als.PerformanceTrace -c Release -- analyze --input <trace.nettrace> --output <fresh-directory>
dotnet run --project tools/Als.PerformanceTrace -c Release -- compare --baseline <summary.json> --current <summary.json> --output <new-comparison.json>
```

Capture retains stdout/stderr, run metadata and raw `.nettrace`, and exports JSON, CSV and Chrome Trace.
It stops tracing at the first visible pose commit, checks paired scopes and startup milestones, and preserves failed runs.
Sample counts represent sampled thread stacks; allocation totals include children and are not resident memory.

See [the instrumentation contract](../../docs/architecture/performance-instrumentation.md) for options, units, AI queries and validation boundaries.
