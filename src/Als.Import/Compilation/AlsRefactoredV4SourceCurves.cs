namespace GodotAls.Import.Compilation;

// Explicit V4 asset-data adapter. These are the same authored scalar samples,
// not a substitution of V4 prediction/locking algorithms or final graph values.
public static class AlsRefactoredV4SourceCurves
{
    public static IReadOnlyList<string> TargetNames { get; } = Array.AsReadOnly(new[] { "GroundPredictionBlock", "FootLeftLock", "FootRightLock" });
    private static readonly string[] SourceNames = ["Mask_LandPrediction", "FootLock_L", "FootLock_R"];

    // Use only at V4-authored curve producers (ModifyCurve overrides and fixed
    // Plant samples). Renaming the raw samples alone misses graph-authored locks.
    // Resolve before the producer's blend/override, never copy the final curve.
    public static string V4ProducerName(string requested)
    {
        for (var pair = 0; pair < TargetNames.Count; pair++)
            if (requested.Equals(TargetNames[pair], StringComparison.OrdinalIgnoreCase)) return SourceNames[pair];
        return requested;
    }

    public static void Bind(string assetPath, ReadOnlySpan<AlsFloatCurveDefinition> authored,
        ReadOnlySpan<string> layout, Span<int> sourceIds)
    {
        if (layout.Length != sourceIds.Length) throw new ArgumentException("Curve alias layout differs.");
        for (var pair = 0; pair < TargetNames.Count; pair++)
        {
            var target = -1;
            for (var i = 0; i < layout.Length; i++)
                if (layout[i].Equals(TargetNames[pair], StringComparison.OrdinalIgnoreCase)) target = i;
            if (target < 0) continue;
            if (!assetPath.StartsWith("/Game/AdvancedLocomotionV4/", StringComparison.Ordinal))
                throw new ArgumentException("V4 curve aliases require the verified V4 asset namespace.");
            if (sourceIds[target] >= 0) throw new ArgumentException("Authored Refactored target conflicts with V4 alias: " + TargetNames[pair]);
            foreach (var curve in authored)
                if (curve.SourceName.Equals(SourceNames[pair], StringComparison.OrdinalIgnoreCase))
                {
                    if (sourceIds[target] >= 0) throw new ArgumentException("Duplicate source alias curve.");
                    sourceIds[target] = curve.CurveId;
                }
            // No authored source means absence. Do not insert a synthetic zero.
        }
    }
}
