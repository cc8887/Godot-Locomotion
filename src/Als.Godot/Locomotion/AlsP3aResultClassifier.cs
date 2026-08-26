using System.Runtime.CompilerServices;

namespace GodotAls.Locomotion;

internal enum AlsP3aResultFailure
{
    Missing,
    Stale,
    Lag,
    GenerationMismatch,
}

internal static class AlsP3aResultClassifier
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static AlsP3aResultFailure Classify(
        int hasPublishedResult,
        long expectedFrameId,
        long publishedFrameId,
        int expectedCharacterId,
        int expectedGeneration,
        int publishedCharacterId,
        int publishedGeneration)
    {
        if (hasPublishedResult == 0)
        {
            return AlsP3aResultFailure.Missing;
        }
        if (publishedFrameId < expectedFrameId)
        {
            return AlsP3aResultFailure.Stale;
        }
        if (publishedFrameId > expectedFrameId)
        {
            return AlsP3aResultFailure.Lag;
        }
        if (publishedCharacterId != expectedCharacterId ||
            publishedGeneration != expectedGeneration)
        {
            return AlsP3aResultFailure.GenerationMismatch;
        }

        return AlsP3aResultFailure.Missing;
    }
}
