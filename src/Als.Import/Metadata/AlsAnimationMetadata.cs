using System.Text.Json;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Metadata;

public sealed record AlsAnimationMetadata(
    bool Overlay,
    bool Prop,
    float PlayLength,
    int FrameRateNumerator,
    int FrameRateDenominator,
    int SampledKeyCount,
    bool Loop,
    int Interpolation,
    bool RootMotionEnabled,
    int RootMotionRootLock,
    bool ForceRootLock,
    bool UseNormalizedRootMotionScale,
    int AdditiveType,
    int AdditiveBasePoseType,
    int AdditiveBasePoseFrame,
    string AdditiveBasePoseObjectPath,
    string AdditiveBasePoseId,
    string SkeletonId,
    string SkeletonObjectPath,
    string[] Curves,
    AlsAnimationNotifyMetadata[] Notifies,
    AlsAnimationSyncMarkerMetadata[] SyncMarkers)
{
    public static AlsAnimationMetadata Read(JsonElement element) =>
        element.Deserialize<AlsAnimationMetadata>(AlsManifestSerializer.JsonOptions)
        ?? throw new JsonException("Animation metadata deserialized to null.");
}

public sealed record AlsAnimationNotifyMetadata(string Name, float Time, float Duration, int SourceIndex);

public sealed record AlsAnimationSyncMarkerMetadata(string Name, float Time);
