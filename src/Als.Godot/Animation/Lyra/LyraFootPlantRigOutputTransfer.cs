using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Fixed Main73 optimized PoseAdapter. Mapping is by target bone identity, not
// Manny indices. Materialize globals before locals, as native UpdateOutput does.
internal sealed class LyraFootPlantRigOutputTransfer
{
    private readonly AlsRigPoseAdapter _adapter;
    public LyraFootPlantRigOutputTransfer(JsonElement descriptor)
    {
        var rows = descriptor.GetProperty("mapping").EnumerateArray().ToArray();
        if (rows.Length != 81 || descriptor.GetProperty("transferLocal").GetBoolean() ||
            !descriptor.GetProperty("livePoseAdapterEnabled").GetBoolean())
            throw new NotSupportedException("Changed original Main73 output adapter.");
        var mapping = rows.Select(r => r.GetProperty("rigBone").GetString() is { Length: > 0 } n ? n : null).ToArray();
        var parents = rows.Select(r => r.GetProperty("parent").GetInt32()).ToArray();
        for (int b = 0; b < rows.Length; b++)
            if (rows[b].GetProperty("pose").GetInt32() != b || parents[b] < -1 || parents[b] >= b)
                throw new NotSupportedException("Invalid target output hierarchy.");
        _adapter = new(mapping, parents);
    }
    public void Export(LyraFootPlantRigHierarchy hierarchy, ReadOnlySpan<AlsPrecisePose> source,
        float alpha, Span<AlsPrecisePose> output)
    {
        if (source.Length != 81 || output.Length != 81 || !float.IsFinite(alpha) || alpha is < 0 or > 1)
            throw new ArgumentException("Invalid Rig output pose/alpha.");
        hierarchy.ExportAdapterLocalPose(_adapter, source, alpha, output);
    }
    public static void CopyChannels(in LyraLayerPoseInput source, float alpha, LyraCompositionPoseBuffer output)
    {
        output.Validate(source);
        source.Curves.CopyTo(output.Curves); source.Attributes.CopyTo(output.Attributes);
        // This authored VM has no curve or animation-attribute writers. Root
        // transform attributes still pass through native additive arithmetic.
        output.RootMotion = source.RootMotion;
        if (alpha > 1e-5f && alpha < 1f - 1e-5f && source.RootMotion.Present)
            output.RootMotion = new(AlsPrecisePoseBlender.BlendAdditiveTarget(source.RootMotion.Value, source.RootMotion.Value, alpha), true);
    }
}
