using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal static class LyraPoseBuffers
{
    // Each layer reads a complete immutable input and writes a distinct output.
    // Reject overlap even on bypass paths so a caller cannot depend on weights
    // to make an otherwise invalid graph safe.
    public static void Validate(ReadOnlySpan<AlsLocalPose> input, Span<AlsLocalPose> output, int bones)
    {
        if (bones == 0 || input.Length != bones || output.Length != bones || input.Overlaps(output))
            throw new ArgumentException("Lyra layers require complete, separate pose buffers.");
    }
    public static void Validate(ReadOnlySpan<AlsPrecisePose> input, Span<AlsPrecisePose> output, int bones = 81)
    {
        if (bones == 0 || input.Length != bones || output.Length != bones || input.Overlaps(output))
            throw new ArgumentException("Lyra logical layers require complete, separate pose buffers.");
    }
}
