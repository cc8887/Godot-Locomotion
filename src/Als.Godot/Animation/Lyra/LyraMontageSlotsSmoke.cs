using System.Text.Json;
using Godot;
using FileAccess = Godot.FileAccess;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMontageSlotsSmoke : Node
{
    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException(label); }
    private static void Exact(float a, float b, string label) => Require(BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b), label + $" {a:R}/{b:R}");
    public override void _Ready() { try { Run(); GetTree().Quit(); } catch (Exception e) { GD.PushError("Lyra Montage slots failed: " + e); GetTree().Quit(1); } }
    private static void Run()
    {
        const string root = "res://assets/generated/lyra_als/";
        var prefix = OS.GetCmdlineUserArgs().Contains("--montage-overlap-v1") ? "montage_slots_v1" : "montage_slots_v2";
        using var requests = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + prefix + "_requests.json"));
        using var native = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + prefix + "_native.json"));
        var data = native.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32() == 1 &&
            data.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + prefix + "_requests.json")), "Stale native Montage request.");
        foreach (var d in data.GetProperty("dependencies").EnumerateObject())
            Require(d.Value.GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + d.Name)), "Stale native Montage dependency.");
        var catalog = new LyraMontageCatalog();
        Require(catalog.Paths.SequenceEqual(requests.RootElement.GetProperty("montagePaths").EnumerateArray().Select(v => v.GetString()!)), "Changed native asset indices.");
        var frames = 0; var updates = 0; var retry = 0; var rejected = 0; var multi = 0; var instanceChecks = 0; var hidden = 0; var invalidCalls = 0;
        void Reject(Action action, string label)
        { try { action(); } catch (InvalidOperationException) { invalidCalls++; return; } throw new Exception("Accepted invalid Slot call: " + label); }
        foreach (var (trace, ti) in data.GetProperty("traces").EnumerateArray().Select((v, i) => (v, i)))
        {
            var runtime = catalog.CreateRuntime(); var slots = new LyraMainSlotUpdateOwner(runtime);
            var input = requests.RootElement.GetProperty("traces")[ti];
            foreach (var (row, i) in trace.GetProperty("frames").EnumerateArray().Select((v, i) => (v, i)))
            {
                var frame = input.GetProperty("frames")[i]; var identity = new AlsFrameIdentity(i, (uint)(41 + ti), 17);
                string? first = null;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    runtime.Begin(identity, frame.GetProperty("delta").GetSingle());
                    if (i == 0)
                    {
                        var foreign = catalog.CreateRuntime(); foreign.Begin(identity, frame.GetProperty("delta").GetSingle());
                        Reject(() => slots.Begin(foreign.Frame), "foreign bank with matching identity"); foreign.Discard();
                    }
                    slots.Begin(runtime.Frame, frame.GetProperty("initialize").GetBoolean());
                    var context = new AlsPoseUpdateContext(identity, frame.GetProperty("weight").GetSingle(), frame.GetProperty("delta").GetSingle(), frame.GetProperty("rootModifier").GetSingle());
                    if (!frame.GetProperty("active").GetBoolean()) context = context.AsInactive();
                    var candidates = new AlsSlotSourceUpdate[5];
                    for (var s = 0; s < 5; s++)
                    {
                        var expected = row.GetProperty("slots")[s];
                        Require(expected.GetProperty("node").GetInt32() == LyraMontageCatalog.MainNodes[s] && expected.GetProperty("slot").GetString() == LyraMontageCatalog.SlotNames[s], "Changed original Main Slot.");
                        if (frame.GetProperty("visited").GetBoolean()) candidates[s] = slots.Update(s, context, data.GetProperty("markSourceInactive").GetBoolean());
                        var weights = slots.Weights(s); var label = $"{trace.GetProperty("hz").GetInt32()}Hz/{i}/{s}";
                        Exact(weights.SourceWeight, expected.GetProperty("sourceWeight").GetSingle(), label + "/source");
                        Exact(weights.SlotNodeWeight, expected.GetProperty("slotWeight").GetSingle(), label + "/slot");
                        Exact(weights.TotalNodeWeight, expected.GetProperty("totalWeight").GetSingle(), label + "/total");
                        Require(candidates[s].Updated == expected.GetProperty("updated").GetBoolean(), label + "/update source");
                        if (candidates[s].Updated)
                        {
                            var c = candidates[s].Context;
                            Exact(c.Weight, expected.GetProperty("weight").GetSingle(), label + "/context weight");
                            Exact(c.RootMotionWeight, expected.GetProperty("rootModifier").GetSingle(), label + "/root modifier");
                            Require(c.IsActive == expected.GetProperty("active").GetBoolean(), label + "/active");
                            Require(c.Identity == identity, label + "/context identity"); if (attempt == 1) updates++;
                        }
                    }
                    var instances = row.GetProperty("instances");
                    Require(runtime.Candidate.Length == instances.GetArrayLength(), $"{ti}/{i}/physical instance count {runtime.Candidate.Length}/{instances.GetArrayLength()}");
                    for (var n = 0; n < runtime.Candidate.Length; n++)
                    {
                        var actual = runtime.Candidate[n]; var expected = instances[n]; var label = $"{ti}/{i}/instance{n}";
                        Require(actual.MontageId == expected.GetProperty("asset").GetInt32() && actual.Playing == expected.GetProperty("playing").GetBoolean(), label + "/asset and playing");
                        Exact(actual.Position, expected.GetProperty("position").GetSingle(), label + "/clock");
                        Exact(actual.Blend.CurrentWeight, expected.GetProperty("weight").GetSingle(), label + "/weight");
                        if (attempt == 1) instanceChecks++;
                    }
                    foreach (var group in runtime.Evaluation.ToArray().GroupBy(e => e.InstanceId))
                    {
                        var entries = group.ToArray(); var definition = catalog.Definitions[entries[0].ActionDefinitionId];
                        Require(entries.Length == 1 + definition.AdditionalTracks.Length, "Dropped Montage track.");
                        if (entries.Length > 1 && attempt == 1) multi++;
                        foreach (var e in entries) Exact(e.Weight, entries[0].Weight, "Dual tracks share physical fade.");
                    }
                    if (i % 53 == 0)
                    {
                        Reject(() => slots.Update(0, new AlsPoseUpdateContext(new(i, 99, 17), 1, context.Delta)), "foreign character context");
                        Reject(() => slots.ValidateCommit(new(i, identity.CharacterId, 18)), "foreign generation commit");
                        if (frame.GetProperty("visited").GetBoolean()) Reject(() => slots.Update(0, context), "duplicate node visit");
                    }
                    foreach (var command in frame.GetProperty("commands").EnumerateArray())
                    {
                        var asset = command.GetProperty("asset").GetInt32();
                        if (command.GetProperty("stop").GetBoolean())
                        {
                            var active = runtime.Candidate.ToArray().LastOrDefault(v => v.MontageId == asset && v.OwnsActiveActionLookup);
                            if (active.InstanceId > 0) Require(runtime.StopInstance(active.InstanceId, command.GetProperty("blend").GetSingle(), catalog.Definitions[asset].Lifecycle.BlendOutOption), "Failed active asset stop.");
                        }
                        else Require(runtime.PlayAction(asset, command.GetProperty("rate").GetSingle(), command.GetProperty("start").GetSingle(),
                            stopGroup: command.TryGetProperty("stopGroup", out var stopGroup) && stopGroup.GetBoolean()), "Failed authored play.");
                    }
                    var signature = JsonSerializer.Serialize(runtime.Candidate.ToArray()) + JsonSerializer.Serialize(candidates);
                    if (attempt == 0)
                    {
                        first = signature; slots.Cancel(); runtime.Discard(); retry++;
                        try { slots.Weights(0); throw new Exception("Accepted discarded Slot frame."); } catch (InvalidOperationException) { rejected++; }
                    }
                    else
                    {
                        Require(first == signature, "Late cancellation changed clock, command identity or Slot context.");
                        slots.ValidateCommit(identity); runtime.ValidateCommit(identity); slots.Commit(identity); runtime.Commit(identity);
                    }
                }
                if (!frame.GetProperty("visited").GetBoolean()) hidden++; frames++;
            }
        }
        Require(frames == 13440 && updates == data.GetProperty("counts").GetProperty("updated").GetInt32() && retry == frames &&
            hidden > 0 && rejected == frames && multi > 0 && instanceChecks > 0 && invalidCalls > 0, "Incomplete original Montage coverage.");
        GD.Print($"LYRA_MONTAGE_SLOTS_GODOT_OK fixture={prefix} frames={frames} slots={frames * 5} updates={updates} instances={instanceChecks} dualTrackFrames={multi} retry={retry} rejected={rejected} invalidCalls={invalidCalls} hidden={hidden} assets={catalog.Paths.Length} clocks=exact weights=exact contexts=exact pose=false production=false");
    }
}
