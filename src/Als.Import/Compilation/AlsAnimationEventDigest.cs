using System.Text;

namespace GodotAls.Import.Compilation;

public static class AlsAnimationEventDigest
{
    public const ulong OffsetBasis = 14695981039346656037UL;

    private const ulong Prime = 1099511628211UL;

    public static int Advance(
        AlsAnimationDefinition clip,
        double previousTime,
        double currentTime,
        ref ulong digest)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (currentTime < previousTime)
        {
            throw new ArgumentOutOfRangeException(nameof(currentTime), "Animation time cannot move backwards.");
        }
        if (clip.PlayLength <= 0f || currentTime == previousTime)
        {
            return 0;
        }

        var events = clip.Timeline
            .Select(value => new TimelineEvent(
                value.TimeSeconds, 0, value.SourceIndex, value.EventId, value.DisplayName))
            .Concat(clip.SyncMarkers.Select(value =>
                new TimelineEvent(
                    value.TimeSeconds, 1, value.SourceIndex, value.MarkerId, value.Name)))
            .OrderBy(value => value.Time)
            .ThenBy(value => value.Kind)
            .ThenBy(value => value.SourceIndex)
            .ToArray();
        var count = 0;
        var finalTime = clip.Loop ? currentTime : Math.Min(currentTime, clip.PlayLength);
        var firstCycle = clip.Loop ? Math.Max(0L, (long)Math.Floor(previousTime / clip.PlayLength)) : 0L;
        var lastCycle = clip.Loop ? Math.Max(0L, (long)Math.Floor(finalTime / clip.PlayLength)) : 0L;
        for (var cycle = firstCycle; cycle <= lastCycle; cycle++)
        {
            foreach (var value in events)
            {
                var occurrence = cycle * clip.PlayLength + value.Time;
                if (occurrence <= previousTime + 1e-9 || occurrence > finalTime + 1e-9)
                {
                    continue;
                }

                Append(ref digest, value.Kind);
                Append(ref digest, cycle);
                Append(ref digest, value.SourceIndex);
                Append(ref digest, value.CompiledId);
                Append(ref digest, (long)Math.Round(value.Time * 1_000_000.0, MidpointRounding.ToEven));
                foreach (var character in Encoding.UTF8.GetBytes(value.Name))
                {
                    digest ^= character;
                    digest *= Prime;
                }
                count++;
            }
        }

        return count;
    }

    private static void Append(ref ulong digest, long value) => Append(ref digest, unchecked((ulong)value));

    private static void Append(ref ulong digest, ulong value)
    {
        for (var shift = 0; shift < 64; shift += 8)
        {
            digest ^= (byte)(value >> shift);
            digest *= Prime;
        }
    }

    private readonly record struct TimelineEvent(
        float Time,
        int Kind,
        int SourceIndex,
        int CompiledId,
        string Name);
}
