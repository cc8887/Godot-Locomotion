using System.Buffers.Binary;

namespace GodotAls.Import.Compilation;

public static partial class AlsP5OccurrenceLayoutCompiler
{
    private const int LayoutVersion = 2;
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public static AlsP5OccurrenceLayout Compile(
        AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose,
        AlsP5aAnimationRuntimeProfile p5a)
    {
        ArgumentNullException.ThrowIfNull(locomotion);
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(p5a);
        if (locomotion.StandingWalkRun is not { Length: 0 })
        {
            throw new ArgumentException(
                "The Cycle source graph requires source-aware P5 bindings; the legacy Base22 layout cannot represent it.", nameof(locomotion));
        }
        if (pose.SkeletonId != locomotion.SkeletonId)
        {
            throw new ArgumentException("P3 and P4 profiles use different skeletons.", nameof(pose));
        }

        var baseAnimationIds = BaseAnimationSlots(locomotion);
        if (locomotion.StandingSamples.Length != 13 || locomotion.CrouchingSamples.Length != 4 ||
            pose.Turns.Length != 8 || pose.Rotates.Length != 4 || baseAnimationIds.Length != 22)
        {
            throw new ArgumentException("P3/P4 physical slot topology does not match the frozen P5 layout.");
        }

        var actions = p5a.Actions;
        var segments = p5a.SegmentBindings;
        var entries = new List<AlsP5OccurrenceLayoutEntry>(
            baseAnimationIds.Length + 2 * pose.Turns.Length + 2 * pose.Rotates.Length + 1 +
            actions.Length + segments.Length);

        AddBank(entries, AlsP5OccurrenceSourceKind.Base, baseAnimationIds.Length, 0);
        AddBank(entries, AlsP5OccurrenceSourceKind.Turn, pose.Turns.Length, 0, 2);
        AddBank(entries, AlsP5OccurrenceSourceKind.Rotate, pose.Rotates.Length, 0, 2);
        AddEntry(entries, AlsP5OccurrenceSourceKind.Transition, 0, 0, 1);

        AddActionEntries(entries, p5a, 2);

        var entryArray = entries.ToArray();
        Validate(LayoutVersion, entryArray);
        var mappings = CompileSyncMappings(p5a, baseAnimationIds, entryArray);
        var digest = ComputeDigest(LayoutVersion, entryArray);
        if (digest == 0)
        {
            throw new InvalidOperationException("The P5 occurrence layout digest must be nonzero.");
        }
        return new AlsP5OccurrenceLayout(LayoutVersion, digest, entryArray, mappings);
    }

