using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsAssetSyncRuntimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReplaysNativeMixedGroupsUsingOnlyOwnTimeAndMarkerHistory(bool batch, bool independent)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_blendspace_tick_native.json")));
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        var sequences = new List<AlsAssetSyncSequence>(); var markers = new List<AlsAssetSyncMarker>();
        var sequenceByPath = new Dictionary<string, int>();
        foreach (var asset in assets)
        {
            var sources = asset.TryGetProperty("samples", out var sourceSamples) ? sourceSamples.EnumerateArray().ToArray() : [asset.GetProperty("sequence")];
            foreach (var source in sources)
            {
                var path = source.GetProperty("path").GetString()!;
                if (sequenceByPath.ContainsKey(path)) continue;
                var id = sequences.Count; sequenceByPath.Add(path, id);
                var start = markers.Count;
                markers.AddRange(source.GetProperty("markers").EnumerateArray().Select(m => new AlsAssetSyncMarker(Symbol(m.GetProperty("name").GetString()!), m.GetProperty("time").GetSingle())));
                sequences.Add(new(id, source.GetProperty("length").GetSingle(), source.GetProperty("rate").GetSingle(), start, markers.Count - start));
            }
        }
        var sequenceTable = sequences.ToArray(); var markerTable = markers.ToArray();
        var frameCount = 0; var sampleCount = 0; var leaderChanges = 0;
        foreach (var trace in doc.RootElement.GetProperty("traces").EnumerateArray())
        {
            if ((trace.GetProperty("scenario").GetInt32() == 6) != independent) continue;
            var group = default(AlsAssetSyncGroupHistory);
            AlsAssetPlayerHistory[] history = []; AlsAssetSampleHistory[] sampleHistory = [];
            var epochs = new long[8]; Array.Fill(epochs, 1);
            var frameIndex = 0;
            foreach (var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var context = $"hz={trace.GetProperty("hz")} scenario={trace.GetProperty("scenario")} frame={frameIndex}";
                var samples = new List<AlsAssetSyncSample>(); var players = new List<AlsAssetSyncPlayer>();
                foreach (var input in frame.GetProperty("input").EnumerateArray())
                {
                    var id = input.GetProperty("slot").GetInt32(); var assetId = input.GetProperty("asset").GetInt32(); var asset = assets[assetId];
                    if (input.GetProperty("reset").GetBoolean()) epochs[id]++;
                    var time = input.GetProperty("time").GetSingle();
                    var own = Array.FindIndex(history, h => h.PlayerId == id && h.Epoch == epochs[id]);
                    if (own >= 0) time = history[own].Time;
                    var start = samples.Count; ulong mask = 0;
                    var isSpace = asset.TryGetProperty("samples", out var definitions);
                    if (isSpace)
                    {
                        var expected = frame.GetProperty("output").EnumerateArray().Single(o => o.GetProperty("slot").GetInt32() == id);
                        // Only weights/order are native inputs here. Playback time, sample time,
                        // marker records and group positions are never fed back from the oracle.
                        foreach (var weight in expected.GetProperty("samples").EnumerateArray())
                        {
                            var index = weight.GetProperty("index").GetInt32(); var definition = definitions[index];
                            samples.Add(new(batch ? id * AlsSyncRuntime.MaxAssetSyncSamples + index : index, sequenceByPath[definition.GetProperty("path").GetString()!], weight.GetProperty("weight").GetSingle(),
                                definition.GetProperty("sampleRate").GetSingle(), weight.GetProperty("sampleRate").GetSingle()));
                        }
                        foreach (var definition in definitions.EnumerateArray())
                            foreach (var marker in definition.GetProperty("markers").EnumerateArray()) mask |= 1UL << Symbol(marker.GetProperty("name").GetString()!);
                    }
                    else
                    {
                        var definition = asset.GetProperty("sequence");
                        samples.Add(new(batch ? id * AlsSyncRuntime.MaxAssetSyncSamples : 0, sequenceByPath[definition.GetProperty("path").GetString()!], 1));
                        foreach (var marker in definition.GetProperty("markers").EnumerateArray()) mask |= 1UL << Symbol(marker.GetProperty("name").GetString()!);
                    }
                    players.Add(new(id, assetId, epochs[id], isSpace ? AlsAssetSyncKind.BlendSpace : AlsAssetSyncKind.Sequence,
                        time, input.GetProperty("rate").GetSingle(), input.GetProperty("weight").GetSingle(), start, samples.Count - start, mask,
                        RequestedInertialization: input.GetProperty("reset").GetBoolean()));
                }
                var ticks = players.ToArray(); var sampleTicks = samples.ToArray();
                var output = new AlsAssetPlayerHistory[ticks.Length]; var sampleOutput = new AlsAssetSampleHistory[sampleTicks.Length];
                Assert.True(Evaluate(batch, independent, group, ticks, sampleTicks, sequenceTable, markerTable,
                    history, sampleHistory, frame.GetProperty("delta").GetSingle(), output, sampleOutput, out var candidate, out var error), context + " " + error);
                Assert.True(frame.GetProperty("leader").GetInt32() == candidate.LeaderPlayerId, context + $" leader expected={frame.GetProperty("leader")} actual={candidate.LeaderPlayerId}");
                Near(frame.GetProperty("previousRatio").GetSingle(), candidate.PreviousRatio, context + " previous ratio");
                Near(frame.GetProperty("ratio").GetSingle(), candidate.Ratio, context + " ratio");
                Assert.Equal(frame.GetProperty("markerSync").GetBoolean(), candidate.ValidMarkerMask != 0);
                if (frame.TryGetProperty("markerStart", out var markerStart)) CheckPosition(markerStart, candidate.MarkerStart, context + " group start");
                if (frame.TryGetProperty("markerEnd", out var markerEnd)) CheckPosition(markerEnd, candidate.MarkerEnd, context + " group end");
                foreach (var expected in frame.GetProperty("output").EnumerateArray())
                {
                    var slot = expected.GetProperty("slot").GetInt32();
                    var actual = output.Single(o => o.PlayerId == slot); var label = context + $" player={slot}";
                    Near(expected.GetProperty("time").GetSingle(), actual.Time, label + " time");
                    Near(expected.GetProperty("previous").GetSingle(), actual.DeltaPrevious, label + " delta previous");
                    Near(expected.GetProperty("delta").GetSingle(), actual.Delta, label + " delta");
                    CheckMarker(expected.GetProperty("marker"), actual.Marker, label);
                    var expectedSamples = expected.GetProperty("samples").EnumerateArray().ToArray();
                    for (var i = 0; i < expectedSamples.Length; i++)
                    {
                        var source = expectedSamples[i]; var result = sampleOutput[actual.SampleStart + i]; var sampleLabel = label + $" sample={result.SampleId}";
                        Assert.Equal((batch ? slot * AlsSyncRuntime.MaxAssetSyncSamples : 0) + source.GetProperty("index").GetInt32(), result.SampleId);
                        Near(source.GetProperty("time").GetSingle(), result.Time, sampleLabel + " time");
                        Near(source.GetProperty("previous").GetSingle(), result.PreviousTime, sampleLabel + " previous");
                        Near(source.GetProperty("deltaPrevious").GetSingle(), result.DeltaPrevious, sampleLabel + " delta previous");
                        Near(source.GetProperty("delta").GetSingle(), result.Delta, sampleLabel + " delta");
                        CheckMarker(source.GetProperty("marker"), result.Marker, sampleLabel);
                        sampleCount++;
                    }
                }
                var retry = new AlsAssetPlayerHistory[ticks.Length]; var sampleRetry = new AlsAssetSampleHistory[sampleTicks.Length];
                Assert.True(Evaluate(batch, independent, group, ticks, sampleTicks, sequenceTable, markerTable,
                    history, sampleHistory, frame.GetProperty("delta").GetSingle(), retry, sampleRetry, out var retriedGroup, out _));
                Assert.Equal(candidate, retriedGroup); Assert.Equal(output, retry); Assert.Equal(sampleOutput, sampleRetry);
                if (group.HasLeader && candidate.HasLeader && group.LeaderPlayerId != candidate.LeaderPlayerId) leaderChanges++;
                group = candidate; history = output; sampleHistory = sampleOutput;
                frameCount++; frameIndex++;
            }
        }
        Assert.Equal(independent ? 192 : 1152, frameCount);
        Assert.True(sampleCount > (independent ? 300 : 15000));
        if (!independent) Assert.True(leaderChanges > 100);
    }

    private static bool Evaluate(bool batch, bool independent, AlsAssetSyncGroupHistory previous, AlsAssetSyncPlayer[] players, AlsAssetSyncSample[] samples,
        AlsAssetSyncSequence[] sequences, AlsAssetSyncMarker[] markers, AlsAssetPlayerHistory[] previousPlayers,
        AlsAssetSampleHistory[] previousSamples, float delta, AlsAssetPlayerHistory[] output, AlsAssetSampleHistory[] sampleOutput,
        out AlsAssetSyncGroupHistory group, out AlsP5FailureCode failure)
    {
        if (independent)
        {
            group = new(0, false, -1, -1, 0, -1, 0, 0, 0, 0, default, default);
            return batch ? AlsSyncRuntime.TryEvaluateAssetSyncBatch([], Enumerable.Repeat(-1, players.Length).ToArray(), players, samples,
                sequences, markers, [], previousPlayers, previousSamples, delta, [], output, sampleOutput, out failure) :
                AlsSyncRuntime.TryEvaluateIndependentAssetPlayers(players, samples, sequences, markers, previousPlayers, previousSamples,
                    delta, output, sampleOutput, out failure);
        }
        if (!batch) return AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, previous, players, samples, sequences, markers,
            previousPlayers, previousSamples, delta, output, sampleOutput, out group, out failure);
        AlsAssetSyncBatchGroupHistory[] before = [new(previous, 0, previousPlayers.Length, 0, previousSamples.Length)];
        var after = new AlsAssetSyncBatchGroupHistory[1];
        var success = AlsSyncRuntime.TryEvaluateAssetSyncBatch([0], new int[players.Length], players, samples, sequences, markers,
            before, previousPlayers, previousSamples, delta, after, output, sampleOutput, out failure);
        group = after[0].Group;
        return success;
    }

    private static int Symbol(string name) => name switch { "None" => 0, "Left" => 1, "Right" => 2, _ => throw new InvalidOperationException(name) };
    private static void CheckPosition(JsonElement expected, in AlsAssetMarkerPosition actual, string label)
    {
        Assert.True(Symbol(expected.GetProperty("previous").GetString()!) == actual.PreviousSymbol, label + " previous symbol");
        Assert.True(Symbol(expected.GetProperty("next").GetString()!) == actual.NextSymbol, label + " next symbol");
        Near(expected.GetProperty("alpha").GetSingle(), actual.Alpha, label + " alpha");
    }
    private static void CheckMarker(JsonElement expected, in AlsAssetMarkerRecord actual, string label)
    {
        Assert.True(expected.GetProperty("previous").GetInt32() == actual.PreviousIndex, label + $" previous marker expected={expected.GetProperty("previous")} actual={actual.PreviousIndex}");
        Assert.True(expected.GetProperty("next").GetInt32() == actual.NextIndex, label + $" next marker expected={expected.GetProperty("next")} actual={actual.NextIndex}");
        Near(expected.GetProperty("previousDistance").GetSingle(), actual.PreviousDistance, label + " previous distance");
        Near(expected.GetProperty("nextDistance").GetSingle(), actual.NextDistance, label + " next distance");
    }
    private static void Near(float expected, float actual, string label) => Assert.True(MathF.Abs(expected - actual) <= .00003f, $"{label}: expected {expected:R}, actual {actual:R}");
}
