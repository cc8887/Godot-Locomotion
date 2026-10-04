using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMontageResourcesSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Montage resources failed: " + error); GetTree().Quit(1); }
    }
    private static void Run()
    {
        var bank = LyraLogicalSourceBank.Load(includeMainLean: true, includeLocomotionExtras: true, includeMontageActions: true);
        var catalog = new LyraMontageCatalog();
        var sourceBindings = catalog.BindSources(bank);
        using var bindings = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MontageActionsRoot + "bindings.json"));
        var binding = bindings.RootElement;
        if (binding.GetProperty("schemaVersion").GetInt32() != 1 ||
            binding.GetProperty("catalogSha256").GetString() != bank.MontageActionsCatalogSha256 ||
            binding.GetProperty("montageCatalogSha256").GetString() != LyraLogicalSourceBank.Sha(
                Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/montage_catalog_v2.json")))
            throw new InvalidOperationException("Stale Montage resource bindings.");
        foreach (var sequence in catalog.SequencePaths)
            if (binding.GetProperty("sequences").GetProperty(sequence).GetString() != sourceBindings[catalog.SequencePaths.IndexOf(sequence)])
                throw new InvalidOperationException("Montage sequence binding differs: " + sequence);
        var tracks = 0;
        foreach (var a in binding.GetProperty("assets").EnumerateArray())
        {
            var id = catalog.Paths.IndexOf(a.GetProperty("path").GetString()!);
            if (id < 0) throw new InvalidOperationException("Unknown Montage resource binding.");
            var original = catalog.Metadata[id].GetProperty("slots");
            var mapped = a.GetProperty("tracks");
            if (mapped.GetArrayLength() != original.GetArrayLength()) throw new InvalidOperationException("Missing Montage track.");
            for (var i = 0; i < mapped.GetArrayLength(); i++)
            {
                if (mapped[i].GetProperty("slot").GetString() != original[i].GetProperty("name").GetString() ||
                    mapped[i].GetProperty("sequence").GetString() != bank.SlotForSource(
                        original[i].GetProperty("segments")[0].GetProperty("animation").GetString()!))
                    throw new InvalidOperationException("Montage track source differs.");
                tracks++;
            }
        }
        var samplers = bank.Slots.ToDictionary(s => s, bank.CreateSampler);
        var pose = new AlsPrecisePose[81]; var repeat = new AlsPrecisePose[81];
        var curves = new LyraCurveSample[bank.Curves.Names.Length];
        var attributes = new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        double maxP = 0, maxQ = 0, maxS = 0;
        var samples = 0; var oldSamples = 0; var curveRows = 0; var curveValues = 0; var attributeValues = 0;
        var actionSlots = new HashSet<string>();
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MontageActionsRoot + "native.json"));
        if (native.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
            native.RootElement.GetProperty("catalogSha256").GetString() != bank.MontageActionsCatalogSha256)
            throw new InvalidOperationException("Stale Montage native oracle.");
        foreach (var row in native.RootElement.GetProperty("rows").EnumerateArray())
        { Compare(row); samples++; actionSlots.Add(row.GetProperty("slot").GetString()!); }
        foreach (var path in new[] { LyraLogicalSourceBank.Root, LyraLogicalSourceBank.MainLeanRoot, LyraLogicalSourceBank.LocomotionExtrasRoot })
        {
            using var old = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(path + "native.json"));
            foreach (var row in old.RootElement.GetProperty("rows").EnumerateArray()) { Compare(row); oldSamples++; }
        }
        void Compare(JsonElement row)
        {
            var slot = row.GetProperty("slot").GetString()!; var time = row.GetProperty("seconds").GetDouble();
            var sampler = samplers[slot];
            sampler.SampleRaw(time, pose);
            LyraLogicalSourceSmoke.Compare(row.GetProperty("raw"), pose, ref maxP, ref maxQ, ref maxS, slot, time, "montageRaw");
            sampler.Sample(time, pose, curves, attributes);
            LyraLogicalSourceSmoke.Compare(row.GetProperty("output"), pose, ref maxP, ref maxQ, ref maxS, slot, time, "montageOutput");
            bank.CreateSampler(slot).Sample(time, repeat);
            if (!pose.SequenceEqual(repeat)) throw new InvalidOperationException("Shared action occurrence scratch.");
            sampler.Sample(time, repeat);
            if (!pose.SequenceEqual(repeat)) throw new InvalidOperationException("Action repeat changed output.");
        }
        foreach (var row in native.RootElement.GetProperty("curveRows").EnumerateArray())
        { CompareChannels(row); curveRows++; }
        var oldCurveRows = 0;
        var oldCurveSlots = new HashSet<string>();
        using (var oldCurves = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "curve_native.json")))
        {
            if (oldCurves.RootElement.GetProperty("curveBankSha256").GetString() != LyraLogicalSourceBank.Sha(
                Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "curve_bank.json")))
                throw new InvalidOperationException("Stale old curve oracle.");
            foreach (var row in oldCurves.RootElement.GetProperty("rows").EnumerateArray())
            {
                var slot = row.GetProperty("slot").GetString()!;
                if (slot.StartsWith("fixture_", StringComparison.Ordinal)) continue;
                CompareChannels(row); oldCurveSlots.Add(slot); oldCurveRows++;
            }
        }
        using (var oldExtras = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.LocomotionExtrasRoot + "native.json")))
            foreach (var row in oldExtras.RootElement.GetProperty("curveRows").EnumerateArray())
            { CompareChannels(row); oldCurveRows++; }
        void CompareChannels(JsonElement row)
        {
            var slot = row.GetProperty("slot").GetString()!; var time = row.GetProperty("seconds").GetDouble();
            bank.Curves.SampleRaw(slot, time, curves);
            LyraSourceCurveSmoke.CheckCurves(bank.Curves, curves, row.GetProperty("raw"), slot, time, "montageRaw", ref curveValues);
            bank.Curves.Sample(slot, time, curves);
            LyraSourceCurveSmoke.CheckCurves(bank.Curves, curves, row.GetProperty("output"), slot, time, "montageOutput", ref curveValues);
            bank.Curves.Attributes.SampleRaw(slot, time, attributes);
            LyraSourceCurveSmoke.CheckAttributes(bank.Curves.Attributes, attributes, row.GetProperty("rawAttributes"), slot, time, ref attributeValues);
            bank.Curves.Attributes.Sample(slot, time, attributes);
            LyraSourceCurveSmoke.CheckAttributes(bank.Curves.Attributes, attributes, row.GetProperty("outputAttributes"), slot, time, ref attributeValues);
        }
        var additive = actionSlots.Count(s => bank.Get(s).IsAdditive);
        var mesh = actionSlots.Count(s => bank.Get(s).IsAdditive && bank.Get(s).MeshSpaceAdditive);
        var nonzero = actionSlots.Count(s => bank.Get(s).IsAdditive && bank.Get(s).BaseSampleTime > 0);
        if (bank.Count != 300 || bank.Curves.Count != 300 || actionSlots.Count != 55 || additive != 27 || mesh != 3 ||
            nonzero != 4 || oldSamples != 1435 || oldCurveSlots.Count != 234 || tracks != 60 || catalog.Paths.Length != 45 || catalog.SequencePaths.Length != 55 ||
            binding.GetProperty("assets").GetArrayLength() != 45 || binding.GetProperty("sequences").EnumerateObject().Count() != 55)
            throw new InvalidOperationException("Incomplete Montage resource closure.");
        GD.Print($"LYRA_MONTAGE_RESOURCES_GODOT_OK sources=300 actions=55 additive=27 local=24 mesh=3 nonzeroBases=4 montages=45 tracks=60 " +
            $"samples={samples} oldSamples={oldSamples} curveRows={curveRows} oldCurveRows={oldCurveRows} curveValues={curveValues} attributeValues={attributeValues} " +
            $"positionCm={maxP} quaternion={maxQ} scale={maxS} logical=81 skin=68 slotPose=false");
    }
}
