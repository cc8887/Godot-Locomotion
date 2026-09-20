namespace GodotAls.Import.Compilation;

public enum AlsP5OccurrenceSourceKind : byte
{
    Base = 1,
    Turn = 2,
    Rotate = 3,
    Transition = 4,
    ActionMontage = 5,
    ActionSequence = 6,
    SourceSample = 7,
    SourceEvaluator = 8,
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
    private GodotAls.Core.Contracts.AlsP5OccurrenceLayoutEntry[] _coreEntries = ToCore(Entries);
    private GodotAls.Core.Contracts.AlsP5SourceOccurrenceMapping[] _sourceMappings = [];
    private int[] _unboundNativeSources = [];

    public GodotAls.Core.Contracts.AlsLocomotionSourceStamp SourceStamp { get; init; }
    public GodotAls.Core.Contracts.AlsP5SourceOccurrenceMapping[] SourceMappings
    {
        get => _sourceMappings.ToArray();
        init => _sourceMappings = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
    public int[] UnboundNativeSourceIndices
    {
        get => _unboundNativeSources.ToArray();
        init => _unboundNativeSources = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public GodotAls.Core.Contracts.AlsP5OccurrenceLayoutView CreateCoreView() => new(Version, Digest, _coreEntries);
    public GodotAls.Core.Contracts.AlsP5SourceOccurrenceView CreateSourceView() => new(
        Version, Digest, SourceStamp, _coreEntries, _sourceMappings);

    public AlsP5OccurrenceLayoutEntry[] Entries
    {
        get => _entries.ToArray();
        init
        {
            _entries = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
            _coreEntries = ToCore(_entries);
        }
    }

    public AlsP5SyncOccurrenceMapping[] SyncMappings
    {
        get => _syncMappings.ToArray();
        init => _syncMappings = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    private static GodotAls.Core.Contracts.AlsP5OccurrenceLayoutEntry[] ToCore(AlsP5OccurrenceLayoutEntry[] entries) =>
        entries.Select(e => new GodotAls.Core.Contracts.AlsP5OccurrenceLayoutEntry(
            (GodotAls.Core.Contracts.AlsP5OccurrenceSourceKind)e.SourceKind, e.SourceBindingIndex,
            e.GraphSlotIndex, e.OccurrenceHandleId, e.AuthorityGroupId)).ToArray();
}
