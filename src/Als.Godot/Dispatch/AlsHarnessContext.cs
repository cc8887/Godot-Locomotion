using GodotAls.Core.Diagnostics;
using GodotAls.Core.Exchange;

namespace GodotAls.Dispatch;

public sealed class AlsHarnessContext
{
    public AlsHarnessContext(
        AlsHarnessMode mode,
        int characterCount,
        int targetFrames,
        int mainManagedThreadId)
    {
        Mode = mode;
        Entries = new AlsHarnessEntry[characterCount];
        Registry = new AlsSlotRegistry(characterCount);
        TargetFrames = targetFrames;
        MainManagedThreadId = mainManagedThreadId;
    }

    public AlsHarnessMode Mode { get; }

    public AlsHarnessEntry[] Entries { get; }

    public AlsSlotRegistry Registry { get; }

    public int TargetFrames { get; }

    public int WarmupFrames { get; } = 20;

    public int MainManagedThreadId { get; }

    public long PublishedFrameId;

    public ulong Digest = AlsResultDigest.OffsetBasis;

    public long MissingResults;

    public long Replacements;

    public long SteadyStateAllocations;
}
