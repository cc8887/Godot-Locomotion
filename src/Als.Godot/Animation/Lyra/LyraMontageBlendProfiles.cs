using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Actions;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraMontageBlendProfile(string Path, AlsMontageBlendProfileMode Mode,
    ImmutableArray<float> Factors);

internal sealed class LyraMontageBlendProfiles
{
    public ImmutableArray<LyraMontageBlendProfile> Profiles { get; }
    public ImmutableArray<(int In, int Out)> Bindings { get; }
    public const string Policy = "res://assets/generated/lyra_als/montage_blend_v1_policy.json";

    public LyraMontageBlendProfiles(IReadOnlyList<string> montagePaths)
    {
        const string root = "res://assets/generated/lyra_als/";
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Policy));
        var data = document.RootElement;
        if (data.GetProperty("schemaVersion").GetInt32() != 1 ||
            !montagePaths.SequenceEqual(data.GetProperty("montagePaths").EnumerateArray().Select(p => p.GetString()!)))
            throw new InvalidOperationException("Changed Montage profile binding.");
        foreach (var d in data.GetProperty("dependencies").EnumerateObject())
            if (d.Value.GetString() != LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + d.Name)))
                throw new InvalidOperationException("Stale Montage profile dependency: " + d.Name);
        using var calibration = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "logical_controls/calibration.json"));
        var names = data.GetProperty("boneNames").EnumerateArray().Select(n => n.GetString()!).ToArray();
        if (names.Length != 81 || !names.SequenceEqual(calibration.RootElement.GetProperty("layout")
            .GetProperty("logicalBoneNames").EnumerateArray().Select(n => n.GetString()!)))
            throw new InvalidOperationException("Montage profile uses another logical skeleton.");
        using var sourceDocument = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root + "skeletal_control_defaults.json"));
        var sourceSkeleton = sourceDocument.RootElement.GetProperty("skeletons").GetProperty("source");
        var sourceNames = sourceSkeleton.GetProperty("layout").GetProperty("logicalBoneNames").EnumerateArray().Select(n => n.GetString()!).ToArray();
        var sourceParents = sourceSkeleton.GetProperty("layout").GetProperty("logicalParents").EnumerateArray().Select(n => n.GetInt32()).ToArray();
        var profiles = ImmutableArray.CreateBuilder<LyraMontageBlendProfile>();
        foreach (var p in data.GetProperty("profiles").EnumerateArray())
        {
            var mode = (AlsMontageBlendProfileMode)p.GetProperty("mode").GetInt32();
            if (mode is not (AlsMontageBlendProfileMode.TimeFactor or AlsMontageBlendProfileMode.WeightFactor))
                throw new NotSupportedException("Unsupported Montage blend profile mode.");
            var factors = p.GetProperty("factors").EnumerateArray().Select(f => f.GetSingle()).ToImmutableArray();
            if (factors.Length != names.Length || factors.Any(f => !float.IsFinite(f)))
                throw new InvalidOperationException("Invalid Montage bone factors.");
            var expected = Enumerable.Repeat(1f, names.Length).ToArray();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var omitted = p.GetProperty("omittedTargetBones").EnumerateArray().ToDictionary(e => e.GetProperty("bone").GetString()!, StringComparer.OrdinalIgnoreCase);
            if (p.GetProperty("skeleton").GetString() != sourceSkeleton.GetProperty("asset").GetString())
                throw new InvalidOperationException("Profile source skeleton differs from its adaptation.");
            foreach (var entry in p.GetProperty("entries").EnumerateArray())
            {
                var bone = entry.GetProperty("bone").GetString()!; var scale = entry.GetProperty("scale").GetSingle();
                if (!seen.Add(bone) || !float.IsFinite(scale)) throw new InvalidOperationException("Invalid original profile entry.");
                var index = Array.FindIndex(names, n => string.Equals(n, bone, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    if (!omitted.Remove(bone, out var mapping)) throw new NotSupportedException("Unmapped profile bone: " + bone);
                    var source = Array.FindIndex(sourceNames, n => string.Equals(n, bone, StringComparison.OrdinalIgnoreCase));
                    if (source < 0) throw new InvalidOperationException("Unknown original profile bone.");
                    var target = -1;
                    while (source >= 0)
                    {
                        target = Array.FindIndex(names, n => string.Equals(n, sourceNames[source], StringComparison.OrdinalIgnoreCase));
                        if (target >= 0) break;
                        source = sourceParents[source];
                    }
                    if (target < 0 || mapping.GetProperty("ancestor").GetString() != sourceNames[source] ||
                        mapping.GetProperty("targetIndex").GetInt32() != target || mapping.GetProperty("scale").GetSingle() != scale ||
                        factors[target] != scale)
                        throw new NotSupportedException("Different transition policy on omitted profile bone: " + bone);
                }
                else expected[index] = scale;
            }
            if (omitted.Count != 0 || !factors.AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("Profile factors differ from source bone names.");
            profiles.Add(new(p.GetProperty("path").GetString()!, mode, factors));
        }
        Profiles = profiles.ToImmutable();
        Bindings = data.GetProperty("bindings").EnumerateArray().Select(b =>
            (b.GetProperty("in").GetInt32(), b.GetProperty("out").GetInt32())).ToImmutableArray();
        if (Bindings.Length != montagePaths.Count || Bindings.Any(b => b.In < -1 || b.Out < -1 || b.In >= Profiles.Length || b.Out >= Profiles.Length))
            throw new InvalidOperationException("Invalid physical Montage profile index.");
    }
}
