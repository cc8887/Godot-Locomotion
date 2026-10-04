using System.Globalization;
using System.Text.Json;
using Godot;
using GodotAls.Core.Sync;
using FileAccess = Godot.FileAccess;

namespace GodotAls.Animation.Lyra;

public partial class LyraCycleSourceSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Cycle source failed: " + error); GetTree().Quit(1); }
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static float Float(JsonElement row, string name) =>
        BitConverter.Int32BitsToSingle(unchecked((int)row.GetProperty(name + "Bits").GetUInt32()));
    private static double Double(JsonElement row, string name) => BitConverter.Int64BitsToDouble(
        unchecked((long)ulong.Parse(row.GetProperty(name + "Bits").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
    private static void Equal(float value, JsonElement row, string name, string label) =>
        Require(BitConverter.SingleToInt32Bits(value) == BitConverter.SingleToInt32Bits(Float(row, name)),
            $"{label}/{name}: actual={value:R} native={Float(row, name):R}");
    private static void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        using var native = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "cycle_source_native_bits.json"));
        using var requests = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "cycle_source_requests.json"));
        using var definitions = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "cycle_source_definitions.json"));
        using var catalog = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "logical_controls/catalog.json"));
        var data = native.RootElement; var staticData = definitions.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32() == 1 && staticData.GetProperty("schemaVersion").GetInt32() == 1,
            "Invalid Cycle schemas.");
        var requestSha = LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + "cycle_source_requests.json"));
        Require(data.GetProperty("requestSha256").GetString() == requestSha &&
            staticData.GetProperty("requestSha256").GetString() == requestSha, "Stale Cycle inputs.");
        foreach (var dependency in data.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + dependency.Name)),
                "Changed Cycle dependency: " + dependency.Name);
        var paths = data.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("path").GetString()!).ToArray();
        var targets = catalog.RootElement.GetProperty("entries").EnumerateArray().GroupBy(e => e.GetProperty("source").GetString()!)
            .ToDictionary(g => g.Key, g => g.First().GetProperty("target").GetString()!);
        var symbols = data.GetProperty("assets").EnumerateArray().SelectMany(a => a.GetProperty("markers").EnumerateArray())
            .Select(m => m.GetProperty("name").GetString()!).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var sequenceList = new List<AlsAssetSyncSequence>(); var markerList = new List<AlsAssetSyncMarker>();
        var masks = new ulong[paths.Length];
        foreach (var row in data.GetProperty("assets").EnumerateArray())
        {
            var markers = row.GetProperty("markers").EnumerateArray().ToArray(); var id = sequenceList.Count;
            sequenceList.Add(new(id, row.GetProperty("length").GetSingle(), row.GetProperty("rateScale").GetSingle(), markerList.Count, markers.Length));
            foreach (var marker in markers)
            {
                var symbol = Array.IndexOf(symbols, marker.GetProperty("name").GetString()!) + 1;
                masks[id] |= 1UL << symbol; markerList.Add(new(symbol, marker.GetProperty("time").GetSingle()));
            }
        }
        var sequences = sequenceList.ToArray(); var allMarkers = markerList.ToArray();
        var nodes = LyraSourceNodeCatalog.Load(); var inventory = LyraLinkedLayerInventory.Load();
        var count = 0; var changes = 0; var clamps = 0; var rejected = 0;
        void Reject(LyraCycleSourceRuntime runtime, LyraCycleCandidate candidate, AlsAssetPlayerHistory output)
        {
            var oldState = runtime.State;
            try { runtime.Commit(candidate, output); throw new InvalidOperationException("Bad Cycle commit accepted."); }
            catch (InvalidOperationException error) when (error.Message.StartsWith("Rejected stale", StringComparison.Ordinal)) { rejected++; }
            Require(runtime.State == oldState, "Rejected commit published state.");
        }
        foreach (var (trace, index) in data.GetProperty("traces").EnumerateArray().Select((trace, index) => (trace, index)))
        {
            var inputTrace = requests.RootElement.GetProperty("traces")[index];
            var profileName = trace.GetProperty("profile").GetString()!; var profile = inventory.Get(profileName);
            var node = nodes.ForClass(profile.ClassPath).Nodes.Values.Single(n => n.Functions.Update == "UpdateCycleAnim");
            var clamp = staticData.GetProperty("clamps").GetProperty(profileName);
            LyraCycleAsset Resolve(string group, LyraCardinalDirection direction)
            {
                var path = targets[profile.Cardinal(group, direction)!]; var id = Array.IndexOf(paths, path);
                var definition = staticData.GetProperty("assets").GetProperty(path);
                return new(id, Float(definition, "length"), Float(definition, "rootDistance"));
            }
            var runtime = new LyraCycleSourceRuntime(node, node.Index, 1, Resolve, Double(clamp, "clampMin"), Double(clamp, "clampMax"));
            var previousGroup = default(AlsAssetSyncGroupHistory);
            var previousPlayers = Array.Empty<AlsAssetPlayerHistory>(); var previousSamples = Array.Empty<AlsAssetSampleHistory>();
            for (var frameIndex = 0; frameIndex < trace.GetProperty("frames").GetArrayLength(); frameIndex++)
            {
                var row = trace.GetProperty("frames")[frameIndex]; var frame = inputTrace.GetProperty("frames")[frameIndex];
                var main = frame.GetProperty("main"); var label = $"{profileName}/{trace.GetProperty("hz")}/{frameIndex}";
                var direction = main.GetProperty("LocalVelocityDirectionNoOffset").GetInt32() switch
                { 0 => LyraCardinalDirection.Forward, 1 => LyraCardinalDirection.Backward,
                    2 => LyraCardinalDirection.Left, 3 => LyraCardinalDirection.Right, _ => throw new InvalidOperationException("Direction") };
                var input = new LyraCycleInput(main.GetProperty("IsCrouching").GetBoolean(), main.GetProperty("GameplayTag_IsADS").GetBoolean(),
                    direction, main.GetProperty("DisplacementSpeed").GetSingle(), main.GetProperty("IsRunningIntoWall").GetBoolean());
                var delta = frame.GetProperty("delta").GetSingle(); var reset = frame.GetProperty("reinitialize").GetBoolean();
                var groupName = input.Crouching ? "Crouch_Walk_Cardinals" : input.Ads ? "Walk_Cardinals" : "Jog_Cardinals";
                var selected = Resolve(groupName, direction); var oldState = runtime.State;
                var cancelled = runtime.Prepare(input, delta, frame.GetProperty("weight").GetSingle(), 0, masks[selected.Id], reset);
                runtime.Cancel(); Require(runtime.State == oldState, "Cancel published source state.");
                var candidate = runtime.Prepare(input, delta, frame.GetProperty("weight").GetSingle(), 0, masks[selected.Id], reset);
                Equal(selected.Length, row, "length", label); Equal(selected.RootDistance, row, "rootDistance", label);
                Require(BitConverter.DoubleToInt64Bits(Double(clamp, "clampMin")) == BitConverter.DoubleToInt64Bits(Double(row, "clampMin")) &&
                    BitConverter.DoubleToInt64Bits(Double(clamp, "clampMax")) == BitConverter.DoubleToInt64Bits(Double(row, "clampMax")), label + "/static clamp");
                Require(candidate.State == cancelled.State && candidate.Player == cancelled.Player && candidate.InertiaDuration == cancelled.InertiaDuration,
                    "Retry changed source candidate.");
                Equal(candidate.Before, row, "before", label);
                Require((candidate.BeforeAsset < 0 ? "" : paths[candidate.BeforeAsset]) == row.GetProperty("beforeAsset").GetString(), label + "/before asset");
                Require(paths[candidate.State.AssetId] == row.GetProperty("asset").GetString(), label + "/selected asset");
                Equal(candidate.State.Time, row, "prepared", label); Equal(candidate.State.PlayRate, row, "playRate", label);
                Require(row.GetProperty("strideType").GetString() == "double" &&
                    BitConverter.DoubleToInt64Bits(candidate.State.StrideAlpha) == BitConverter.DoubleToInt64Bits(Double(row, "strideAlpha")),
                    $"{label}/stride: actual={candidate.State.StrideAlpha:R} native={Double(row, "strideAlpha"):R}");
                var inertia = row.GetProperty("inertia");
                Require(inertia.GetArrayLength() == (candidate.InertiaDuration > 0 ? 1 : 0), label + "/inertia count");
                if (candidate.InertiaDuration > 0) { Equal(candidate.InertiaDuration, inertia[0], "duration", label); changes++; }
                if (candidate.Before > candidate.State.Time) clamps++;
                var outputs = new AlsAssetPlayerHistory[1]; var samplesOut = new AlsAssetSampleHistory[1];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, previousGroup, [candidate.Player], [new(0, selected.Id, 1)], sequences,
                    allMarkers, previousPlayers, previousSamples, delta, outputs, samplesOut, out var group, out var failure), label + "/Sync: " + failure);
                var synced = outputs[0];
                Equal(synced.Time, row, "time", label); Equal(synced.DeltaPrevious, row, "previous", label); Equal(synced.Delta, row, "delta", label);
                Require(synced.Marker.PreviousIndex == row.GetProperty("markerPrevious").GetInt32() &&
                    synced.Marker.NextIndex == row.GetProperty("markerNext").GetInt32(), label + "/marker indices");
                Equal(synced.Marker.PreviousDistance, row, "markerPreviousDistance", label);
                Equal(synced.Marker.NextDistance, row, "markerNextDistance", label);
                Reject(runtime, cancelled, synced);
                if (frameIndex == 0)
                {
                    Reject(runtime, candidate, synced with { Epoch = 2 });
                    Reject(runtime, candidate, synced with { Time = candidate.Length * 2 });
                    Reject(runtime, candidate, synced with { Delta = float.NaN });
                }
                Require(runtime.State == oldState, "Rejected commit published state.");
                runtime.Commit(candidate, synced);
                Reject(runtime, candidate, synced);
                previousGroup = group; previousPlayers = outputs; previousSamples = samplesOut; count++;
            }
        }
        Require(count == 3780 && changes == 108 && clamps == 7 && rejected == count * 2 + 27, "Missing Cycle coverage.");
        GD.Print($"LYRA_CYCLE_SOURCE_GODOT_OK traces=9 frames={count} assets={paths.Length} changes={changes} length_clamps={clamps} stale_rejected={rejected} exact_bits=true");
    }
}
