using System.Text.Json;
using Godot;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraEvaluatorSyncSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Evaluator Sync failed: " + error); GetTree().Quit(1); }
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal(float actual, float native, string field)
    { Require(BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(native), $"{field}: actual={actual:R} native={native:R}"); }
    private static float NativeFloat(JsonElement row, string field) =>
        BitConverter.Int32BitsToSingle(unchecked((int)row.GetProperty(field + "Bits").GetUInt32()));
    private static void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        var nonloop = OS.GetCmdlineUserArgs().Contains("--lyra-evaluator-nonloop-smoke");
        var fixture = nonloop ? "evaluator_nonloop_native_bits.json" : "evaluator_sync_native_bits.json";
        var requests = nonloop ? "evaluator_nonloop_requests.json" : "evaluator_sync_requests.json";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + fixture));
        var data = document.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32() == 2, "Missing exact native float encoding.");
        foreach (var (key, file) in new[] { ("sourceNodesSha256", "source_nodes.json"), ("requestSha256", requests),
                     ("catalogSha256", "logical_controls/catalog.json"), ("calibrationSha256", "logical_controls/calibration.json") })
            Require(data.GetProperty(key).GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + file)), "Stale evaluator fixture: " + key);
        var symbols = data.GetProperty("assets").EnumerateArray().SelectMany(a => a.GetProperty("markers").EnumerateArray())
            .Select(m => m.GetProperty("name").GetString()!).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        int Symbol(string name) => name == "None" ? 0 : Array.IndexOf(symbols, name) + 1;
        var sequences = new List<AlsAssetSyncSequence>(); var markers = new List<AlsAssetSyncMarker>();
        foreach (var row in data.GetProperty("assets").EnumerateArray())
        {
            var count = row.GetProperty("markers").GetArrayLength();
            sequences.Add(new(sequences.Count, row.GetProperty("length").GetSingle(), row.GetProperty("rateScale").GetSingle(), markers.Count, count));
            markers.AddRange(row.GetProperty("markers").EnumerateArray().Select(m => new AlsAssetSyncMarker(Symbol(m.GetProperty("name").GetString()!), m.GetProperty("time").GetSingle())));
        }
        var sequenceArray = sequences.ToArray(); var markerArray = markers.ToArray();
        var sourceCatalog = LyraSourceNodeCatalog.Load();
        var factoryNode = sourceCatalog.Classes.First(c => c.ClassPath.Contains("ABP_UnarmedAnimLayers", StringComparison.Ordinal))
            .Nodes.Values.Single(n => n.Functions.BecomeRelevant == "SetUpFallLandAnim");
        var frames = 0; var ticks = 0; var empty = 0; var traces = 0; var fullTurns = 0; var forced = 0;
        foreach (var trace in data.GetProperty("traces").EnumerateArray())
        {
            var clocks = new float[3]; var epochs = new long[3];
            var markerStorage = Enumerable.Repeat(AlsAssetMarkerRecord.Invalid, 3).ToArray();
            var previousPlayers = Array.Empty<AlsAssetPlayerHistory>(); var previousSamples = Array.Empty<AlsAssetSampleHistory>();
            var previousGroups = Array.Empty<AlsAssetSyncBatchGroupHistory>();
            var groups = new AlsAssetSyncBatchGroupHistory[1];
            var label = trace.GetProperty("scenario").GetString() + "/" + trace.GetProperty("hz").GetInt32();
            var frameIndex = 0;
            foreach (var frame in trace.GetProperty("frames").EnumerateArray())
            {
                var delta = frame.GetProperty("delta").GetSingle();
                var inputs = frame.GetProperty("inputs").EnumerateArray().ToArray();
                var players = new AlsAssetSyncPlayer[inputs.Length]; var samples = new AlsAssetSyncSample[inputs.Length];
                var playerGroups = new int[inputs.Length];
                for (var index = 0; index < inputs.Length; index++)
                {
                    var input = inputs[index]; var slot = input.GetProperty("slot").GetInt32();
                    var asset = input.GetProperty("asset").GetInt32(); var sequence = sequenceArray[asset];
                    var reset = input.GetProperty("reinitialize").GetBoolean(); if (reset) epochs[slot]++;
                    if (reset) markerStorage[slot] = markerStorage[slot] with { PreviousIndex = -2, NextIndex = -2, Initialized = false };
                    if (epochs[slot] == 0) epochs[slot] = 1;
                    if (slot == 2 && reset) clocks[slot] = input.GetProperty("startPosition").GetSingle();
                    var location = $"{label}/{frameIndex}/{slot}";
                    ulong mask = 0;
                    foreach (var marker in markerArray.AsSpan(sequence.MarkerStart, sequence.MarkerCount)) mask |= 1UL << marker.Symbol;
                    AlsAssetSyncPlayer? preparedPlayer = null;
                    Equal(clocks[slot], NativeFloat(input, "before"), location + "/before");
                    var time = clocks[slot]; var rate = input.GetProperty("playRate").GetSingle();
                    var method = input.GetProperty("method").GetInt32();
                    if (slot < 2)
                    {
                        // Exercise the real compiled-node adapter, with this
                        // probe's controlled configuration overrides. Native
                        // updates use the same standalone evaluator settings.
                        var configured = factoryNode with { Method = (LyraSourceSyncMethod)method,
                            Group = method == 1 ? "Probe" : "None", Role = (LyraSourceGroupRole)input.GetProperty("role").GetInt32(),
                            Looping = input.GetProperty("looping").GetBoolean(), Settings = input, OverridePositionWhenJoining = false };
                        var sourceTick = LyraEvaluatorSourceTick.Prepare(configured, slot, asset, epochs[slot], time,
                            input.GetProperty("explicitTime").GetSingle(), sequence, delta, input.GetProperty("weight").GetSingle(), index, mask,
                            reset, input.GetProperty("inertial").GetBoolean(), markerRecord: markerStorage[slot]);
                        var tick = sourceTick.Preparation;
                        preparedPlayer = sourceTick.Player;
                        time = tick.Time; rate = tick.PlayRate;
                        if (!tick.Advances && input.GetProperty("explicitTime").GetSingle() == sequence.DurationSeconds) fullTurns++;
                        Equal(time, NativeFloat(input, "prepared"), location + "/prepared");
                    }
                    Equal(rate, NativeFloat(input, "preparedRate"), location + "/rate");
                    Require(input.GetProperty("isEvaluator").GetBoolean() == (slot < 2), "Native evaluator flag differs.");
                    players[index] = preparedPlayer ?? new(slot, asset, epochs[slot], AlsAssetSyncKind.Sequence, time, rate,
                        input.GetProperty("weight").GetSingle(), index, 1, mask, Looping: input.GetProperty("looping").GetBoolean(),
                        RequestedInertialization: input.GetProperty("inertial").GetBoolean(),
                        Role: method == 2 ? AlsAssetSyncRole.CanBeLeader : (AlsAssetSyncRole)input.GetProperty("role").GetInt32(), IsEvaluator: slot < 2,
                        MarkerRecord: markerStorage[slot]);
                    samples[index] = new(slot, asset, 1); playerGroups[index] = method == 0 ? -1 : 0;
                }
                // Keep one named group, including empty frames, like the
                // native double-buffer map. All input histories are committed
                // C# results; native rows never feed subsequent clocks.
                var output = new AlsAssetPlayerHistory[players.Length]; var sampleOutput = new AlsAssetSampleHistory[samples.Length];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0], playerGroups, players, samples, sequenceArray, markerArray,
                    previousGroups, previousPlayers, previousSamples, delta, groups, output, sampleOutput, out var failure),
                    $"{label}/{frameIndex}: Sync rejected {failure}");
                var group = groups[0].Group; var leader = frame.GetProperty("leader").GetInt32();
                Require(group.HasLeader == (leader >= 0) && (!group.HasLeader || group.LeaderPlayerId == leader), $"{label}/{frameIndex}: leader={group.LeaderPlayerId} native={leader}");
                if (group.HasLeader)
                {
                    Equal(group.LeaderScore, NativeFloat(frame, "leaderScore"), label + "/score");
                    Require(group.SortedLeaderIndex == frame.GetProperty("leaderIndex").GetInt32(), "Native fallback leader index differs.");
                    Equal(group.PreviousRatio, NativeFloat(frame, "previousRatio"), label + "/previousRatio");
                    Equal(group.Ratio, NativeFloat(frame, "ratio"), label + "/ratio");
                    if (group.LeaderScore == 2) forced++;
                }
                foreach (var (value, field) in new[] { (group.MarkerStart, "markerStart"), (group.MarkerEnd, "markerEnd") })
                {
                    var position = frame.GetProperty(field);
                    Require(value.PreviousSymbol == Symbol(position.GetProperty("previous").GetString()!) &&
                        value.NextSymbol == Symbol(position.GetProperty("next").GetString()!), label + "/" + field + "/symbols");
                    if (value.PreviousSymbol != 0 || value.NextSymbol != 0)
                        Equal(value.Alpha, NativeFloat(position, "alpha"), label + "/" + field + "/alpha");
                }
                foreach (var expected in frame.GetProperty("outputs").EnumerateArray())
                {
                    var slot = expected.GetProperty("slot").GetInt32(); var actual = output.Single(p => p.PlayerId == slot);
                    var location = $"{label}/{frameIndex}/{slot}";
                    Equal(actual.Time, NativeFloat(expected, "time"), location + "/time");
                    Equal(actual.DeltaPrevious, NativeFloat(expected, "previous"), location + "/previous");
                    Equal(actual.Delta, NativeFloat(expected, "delta"), location + "/delta");
                    var marker = expected.GetProperty("marker");
                    Require(actual.Marker.PreviousIndex == marker.GetProperty("previous").GetInt32() && actual.Marker.NextIndex == marker.GetProperty("next").GetInt32(), location + "/marker index");
                    if (actual.Marker.Initialized)
                    {
                        Equal(actual.Marker.PreviousDistance, NativeFloat(marker, "previousDistance"), location + "/marker previous");
                        Equal(actual.Marker.NextDistance, NativeFloat(marker, "nextDistance"), location + "/marker next");
                    }
                    clocks[slot] = actual.Time; markerStorage[slot] = actual.Marker; ticks++;
                }
                // Retry from the same committed inputs must match every field.
                var retry = new AlsAssetPlayerHistory[players.Length]; var retrySamples = new AlsAssetSampleHistory[samples.Length];
                var retryGroups = new AlsAssetSyncBatchGroupHistory[1];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0], playerGroups, players, samples, sequenceArray, markerArray,
                    previousGroups, previousPlayers, previousSamples, delta, retryGroups, retry, retrySamples, out _) &&
                    output.SequenceEqual(retry) && sampleOutput.SequenceEqual(retrySamples) && groups.SequenceEqual(retryGroups), "Sync retry changed candidate.");
                previousGroups = groups.ToArray(); previousPlayers = output; previousSamples = sampleOutput;
                if (players.Length == 0) empty++; frameIndex++; frames++;
            }
            traces++;
        }
        Require(traces == 27 && frames == 4725 && ticks > 13000 && empty > 0 && forced > 0 && fullTurns > 0, "Incomplete evaluator coverage.");
        GD.Print($"LYRA_EVALUATOR_SYNC_OK nonloop={nonloop} traces={traces} frames={frames} ticks={ticks} empty={empty} forced={forced} " +
            $"exact=1 retry=1 scope=standaloneUpdateAndSharedSync");
    }
}
