using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsP5FailureRecordTests
{
    private const ulong CanonicalDigest = 5198795066917363985UL;

    [Fact]
    public void FailureRecordDigestUsesFrozenRawLittleEndianBytes()
    {
        var record = CanonicalRecord();
        var digest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.AppendFailureRecord(ref digest, in record);

        Assert.Equal(CanonicalDigest, digest);
        Assert.Equal(ReferenceDigest(record), digest);
    }

    [Fact]
    public void FailureRecordDigestIncludesEveryField()
    {
        AssertMutationChanges(static record => record with
        {
            Identity = new AlsFrameIdentity(record.Identity.FrameId + 1, record.Identity.CharacterId, record.Identity.SlotGeneration),
        });
        AssertMutationChanges(static record => record with
        {
            Identity = new AlsFrameIdentity(record.Identity.FrameId, record.Identity.CharacterId + 1, record.Identity.SlotGeneration),
        });
        AssertMutationChanges(static record => record with
        {
            Identity = new AlsFrameIdentity(record.Identity.FrameId, record.Identity.CharacterId, record.Identity.SlotGeneration + 1),
        });
        AssertMutationChanges(static record => record with { Code = AlsP5FailureCode.StalePreparedFrame });
        AssertMutationChanges(static record => record with { LastCommittedResultDigest = record.LastCommittedResultDigest + 1 });
        AssertMutationChanges(static record => record with { AttemptOrdinal = record.AttemptOrdinal + 1 });
    }

    [Fact]
    public void FailureRecordSerializerAcceptsRawNoneAndZeroAttempt()
    {
        var record = new AlsP5FailureRecord(
            new AlsFrameIdentity(0, 0, 1), AlsP5FailureCode.None, 0, 0);
        var digest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.AppendFailureRecord(ref digest, in record);

        Assert.NotEqual(AlsResultDigest.OffsetBasis, digest);
        Assert.Equal(ReferenceDigest(record), digest);
    }

    private static AlsP5FailureRecord CanonicalRecord() => new(
        new AlsFrameIdentity(1234567890123L, 0x89ABCDEFU, 0x10203040U),
        AlsP5FailureCode.EventBufferOverflow,
        0x0123456789ABCDEFUL,
        7);

    private static void AssertMutationChanges(Func<AlsP5FailureRecord, AlsP5FailureRecord> mutate)
    {
        var baseline = CanonicalRecord();
        var changed = mutate(baseline);
        var baselineDigest = AlsResultDigest.OffsetBasis;
        var changedDigest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.AppendFailureRecord(ref baselineDigest, in baseline);
        AlsResultDigest.AppendFailureRecord(ref changedDigest, in changed);
        Assert.NotEqual(baselineDigest, changedDigest);
    }

    private static ulong ReferenceDigest(in AlsP5FailureRecord record)
    {
        var digest = AlsResultDigest.OffsetBasis;
        AppendByte(ref digest, (byte)'P');
        AppendByte(ref digest, (byte)'5');
        AppendByte(ref digest, (byte)'F');
        AppendByte(ref digest, (byte)'1');
        AppendUInt64(ref digest, unchecked((ulong)record.Identity.FrameId));
        AppendUInt32(ref digest, record.Identity.CharacterId);
        AppendUInt32(ref digest, record.Identity.SlotGeneration);
        AppendUInt16(ref digest, (ushort)record.Code);
        AppendUInt64(ref digest, record.LastCommittedResultDigest);
        AppendUInt32(ref digest, record.AttemptOrdinal);
        return digest;
    }

    private static void AppendUInt64(ref ulong digest, ulong value)
    {
        for (var index = 0; index < 8; index++)
        {
            AppendByte(ref digest, (byte)(value >> (index * 8)));
        }
    }

    private static void AppendUInt32(ref ulong digest, uint value)
    {
        for (var index = 0; index < 4; index++)
        {
            AppendByte(ref digest, (byte)(value >> (index * 8)));
        }
    }

    private static void AppendUInt16(ref ulong digest, ushort value)
    {
        AppendByte(ref digest, (byte)value);
        AppendByte(ref digest, (byte)(value >> 8));
    }

    private static void AppendByte(ref ulong digest, byte value)
    {
        digest ^= value;
        digest *= 1099511628211UL;
    }
}
