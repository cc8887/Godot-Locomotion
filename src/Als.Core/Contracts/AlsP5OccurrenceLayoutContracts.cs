using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsP5OccurrenceLayoutEntry(
    AlsP5OccurrenceSourceKind SourceKind,
    int SourceBindingIndex,
    int GraphSlotIndex,
    int OccurrenceHandleId,
    int AuthorityGroupId);

public readonly ref struct AlsP5OccurrenceLayoutView
{
    public readonly int Version;
    public readonly ulong Digest;
    public readonly ReadOnlySpan<AlsP5OccurrenceLayoutEntry> Entries;

    public AlsP5OccurrenceLayoutView(
        int version,
        ulong digest,
        ReadOnlySpan<AlsP5OccurrenceLayoutEntry> entries)
    {
        Version = version;
        Digest = digest;
        Entries = entries;
    }
}

public static class AlsP5OccurrenceLayoutContract
{
    public const int CurrentVersion = 2;
    public const int SourceGraphVersion = 3;

    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    public static void Validate(
        int version,
        ulong digest,
        ReadOnlySpan<AlsP5OccurrenceLayoutEntry> entries)
    {
        if (version != CurrentVersion && version != SourceGraphVersion || digest == 0 || entries.IsEmpty)
        {
            throw new ArgumentException("The P5 occurrence layout header is invalid.");
        }

        var maxAuthority = -1;
        var hasSource = false;
        for (var index = 0; index < entries.Length; index++)
        {
            ref readonly var entry = ref entries[index];
            if (entry.SourceKind < AlsP5OccurrenceSourceKind.Base || entry.SourceKind >
                    (version == SourceGraphVersion ? AlsP5OccurrenceSourceKind.SourceEvaluator : AlsP5OccurrenceSourceKind.ActionSequence) ||
                entry.SourceBindingIndex < 0 ||
                entry.GraphSlotIndex < 0 ||
                entry.OccurrenceHandleId < 0 ||
                entry.AuthorityGroupId < 0 ||
                entry.OccurrenceHandleId != index ||
                entry.AuthorityGroupId >= entries.Length ||
                (entry.SourceKind is AlsP5OccurrenceSourceKind.Transition or
                    AlsP5OccurrenceSourceKind.ActionMontage or
                    AlsP5OccurrenceSourceKind.ActionSequence) && entry.GraphSlotIndex != 0)
            {
                throw new ArgumentException("The P5 occurrence layout entry is invalid.");
            }
            hasSource |= entry.SourceKind is AlsP5OccurrenceSourceKind.SourceSample or AlsP5OccurrenceSourceKind.SourceEvaluator;

            if (entry.AuthorityGroupId > maxAuthority)
            {
                maxAuthority = entry.AuthorityGroupId;
            }

            for (var previousIndex = 0; previousIndex < index; previousIndex++)
            {
                ref readonly var previous = ref entries[previousIndex];
                if ((previous.SourceKind == entry.SourceKind &&
                        previous.SourceBindingIndex == entry.SourceBindingIndex &&
                        previous.GraphSlotIndex == entry.GraphSlotIndex) ||
                    previous.OccurrenceHandleId == entry.OccurrenceHandleId)
                {
                    throw new ArgumentException("The P5 occurrence layout contains a duplicate identity.");
                }
            }
        }

        for (var authority = 0; authority <= maxAuthority; authority++)
        {
            var found = false;
            for (var index = 0; index < entries.Length; index++)
            {
                if (entries[index].AuthorityGroupId == authority)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                throw new ArgumentException("The P5 occurrence layout authority IDs are sparse.");
            }
        }

        if (version == SourceGraphVersion && !hasSource || ComputeDigest(version, entries) != digest)
        {
            throw new ArgumentException("The P5 occurrence layout digest is stale.");
        }
    }

    private static ulong ComputeDigest(
        int version,
        ReadOnlySpan<AlsP5OccurrenceLayoutEntry> entries)
    {
        var digest = OffsetBasis;
        Append(ref digest, version);
        Append(ref digest, entries.Length);

        foreach (ref readonly var entry in entries)
        {
            Append(ref digest, (byte)entry.SourceKind);
            Append(ref digest, entry.SourceBindingIndex);
            Append(ref digest, entry.GraphSlotIndex);
            Append(ref digest, entry.OccurrenceHandleId);
            Append(ref digest, entry.AuthorityGroupId);
        }

        return digest;
    }

    private static void Append(ref ulong digest, int value)
    {
        var unsigned = unchecked((uint)value);
        Append(ref digest, (byte)unsigned);
        Append(ref digest, (byte)(unsigned >> 8));
        Append(ref digest, (byte)(unsigned >> 16));
        Append(ref digest, (byte)(unsigned >> 24));
    }

    private static void Append(ref ulong digest, byte value)
    {
        digest ^= value;
        digest *= Prime;
    }
}
