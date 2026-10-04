using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Exclusive character sampler. Its inputs are the physical bank's frozen
// evaluation records, never a second player clock or native oracle output.
internal sealed class LyraMontageTrackSampler : IDisposable
{
    private const string Root = "res://assets/generated/lyra_als/";
    private readonly LyraLogicalSourceBank _bank;
    private readonly LyraMontageCatalog _catalog;
    private readonly ImmutableArray<string> _bindings;
    private readonly LyraLogicalSourceSampler[] _poses;
    private readonly AlsRawRootMotionIntervalSampler[] _roots;
    private readonly LyraCompressedRootBank _compressed;
    private readonly (int Index, AlsNativeRichCurve Curve)[][] _curves;
    private int _sampling;
    private bool _disposed;
    public LyraMontageTrackSampler(LyraLogicalSourceBank bank, LyraMontageCatalog catalog)
    {
        _bank = bank; _catalog = catalog; _bindings = catalog.BindSources(bank);
        if (bank.MontageActionsCatalogSha256 is null || catalog.BlendProfiles is null)
            throw new InvalidOperationException("Complete Montage sources and target blend profiles are required.");
        using var policyDocument = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root + "montage_sampling_v1_policy.json"));
        var policy = policyDocument.RootElement;
        foreach (var d in policy.GetProperty("dependencies").EnumerateObject())
            if (d.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root + d.Name)))
                throw new InvalidOperationException("Stale Montage sampling resource dependency.");
        var rootBytes = Godot.FileAccess.GetFileAsBytes(Root + "montage_sampling_v1_roots.json");
        if (policy.GetProperty("schemaVersion").GetInt32() != 1 || policy.GetProperty("sourceLayout").GetString() != "ALS81" ||
            policy.GetProperty("curveCombine").GetString() != "MontageOverridesSequence" ||
            policy.GetProperty("rootExtraction").GetString() != "CompressedSequenceTrack" ||
            policy.GetProperty("rootSha256").GetString() != LyraLogicalSourceBank.Sha(rootBytes) ||
            !policy.GetProperty("montagePaths").EnumerateArray().Select(v => v.GetString()!).SequenceEqual(catalog.Paths) ||
            !policy.GetProperty("sequencePaths").EnumerateArray().Select(v => v.GetString()!).SequenceEqual(catalog.SequencePaths))
            throw new InvalidOperationException("Changed Montage sampling policy or source bindings.");
        using var rootsDocument = JsonDocument.Parse(rootBytes);
        if (rootsDocument.RootElement.GetProperty("assets").EnumerateObject().Count() != _bindings.Length)
            throw new InvalidOperationException("Incomplete Montage compressed root closure.");
        _compressed = LyraCompressedRootBank.Load(rootsDocument.RootElement, bank);
        _poses = _bindings.Select(bank.CreateSampler).ToArray();
        _roots = _bindings.Select(s => _compressed.CreateSampler(s, bank.Reference[0], bank.Get(s).NormalizedRootMotionScale)).ToArray();
        _curves = catalog.Metadata.Select(a => a.GetProperty("curves").GetProperty("curves").EnumerateArray().Select(c =>
        {
            var index = bank.Curves.Index(c.GetProperty("name").GetString()!);
            if (index < 0 || c.GetProperty("preInfinity").GetString() != "RCCE_Constant" ||
                c.GetProperty("postInfinity").GetString() != "RCCE_Constant")
                throw new NotSupportedException("Unbound or unsupported Montage curve.");
            var keys = c.GetProperty("keys").EnumerateArray().Select(k =>
            {
                if (k.GetProperty("weightMode").GetString() != "RCTWM_WeightedNone")
                    throw new NotSupportedException("Weighted Montage curve requires native evaluation support.");
                var mode = k.GetProperty("interpolation").GetString() switch
                { "RCIM_Constant" => AlsCurveInterpolationMode.Constant, "RCIM_Linear" => AlsCurveInterpolationMode.Linear,
                  "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic, _ => throw new NotSupportedException("Unknown Montage curve interpolation.") };
                return new AlsCurveKey(k.GetProperty("time").GetSingle(), k.GetProperty("value").GetSingle(),
                    k.GetProperty("arriveTangent").GetSingle(), k.GetProperty("leaveTangent").GetSingle(), mode);
            }).ToArray();
            return (index, new AlsNativeRichCurve(keys, AlsNativeBezierEvaluation.NestedLerp));
        }).ToArray()).ToArray();
    }
    public void Sample(in AlsMontageEvaluation e, bool extractRootMotion, LyraCompositionPoseBuffer output,
        bool combineMontageCurves = true)
    {
        if (_disposed || e.InstanceId <= 0 || !ReferenceEquals(output.Layout, _bank) || (uint)e.ActionDefinitionId >= _catalog.Definitions.Length ||
            (uint)e.AnimationId >= _bindings.Length || !float.IsFinite(e.Position) || !float.IsFinite(e.MontagePosition) ||
            !float.IsFinite(e.DeltaTimeRecord.PreviousPosition) || !float.IsFinite(e.DeltaTimeRecord.Delta))
            throw new ArgumentException("Invalid Montage track sample or output layout.");
        var asset = _catalog.Definitions[e.ActionDefinitionId];
        if (e.MontagePosition < 0 || e.MontagePosition > asset.Duration || e.DeltaTimeRecord.PreviousPosition < 0 ||
            e.DeltaTimeRecord.PreviousPosition > asset.Duration)
            throw new ArgumentOutOfRangeException(nameof(e),"Montage record lies outside its physical section.");
        var track = new AlsMontageTrack(asset.AnimationId, asset.Slot, asset.ClipStart, asset.ClipRate, asset.AdditiveType) { ClipEnd=asset.ClipEnd };
        if (track.AnimationId != e.AnimationId || track.Slot != e.Slot)
        {
            var found = false;
            foreach (var child in asset.AdditionalTracks)
                if (child.AnimationId == e.AnimationId && child.Slot == e.Slot) { track = child; found = true; break; }
            if (!found) throw new InvalidOperationException("Foreign Montage track source.");
        }
        var time = track.SamplePosition(e.MontagePosition);
        if (time != e.Position || track.AdditiveType != e.AdditiveType)
            throw new InvalidOperationException("Frozen track does not match its original Montage segment.");
        if (Interlocked.CompareExchange(ref _sampling, 1, 0) != 0)
            throw new InvalidOperationException("Montage character sampler is already in use.");
        try
        {
            var definition = _bank.Get(_bindings[e.AnimationId]);
            _poses[e.AnimationId].Sample(time, output.Pose, output.Curves, output.Attributes, extractRootMotion);
            // FAnimTrack preserves the physical delta and reconstructs sequence
            // previous time in double, narrowing only at SetPrevious.
            var previous = (float)((double)time - e.DeltaTimeRecord.Delta);
            output.RootMotion = definition.EnableRootMotion ? new(_roots[e.AnimationId].Extract(previous,
                e.DeltaTimeRecord.Delta, false), true) : default;
            if (combineMontageCurves)
                foreach (var (index, curve) in _curves[e.ActionDefinitionId])
                    output.Curves[index] = new(curve.Sample(e.MontagePosition), true);
        }
        finally { Volatile.Write(ref _sampling, 0); }
    }
    public void Dispose() { _disposed = true; _compressed.Dispose(); }
}
