using System.Collections.ObjectModel;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraRigCurveValue(float Value, bool Set);

// Main73's optimized input boundary. The solver and output adapter are separate
// dependencies. All mutable state belongs to the caller's Rig candidate.
internal sealed class LyraFootPlantRigInputTransfer
{
    private readonly string?[] _mapping;
    private readonly Dictionary<string, LyraRigCurveValue> _curves;
    public LyraFootPlantRigHierarchy Hierarchy { get; }
    public IReadOnlyDictionary<string, LyraRigCurveValue> Curves => new ReadOnlyDictionary<string, LyraRigCurveValue>(_curves);

    public LyraFootPlantRigInputTransfer(JsonElement graph, JsonElement layout, JsonElement initial,
        JsonElement settings, JsonElement initialCurves, JsonElement descriptor)
    {
        Hierarchy = new(graph, initial, settings);
        var bones = graph.GetProperty("hierarchy").EnumerateArray().Where(r => r.GetProperty("type").GetString()!.Contains("BONE"))
            .ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("parent").GetString()!, StringComparer.OrdinalIgnoreCase);
        var names = layout.GetProperty("logicalBoneNames").EnumerateArray().Select(v => v.GetString()!).ToArray();
        var parents = layout.GetProperty("logicalParents").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        _mapping = names.Select(n => bones.Keys.FirstOrDefault(b => StringComparer.OrdinalIgnoreCase.Equals(b, n))).ToArray();
        if (names.Length != 81 || parents.Length != 81 || descriptor.GetProperty("mapping").GetArrayLength() != 81 ||
            !descriptor.GetProperty("livePoseAdapterEnabled").GetBoolean() || descriptor.GetProperty("nodeTransferGlobal").GetBoolean() ||
            !descriptor.GetProperty("nodeResetInput").GetBoolean())
            throw new NotSupportedException("Changed original Main73 input mode.");
        bool mismatch = false;
        for (int i = 0; i < 81; i++)
        {
            var row = descriptor.GetProperty("mapping")[i];
            bool requires = _mapping[i] is { } bone && bones[bone] != (parents[i] < 0 ? "None" : names[parents[i]]);
            mismatch |= requires;
            if (row.GetProperty("pose").GetInt32() != i || row.GetProperty("parent").GetInt32() != parents[i] ||
                row.GetProperty("rigBone").GetString() != (_mapping[i] ?? "") || row.GetProperty("requiresSpace").GetBoolean() != requires)
                throw new NotSupportedException("Changed native ALS Rig input mapping: " + i);
        }
        if (descriptor.GetProperty("transferLocal").GetBoolean() == mismatch ||
            !descriptor.GetProperty("resetBones").EnumerateArray().Select(v => v.GetString()!)
                .SequenceEqual(bones.Keys.Where(b => !_mapping.Contains(b, StringComparer.OrdinalIgnoreCase))))
            throw new NotSupportedException("Changed original Rig reset/space contract.");
        _curves = new(StringComparer.OrdinalIgnoreCase);
        foreach (var row in graph.GetProperty("hierarchy").EnumerateArray().Where(r => r.GetProperty("type").GetString()!.Contains("CURVE")))
        {
            var name = row.GetProperty("name").GetString()!; var value = initialCurves.GetProperty(name);
            _curves.Add(name, new(value.GetProperty("value").GetSingle(), value.GetProperty("set").GetBoolean()));
        }
        if (_curves.Count != 109) throw new NotSupportedException("Changed Rig curve layout.");
    }
    private LyraFootPlantRigInputTransfer(LyraFootPlantRigInputTransfer source)
    { Hierarchy = source.Hierarchy.Clone(); _mapping = (string?[])source._mapping.Clone(); _curves = new(source._curves, StringComparer.OrdinalIgnoreCase); }
    public LyraFootPlantRigInputTransfer Clone() => new(this);
    public void CopyFrom(LyraFootPlantRigInputTransfer source)
    {
        if (!_mapping.SequenceEqual(source._mapping) || !_curves.Keys.SequenceEqual(source._curves.Keys))
            throw new InvalidOperationException("Foreign Rig input candidate.");
        Hierarchy.CopyFrom(source.Hierarchy);
        foreach (var (name, value) in source._curves) _curves[name] = value;
    }
    public void Import(ReadOnlySpan<AlsPrecisePose> localPose, IReadOnlyDictionary<string, LyraCurveSample> curves)
    {
        foreach (var sample in curves.Values)
            if (!float.IsFinite(sample.Value)) throw new ArgumentException("Non-finite Rig input curve.");
        Hierarchy.ImportAdapterLocalPose(_mapping, localPose);
        // Unset preserves the stored number. Pose curves carry presence and are
        // matched by FName; Rig storage keeps value/set, not source curve flags.
        foreach (var name in _curves.Keys.ToArray()) _curves[name] = _curves[name] with { Set = false };
        foreach (var (name, sample) in curves)
            if (sample.Present && _curves.ContainsKey(name)) _curves[name] = new(sample.Value, true);
    }
}
