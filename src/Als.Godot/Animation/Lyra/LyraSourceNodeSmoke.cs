using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraSourceNodeSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Lyra source inventory failed: " + error); GetTree().Quit(1); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static byte[] Bytes(JsonNode value) => Encoding.UTF8.GetBytes(value.ToJsonString());
    private void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        var sourceBytes = Godot.FileAccess.GetFileAsBytes(root + "source_nodes.json");
        var inventoryBytes = Godot.FileAccess.GetFileAsBytes(root + "linked_layer_inventory.json");
        var runtimeBytes = Godot.FileAccess.GetFileAsBytes(root + "runtime_graph.json");
        var logicalBytes = Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "catalog.json");
        var catalog = LyraSourceNodeCatalog.Parse(sourceBytes, inventoryBytes, runtimeBytes, logicalBytes);
        var providers = 0;
        foreach (var owner in catalog.Classes)
        {
            if (owner.ClassPath == LyraRuntimeGraphCatalog.MainClass) continue;
            providers++;
            Require(owner.Nodes.Count == 28 && owner.LayerInventory.Count == 14, "Incomplete original provider inventory.");
            var start = owner.Nodes.Values.Single(n => n.Functions.Update == "UpdateStartAnim");
            var stop = owner.Nodes.Values.Single(n => n.Functions.Update == "UpdateStopAnim");
            var cycle = owner.Nodes.Values.Single(n => n.Functions.Update == "UpdateCycleAnim");
            var pivots = owner.Nodes.Values.Where(n => n.Functions.Update == "UpdatePivotAnim").ToArray();
            var land = owner.Nodes.Values.Single(n => n.Functions.Update == "UpdateFallLandAnim");
            Require(start.Kind == LyraSourceKind.SequenceEvaluator && start.Group == "Locomotion" &&
                start.Role == LyraSourceGroupRole.CanBeLeader && start.Settings.GetProperty("teleport").GetBoolean(), "Start source policy changed.");
            Require(stop.Kind == LyraSourceKind.SequenceEvaluator && stop.Group == "Stop" &&
                stop.Role == LyraSourceGroupRole.CanBeLeader && stop.Settings.GetProperty("teleport").GetBoolean(), "Stop source policy changed.");
            Require(cycle.Kind == LyraSourceKind.SequencePlayer && cycle.Group == "Locomotion" && cycle.Looping &&
                cycle.Role == LyraSourceGroupRole.AlwaysFollower, "Cycle source policy changed.");
            Require(pivots.Length == 2 && pivots.Append(land).All(n => n.Kind == LyraSourceKind.SequenceEvaluator &&
                n.Group == "Locomotion" && n.Role == LyraSourceGroupRole.AlwaysLeader &&
                !n.Settings.GetProperty("teleport").GetBoolean()), "Pivot/FallLand leader policy changed.");
            Require(new[] { start, stop, cycle, land }.Concat(pivots).All(n =>
                n.Method == LyraSourceSyncMethod.SyncGroup && !n.IgnoreRelevancy), "Source sync/relevancy changed.");
            Require(owner.LayerInventory["FullBody_PivotState"].Count == 3 &&
                pivots.All(n => owner.LayerInventory["FullBody_PivotState"].Contains(n.Index)) &&
                owner.LayerInventory["FullBody_IdleState"].Count == 5, "Nested sources were dropped from their layer owner.");
        }
        var main = catalog.ForClass(LyraRuntimeGraphCatalog.MainClass);
        Require(providers == 9 && main.Nodes.Count == 3 && main.LayerInventory["AnimGraph"].Count == 3 &&
            main.Nodes.Values.All(n => n.Kind == LyraSourceKind.BlendSpacePlayer && n.IgnoreRelevancy &&
                n.Method == LyraSourceSyncMethod.DoNotSync && n.Settings.GetProperty("samples").GetArrayLength() == 3),
            "Main Lean source inventory changed.");
        using var logical = JsonDocument.Parse(logicalBytes);
        var assets = logical.RootElement.GetProperty("entries").EnumerateArray().Select(v => v.GetProperty("source").GetString()!).ToHashSet();
        var missing = main.Nodes.Values.SelectMany(n => n.Settings.GetProperty("samples").EnumerateArray()
            .Select(s => s.GetProperty("animation").GetString()!)).Distinct().Where(p => !assets.Contains(p)).ToArray();
        Require(missing.Length == 3, "Reassess main Lean resource closure after resource changes.");
        var rejected = 0;
        void Reject(Action<JsonNode, JsonNode> mutation, bool updateRuntimeHash = false)
        {
            var source = JsonNode.Parse(sourceBytes)!; var runtime = JsonNode.Parse(runtimeBytes)!;
            mutation(source, runtime);
            var modifiedRuntime = Bytes(runtime);
            if (updateRuntimeHash) source["runtimeGraphSha256"] = Convert.ToHexString(SHA256.HashData(modifiedRuntime)).ToLowerInvariant();
            try { _ = LyraSourceNodeCatalog.Parse(Bytes(source), inventoryBytes,
                updateRuntimeHash ? modifiedRuntime : runtimeBytes, logicalBytes); }
            catch (InvalidOperationException) { rejected++; return; }
            catch (NotSupportedException) { rejected++; return; }
            throw new InvalidOperationException("Bad source contract was accepted.");
        }
        Reject((s, _) => s["runtimeGraphSha256"] = new string('0', 64));
        Reject((s, _) => s["classes"]!["unarmed"]!["sources"]![0]!["role"] = 999);
        Reject((s, _) => s["classes"]!["unarmed"]!["sources"]![0]!["nativeNodeIndex"] = 999);
        Reject((s, _) => s["classes"]!["unarmed"]!["graphs"]![0]!["players"]!.AsArray().Add(65535));
        Reject((_, r) => r["classes"]!["unarmed"]!["machines"]![3]!["states"]![0]!["playerNodeIndices"]!.AsArray().Clear(), true);
        Reject((s, _) => s["classes"]!["unarmed"]!["sources"]!.AsArray().Add(
            s["classes"]!["unarmed"]!["sources"]![0]!.DeepClone()));
        GD.Print($"LYRA_SOURCE_NODE_OK classes={providers + 1} sources={catalog.Classes.Sum(c => c.Nodes.Count)} " +
            $"nestedOwners=9 missingLean={missing.Length} rejected={rejected} scope=configurationAndOwnership");
    }
}
