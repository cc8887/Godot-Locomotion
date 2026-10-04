using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainLeanRuntimeSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Main Lean runtime failed: " + error); GetTree().Quit(1); }
    }
    private static void Run()
    {
        var bank = LyraLogicalSourceBank.Load(includeMainLean: true);
        var requestBytes = Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "runtime_requests.json");
        using var requests = JsonDocument.Parse(requestBytes);
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "runtime_native.json"));
        var root = native.RootElement;
        if (root.GetProperty("requestSha256").GetString() != LyraLogicalSourceBank.Sha(requestBytes) ||
            root.GetProperty("catalogSha256").GetString() != bank.MainLeanCatalogSha256 || root.GetProperty("angleType").GetString() != "double" ||
            root.GetProperty("behaviorSha256").GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "behavior.json")) ||
            root.GetProperty("sourceNodesSha256").GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/source_nodes.json")))
            throw new InvalidOperationException("Stale Main Lean runtime fixture.");
        var sequences = new[] { "main_lean_center", "main_lean_left", "main_lean_right" }.Select(s => bank.Get(s))
            .Select(s => new AlsAssetSyncSequence(s.Data.Identity.AnimationId, (float)s.Data.PlayLength, 1, 0, 0)).ToArray();
        var pose = new AlsPrecisePose[81]; var repeat = new AlsPrecisePose[81];
        var parallel = Enumerable.Range(0, 3).Select(_ => new AlsPrecisePose[81]).ToArray();
        double maxP = 0, maxQ = 0, maxS = 0;
        var frames = 0; var ticks = 0; var sampleTicks = 0; var rejected = 0; var tripleSamples = 0;
        void Reject(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { rejected++; return; }
            catch (ArgumentException) { rejected++; return; }
            throw new InvalidOperationException("Invalid Main Lean lifecycle/input was accepted.");
        }
        var nativeTraces = root.GetProperty("traces"); var requestTraces = requests.RootElement.GetProperty("traces");
        if (nativeTraces.GetArrayLength() != 3 || requestTraces.GetArrayLength() != 3) throw new InvalidOperationException("Missing Main Lean rates.");
        for (var traceIndex = 0; traceIndex < 3; traceIndex++)
        {
            var trace = nativeTraces[traceIndex]; var request = requestTraces[traceIndex];
            if (trace.GetProperty("hz").GetInt32() != request.GetProperty("hz").GetInt32()) throw new InvalidOperationException("Reordered Main Lean trace.");
            var host = new LyraMainLeanSourceHost(bank, sequences, 1000, 9000, 0, 1);
            var sparse = new LyraMainLeanSourceHost(bank, sequences, 1000, 9000, 0, 1);
            LyraMainLeanResolved? previous = null;
            var rows = trace.GetProperty("frames"); var inputRows = request.GetProperty("frames");
            for (var frameIndex = 0; frameIndex < rows.GetArrayLength(); frameIndex++)
            {
                var frame = inputRows[frameIndex]; var expectedNodes = rows[frameIndex].GetProperty("nodes");
                var angle = frame.GetProperty("angle").GetDouble(); var delta = frame.GetProperty("delta").GetSingle();
                var active = frame.GetProperty("active").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
                var initialize = frame.GetProperty("initialize").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
                var weights = frame.GetProperty("weights").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                var order = frame.GetProperty("order").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var before = host.States;
                LyraMainLeanResolved Execute(LyraMainLeanSourceHost owner)
                {
                    var candidate = owner.Prepare(angle, delta, weights, active, initialize, order);
                    var collected = owner.CollectAtCommonSync(candidate);
                    var playerOutput = new AlsAssetPlayerHistory[collected.Players.Length];
                    var sampleOutput = new AlsAssetSampleHistory[collected.Samples.Length];
                    if (!AlsSyncRuntime.TryEvaluateAssetSyncBatch([], Enumerable.Repeat(-1, collected.Players.Length).ToArray(),
                        collected.Players, collected.Samples, sequences, [], [], [], [], delta, [], playerOutput, sampleOutput, out var failure))
                        throw new InvalidOperationException($"Main Lean common Sync failed trace={traceIndex} frame={frameIndex}: {failure}");
                    if (ReferenceEquals(owner, host) && playerOutput.Length != 0)
                    {
                        var poisoned = playerOutput.ToArray(); poisoned[0] = poisoned[0] with { Epoch = 2 };
                        Reject(() => owner.Resolve(collected, poisoned, sampleOutput));
                        Reject(() => owner.Resolve(collected, playerOutput[..^1], sampleOutput));
                        var invalid = sampleOutput.ToArray(); invalid[0] = invalid[0] with { AnimationId = -1 };
                        Reject(() => owner.Resolve(collected, playerOutput, invalid));
                    }
                    return owner.Resolve(collected, playerOutput, sampleOutput);
                }
                var result = Execute(host);
                Parallel.For(0, 3, node => { if (active[node]) host.Evaluate(result, node, parallel[node]); });
                for (var node = 0; node < 3; node++)
                {
                    var expected = expectedNodes[node]; var state = result.States[node];
                    Equal(expected, "before", result.Inputs.Candidate.Prepared[node].Time, traceIndex, frameIndex, node);
                    Equal(expected, "pin", state.Pin, traceIndex, frameIndex, node);
                    Equal(expected, "cachedWeight", state.CachedWeight, traceIndex, frameIndex, node);
                    Equal(expected, "time", state.Time, traceIndex, frameIndex, node);
                    Equal(expected, "previous", state.DeltaPrevious, traceIndex, frameIndex, node);
                    Equal(expected, "delta", state.Delta, traceIndex, frameIndex, node);
                    if (expected.GetProperty("cache").GetInt32() != -1 || expected.GetProperty("active").GetBoolean() != active[node] ||
                        expected.GetProperty("samples").GetArrayLength() != state.Samples.Length)
                        throw new InvalidOperationException("Main Lean sample/cache inventory differs.");
                    var expectedSamples = expected.GetProperty("samples");
                    for (var sample = 0; sample < state.Samples.Length; sample++)
                    {
                        var s = state.Samples[sample]; var e = expectedSamples[sample];
                        if (s.Weight.SampleId != e.GetProperty("index").GetInt32()) throw new InvalidOperationException("Main Lean sample order differs.");
                        Equal(e, "weight", s.Weight.Weight, traceIndex, frameIndex, node);
                        Equal(e, "weightRate", s.Weight.WeightRate, traceIndex, frameIndex, node);
                        Equal(e, "time", s.Clock.Time, traceIndex, frameIndex, node);
                        Equal(e, "previous", s.Clock.PreviousTime, traceIndex, frameIndex, node);
                        Equal(e, "deltaPrevious", s.Clock.DeltaPrevious, traceIndex, frameIndex, node);
                        Equal(e, "delta", s.Clock.Delta, traceIndex, frameIndex, node);
                        Equal(e, "rate", 1, traceIndex, frameIndex, node);
                    }
                    var capturedNode = node;
                    if (!active[node]) { Reject(() => host.Evaluate(result, capturedNode, pose)); continue; }
                    host.Evaluate(result, node, pose); host.Evaluate(result, node, repeat);
                    if (!pose.SequenceEqual(repeat) || !pose.SequenceEqual(parallel[node]))
                        throw new InvalidOperationException("Repeated/parallel Main Lean occurrence scratch changed its pose.");
                    var output = expected.GetProperty("output");
                    LyraLogicalSourceSmoke.Compare(output.GetProperty("pose"), pose, ref maxP, ref maxQ, ref maxS, "mainLean", frameIndex, "runtime");
                    if (output.GetProperty("curves").EnumerateObject().Any() || output.GetProperty("attributes").GetArrayLength() != 0)
                        throw new InvalidOperationException("Do not discard Main Lean metadata.");
                    ticks++; sampleTicks += state.Samples.Length; if (state.Samples.Length == 3) tripleSamples++;
                    Reject(() => host.Evaluate(result, capturedNode, new AlsPrecisePose[80]));
                }
                host.Cancel();
                if (host.States != before) throw new InvalidOperationException("Cancelled Main Lean frame published history.");
                Reject(() => host.Commit(result));
                var retry = Execute(host);
                Same(result, retry);
                for (var node = 0; node < 3; node++) if (active[node])
                {
                    host.Evaluate(retry, node, pose);
                    LyraLogicalSourceSmoke.Compare(expectedNodes[node].GetProperty("output").GetProperty("pose"), pose,
                        ref maxP, ref maxQ, ref maxS, "mainLeanRetry", frameIndex, "runtime");
                }
                host.Commit(retry); Reject(() => host.Commit(retry));
                if (previous is not null) { var old = previous; Reject(() => host.Commit(old)); }
                previous = retry;
                var sparseResult = Execute(sparse);
                if (frameIndex % 17 == 0)
                    for (var node = 0; node < 3; node++) if (active[node]) sparse.Evaluate(sparseResult, node, repeat);
                Same(retry, sparseResult); sparse.Commit(sparseResult);
                frames++;
            }
        }
        if (frames != 2100 || ticks != 2121 || tripleSamples == 0 || rejected < 10000)
            throw new InvalidOperationException("Incomplete Main Lean runtime coverage.");
        GD.Print($"LYRA_MAIN_LEAN_RUNTIME_GODOT_OK frames={frames} ticks={ticks} sampleTicks={sampleTicks} tripleSamples={tripleSamples} rejected={rejected} " +
            $"positionCm={maxP} quaternion={maxQ} scale={maxS} clocksAndWeights=exactBits retry=true updateOnly=true parallelOccurrences=true stage=MainLeanSources production=false");
    }
    private static void Equal(JsonElement expected, string field, float actual, int trace, int frame, int node)
    {
        var bits = expected.GetProperty(field + "Bits").GetUInt32();
        if (unchecked((uint)BitConverter.SingleToInt32Bits(actual)) != bits)
            throw new InvalidOperationException($"Main Lean {field} differs trace={trace} frame={frame} node={node} actual={actual:R} expected={expected.GetProperty(field)}.");
    }
    private static void Same(LyraMainLeanResolved a, LyraMainLeanResolved b)
    {
        for (var i = 0; i < 3; i++)
        {
            var x = a.States[i]; var y = b.States[i];
            if (x.Initialized != y.Initialized || x.Pin != y.Pin || x.CachedWeight != y.CachedWeight || x.Time != y.Time ||
                x.DeltaPrevious != y.DeltaPrevious || x.Delta != y.Delta || !x.Samples.SequenceEqual(y.Samples))
                throw new InvalidOperationException("Main Lean cancelled/retry/update-only histories differ.");
        }
    }
}
