using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraLocomotionMachineSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static LyraLocomotionRuleInputs Empty => new(false, false, false, false, false, false, false,
        false, false, true, AlsDoubleVector.Zero, AlsDoubleVector.Zero, 0, 0, 0, 0, 0, 0, 0, 1000, 0);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static LyraLocomotionRuleInputs ReadNativeInput(JsonElement row)
    {
        var f = row.GetProperty("fields");
        bool B(string key) => f.GetProperty(key).GetBoolean();
        double D(string key) => f.GetProperty(key).GetDouble();
        int I(string key) => f.GetProperty(key).GetInt32();
        AlsDoubleVector V(string key) { var v = f.GetProperty(key); return new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble()); }
        return new(B("HasAcceleration"), B("HasVelocity"), B("GameplayTag_IsMelee"),
            B("IsRunningIntoWall"), B("LinkedLayerChanged"), B("CrouchStateChange"), B("ADSStateChanged"), B("IsJumping"),
            B("IsFalling"), B("IsOnGround"), V("LocalVelocity2D"), V("LocalAcceleration2D"), I("StartDirection"),
            I("LocalVelocityDirection"), I("PivotInitialDirection"), D("DisplacementSpeed"), D("RootYawOffset"),
            D("LastPivotTime"), D("TimeToJumpApex"), D("GroundDistance"), row.GetProperty("elapsed").GetSingle());
    }
    private void Run()
    {
        var catalog = LyraRuntimeGraphCatalog.Load(); var machine = new LyraLocomotionMachine(catalog);
        var ruleBytes = Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/locomotion_rules_native.json");
        using var native = JsonDocument.Parse(ruleBytes);
        var root = native.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("runtimeGraphSha256").GetString() == catalog.Sha256 &&
            root.GetProperty("mainAssetSha256").GetString() == catalog.MainAssetSha256, "Stale compiled rule oracle.");
        var rules = 0; var cases = 0; var truth = new Dictionary<int, HashSet<bool>>();
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var input = ReadNativeInput(row);
            foreach (var rule in row.GetProperty("rules").EnumerateArray())
            {
                var edge = rule.GetProperty("edge").GetInt32(); var expected = rule.GetProperty("result").GetBoolean();
                Require(LyraLocomotionMachine.Predicate(edge, input) == expected, $"Native predicate differs case={cases} edge={edge}.");
                if (!truth.TryGetValue(edge, out var values)) truth.Add(edge, values = []);
                values.Add(expected); rules++;
            }
            cases++;
        }
        Require(cases == 576 && rules == 1536 && truth.Count == 32, "Incomplete native rule coverage.");
        Require(truth.Where(p => p.Key is not (1 or 6 or 11 or 14 or 18 or 19 or 31)).All(p => p.Value.Count == 2), "Missing nonconstant predicate outcome.");

        using var selection = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/locomotion_selection_native.json"));
        var selectionRoot = selection.RootElement;
        Require(selectionRoot.GetProperty("schemaVersion").GetInt32() == 1 && selectionRoot.GetProperty("runtimeGraphSha256").GetString() == catalog.Sha256 &&
            selectionRoot.GetProperty("ruleProbeSha256").GetString() == LyraLogicalSourceBank.Sha(ruleBytes), "Stale native selection oracle.");
        var selections = 0; var paths = 0;
        foreach (var row in selectionRoot.GetProperty("rows").EnumerateArray())
        {
            var input = ReadNativeInput(row); var expected = row.GetProperty("selection");
            var actual = machine.Select(row.GetProperty("state").GetInt32(), input);
            Require((actual is not null) == expected.GetProperty("found").GetBoolean(), $"Native selection availability differs case={selections}.");
            if (actual is not null)
            {
                Require(actual.Edge == expected.GetProperty("edge").GetInt32() && actual.Next == expected.GetProperty("next").GetInt32() &&
                    actual.CrossfadeAdjustment == expected.GetProperty("crossfadeAdjustment").GetSingle() &&
                    actual.ConduitPath.SequenceEqual(expected.GetProperty("path").EnumerateArray().Select(p => p.GetInt32())),
                    $"Native selected edge/path differs case={selections}.");
                if (actual.ConduitPath.Count > 1) paths++;
            }
            selections++;
        }
        Require(selections == 480 && paths > 0, "Missing native conduit selection coverage.");

        var graphBytes = Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/runtime_graph.json");
        var inventoryBytes = Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/linked_layer_inventory.json");
        var rejected = 0;
        foreach (var mutation in new Action<JsonNode>[] {
            n => n["inventorySha256"] = "bad",
            n => n["classes"]!["main"]!["nodes"]![1]!["settings"]!["maxTransitionsPerFrame"] = 2,
            n => n["classes"]!["main"]!["nodes"]![0]!["settings"]!["filteredBones"] = new JsonArray("root"),
            n => n["classes"]!["main"]!["machines"]![0]!["states"]![1]!["stateName"] = "Idle",
            n => n["classes"]!["main"]!["machines"]![0]!["transitions"]![0]!["nextState"] = 2,
            n => n["classes"]!["main"]!["machines"]![0]!["transitions"]![3]!["logicType"] = "TLT_StandardBlend",
        })
        {
            var clone = JsonNode.Parse(graphBytes)!; mutation(clone);
            try { LyraRuntimeGraphCatalog.Parse(Encoding.UTF8.GetBytes(clone.ToJsonString()), inventoryBytes); }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException) { rejected++; }
        }
        Require(rejected == 6, "Changed compiled contract accepted.");

        var boundaries = 0;
        void Check(int state, LyraLocomotionRuleInputs input, int edge, int target, bool inertial = false)
        {
            var t = machine.Select(state, input); Require(t is not null && t.Edge == edge && t.Next == target && t.Inertial == inertial,
                $"Baked transition selection differs state={state} expected={edge} actual={t?.Edge}."); boundaries++;
        }
        var move = Empty with { HasAcceleration = true, HasVelocity = true, LocalVelocity = new(100, 0, 0), LocalAcceleration = new(100, 0, 0) };
        Check(0, Empty with { IsMelee = true, HasVelocity = true }, 0, 1);
        Check(0, move with { IsJumping = true }, 0, 1);
        Check(0, Empty with { IsJumping = true, IsOnGround = false }, 23, 6);
        Check(0, Empty with { IsJumping = true, IsFalling = true, IsOnGround = false }, 22, 7);
        Check(1, move with { LinkedLayerChanged = true, LocalAcceleration = new(-100, 0, 0) }, 2, 4);
        Check(1, move with { LinkedLayerChanged = true }, 3, 2, true);
        Check(1, move with { AdsChanged = true }, 8, 2);
        Check(1, move with { RootYawOffset = 60.001, LocomotionSyncValid = true }, 5, 2);
        Require(machine.Select(1, move with { RootYawOffset = 60.001 }) is null, "Invalid Sync group accepted."); boundaries++;
        Require(machine.Select(1, move with { RootYawOffset = 60, LocomotionSyncValid = true }) is null, "RootYaw strict threshold changed."); boundaries++;
        Check(1, move with { RelevantSourceValid = true, SourceLength = 2, SourceTime = 1.125f }, 7, 2);
        var auto = machine.Select(1, move with { RelevantSourceValid = true, SourceLength = 2, SourceTime = 1.125f })!;
        Require(auto.CrossfadeAdjustment == .125f && auto.StandardBlendDuration == .875f, "Automatic boundary adjustment differs."); boundaries++;
        Check(2, Empty with { IsFalling = true, IsOnGround = false }, 10, 3);
        Check(3, Empty with { LinkedLayerChanged = true, HasAcceleration = true }, 12, 0, true);
        Check(3, Empty with { AdsChanged = true }, 15, 0, true);
        Check(4, Empty with { PivotNotify = true }, 19, 2);
        Check(4, move with { VelocityDirection = 2, PivotInitialDirection = 0 }, 21, 2);
        Check(6, Empty with { RelevantSourceValid = true, SourceLength = 1, SourceTime = 1 }, 24, 10);
        Check(7, Empty, 31, 0);
        Check(8, move, 30, 2);
        Check(10, Empty with { IsOnGround = false, TimeToJumpApex = .399999 }, 32, 7);
        Check(11, Empty with { IsOnGround = false, GroundDistance = 199.999 }, 34, 8);
        var through = machine.Select(0, Empty with { IsJumping = true, IsOnGround = false })!;
        Require(through.ConduitPath.SequenceEqual(new[] { 23, 1 }) && through.Previous == 0 && through.Duration == .1f,
            "Conduit uses the wrong terminal transition."); boundaries++;
        var first = machine.Update(move, 1f / 60)!;
        Require(first.DiscardFirstBlend && machine.State == 1 && first.StandardBlendDuration == 0, "First update skipped state change instead of blend."); boundaries++;
        var candidate = new LyraLocomotionMachine(catalog); candidate.CopyFrom(machine);
        var retry = candidate.Update(move with { LinkedLayerChanged = true }, 1f / 60)!;
        Require(machine.State == 1 && machine.TransitionCount == 1, "Candidate mutated committed state.");
        candidate.CopyFrom(machine); var retried = candidate.Update(move with { LinkedLayerChanged = true }, 1f / 60)!;
        Require(retry.Edge == retried.Edge && retry.Next == retried.Next && retry.Duration == retried.Duration &&
            retry.Inertial == retried.Inertial && retry.ConduitPath.SequenceEqual(retried.ConduitPath) &&
            candidate.State == 2 && candidate.TransitionCount == 2, "Cancelled retry differs."); boundaries++;

        var frames = 0; var covered = new HashSet<int>();
        foreach (var hz in new[] { 30, 60, 120 })
        {
            machine.Reset(); var sourceTime = 0f; var sourceState = 0; var localCoverage = new HashSet<int>();
            for (var frame = 0; frame < hz * 9; frame++)
            {
                var seconds = (double)frame / hz;
                var input = Empty with { RelevantSourceValid = true, SourceLength = .6f, SourceTime = sourceTime,
                    LocomotionSyncValid = true };
                if (seconds is >= .8 and < 2 or >= 4 and < 5.8)
                    input = input with { HasAcceleration = true, HasVelocity = true, LocalVelocity = new(100, 0, 0),
                        LocalAcceleration = new(seconds is >= 4 and < 5 ? -100 : 100, 0, 0), DisplacementSpeed = 100 };
                if (seconds is >= 5 and < 5.8) input = input with { PivotNotify = true };
                if (seconds is >= 6 and < 7.3) input = input with { IsOnGround = false, IsJumping = seconds < 6.5,
                    IsFalling = seconds >= 6.5, TimeToJumpApex = seconds < 6.5 ? 1 : .2,
                    GroundDistance = seconds >= 7.1 ? 100 : 1000 };
                if (seconds is >= 6 and < 6.5) input = input with { SourceLength = .2f };
                machine.Update(input, 1f / hz); covered.Add(machine.State); localCoverage.Add(machine.State);
                if (machine.State != sourceState) { sourceState = machine.State; sourceTime = 0; }
                sourceTime += 1f / hz; frames++;
            }
            Require(localCoverage.Count == 10, "Controlled trace missed a state at " + hz + "Hz.");
        }
        Require(covered.Count == 10 && !covered.Contains(5) && !covered.Contains(9), "Controlled trace missed a real state.");
        GD.Print($"LYRA_LOCOMOTION_MACHINE_OK nativeCases={cases} nativeRules={rules} nativeSelections={selections} nativeConduits={paths} states=12 edges=36 boundaries={boundaries} " +
            $"hz=30,60,120 frames={frames} realStates={covered.Count} rejected={rejected} retry=True scope=rulesAndSelection");
    }
}
