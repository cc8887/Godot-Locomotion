using System.Text.Json;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsGroundedPoseDependencies
{
    private readonly float[] _factors;
    private readonly bool[] _entries;
    private readonly AlsCurveKey[] _keys;
    private readonly float[] _logicalFactors;
    private readonly bool[] _logicalEntries;
    public ReadOnlySpan<float> QuickFeetFactors => _factors;
    public ReadOnlySpan<bool> QuickFeetEntries => _entries;
    public ReadOnlySpan<AlsCurveKey> ChangeStanceKeys => _keys;
    public ReadOnlySpan<float> QuickFeetLogicalFactors => _logicalFactors;
    public ReadOnlySpan<bool> QuickFeetLogicalEntries => _logicalEntries;
    internal AlsGroundedPoseDependencies(float[] factors, bool[] entries, AlsCurveKey[] keys, float[] logicalFactors, bool[] logicalEntries)
    { _factors = factors.ToArray(); _entries = entries.ToArray(); _keys = keys.ToArray();
        _logicalFactors = logicalFactors.ToArray(); _logicalEntries = logicalEntries.ToArray(); }
    public System.Numerics.Vector2 QuickFeetWeights(int physicalBone, float alpha) =>
        AlsGroundedPoseBlend.WeightFactor(alpha, _factors[physicalBone], _entries[physicalBone]);
    public System.Numerics.Vector2 QuickFeetLogicalWeights(int logicalBone, float alpha) =>
        AlsGroundedPoseBlend.WeightFactor(alpha, _logicalFactors[logicalBone], _logicalEntries[logicalBone]);
    public float ChangeStance(float progress)
    {
        if (!AlsCurveRuntime.TrySample(new(0, 0, _keys.Length, 1, 1, 0), _keys, 0, progress, out var value, out var failure))
            throw new ArgumentException($"Invalid ChangeStance sample: {failure}");
        return value;
    }
}

public static class AlsGroundedPoseDependencyCompiler
{
    public static AlsGroundedPoseDependencies Compile(string json, AlsSkeletonDefinition skeleton)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("groundedSourceSchemaVersion").GetInt32() == 1 &&
            Text(root, "scope") == "Main Grounded with Standing and Crouching source dependencies", "Missing Grounded dependencies.");
        var profile = root.GetProperty("quickFeet");
        Require(Text(profile, "skeleton") == skeleton.ObjectPath && Text(profile, "path") == skeleton.ObjectPath + ":QuickFeet" &&
            profile.GetProperty("mode").GetInt32() == 1, "Unsupported QuickFeet provenance/mode.");
        var bones = profile.GetProperty("bones").EnumerateArray().ToArray();
        var entries = profile.GetProperty("entries").EnumerateArray().ToArray();
        Require(bones.Length == skeleton.LogicalBones.Length && entries.Length == 15, "Incomplete QuickFeet bone table.");
        var factors = Enumerable.Repeat(1f, skeleton.PhysicalBones.Length).ToArray();
        var mapped = new bool[factors.Length]; var used = new HashSet<int>();
        var logicalFactors = Enumerable.Repeat(1f, bones.Length).ToArray(); var logicalEntries = new bool[bones.Length];
        for (var index = 0; index < bones.Length; index++)
        {
            var bone = bones[index]; var definition = skeleton.LogicalBones[index];
            Require(bone.GetProperty("index").GetInt32() == index &&
                string.Equals(Text(bone, "name"), definition.Name, StringComparison.OrdinalIgnoreCase) &&
                bone.GetProperty("parent").GetInt32() == definition.ParentLogicalId, "QuickFeet skeleton topology differs.");
            var entry = bone.GetProperty("entry").GetInt32(); var scale = Number(bone, "scale");
            Require(scale >= 0 && entry >= -1 && entry < entries.Length, "Invalid QuickFeet bone entry.");
            if (entry == -1) { Require(scale == 1, "Unbound bone must retain the source weight."); continue; }
            var source = entries[entry];
            Require(used.Add(entry) && source.GetProperty("index").GetInt32() == entry &&
                string.Equals(Text(source, "bone"), definition.Name, StringComparison.OrdinalIgnoreCase) &&
                Number(source, "scale") == scale, "QuickFeet entry ownership differs.");
            var physical = skeleton.LogicalToPhysical[index];
            logicalFactors[index] = scale; logicalEntries[index] = true;
            Require(physical >= 0 && physical < factors.Length && !mapped[physical], "Unsupported QuickFeet virtual/aliased bone.");
            factors[physical] = scale; mapped[physical] = true;
        }
        Require(used.Count == entries.Length, "Unresolved QuickFeet entries.");
        var nativeCurve = root.GetProperty("changeStanceCurve");
        Require(Text(nativeCurve, "path") == "/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/ChangeStance.ChangeStance",
            "ChangeStance curve provenance differs.");
        var curve = nativeCurve.GetProperty("curve");
        Require(Text(curve, "preInfinityExtrap") == "RCCE_Constant" && Text(curve, "postInfinityExtrap") == "RCCE_Constant",
            "Unsupported ChangeStance extrapolation.");
        var keys = curve.GetProperty("keys").EnumerateArray().Select(key =>
        {
            Require(Text(key, "tangentWeightMode") == "RCTWM_WeightedNone", "Unsupported weighted ChangeStance tangent.");
            return new AlsCurveKey(Number(key, "time"), Number(key, "value"), Number(key, "arriveTangent"), Number(key, "leaveTangent"),
                Text(key, "interpMode") switch { "RCIM_Constant" => AlsCurveInterpolationMode.Constant,
                    "RCIM_Linear" => AlsCurveInterpolationMode.Linear, "RCIM_Cubic" => AlsCurveInterpolationMode.Cubic,
                    _ => throw new FormatException("Unsupported ChangeStance interpolation.") });
        }).ToArray();
        Require(keys.Length >= 2 && keys[0].TimeSeconds == 0 && keys[^1].TimeSeconds == 1 &&
            keys[0].Value == 0 && keys[^1].Value == 1 && keys.All(k => k.TimeSeconds is >= 0 and <= 1) &&
            keys.Zip(keys.Skip(1)).All(p => p.First.TimeSeconds < p.Second.TimeSeconds), "Invalid ChangeStance key domain.");
        return new(factors, mapped, keys, logicalFactors, logicalEntries);
    }
    private static string Text(JsonElement row, string name) => row.GetProperty(name).GetString()!;
    private static float Number(JsonElement row, string name)
    { var value = row.GetProperty(name).GetSingle(); Require(float.IsFinite(value), "Non-finite Grounded dependency."); return value; }
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
