using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

public sealed class AlsSyncRuntimeTests
{
    [Fact]
    public void ContractsFreezeLayoutUnmanagedStatusAndFieldOrder()
    {
        AssertContract<AlsSyncMarkerDefinition>();
        AssertContract<AlsSyncGroupBinding>();
        AssertContract<AlsSyncMemberBinding>();
        AssertContract<AlsSyncPlayback>();
        AssertContract<AlsSyncMappedPlayback>();
        AssertFields<AlsSyncMarkerDefinition>(("MarkerId", typeof(int)), ("MarkerNameId", typeof(int)), ("AnimationId", typeof(int)), ("SourceIndex", typeof(int)), ("TrackIndex", typeof(int)), ("TimeSeconds", typeof(float)));
        AssertFields<AlsSyncGroupBinding>(("GroupId", typeof(int)), ("MemberOffset", typeof(int)), ("MemberCount", typeof(int)), ("LeftMarkerNameId", typeof(int)), ("RightMarkerNameId", typeof(int)));
        AssertFields<AlsSyncMemberBinding>(("GroupId", typeof(int)), ("AnimationId", typeof(int)), ("DurationSeconds", typeof(float)), ("Loop", typeof(byte)), ("CanLead", typeof(byte)));
        AssertFields<AlsSyncPlayback>(("OccurrenceHandleId", typeof(int)), ("AnimationId", typeof(int)), ("PlaybackEpoch", typeof(long)), ("PreviousUnwrappedTimeSeconds", typeof(double)), ("CurrentUnwrappedTimeSeconds", typeof(double)), ("Weight", typeof(float)));
        AssertFields<AlsSyncMappedPlayback>(("OccurrenceHandleId", typeof(int)), ("AnimationId", typeof(int)), ("PlaybackEpoch", typeof(long)), ("DurationSeconds", typeof(float)), ("PreviousCycle", typeof(long)), ("CurrentCycle", typeof(long)), ("PreviousTimeSeconds", typeof(float)), ("CurrentTimeSeconds", typeof(float)), ("MappedPlayRate", typeof(float)));
    }

