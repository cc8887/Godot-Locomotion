using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Bind results evaluated by the native Blueprint with its actual CDO references.
// Never infer hand occupancy from prop names.
public sealed class AlsGetUpSelectionProfile
{
    private readonly int[] _definitions;
    internal AlsGetUpSelectionProfile(int[] definitions) => _definitions = definitions.ToArray();
    public int Select(AlsOverlayKind overlay, bool facingUpward)
    {
        if ((uint)overlay > (uint)AlsOverlayKind.Barrel) throw new ArgumentOutOfRangeException(nameof(overlay));
        return _definitions[(int)overlay * 2 + (facingUpward ? 1 : 0)];
    }
}

public static class AlsGetUpSelectionCompiler
{
    public static AlsGetUpSelectionProfile Compile(string json, AlsAnimationSetDefinition set,
        AlsP5aAnimationRuntimeProfile actions)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new ArgumentException("Unsupported Get-up selection schema.");
        var definitions = Enumerable.Repeat(-1, ((int)AlsOverlayKind.Barrel + 1) * 2).ToArray();
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            var overlay = row.GetProperty("overlay").GetInt32();
            if ((uint)overlay > (uint)AlsOverlayKind.Barrel) throw new ArgumentException("Foreign native Overlay.");
            var index = overlay * 2 + (row.GetProperty("facingUpward").GetBoolean() ? 1 : 0);
            if (definitions[index] >= 0) throw new ArgumentException("Duplicate native Get-up selection.");
            var path = row.GetProperty("montage").GetString();
            var matches = actions.Actions.Where(a => set.Montages[a.MontageId].ObjectPath == path).ToArray();
            if (matches.Length != 1 || path is null || !path.StartsWith(
                "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions/ALS_CLF_GetUp_", StringComparison.Ordinal))
                throw new ArgumentException("Native Get-up montage has no unique recovery action binding.");
            definitions[index] = matches[0].DefinitionId;
        }
        if (definitions.Any(d => d < 0)) throw new ArgumentException("Incomplete native Get-up selection.");
        return new(definitions);
    }
}
