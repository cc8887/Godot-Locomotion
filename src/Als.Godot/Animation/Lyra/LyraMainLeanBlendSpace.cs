using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Static source evaluation only. The three Main player occurrences must each
// retain their own native weight-smoothing history and clock at runtime.
internal sealed class LyraMainLeanBlendSpace
{
    private readonly LyraLogicalSourceSampler[] _samplers;
    private readonly double[] _lengths;
    private readonly AlsPrecisePose[] _scratch = new AlsPrecisePose[81];
    public LyraMainLeanBlendSpace(LyraLogicalSourceBank bank)
    {
        if (bank.MainLeanCatalogSha256 is null) throw new InvalidOperationException("Main requires its Lean source extension.");
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "inventory.json"));
        var inventory = document.RootElement;
        var axes = inventory.GetProperty("axes"); var axis = axes[0];
        var segments = inventory.GetProperty("nativeTriangulation").GetProperty("data").GetProperty("segments");
        if (axis.GetProperty("name").GetString() != "LeanAngle" || axis.GetProperty("min").GetDouble() != -20 ||
            axis.GetProperty("max").GetDouble() != 20 || axis.GetProperty("wrap").GetBoolean() ||
            inventory.GetProperty("useGrid").GetBoolean() || inventory.GetProperty("axisToScale").GetString() != "<BlendSpaceAxis.BSA_NONE: 0>" ||
            segments.GetArrayLength() != 2 || !Indices(segments[0], 1, 0) || !Indices(segments[1], 0, 2) ||
            segments[0].GetProperty("vertices")[0].GetDouble() != 0 || segments[0].GetProperty("vertices")[1].GetDouble() != .5 ||
            segments[1].GetProperty("vertices")[0].GetDouble() != .5 || segments[1].GetProperty("vertices")[1].GetDouble() != 1)
            throw new NotSupportedException("Changed Main Lean static BlendSpace topology.");
        var slots = new[] { "main_lean_center", "main_lean_left", "main_lean_right" };
        var samples = inventory.GetProperty("samples");
        for (var i = 0; i < slots.Length; i++)
        {
            var definition = bank.Get(slots[i]); var sample = samples[i];
            if (!definition.IsAdditive || definition.MeshSpaceAdditive || definition.BaseSlot != slots[0] ||
                sample.GetProperty("singleFrame").GetBoolean() || sample.GetProperty("mirror").GetBoolean() ||
                sample.GetProperty("rateScale").GetSingle() != 1)
                throw new NotSupportedException("Changed Main Lean sample policy.");
        }
        _samplers = slots.Select(bank.CreateSampler).ToArray();
        _lengths = slots.Select(s => bank.Get(s).Data.PlayLength).ToArray();
    }
    private static bool Indices(JsonElement segment, int first, int second) =>
        segment.GetProperty("sampleIndices")[0].GetInt32() == first && segment.GetProperty("sampleIndices")[1].GetInt32() == second;

    public static int StaticWeights(float angle, Span<AlsAimGridVertex> output)
    {
        if (!float.IsFinite(angle) || output.Length < 2) throw new ArgumentException("Invalid Main Lean input/buffer.");
        // GetNormalizedBlendInput computes in double; GetSamples1D uses float.
        var position = (float)((Math.Clamp((double)angle, -20, 20) + 20) / 40);
        var left = position < .5f;
        var fraction = Math.Clamp((position - (left ? 0 : .5f)) / .5f, 0, 1);
        var first = new AlsAimGridVertex(left ? 1 : 0, 1 - fraction);
        var second = new AlsAimGridVertex(left ? 0 : 2, fraction);
        // Native small-array sort reverses equal weights, after strict pruning.
        if (first.Weight <= AlsPoseBlender.WeightThreshold) { output[0] = second with { Weight = 1 }; output[1] = default; return 1; }
        if (second.Weight <= AlsPoseBlender.WeightThreshold) { output[0] = first with { Weight = 1 }; output[1] = default; return 1; }
        if (first.Weight <= second.Weight) (first, second) = (second, first);
        var total = first.Weight + second.Weight;
        output[0] = first with { Weight = first.Weight / total };
        output[1] = second with { Weight = second.Weight / total };
        return 2;
    }
    public int EvaluateStatic(float angle, float normalized, Span<AlsPrecisePose> output, Span<AlsAimGridVertex> weights)
    {
        if (!float.IsFinite(normalized) || output.Length != 81) throw new ArgumentException("Invalid static Lean pose buffer/time.");
        var count = StaticWeights(angle, weights);
        normalized = Math.Clamp(normalized, 0, 1);
        for (var i = 0; i < count; i++)
        {
            var sample = weights[i];
            _samplers[sample.Sample].Sample(normalized * (float)_lengths[sample.Sample], _scratch);
            for (var bone = 0; bone < output.Length; bone++)
                output[bone] = i == 0 ? AlsPrecisePoseBlender.Scale(_scratch[bone], sample.Weight)
                    : AlsPrecisePoseBlender.Accumulate(output[bone], _scratch[bone], sample.Weight);
        }
        if(count>1)
            for (var bone = 0; bone < output.Length; bone++) output[bone] = output[bone].Normalized();
        for (var bone = 0; bone < output.Length; bone++) output[bone] = output[bone].Normalized();
        return count;
    }
}
