using System.Text.Json;
using Godot;
using GodotAls.Core.Sync;
using FileAccess = Godot.FileAccess;

namespace GodotAls.Animation.Lyra;

public partial class LyraStartSourceSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Start source failed: " + error); GetTree().Quit(1); }
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal(float value, JsonElement row, string name, string label) =>
        Require(BitConverter.SingleToInt32Bits(value) == BitConverter.SingleToInt32Bits(LyraStartDistanceBank.Float(row, name)),
            $"{label}/{name}: actual={value:R} native={LyraStartDistanceBank.Float(row, name):R}");
    private static void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        using var native = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "start_source_native_bits.json"));
        using var requests = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "start_source_requests.json"));
        using var definitions = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "start_source_definitions.json"));
        using var catalog = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "logical_controls/catalog.json"));
        var data = native.RootElement; var staticData = definitions.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32() == 1, "Invalid Start schema.");
        var requestSha = LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + "start_source_requests.json"));
        Require(data.GetProperty("requestSha256").GetString() == requestSha &&
            staticData.GetProperty("requestSha256").GetString() == requestSha, "Stale Start inputs.");
        foreach (var dependency in data.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + dependency.Name)),
                "Changed Start dependency: " + dependency.Name);
        var bank = new LyraStartDistanceBank(staticData);
        var targets = catalog.RootElement.GetProperty("entries").EnumerateArray().GroupBy(e => e.GetProperty("source").GetString()!)
            .ToDictionary(g => g.Key, g => g.First().GetProperty("target").GetString()!);
        var nodes = LyraSourceNodeCatalog.Load(); var inventory = LyraLinkedLayerInventory.Load();
        var count = 0; var activeCount = 0; var relevantCount = 0; var hiddenCount = 0;
        var retained = 0; var changed = 0; var diverged = 0; var rejected = 0;
        var selectedAssets = new HashSet<int>();
        void Reject(LyraStartSourceRuntime runtime, LyraStartCandidate candidate, AlsAssetPlayerHistory output)
        {
            var old = runtime.State;
            try { runtime.Commit(candidate, output); throw new InvalidOperationException("Bad Start commit accepted."); }
            catch (InvalidOperationException error) when (error.Message.StartsWith("Rejected stale", StringComparison.Ordinal)) { rejected++; }
            Require(old == runtime.State, "Rejected Start commit published history.");
        }
        foreach (var (trace, index) in data.GetProperty("traces").EnumerateArray().Select((trace, index) => (trace, index)))
        {
            var inputTrace = requests.RootElement.GetProperty("traces")[index];
            var profileName = trace.GetProperty("profile").GetString()!; var profile = inventory.Get(profileName);
            var node = nodes.ForClass(profile.ClassPath).Nodes.Values.Single(n => n.Functions.Update == "UpdateStartAnim");
            LyraStartAsset Resolve(string group, LyraCardinalDirection direction) => bank.Asset(targets[profile.Cardinal(group, direction)!]);
            var runtime = new LyraStartSourceRuntime(node, node.Index, 1, Resolve, bank.Asset, bank.Policy(profileName));
            var previousGroup = default(AlsAssetSyncGroupHistory);
            var previousPlayers = Array.Empty<AlsAssetPlayerHistory>(); var previousSamples = Array.Empty<AlsAssetSampleHistory>();
            for (var frameIndex = 0; frameIndex < trace.GetProperty("frames").GetArrayLength(); frameIndex++)
            {
                var row = trace.GetProperty("frames")[frameIndex]; var frame = inputTrace.GetProperty("frames")[frameIndex];
                var main = frame.GetProperty("main"); var label = $"{profileName}/{trace.GetProperty("hz")}/{frameIndex}";
                var direction = main.GetProperty("LocalVelocityDirection").GetInt32() switch
                { 0 => LyraCardinalDirection.Forward, 1 => LyraCardinalDirection.Backward,
                    2 => LyraCardinalDirection.Left, 3 => LyraCardinalDirection.Right, _ => throw new InvalidOperationException("Direction") };
                var input = new LyraStartInput(main.GetProperty("IsCrouching").GetBoolean(), main.GetProperty("GameplayTag_IsADS").GetBoolean(),
                    direction, main.GetProperty("DisplacementSinceLastUpdate").GetDouble());
                var delta = frame.GetProperty("delta").GetSingle(); var reset = frame.GetProperty("reinitialize").GetBoolean();
                var active = frame.GetProperty("active").GetBoolean(); var weight = frame.GetProperty("weight").GetSingle();
                var oldState = runtime.State;
                var cancelled = runtime.Prepare(input, delta, weight, 0, reset, active);
                runtime.Cancel(); Require(runtime.State == oldState, "Start Cancel published state.");
                var candidate = runtime.Prepare(input, delta, weight, 0, reset, active);
                Require(candidate.State == cancelled.State && candidate.Tick == cancelled.Tick &&
                    candidate.BecameRelevant == cancelled.BecameRelevant, "Start retry changed computed callback.");
                Equal(candidate.Before, row, "before", label); Equal(candidate.ExplicitBefore, row, "explicitBefore", label);
                Require((candidate.BeforeAsset < 0 ? "" : bank.Paths[candidate.BeforeAsset]) == row.GetProperty("beforeAsset").GetString(), label + "/before asset");
                Require(bank.Paths[candidate.State.AssetId] == row.GetProperty("asset").GetString(), label + "/selected asset");
                Require(candidate.BecameRelevant == row.GetProperty("becameRelevant").GetBoolean(), label + "/setup relevance");
                Equal(candidate.State.Time, row, "prepared", label); Equal(candidate.State.ExplicitTime, row, "explicit", label);
                Equal(candidate.State.CachedWeight, row, "cachedWeight", label);
                Require(BitConverter.DoubleToInt64Bits(candidate.State.StrideAlpha) ==
                    BitConverter.DoubleToInt64Bits(LyraStartDistanceBank.Double(row, "StrideWarpingStartAlpha")),
                    $"{label}/stride: actual={candidate.State.StrideAlpha:R} native={LyraStartDistanceBank.Double(row, "StrideWarpingStartAlpha"):R}");
                var players = active ? new[] { candidate.Tick.Player } : Array.Empty<AlsAssetSyncPlayer>();
                var samples = active ? new[] { new AlsAssetSyncSample(0, candidate.State.AssetId, 1) } : Array.Empty<AlsAssetSyncSample>();
                var outputs = new AlsAssetPlayerHistory[players.Length]; var sampleOutputs = new AlsAssetSampleHistory[samples.Length];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, previousGroup, players, samples, bank.Sequences,
                    bank.Markers, previousPlayers, previousSamples, delta, outputs, sampleOutputs, out var groupResult, out var failure), label + "/Sync: " + failure);
                if (active)
                {
                    Equal(candidate.Tick.Player.PlayRate, row, "rate", label);
                    var synced = outputs[0]; Equal(synced.Time, row, "time", label);
                    Equal(synced.DeltaPrevious, row, "previous", label); Equal(synced.Delta, row, "delta", label);
                    Require(synced.Marker.PreviousIndex == row.GetProperty("markerPrevious").GetInt32() &&
                        synced.Marker.NextIndex == row.GetProperty("markerNext").GetInt32(), label + "/marker indices");
                    Equal(synced.Marker.PreviousIndex == -2 ? 0 : synced.Marker.PreviousDistance, row, "markerPreviousDistance", label);
                    Equal(synced.Marker.NextIndex == -2 ? 0 : synced.Marker.NextDistance, row, "markerNextDistance", label);
                    Reject(runtime, cancelled, synced); Reject(runtime, candidate, synced with { Epoch = 2 });
                    Reject(runtime, candidate, synced with { Delta = float.NaN });
                    Reject(runtime, candidate, synced with { Time = candidate.Length * 2 });
                    Reject(runtime, candidate, synced with { Marker = synced.Marker with { PreviousIndex = int.MaxValue } });
                    runtime.Commit(candidate, synced); Reject(runtime, candidate, synced);
                    selectedAssets.Add(candidate.State.AssetId); activeCount++;
                    relevantCount += candidate.BecameRelevant ? 1 : 0;
                    changed += candidate.BeforeAsset != candidate.State.AssetId ? 1 : 0;
                    var desired = Resolve(input.Crouching ? "Crouch_Start_Cardinals" : input.Ads ? "ADS_Start_Cardinals" : "Jog_Start_Cardinals", direction);
                    retained += desired.Id != candidate.State.AssetId ? 1 : 0;
                    diverged += runtime.State.ExplicitTime != runtime.State.Time ? 1 : 0;
                }
                else
                {
                    try { runtime.CommitInactive(cancelled); throw new InvalidOperationException("Stale hidden Start candidate accepted."); }
                    catch (InvalidOperationException error) when (error.Message.StartsWith("Rejected stale", StringComparison.Ordinal)) { rejected++; }
                    runtime.CommitInactive(candidate); hiddenCount++;
                    Equal(runtime.State.Time, row, "time", label); Equal(runtime.State.DeltaPrevious, row, "previous", label);
                    Equal(runtime.State.Delta, row, "delta", label);
                    Require(runtime.State.Marker.PreviousIndex == row.GetProperty("markerPrevious").GetInt32() &&
                        runtime.State.Marker.NextIndex == row.GetProperty("markerNext").GetInt32(), label + "/hidden markers");
                }
                previousGroup = groupResult; previousPlayers = outputs; previousSamples = sampleOutputs; count++;
            }
        }
        Require(count == 3780 && activeCount == 3672 && hiddenCount == 108 && selectedAssets.Count == 36 &&
            relevantCount > 108 && retained > 0 && diverged > 0 && rejected == activeCount * 6 + hiddenCount, "Missing Start boundary coverage.");
        GD.Print($"LYRA_START_SOURCE_GODOT_OK traces=9 frames={count} active={activeCount} hidden={hiddenCount} assets={selectedAssets.Count} setups={relevantCount} changes={changed} retained={retained} clock_diverged={diverged} stale_rejected={rejected} exact_bits=true");
    }
}
