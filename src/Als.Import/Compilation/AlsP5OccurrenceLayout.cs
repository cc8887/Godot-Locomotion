namespace GodotAls.Import.Compilation;

public enum AlsP5OccurrenceSourceKind : byte
{
    Base = 1,
    Turn = 2,
    Rotate = 3,
    Transition = 4,
    ActionMontage = 5,
    ActionSequence = 6,
}

public readonly record struct AlsP5OccurrenceLayoutEntry(
    AlsP5OccurrenceSourceKind SourceKind,
    int SourceBindingIndex,
    int GraphSlotIndex,
    int OccurrenceHandleId,
    int AuthorityGroupId);

public readonly record struct AlsP5SyncOccurrenceMapping(
    int SyncGroupId,
    int GroupMemberIndex,
    int AnimationId,
    int OccurrenceHandleId);

public sealed record AlsP5OccurrenceLayout(
    int Version,
    ulong Digest,
    AlsP5OccurrenceLayoutEntry[] Entries,
    AlsP5SyncOccurrenceMapping[] SyncMappings)
{
    private AlsP5OccurrenceLayoutEntry[] _entries = Entries.ToArray();
    private AlsP5SyncOccurrenceMapping[] _syncMappings = SyncMappings.ToArray();

    public AlsP5OccurrenceLayoutEntry[] Entries
    {
        get => _entries.ToArray();
        init => _entries = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsP5SyncOccurrenceMapping[] SyncMappings
    {
        get => _syncMappings.ToArray();
        init => _syncMappings = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}
