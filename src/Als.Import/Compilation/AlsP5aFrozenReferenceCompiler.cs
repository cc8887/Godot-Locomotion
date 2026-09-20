namespace GodotAls.Import.Compilation;

/// <summary>
/// Reconstructs the historical graph captured by the P5A v1 fixtures. This is
/// replay-only: production code must use AlsP5CoreRuntimeBindingCompiler.
/// Neither the recorded fixture bytes nor the current V4 settings are modified.
/// </summary>
public static class AlsP5aFrozenReferenceCompiler
{
    public static AlsP5CoreRuntimeBindingSnapshot Compile(
        AlsAnimationSetDefinition animationSet, AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose, AlsP5aAnimationRuntimeProfile p5a, AlsP5OccurrenceLayout layout)
    {
        var current = AlsP5CoreRuntimeBindingCompiler.Compile(animationSet, locomotion, pose, p5a, layout);
        RequireIdentity(current, 0xFD59AB9C657B60DDUL, "current V4");
        // Recompile real historical graph fields; never relabel a current graph
        // with a recorded digest. Only four crouching ScaleAngle bits differ.
        var historical = AlsP5CoreRuntimeBindingCompiler.CompileHistoricalTurnScaling(
            animationSet, locomotion, pose, p5a, layout);
        RequireIdentity(historical, 0x44403C2869D8F615UL, "frozen P5A v1");
        return historical;
    }

    private static void RequireIdentity(AlsP5CoreRuntimeBindingSnapshot snapshot, ulong graph, string scope)
    {
        if (snapshot.Version != 2 || snapshot.LayoutDigest != 0xF2336240D749284BUL ||
            snapshot.Digest != 0x40F33E59692DFD38UL || snapshot.GraphDigest != graph ||
            snapshot.AnimationSetDefinitionDigest != "152e79130c55ebd7f13cd3efbe40a30c21d52c81af863ab1e1926f2da86b5129")
            throw new ArgumentException($"Cannot replay P5A v1: unsupported {scope} snapshot " +
                $"(version={snapshot.Version}, layout={snapshot.LayoutDigest:x16}, " +
                $"bindings={snapshot.Digest:x16}, graph={snapshot.GraphDigest:x16}).");
    }
}
