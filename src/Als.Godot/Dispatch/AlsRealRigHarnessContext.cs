using Godot;
using GodotAls.Core.Exchange;
using GodotAls.Import.Compilation;

namespace GodotAls.Dispatch;

public sealed class AlsRealRigHarnessContext
{
    public const ulong OffsetBasis = 14695981039346656037UL;

    public AlsRealRigHarnessContext(
        AlsHarnessMode mode,
        int characterCount,
        int targetFrames,
        int mainManagedThreadId,
        PackedScene mannequinScene,
        PackedScene walkScene,
        AlsAnimationDefinition walkClip,
        AlsSkeletonDefinition skeletonDefinition)
    {
        Mode = mode;
        Entries = new AlsRealRigHarnessEntry[characterCount];
        Registry = new AlsSlotRegistry(characterCount);
        TargetFrames = targetFrames;
        MainManagedThreadId = mainManagedThreadId;
        MannequinScene = mannequinScene;
        WalkScene = walkScene;
        WalkClip = walkClip;
        SkeletonDefinition = skeletonDefinition;
    }

    public AlsHarnessMode Mode { get; }

    public AlsRealRigHarnessEntry[] Entries { get; }

    public AlsSlotRegistry Registry { get; }

    public int TargetFrames { get; }

    public int MainManagedThreadId { get; }

    public PackedScene MannequinScene { get; }

    public PackedScene WalkScene { get; }

    public AlsAnimationDefinition WalkClip { get; }

    public AlsSkeletonDefinition SkeletonDefinition { get; }

    public long PublishedFrameId;

    public ulong Digest = OffsetBasis;

    public long MissingResults;

    public long StaleResults;

    public long EventOccurrences;

    public long Replacements;
}

public sealed class AlsRealRigHarnessEntry
{
    public AlsRealRigHarnessEntry(AlsSlotHandle handle, AlsRealRigExchange exchange)
    {
        Handle = handle;
        Exchange = exchange;
    }

    public AlsSlotHandle Handle { get; }

    public AlsRealRigExchange Exchange { get; }

    public AlsRealRigWorkerRoot Worker { get; set; } = null!;

    public int ObservedOffMainThread;
}
