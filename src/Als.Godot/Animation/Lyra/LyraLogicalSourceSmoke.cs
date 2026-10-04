using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraLogicalSourceSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Logical source native smoke failed: " + error); GetTree().Quit(1); }
    }
    private static void Run()
    {
        var bank = LyraLogicalSourceBank.Load();
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "native.json"));
        var root = native.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("catalogSha256").GetString() != bank.CatalogSha256 ||
            root.GetProperty("calibrationSha256").GetString() != bank.CalibrationSha256)
            throw new InvalidOperationException("Stale logical source oracle.");
        var samplers = bank.Slots.ToDictionary(slot => slot, bank.CreateSampler);
        var first = new AlsPrecisePose[81]; var second = new AlsPrecisePose[81];
        double maxPosition = 0, maxQuaternion = 0, maxScale = 0;
        var cases = 0; var additive = 0;
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var slot = row.GetProperty("slot").GetString()!; var seconds = row.GetProperty("seconds").GetDouble();
            var sampler = samplers[slot];
            sampler.SampleRaw(seconds, first);
            Compare(row.GetProperty("raw"), first, ref maxPosition, ref maxQuaternion, ref maxScale, slot, seconds, "raw");
            sampler.Sample(seconds, first);
            Compare(row.GetProperty("output"), first, ref maxPosition, ref maxQuaternion, ref maxScale, slot, seconds, "output");
            // A second occurrence must not inherit another player's time or scratch.
            var independent = bank.CreateSampler(slot);
            independent.Sample(seconds, second);
            if (!first.SequenceEqual(second)) throw new InvalidOperationException("Logical source scratch leaked across occurrences.");
            sampler.Sample(seconds, second);
            if (!first.SequenceEqual(second)) throw new InvalidOperationException("Repeated logical sampling changed its output.");
            if (bank.Get(slot).IsAdditive) additive++;
            cases++;
        }
        if (bank.Count != 234 || cases != 936 || additive != 180 ||
            maxPosition > 1e-8 || maxQuaternion > 1e-10 || maxScale > 1e-12)
            throw new InvalidOperationException($"Logical source mismatch cases={cases} additive={additive} " +
                $"p={maxPosition} q={maxQuaternion} s={maxScale}.");
        GD.Print($"LYRA_LOGICAL_SOURCE_NATIVE_OK sources={bank.Count} raw=69 logical=81 skin=68 cases={cases} " +
            $"additive={additive} positionCm={maxPosition} quaternion={maxQuaternion} scale={maxScale} " +
            "virtual=beforeInterpolation occurrenceScratch=isolated stage=rawSource runtimeIntegration=separate");
    }
    internal static void Compare(JsonElement expected, ReadOnlySpan<AlsPrecisePose> actual,
        ref double maxPosition, ref double maxQuaternion, ref double maxScale, string slot, double time, string kind)
    {
        if (expected.GetArrayLength() != actual.Length) throw new InvalidOperationException("Incomplete native pose.");
        for (var bone = 0; bone < actual.Length; bone++)
        {
            var target = LyraLogicalSourceBank.ParsePose(expected[bone]); var value = actual[bone];
            var p = Math.Sqrt((value.Position - target.Position).LengthSquared);
            var a = value.Rotation.Normalized(); var b = target.Rotation.Normalized();
            var sign = AlsQuaternion.Dot(a, b) < 0 ? -1 : 1;
            var q = Math.Sqrt((a + b * -sign).LengthSquared);
            var s = Math.Sqrt((value.Scale - target.Scale).LengthSquared);
            maxPosition = Math.Max(maxPosition, p); maxQuaternion = Math.Max(maxQuaternion, q); maxScale = Math.Max(maxScale, s);
            if (p > 1e-8 || q > 1e-10 || s > 1e-12)
                throw new InvalidOperationException($"{slot} time={time} kind={kind} bone={bone}: p={p} q={q} s={s}.");
        }
    }
}
