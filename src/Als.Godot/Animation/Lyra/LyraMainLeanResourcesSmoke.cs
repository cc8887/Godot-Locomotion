using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainLeanResourcesSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Main Lean resource smoke failed: " + error); GetTree().Quit(1); }
    }
    private static void Run()
    {
        var bank = LyraLogicalSourceBank.Load(includeMainLean: true);
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "native.json"));
        if (native.RootElement.GetProperty("catalogSha256").GetString() != bank.MainLeanCatalogSha256)
            throw new InvalidOperationException("Stale Main Lean native fixture.");
        var pose = new AlsPrecisePose[81]; var repeat = new AlsPrecisePose[81];
        var curves = new LyraCurveSample[bank.Curves.Names.Length];
        var attributes = new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        double maxP = 0, maxQ = 0, maxS = 0; var samples = 0; var oldSamples = 0;
        foreach (var row in native.RootElement.GetProperty("rows").EnumerateArray())
        {
            var slot = row.GetProperty("slot").GetString()!; var time = row.GetProperty("seconds").GetDouble();
            var sampler = bank.CreateSampler(slot);
            sampler.SampleRaw(time, pose);
            LyraLogicalSourceSmoke.Compare(row.GetProperty("raw"), pose, ref maxP, ref maxQ, ref maxS, slot, time, "raw");
            sampler.Sample(time, pose, curves, attributes);
            LyraLogicalSourceSmoke.Compare(row.GetProperty("output"), pose, ref maxP, ref maxQ, ref maxS, slot, time, "localAdditive");
            bank.CreateSampler(slot).Sample(time, repeat);
            if (!pose.SequenceEqual(repeat) || curves.Any(c => c.Present) || attributes.Any(a => a.Present))
                throw new InvalidOperationException("Lean occurrence or absent metadata differs.");
            samples++;
        }
        var space = new LyraMainLeanBlendSpace(bank); var weights = new AlsAimGridVertex[2]; var blends = 0;
        foreach (var row in native.RootElement.GetProperty("blendRows").EnumerateArray())
        {
            var angle = row.GetProperty("angle").GetSingle(); var time = row.GetProperty("normalized").GetSingle();
            var output = row.GetProperty("output"); var expectedWeights = output.GetProperty("samples");
            var count = space.EvaluateStatic(angle, time, pose, weights);
            if (count != expectedWeights.GetArrayLength() || output.GetProperty("curves").EnumerateObject().Any())
                throw new InvalidOperationException("Lean static sample inventory differs.");
            for (var i = 0; i < count; i++)
                if (weights[i].Sample != expectedWeights[i].GetProperty("index").GetInt32() ||
                    BitConverter.SingleToInt32Bits(weights[i].Weight) != BitConverter.SingleToInt32Bits(expectedWeights[i].GetProperty("weight").GetSingle()) ||
                    time * (float)bank.Get(weights[i].Sample switch { 0 => "main_lean_center", 1 => "main_lean_left", _ => "main_lean_right" }).Data.PlayLength !=
                        expectedWeights[i].GetProperty("seconds").GetDouble())
                    throw new InvalidOperationException($"Lean static sample order/weight/time differs angle={angle} normalized={time} index={i} actual={weights[i]} expected={expectedWeights[i]}.");
            LyraLogicalSourceSmoke.Compare(output.GetProperty("pose"), pose, ref maxP, ref maxQ, ref maxS, "leanBlend", angle, "static");
            blends++;
        }
        // Adding Main resources must retain every previous source and shared layout.
        using var oldNative = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "native.json"));
        double oldP = 0, oldQ = 0, oldS = 0;
        foreach (var row in oldNative.RootElement.GetProperty("rows").EnumerateArray())
        {
            var slot = row.GetProperty("slot").GetString()!; var time = row.GetProperty("seconds").GetDouble();
            var sampler = bank.CreateSampler(slot);
            sampler.SampleRaw(time, pose);
            LyraLogicalSourceSmoke.Compare(row.GetProperty("raw"), pose, ref oldP, ref oldQ, ref oldS, slot, time, "rawExtendedBank");
            sampler.Sample(time, pose);
            LyraLogicalSourceSmoke.Compare(row.GetProperty("output"), pose, ref oldP, ref oldQ, ref oldS, slot, time, "outputExtendedBank");
            oldSamples++;
        }
        if (bank.Count != 237 || bank.Curves.Count != 237 || samples != 24 || blends != 33 || oldSamples != 936)
            throw new InvalidOperationException("Incomplete Main Lean verification.");
        GD.Print($"LYRA_MAIN_LEAN_RESOURCES_NATIVE_OK sources=237 logical=81 skin=68 localSamples={samples} staticBlends={blends} " +
            $"oldSamples={oldSamples} positionCm={maxP} quaternion={maxQ} scale={maxS} oldPositionCm={oldP} oldQuaternion={oldQ} oldScale={oldS} " +
            "scope=resourcesAndStaticPose runtimeSmoothingAndClocks=pending");
    }
}