    [Fact]
    public void SelectsMaximumWeightLeaderAndWritesAllOccurrencesInFullKeyOrder()
    {
        var members = Members();
        var playbacks = new[]
        {
            Playback(9, 20, 4, 0.1, 0.2, 0.5f),
            Playback(4, 10, 3, 0.2, 0.3, 0.75f),
            Playback(2, 20, 2, 0.1, 0.2, 1f),
        };
        var mapped = new AlsSyncMappedPlayback[3];

        var result = Evaluate(Markers(), Group(), members, playbacks, 0.1, mapped, out var count, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(3, count);
        Assert.Equal(2, result.LeaderOccurrenceHandleId);
        Assert.Equal(20, result.LeaderAnimationId);
        Assert.Equal(2L, result.LeaderPlaybackEpoch);
        Assert.Equal(new[] { 2, 4, 9 }, mapped.Select(static item => item.OccurrenceHandleId));
    }

    [Fact]
    public void LeaderTieBreaksByAnimationThenEpochThenOccurrenceHandle()
    {
        var members = Members();
        var cases = new[]
        {
            (new[] { Playback(8, 20, 4, 0.2, 0.3, 1f), Playback(1, 10, 8, 0.2, 0.3, 1f) }, 1),
            (new[] { Playback(8, 10, 4, 0.2, 0.3, 1f), Playback(1, 10, 8, 0.2, 0.3, 1f) }, 8),
            (new[] { Playback(8, 10, 4, 0.2, 0.3, 1f), Playback(1, 10, 4, 0.2, 0.3, 1f) }, 1),
        };

        foreach (var item in cases)
        {
            var mapped = new AlsSyncMappedPlayback[2];
            var result = Evaluate(Markers(), Group(), members, item.Item1, 0.1, mapped, out var count, out var failure);
            Assert.Equal(AlsP5FailureCode.None, failure);
            Assert.Equal(2, count);
            Assert.Equal(item.Item2, result.LeaderOccurrenceHandleId);
            Assert.Equal(10, result.LeaderAnimationId);
        }
    }

    [Fact]
    public void MapsLeaderAndFollowerAtExactPreviousAndCurrentTimesAcrossDifferentDurations()
    {
        var mapped = new AlsSyncMappedPlayback[2];
        var result = Evaluate(Markers(), Group(), Members(),
            [Playback(5, 10, 1, (double)0.2f, (double)0.6f, 2f), Playback(7, 20, 1, 0.3, 0.7, 1f)],
            0.4, mapped, out var count, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(2, count);
        Assert.Equal(102, result.PreviousMarkerId);
        Assert.Equal(101, result.NextMarkerId);
        Assert.Equal(0L, result.Cycle);
        AssertPositiveZero(result.Phase);
        AssertMapping(mapped[0], 5, 10, 1, 1f, 0, 0, 0.2f, 0.6f, 1f);
        AssertMapping(mapped[1], 7, 20, 1, 2f, 0, 1, 1.4f, 0.6f, 3f);
    }

    [Fact]
    public void SupportsBothCanonicalMarkerOrdersAndSemanticFootPhase()
    {
        var leftFirst = new AlsSyncMappedPlayback[1];
        var leftResult = Evaluate(Markers(), Group(), Members(), [Playback(1, 10, 1, (double)0.2f, 0.4, 1f)], 0.2, leftFirst, out _, out var leftFailure);
        Assert.Equal(AlsP5FailureCode.None, leftFailure);
        Assert.Equal(101, leftResult.PreviousMarkerId);
        Assert.Equal(102, leftResult.NextMarkerId);
        AssertClose(0.5f, leftResult.LeftFootPhase);
        AssertClose(0.5f, leftResult.RightFootPhase);

        var rightFirst = new AlsSyncMappedPlayback[1];
        var rightResult = Evaluate(Markers(), Group(), Members(), [Playback(1, 20, 1, (double)0.6f, 1.0, 1f)], 0.4, rightFirst, out _, out var rightFailure);
        Assert.Equal(AlsP5FailureCode.None, rightFailure);
        Assert.Equal(202, rightResult.PreviousMarkerId);
        Assert.Equal(201, rightResult.NextMarkerId);
        AssertClose(0.5f, rightResult.LeftFootPhase);
        AssertClose(0.5f, rightResult.RightFootPhase);
    }

    [Fact]
    public void OppositeOrderFollowerNeverJumpsBackwardAcrossLeftOrRightMarkers()
    {
        var atLeft = new AlsSyncMappedPlayback[2];
        var left = Evaluate(Markers(), Group(), Members(), [Playback(1, 10, 1, 0.1, (double)0.2f, 1f), Playback(2, 20, 1, 0.1, (double)0.2f, 0.5f)], 0.1, atLeft, out _, out var leftFailure);
        Assert.Equal(AlsP5FailureCode.None, leftFailure);
        Assert.Equal(101, left.PreviousMarkerId);
        AssertPositiveZero(left.Phase);
        AssertClose(0f, left.LeftFootPhase);
        AssertClose(1f, left.RightFootPhase);
        AssertClose(1.4f, atLeft[1].CurrentTimeSeconds);

        var atRight = new AlsSyncMappedPlayback[2];
        var right = Evaluate(Markers(), Group(), Members(), [Playback(1, 10, 1, 0.5, (double)0.6f, 1f), Playback(2, 20, 1, 0.5, (double)0.6f, 0.5f)], 0.1, atRight, out _, out var rightFailure);
        Assert.Equal(AlsP5FailureCode.None, rightFailure);
        Assert.Equal(102, right.PreviousMarkerId);
        AssertPositiveZero(right.Phase);
        AssertClose(1f, right.LeftFootPhase);
        AssertClose(0f, right.RightFootPhase);
        AssertClose(0.6f, atRight[1].CurrentTimeSeconds);
        Assert.Equal(1L, atRight[1].CurrentCycle);
        Assert.True(atRight[1].CurrentCycle * 2d + atRight[1].CurrentTimeSeconds > atLeft[1].CurrentCycle * 2d + atLeft[1].CurrentTimeSeconds);
    }

    [Fact]
    public void MapsBeforeEarlyAndAfterLateThroughLoopCycles()
    {
        var mapped = new AlsSyncMappedPlayback[2];
        var result = Evaluate(Markers(), Group(), Members(),
            [Playback(4, 10, 1, 1.8, 2d + (double)0.2f, 1f), Playback(5, 20, 1, 1.8, 2d + (double)0.2f, 0.5f)],
            0.4, mapped, out _, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(101, result.PreviousMarkerId);
        Assert.Equal(102, result.NextMarkerId);
        Assert.Equal(2L, result.Cycle);
        AssertPositiveZero(result.Phase);
        AssertMapping(mapped[0], 4, 10, 1, 1f, 1, 2, 0.8f, 0.2f, 1f);
        AssertMapping(mapped[1], 5, 20, 1, 2f, 2, 2, 0.8666667f, 1.4f, 1.3333334f);
    }

    [Theory]
    [InlineData(0.2f, 0.6f, 102)]
    [InlineData(0.6f, 0.2f, 101)]
    public void AdjacentDoubleBelowEarlyMarkerStaysInThePreviousHalfOpenInterval(float leftTime, float rightTime, int expectedPreviousMarkerId)
    {
        var markers = new[]
        {
            new AlsSyncMarkerDefinition(101, 11, 10, 0, 0, leftTime),
            new AlsSyncMarkerDefinition(102, 12, 10, 1, 0, rightTime),
        };
        var local = System.Math.BitDecrement((double)System.Math.Min(leftTime, rightTime));
        var mapped = new AlsSyncMappedPlayback[1];

        var result = Evaluate(markers, new AlsSyncGroupBinding(1, 0, 1, 11, 12), [new AlsSyncMemberBinding(1, 10, 1f, 1, 1)], [Playback(1, 10, 1, local, local, 1f)], 0.1, mapped, out _, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(expectedPreviousMarkerId, result.PreviousMarkerId);
        Assert.True(result.Phase > 0f);
    }

    [Theory]
    [InlineData(0.2f, 0.6f, 102)]
    [InlineData(0.6f, 0.2f, 101)]
    public void AdjacentDoubleBelowDurationDoesNotBecomeTheNextCycle(float leftTime, float rightTime, int expectedPreviousMarkerId)
    {
        var markers = new[]
        {
            new AlsSyncMarkerDefinition(101, 11, 10, 0, 0, leftTime),
            new AlsSyncMarkerDefinition(102, 12, 10, 1, 0, rightTime),
        };
        var local = System.Math.BitDecrement(1d);
        var mapped = new AlsSyncMappedPlayback[1];

        var result = Evaluate(markers, new AlsSyncGroupBinding(1, 0, 1, 11, 12), [new AlsSyncMemberBinding(1, 10, 1f, 1, 1)], [Playback(1, 10, 1, local, local, 1f)], 0.1, mapped, out _, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(0L, result.Cycle);
        Assert.Equal(expectedPreviousMarkerId, result.PreviousMarkerId);
        Assert.Equal(BitConverter.SingleToInt32Bits(System.MathF.BitDecrement(1f)), BitConverter.SingleToInt32Bits(mapped[0].CurrentTimeSeconds));
        Assert.True(mapped[0].CurrentTimeSeconds < 1f);
    }

    [Fact]
    public void NarrowedLeftToRightInteriorPhaseCannotRoundUpToItsRightMarker()
    {
        var left = System.MathF.BitDecrement(0.2f);
        const float right = 0.2f;
        var markers = new[]
        {
            new AlsSyncMarkerDefinition(101, 11, 10, 0, 0, left),
            new AlsSyncMarkerDefinition(102, 12, 10, 1, 0, right),
        };
        var time = System.Math.BitDecrement((double)right);
        var mapped = new AlsSyncMappedPlayback[1];

        var result = Evaluate(markers, new AlsSyncGroupBinding(1, 0, 1, 11, 12), [new AlsSyncMemberBinding(1, 10, 1f, 1, 1)], [Playback(1, 10, 1, time, time, 1f)], 0.1, mapped, out _, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(101, result.PreviousMarkerId);
        Assert.Equal(102, result.NextMarkerId);
        Assert.True(result.Phase > 0f);
        Assert.Equal(BitConverter.SingleToInt32Bits(left), BitConverter.SingleToInt32Bits(mapped[0].CurrentTimeSeconds));
        Assert.True(mapped[0].CurrentTimeSeconds < right);
    }

    [Fact]
    public void NarrowedRightToLeftInteriorPhaseCannotRoundUpToItsLeftMarker()
    {
        const float left = 0.2f;
        var right = System.MathF.BitDecrement(left);
        var markers = new[]
        {
            new AlsSyncMarkerDefinition(101, 11, 10, 0, 0, left),
            new AlsSyncMarkerDefinition(102, 12, 10, 1, 0, right),
        };
        var time = System.Math.BitDecrement((double)left);
        var mapped = new AlsSyncMappedPlayback[1];

        var result = Evaluate(markers, new AlsSyncGroupBinding(1, 0, 1, 11, 12), [new AlsSyncMemberBinding(1, 10, 1f, 1, 1)], [Playback(1, 10, 1, time, time, 1f)], 0.1, mapped, out _, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(102, result.PreviousMarkerId);
        Assert.Equal(101, result.NextMarkerId);
        Assert.True(result.Phase > 0f);
        Assert.Equal(BitConverter.SingleToInt32Bits(right), BitConverter.SingleToInt32Bits(mapped[0].CurrentTimeSeconds));
        Assert.True(mapped[0].CurrentTimeSeconds < left);
    }

    [Fact]
    public void LargeCycleFollowerKeepsEpsilonMarkerDeltaAndMappedRate()
    {
        const long cycle = 1L << 50;
        var markers = new[]
        {
            new AlsSyncMarkerDefinition(101, 11, 10, 0, 0, 0f),
            new AlsSyncMarkerDefinition(102, 12, 10, 1, 0, 0.5f),
            new AlsSyncMarkerDefinition(201, 11, 20, 0, 0, 0f),
            new AlsSyncMarkerDefinition(202, 12, 20, 1, 0, float.Epsilon),
        };
        var members = new[] { new AlsSyncMemberBinding(1, 10, 1f, 1, 1), new AlsSyncMemberBinding(1, 20, 1f, 1, 1) };
        var mapped = new AlsSyncMappedPlayback[2];

        _ = Evaluate(markers, Group(), members, [Playback(1, 10, 1, cycle, cycle + 0.5d, 1f), Playback(2, 20, 1, cycle, cycle + 0.5d, 0.5f)], 0.5d, mapped, out var count, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(2, count);
        AssertMapping(mapped[0], 1, 10, 1, 1f, cycle, cycle, 0f, 0.5f, 1f);
        AssertMapping(mapped[1], 2, 20, 1, 1f, cycle, cycle, 0f, float.Epsilon, float.Epsilon * 2f);
        Assert.True(mapped[1].MappedPlayRate > 0f);
    }

    [Fact]
    public void ManyPlaybacksAreMappedInAscendingFullKeyOrder()
    {
        var playbacks = new[]
        {
            Playback(17, 20, 1, 0.2, 0.4, 0.1f), Playback(2, 10, 2, 0.2, 0.4, 0.1f),
            Playback(9, 20, 1, 0.2, 0.4, 0.1f), Playback(4, 10, 1, 0.2, 0.4, 2f),
            Playback(13, 20, 2, 0.2, 0.4, 0.1f), Playback(1, 10, 1, 0.2, 0.4, 0.1f),
            Playback(15, 20, 1, 0.2, 0.4, 0.1f), Playback(7, 10, 2, 0.2, 0.4, 0.1f),
            Playback(19, 20, 2, 0.2, 0.4, 0.1f), Playback(3, 10, 1, 0.2, 0.4, 0.1f),
            Playback(11, 20, 1, 0.2, 0.4, 0.1f), Playback(5, 10, 2, 0.2, 0.4, 0.1f),
        };
        var mapped = new AlsSyncMappedPlayback[playbacks.Length];

        var result = Evaluate(Markers(), Group(), Members(), playbacks, 0.2, mapped, out var count, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(4, result.LeaderOccurrenceHandleId);
        Assert.Equal(playbacks.Length, count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 7, 9, 11, 13, 15, 17, 19 }, mapped.Select(static item => item.OccurrenceHandleId));
    }

    [Fact]
    public void InputPermutationDoesNotChangeOrderedMappings()
    {
        var playbacks = new[] { Playback(9, 20, 2, 0.3, 0.6, 0.5f), Playback(1, 10, 1, 0.2, 0.5, 1f), Playback(7, 20, 1, 0.1, 0.4, 0.25f) };
        var a = new AlsSyncMappedPlayback[3];
        var b = new AlsSyncMappedPlayback[3];
        var first = Evaluate(Markers(), Group(), Members(), playbacks, 0.3, a, out var firstCount, out var firstFailure);
        var second = Evaluate(Markers(), Group(), Members(), [playbacks[2], playbacks[0], playbacks[1]], 0.3, b, out var secondCount, out var secondFailure);

        Assert.Equal(AlsP5FailureCode.None, firstFailure);
        Assert.Equal(AlsP5FailureCode.None, secondFailure);
        Assert.Equal(first, second);
        Assert.Equal(firstCount, secondCount);
        Assert.Equal(MemoryMarshal.AsBytes(a.AsSpan()).ToArray(), MemoryMarshal.AsBytes(b.AsSpan()).ToArray());
    }

    [Fact]
    public void IgnoresUnrelatedMarkersAndAllowsSameClipCurrentAndOutgoingWithDistinctHandles()
    {
        var markers = Markers().Concat(new[] { new AlsSyncMarkerDefinition(999, 77, 999, 0, 0, float.NaN), new AlsSyncMarkerDefinition(998, 11, 999, 0, 0, 0f) }).ToArray();
        var mapped = new AlsSyncMappedPlayback[3];
        var result = Evaluate(markers, Group(), Members(), [Playback(7, 10, 1, 0.2, 0.4, 1f), Playback(8, 10, 1, 0.2, 0.4, 0.5f), Playback(2, 20, 1, 0.3, 0.5, 0.25f)], 0.2, mapped, out var count, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(3, count);
        Assert.Equal(7, result.LeaderOccurrenceHandleId);
        Assert.Equal(new[] { 2, 7, 8 }, mapped.Select(static value => value.OccurrenceHandleId));
    }

    [Fact]
    public void MissingPairDuplicateTimeIllegalOrderAndUndeclaredMemberFailAtomically()
    {
        var malformed = new[]
        {
            Markers().Where(static marker => marker.MarkerId != 102).ToArray(),
            Markers().Select(static marker => marker.MarkerId == 102 ? marker with { TimeSeconds = 0.2f } : marker).ToArray(),
            Markers().Concat([new AlsSyncMarkerDefinition(103, 11, 10, 2, 0, 0.8f)]).ToArray(),
            Markers().Select(static marker => marker.AnimationId == 20 ? marker with { AnimationId = 30 } : marker).ToArray(),
        };

        foreach (var markers in malformed)
        {
            AssertFailure(markers, Group(), Members(), [Playback(1, 10, 1, 0.2, 0.3, 1f)], 0.1, AlsP5FailureCode.InvalidSyncGroup);
        }
    }

    [Fact]
    public void InvalidGroupAndOccurrenceInputsFailWithoutWritingOutput()
    {
        var invalidGroups = new[]
        {
            Group() with { GroupId = -1 }, Group() with { MemberCount = 0 }, Group() with { MemberOffset = 1 }, Group() with { LeftMarkerNameId = 11, RightMarkerNameId = 11 },
        };
        foreach (var group in invalidGroups)
        {
            AssertFailure(Markers(), group, Members(), [Playback(1, 10, 1, 0.2, 0.3, 1f)], 0.1, AlsP5FailureCode.InvalidSyncGroup);
        }
        AssertFailure(Markers(), Group(), [new AlsSyncMemberBinding(1, 10, 1f, 1, 0), new AlsSyncMemberBinding(1, 20, 2f, 1, 0)], [Playback(1, 10, 1, 0.2, 0.3, 1f)], 0.1, AlsP5FailureCode.InvalidSyncGroup);
        AssertFailure(Markers(), Group(), [new AlsSyncMemberBinding(1, 10, 1f, 1, 2), new AlsSyncMemberBinding(1, 20, 2f, 1, 1)], [Playback(1, 10, 1, 0.2, 0.3, 1f)], 0.1, AlsP5FailureCode.InvalidSyncGroup);

        var invalidPlaybackCases = new[]
        {
            Array.Empty<AlsSyncPlayback>(),
            new[] { Playback(1, 10, 0, 0.2, 0.3, 1f) },
            new[] { Playback(1, 10, 1, -0.1, 0.3, 1f) },
            new[] { Playback(1, 10, 1, 0.4, 0.3, 1f) },
            new[] { Playback(1, 10, 1, 0.2, 0.3, -0.1f) },
            new[] { Playback(1, 10, 1, 0.2, double.NaN, 1f) },
            new[] { Playback(1, 99, 1, 0.2, 0.3, 1f) },
            new[] { Playback(1, 10, 1, 0.2, 0.3, 1f), Playback(1, 10, 1, 0.2, 0.3, 0.5f) },
        };
        foreach (var playbacks in invalidPlaybackCases)
        {
            AssertFailure(Markers(), Group(), Members(), playbacks, 0.1, AlsP5FailureCode.InvalidSyncGroup);
        }
    }

    [Fact]
    public void RejectsNonFiniteDeltaShortOutputAndOutputOverflowTransactionally()
    {
        foreach (var delta in new[] { 0d, -0.1d, double.NaN, double.PositiveInfinity })
        {
            AssertFailure(Markers(), Group(), Members(), [Playback(1, 10, 1, 0.2, 0.3, 1f)], delta, AlsP5FailureCode.InvalidSyncGroup);
        }
        AssertFailure(Markers(), Group(), Members(), [Playback(1, 10, 1, 0.2, 0.3, 1f), Playback(2, 20, 1, 0.2, 0.3, 0.5f)], 0.1, AlsP5FailureCode.InvalidSyncGroup, outputLength: 1);
        AssertFailure(Markers(), Group(), Members(), [Playback(1, 10, 1, double.MaxValue * 0.75, double.MaxValue, 1f)], 0.1, AlsP5FailureCode.InvalidSyncGroup);
        AssertFailure(Markers(), Group(), Members(), [Playback(1, 10, 1, 0.2, 0.3, float.MaxValue)], double.Epsilon, AlsP5FailureCode.NonFiniteOutput);
    }

    [Fact]
    public void DoesNotPublishMarkersToAnExistingEventBuffer()
    {
        var events = new AlsEventBuffer();
        Assert.True(events.TryAdd(new AlsAnimationEvent(1, 2, -1, 3, 1, 0, 0, 0, 0, 0f, 1f, AlsTimelineEventKind.Footstep, AlsAnimationEventPhase.Trigger, default)));
        var before = events;
        var mapped = new AlsSyncMappedPlayback[1];

        _ = Evaluate(Markers(), Group(), Members(), [Playback(1, 10, 1, 0.2, 0.3, 1f)], 0.1, mapped, out _, out var failure);

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(before.Count, events.Count);
        Assert.Equal(before[0], events[0]);
    }

    private static AlsSyncResult Evaluate(ReadOnlySpan<AlsSyncMarkerDefinition> markers, in AlsSyncGroupBinding group, ReadOnlySpan<AlsSyncMemberBinding> members, ReadOnlySpan<AlsSyncPlayback> playbacks, double delta, Span<AlsSyncMappedPlayback> mapped, out int count, out AlsP5FailureCode failure)
    {
        Assert.True(AlsSyncRuntime.TryEvaluateGroup(markers, group, members, playbacks, delta, mapped, out count, out var result, out failure), failure.ToString());
        return result;
    }

    private static void AssertFailure(ReadOnlySpan<AlsSyncMarkerDefinition> markers, in AlsSyncGroupBinding group, ReadOnlySpan<AlsSyncMemberBinding> members, ReadOnlySpan<AlsSyncPlayback> playbacks, double delta, AlsP5FailureCode expectedFailure, int outputLength = 2)
    {
        var mapped = Enumerable.Range(0, outputLength).Select(static index => new AlsSyncMappedPlayback(100 + index, 200 + index, 300 + index, 4f, 5, 6, 0.7f, 0.8f, 0.9f)).ToArray();
        var before = MemoryMarshal.AsBytes(mapped.AsSpan()).ToArray();
        Assert.False(AlsSyncRuntime.TryEvaluateGroup(markers, group, members, playbacks, delta, mapped, out var count, out var result, out var failure));
        Assert.Equal(expectedFailure, failure);
        Assert.Equal(0, count);
        Assert.Equal(AlsSyncResult.CreateDefault(), result);
        Assert.Equal(before, MemoryMarshal.AsBytes(mapped.AsSpan()).ToArray());
    }

    private static AlsSyncMarkerDefinition[] Markers() =>
    [
        new(101, 11, 10, 0, 0, 0.2f), new(102, 12, 10, 1, 0, 0.6f),
        new(201, 11, 20, 0, 0, 1.4f), new(202, 12, 20, 1, 0, 0.6f),
    ];

    private static AlsSyncGroupBinding Group() => new(1, 0, 2, 11, 12);

    private static AlsSyncMemberBinding[] Members() => [new(1, 10, 1f, 1, 1), new(1, 20, 2f, 1, 1)];

    private static AlsSyncPlayback Playback(int handle, int animation, long epoch, double previous, double current, float weight) => new(handle, animation, epoch, previous, current, weight);

    private static void AssertMapping(in AlsSyncMappedPlayback actual, int handle, int animation, long epoch, float duration, long previousCycle, long currentCycle, float previousTime, float currentTime, float rate)
    {
        Assert.Equal(handle, actual.OccurrenceHandleId);
        Assert.Equal(animation, actual.AnimationId);
        Assert.Equal(epoch, actual.PlaybackEpoch);
        Assert.Equal(duration, actual.DurationSeconds);
        Assert.Equal(previousCycle, actual.PreviousCycle);
        Assert.Equal(currentCycle, actual.CurrentCycle);
        AssertClose(previousTime, actual.PreviousTimeSeconds);
        AssertClose(currentTime, actual.CurrentTimeSeconds);
        AssertClose(rate, actual.MappedPlayRate);
    }

    private static void AssertContract<T>() where T : struct
    {
        Assert.Equal(LayoutKind.Sequential, typeof(T).StructLayoutAttribute?.Value);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    private static void AssertFields<T>(params (string Name, Type Type)[] expected)
    {
        var actual = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).OrderBy(static field => field.MetadataToken).Select(static field => (Normalize(field.Name), field.FieldType));
        Assert.Equal(expected, actual);
    }

    private static string Normalize(string name) => name.StartsWith('<') ? name[1..name.IndexOf('>')] : name;
    private static void AssertClose(float expected, float actual) => Assert.InRange(actual, expected - 1e-5f, expected + 1e-5f);
    private static void AssertPositiveZero(float value) => Assert.Equal(0, BitConverter.SingleToInt32Bits(value));
}
