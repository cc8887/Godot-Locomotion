namespace GodotAls.Core.Sync;

/// <summary>SequencePlayer/BlendSpacePlayer initialization with the imported default rate clamp.
/// This computes an initial position only; callers own epochs, cached weights, filters and history.</summary>
public static class AlsAssetSourceInitialization
{
    public static float Time(AlsAssetSyncKind kind, float startPosition, float duration,
        float playRate, float playRateBasis = 1, float assetRateScale = 1)
    {
        if (!Enum.IsDefined(kind) || !float.IsFinite(startPosition) || !float.IsFinite(duration) || duration <= 0 ||
            !float.IsFinite(playRate) || !float.IsFinite(playRateBasis) || !float.IsFinite(assetRateScale))
            throw new ArgumentException("Invalid source initialization input.");
        var length = kind == AlsAssetSyncKind.BlendSpace ? 1 : duration;
        var adjusted = kind == AlsAssetSyncKind.BlendSpace ? playRate :
            (MathF.Abs(playRateBasis) <= 1e-8f ? 0 : playRate / playRateBasis) * assetRateScale;
        if (!float.IsFinite(adjusted)) throw new ArgumentException("Nonfinite source initialization rate.");
        // UE tests the un-clamped start, so a negative start clamped to zero is not a reverse start.
        return startPosition == 0 && adjusted < 0 ? length : System.Math.Clamp(startPosition, 0, length);
    }

    public static long NextEpoch(long previous)
    {
        if (previous < 0 || previous == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(previous));
        return previous + 1;
    }
}
