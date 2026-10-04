using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraSourceCurveSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Lyra source curves failed: " + error); GetTree().Quit(1); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private void Run()
    {
        var poses = LyraLogicalSourceBank.Load(); var curves = poses.Curves;
        var bytes = Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "curve_bank.json");
        var fixture = LyraSourceCurveBank.Fixture(bytes, poses);
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "curve_native.json"));
        Require(native.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
            native.RootElement.GetProperty("curveBankSha256").GetString() == LyraLogicalSourceBank.Sha(bytes), "Stale native curve trace.");
        Require(curves.Count == 234 && curves.CurveCount == 159 && curves.Names.Length == 6 &&
            curves.Attributes.AuthoredCount == 936 && curves.Attributes.Layout.Length == 4, "Incomplete current source closure.");
        var rejected = RejectInvalidBanks(bytes, poses);
        var ordinaryRaw = new LyraCurveSample[curves.Names.Length]; var ordinaryOutput = new LyraCurveSample[curves.Names.Length];
        var fixtureRaw = new LyraCurveSample[fixture.Names.Length]; var fixtureOutput = new LyraCurveSample[fixture.Names.Length];
        var attributes = new LyraAttributeSample[curves.Attributes.Layout.Length];
        var fixtureAttributes = new LyraAttributeSample[fixture.Attributes.Layout.Length];
        var slots = new HashSet<string>(); var rows = 0; var values = 0; var attributeValues = 0;
        foreach (var row in native.RootElement.GetProperty("rows").EnumerateArray())
        {
            var slot = row.GetProperty("slot").GetString()!; var time = row.GetProperty("seconds").GetDouble();
            var bank = slot.StartsWith("fixture_", StringComparison.Ordinal) ? fixture : curves;
            var raw = bank == fixture ? fixtureRaw : ordinaryRaw; var output = bank == fixture ? fixtureOutput : ordinaryOutput;
            bank.SampleRaw(slot, time, raw); bank.Sample(slot, time, output);
            CheckCurves(bank, raw, row.GetProperty("raw"), slot, time, "raw", ref values);
            CheckCurves(bank, output, row.GetProperty("output"), slot, time, "output", ref values);
            var attributeBuffer = bank == fixture ? fixtureAttributes : attributes;
            bank.Attributes.SampleRaw(slot, time, attributeBuffer);
            CheckAttributes(bank.Attributes, attributeBuffer, row.GetProperty("rawAttributes"), slot, time, ref attributeValues);
            bank.Attributes.Sample(slot, time, attributeBuffer);
            CheckAttributes(bank.Attributes, attributeBuffer, row.GetProperty("outputAttributes"), slot, time, ref attributeValues);
            slots.Add(slot); rows++;
        }
        Require(slots.Count == 236, "Missing native curve source coverage.");
        fixture.Sample("fixture_additive", 0, fixtureOutput);
        Require(fixtureOutput[fixture.Index("NativeBaseOnly")] == new LyraCurveSample(-8, true) &&
            fixtureOutput[fixture.Index("NativeBoth")] == new LyraCurveSample(3, true) &&
            fixtureOutput[fixture.Index("NativeSourceOnly")] == new LyraCurveSample(-2, true), "Additive curve union semantics differ.");
        // Exercise the actual joint pose/curve/attribute sampler, including all
        // additive bases. It must reproduce the original pose-only path exactly.
        var pose = new AlsPrecisePose[81]; var independent = new AlsPrecisePose[81]; var joint = 0;
        foreach (var slot in poses.Slots)
        {
            var sampler = poses.CreateSampler(slot); var other = poses.CreateSampler(slot);
            foreach (var ratio in new[] { 0, .37, .75, 1 })
            {
                var time = poses.Get(slot).Data.PlayLength * ratio;
                sampler.Sample(time, pose, ordinaryOutput, attributes); other.Sample(time, independent);
                Require(pose.AsSpan().SequenceEqual(independent), "Joint source sampler changed bone output."); joint++;
            }
        }
        for (var i = 0; i < 200; i++)
        { curves.Sample("jog_fwd_start", .4, ordinaryOutput); curves.Attributes.Sample("jog_fwd_start", .4, attributes); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        { curves.Sample("jog_fwd_start", .4, ordinaryOutput); curves.Attributes.Sample("jog_fwd_start", .4, attributes); }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, "Source scalar sampling allocated per frame.");
        GD.Print($"LYRA_SOURCE_CURVES_OK clips=234 curves=159 names=6 attributes=936 rows={rows} " +
            $"values={values} attributeValues={attributeValues} joint={joint} rejected={rejected} error=0 flags=0 allocation={allocated} scope=rawAndAdditiveSources");
    }
    private static int RejectInvalidBanks(byte[] bytes, LyraLogicalSourceBank poses)
    {
        Action<JsonObject>[] changes = [
            root => root["catalogSha256"] = "stale",
            root => root["entries"]![0]!["attributes"]![0]!["type"] = "/Script/Engine.StringAnimationAttribute",
            root => root["entries"]![0]!["attributes"]![0]!["bone"] = "missing_bone",
            root => root["entries"]![0]!["attributes"]!.AsArray().RemoveAt(0),
            root => root["entries"]!.AsArray().First(r => r!["curves"]!.AsArray().Count > 0)!["curves"]![0]!["keys"]![0]!["weightMode"] = "RCTWM_WeightedBoth",
            root => root["entries"]!.AsArray().First(r => r!["additive"]!.GetValue<bool>())!["baseSlot"] = "missing_base"];
        foreach (var change in changes)
        {
            var root = JsonNode.Parse(bytes)!.AsObject(); change(root); var rejected = false;
            try { LyraSourceCurveBank.Parse(Encoding.UTF8.GetBytes(root.ToJsonString()), poses); }
            catch (Exception error) when (error is InvalidOperationException or NotSupportedException or KeyNotFoundException)
            { rejected = true; }
            Require(rejected, "Invalid source curve/attribute binding accepted.");
        }
        return changes.Length;
    }
    internal static void CheckCurves(LyraSourceCurveBank bank, LyraCurveSample[] actual, JsonElement expected,
        string slot, double time, string mode, ref int values)
    {
        for (var id = 0; id < actual.Length; id++)
        {
            var name = bank.Names[id]; var present = expected.TryGetProperty(name, out var native);
            if (!present)
                foreach (var property in expected.EnumerateObject())
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    { native = property.Value; present = true; break; }
            Require(actual[id].Present == present, $"Curve presence {slot}/{name}/{mode} t={time:R}");
            if (!present) continue;
            var value = native.GetProperty("value").GetSingle(); var flags = native.GetProperty("flags").GetUInt32();
            Require(BitConverter.SingleToInt32Bits(actual[id].Value) == BitConverter.SingleToInt32Bits(value) && actual[id].Flags == flags,
                $"Curve value {slot}/{name}/{mode} t={time:R} actual={actual[id].Value:R} native={value:R} flags={actual[id].Flags}/{flags}");
            values++;
        }
        Require(expected.EnumerateObject().Count() == actual.Count(v => v.Present), "Native curve has an unknown name.");
    }
    internal static void CheckAttributes(LyraSourceAttributeBank bank, LyraAttributeSample[] actual, JsonElement expected,
        string slot, double time, ref int values)
    {
        for (var id = 0; id < actual.Length; id++)
        {
            var identity = bank.Layout[id]; JsonElement? matched = null;
            foreach (var row in expected.EnumerateArray())
                if (row.GetProperty("type").GetString() == identity.Type && row.GetProperty("namespace").GetString() == identity.Namespace &&
                    string.Equals(row.GetProperty("bone").GetString(), identity.Bone, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.GetProperty("name").GetString(), identity.Name, StringComparison.OrdinalIgnoreCase)) matched = row;
            Require(actual[id].Present == matched.HasValue, $"Attribute presence {slot}/{identity.Name} t={time:R}");
            if (matched is not { } value) continue;
            Require(actual[id].Value == value.GetProperty("value").GetProperty("value").GetInt32(),
                $"Attribute value {slot}/{identity.Name} t={time:R} actual={actual[id].Value} native={value}"); values++;
        }
        Require(expected.GetArrayLength() == actual.Count(v => v.Present), "Unknown native attribute identity.");
    }
}
