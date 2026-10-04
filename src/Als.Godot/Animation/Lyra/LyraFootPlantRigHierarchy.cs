using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Fixed FootPlant resource/layout adaptation. Core owns all mutable caches.
internal sealed class LyraFootPlantRigHierarchy
{
    private readonly AlsRigHierarchy _runtime;
    public LyraFootPlantRigHierarchy(JsonElement graph, JsonElement initial, JsonElement controlSettings, JsonElement? nativeLayout = null)
    {
        var elements = new List<AlsRigElementDefinition>();
        foreach (var row in graph.GetProperty("hierarchy").EnumerateArray())
        {
            string name = row.GetProperty("name").GetString()!;
            if (!initial.TryGetProperty(name, out var pose)) continue;
            string parent = row.GetProperty("parent").GetString()!;
            bool control = row.GetProperty("type").GetString()!.Contains("CONTROL", StringComparison.Ordinal);
            AlsPrecisePose P(string field) => LyraFootPlantRigConstruction.Pose(pose.GetProperty(field));
            elements.Add(new(name, parent == "None" ? null : parent, control,
                new(P("local"), P("global"), P("initialLocal"), P("initialGlobal"),
                    control ? P("offsetLocal") : null, control ? P("offsetGlobal") : null,
                    control ? P("initialOffsetLocal") : null, control ? P("initialOffsetGlobal") : null)));
        }
        if (elements.Count != 98 || elements.Count(e => e.Control) != 7)
            throw new NotSupportedException("Changed FootPlant hierarchy layout.");
        foreach (var e in elements)
        {
            if (e.Control)
            {
                var settings = controlSettings.GetProperty(e.Name);
                if (settings.GetProperty("controlType").GetString() != "Transform" ||
                    settings.GetProperty("limits").EnumerateArray().Any(l => l.GetProperty("minimum").GetBoolean() || l.GetProperty("maximum").GetBoolean()))
                    throw new NotSupportedException("Changed FootPlant control value storage: " + e.Name);
            }
            if (nativeLayout is { } layout)
            {
                var row = layout.GetProperty(e.Name);
                var parents = row.GetProperty("parents").EnumerateArray().Select(p => p.GetString()).ToArray();
                if (row.GetProperty("type").GetString() != (e.Control ? "Control" : "Bone") ||
                    parents.Length != (e.Parent is null ? 0 : 1) || parents.Length == 1 && parents[0] != e.Parent)
                    throw new NotSupportedException("Changed native Rig parents: " + e.Name);
                if (e.Control && (row.GetProperty("limitsEnabled").GetBoolean() || row.GetProperty("animationChannel").GetBoolean() ||
                    row.GetProperty("weights").EnumerateArray().Any(w => new[] { "current", "initial" }.Any(f =>
                        w.GetProperty(f).EnumerateArray().Any(v => v.GetDouble() != 1)))))
                    throw new NotSupportedException("Unsupported native control constraints: " + e.Name);
            }
        }
        _runtime = new(elements);
    }
    private LyraFootPlantRigHierarchy(AlsRigHierarchy runtime) => _runtime = runtime;
    public LyraFootPlantRigHierarchy Clone() => new(_runtime.Clone());
    public void CopyFrom(LyraFootPlantRigHierarchy source) => _runtime.CopyFrom(source._runtime);
    public AlsPrecisePose GetParent(string name, bool initial = false) => _runtime.GetParent(name, initial);
    public bool IsGlobalDirty(string name) => _runtime.IsGlobalDirty(name);
    public void ApplySingleParentConstraint(string child, string parent) => _runtime.ApplySingleParentConstraint(child, parent);
    public void ExportAdapterLocalPose(AlsRigPoseAdapter adapter, ReadOnlySpan<AlsPrecisePose> source, float alpha, Span<AlsPrecisePose> output) =>
        adapter.ExportLocalPose(_runtime, source, alpha, output);
    public AlsPrecisePose Get(string name, bool local = false, bool initial = false, bool offset = false) => _runtime.Get(name, local, initial, offset);
    public void Set(string name, AlsPrecisePose value, bool local = false, bool initial = false, bool offset = false, bool children = true, bool force = false) =>
        _runtime.Set(name, value, local, initial, offset, children, force);
    public void Reset() => _runtime.Reset();
    public IReadOnlyDictionary<string, AlsPrecisePose> CaptureConstructionPose(bool initial) => _runtime.CaptureConstructionPose(initial);
    public void RestoreConstructionPose(IReadOnlyDictionary<string, AlsPrecisePose> pose) => _runtime.RestoreConstructionPose(pose);
    public void ImportAdapterLocalPose(IReadOnlyList<string?> mapping, ReadOnlySpan<AlsPrecisePose> pose)
    {
        if (mapping.Count != 81 || pose.Length != 81) throw new ArgumentException("Incomplete ALS Rig pose.");
        _runtime.ImportAdapterLocalPose(mapping, pose);
    }
    public IReadOnlyDictionary<string, LyraRigElementTransforms> Snapshot() => _runtime.Snapshot().ToDictionary(p => p.Key,
        p => new LyraRigElementTransforms(p.Value.Local, p.Value.Global, p.Value.InitialLocal, p.Value.InitialGlobal,
            p.Value.OffsetLocal, p.Value.OffsetGlobal, p.Value.InitialOffsetLocal, p.Value.InitialOffsetGlobal), StringComparer.OrdinalIgnoreCase);
}