    private static void AddActionEntries(List<AlsP5OccurrenceLayoutEntry> entries, AlsP5aAnimationRuntimeProfile p5a, int firstAuthority)
    {
        var actions = p5a.Actions; var segments = p5a.SegmentBindings;
        for (var actionIndex = 0; actionIndex < actions.Length; actionIndex++)
        {
            if (actions[actionIndex].DefinitionId != actionIndex)
            {
                throw new ArgumentException("Action definition IDs must be contiguous and ordered.", nameof(p5a));
            }
            AddEntry(entries, AlsP5OccurrenceSourceKind.ActionMontage,
                actionIndex, 0, checked(firstAuthority + actionIndex * 2));
        }

        for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            if ((uint)segment.ActionDefinitionId >= (uint)actions.Length)
            {
                throw new ArgumentException("Action segment has an invalid definition ID.", nameof(p5a));
            }
            AddEntry(entries, AlsP5OccurrenceSourceKind.ActionSequence,
                segmentIndex, 0, checked(firstAuthority + 1 + segment.ActionDefinitionId * 2));
        }
    }

    public static void Validate(int version, IReadOnlyList<AlsP5OccurrenceLayoutEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (version != LayoutVersion && version != GodotAls.Core.Contracts.AlsP5OccurrenceLayoutContract.SourceGraphVersion)
        {
            throw new ArgumentException("Unsupported P5 occurrence layout version.", nameof(version));
        }
        if (entries.Count == 0)
        {
            throw new ArgumentException("P5 occurrence layout must not be empty.", nameof(entries));
        }

        var keys = new HashSet<(AlsP5OccurrenceSourceKind Kind, int Binding, int Slot)>();
        var handles = new HashSet<int>();
        var authorities = new HashSet<int>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!Enum.IsDefined(entry.SourceKind) || version == LayoutVersion && entry.SourceKind > AlsP5OccurrenceSourceKind.ActionSequence || entry.SourceBindingIndex < 0 ||
                entry.GraphSlotIndex < 0 || entry.OccurrenceHandleId < 0 ||
                entry.AuthorityGroupId < 0)
            {
                throw new ArgumentException("P5 occurrence layout contains an invalid field.", nameof(entries));
            }
            if (!keys.Add((entry.SourceKind, entry.SourceBindingIndex, entry.GraphSlotIndex)) ||
                !handles.Add(entry.OccurrenceHandleId))
            {
                throw new ArgumentException("P5 occurrence layout contains duplicate identity.", nameof(entries));
            }
            if (entry.OccurrenceHandleId != index)
            {
                throw new ArgumentException("P5 occurrence handles must be contiguous and ordered.", nameof(entries));
            }
            authorities.Add(entry.AuthorityGroupId);
        }

        if (!authorities.Order().SequenceEqual(Enumerable.Range(0, authorities.Count)))
        {
            throw new ArgumentException("P5 authority groups must be globally dense.", nameof(entries));
        }
        if (version != LayoutVersion && !entries.Any(e => e.SourceKind is AlsP5OccurrenceSourceKind.SourceSample or AlsP5OccurrenceSourceKind.SourceEvaluator))
            throw new ArgumentException("Source-aware layout has no source occurrences.", nameof(entries));
    }

    private static int[] BaseAnimationSlots(AlsLocomotionAnimationProfile locomotion) =>
    [
        locomotion.StandingIdleAnimationId,
        .. locomotion.StandingSamples.Select(value => value.AnimationId),
        locomotion.CrouchingIdleAnimationId,
        .. locomotion.CrouchingSamples.Select(value => value.AnimationId),
        locomotion.JumpStartAnimationId,
        locomotion.FallLoopAnimationId,
        locomotion.LandAnimationId,
    ];

    private static void AddBank(
        List<AlsP5OccurrenceLayoutEntry> entries,
        AlsP5OccurrenceSourceKind kind,
        int count,
        int authorityGroupId,
        int bankCount = 1)
    {
        for (var bank = 0; bank < bankCount; bank++)
        {
            for (var index = 0; index < count; index++)
            {
                AddEntry(entries, kind, index, bank * count + index, authorityGroupId);
            }
        }
    }

    private static void AddEntry(
        List<AlsP5OccurrenceLayoutEntry> entries,
        AlsP5OccurrenceSourceKind kind,
        int sourceBindingIndex,
        int graphSlotIndex,
        int authorityGroupId) =>
        entries.Add(new AlsP5OccurrenceLayoutEntry(
            kind, sourceBindingIndex, graphSlotIndex, entries.Count, authorityGroupId));

    private static AlsP5SyncOccurrenceMapping[] CompileSyncMappings(
        AlsP5aAnimationRuntimeProfile p5a,
        int[] baseAnimationIds,
        AlsP5OccurrenceLayoutEntry[] entries)
    {
        var result = new List<AlsP5SyncOccurrenceMapping>();
        foreach (var group in p5a.SyncGroups.OrderBy(value => value.GroupId))
        {
            foreach (var member in group.Members.OrderBy(value => value.GroupMemberIndex))
            {
                var sourceSlots = baseAnimationIds
                    .Select((animationId, sourceBindingIndex) => (animationId, sourceBindingIndex))
                    .Where(value => value.animationId == member.AnimationId)
                    .ToArray();
                if (sourceSlots.Length != 1)
                {
                    throw new ArgumentException(
                        "Every declared Sync member must resolve to exactly one physical Base slot.", nameof(p5a));
                }
                var handle = entries.Single(value =>
                    value.SourceKind == AlsP5OccurrenceSourceKind.Base &&
                    value.SourceBindingIndex == sourceSlots[0].sourceBindingIndex).OccurrenceHandleId;
                result.Add(new AlsP5SyncOccurrenceMapping(
                    group.GroupId, member.GroupMemberIndex, member.AnimationId, handle));
            }
        }
        return result.ToArray();
    }

    internal static ulong ComputeDigest(int version, IReadOnlyList<AlsP5OccurrenceLayoutEntry> entries)
    {
        var hash = FnvOffset;
        Add(version);
        Add(entries.Count);
        foreach (var entry in entries)
        {
            AddByte((byte)entry.SourceKind);
            Add(entry.SourceBindingIndex);
            Add(entry.GraphSlotIndex);
            Add(entry.OccurrenceHandleId);
            Add(entry.AuthorityGroupId);
        }
        return hash;

        void Add(int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            foreach (var item in bytes)
            {
                hash ^= item;
                hash *= FnvPrime;
            }
        }

        void AddByte(byte value)
        {
            hash ^= value;
            hash *= FnvPrime;
        }
    }
}
