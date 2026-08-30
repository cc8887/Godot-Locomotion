using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using GodotAls.Core.Contracts;

namespace GodotAls.Locomotion;

public interface IAlsP3RuntimeMeasurement
{
    bool TryGetMeasurementIndex(in AlsFrameIdentity identity, out int measurementIndex);

    void RecordGatherStart(int measurementIndex, long timestamp);

    void RecordGatherEnd(int measurementIndex, long timestamp);

    void RecordWorkerStart(int measurementIndex, long timestamp);

    void RecordWorkerAdvance(int measurementIndex, long elapsedTicks);

    void RecordWorkerEnd(int measurementIndex, long timestamp);

    void RecordModifierAdvance(int measurementIndex, int transactionCount);

    void RecordCommitStart(int measurementIndex, long timestamp);

    void RecordCommitEnd(int measurementIndex, long timestamp);

    void AddModelAllocations(int measurementIndex, long value);

    void AddCurveAllocations(int measurementIndex, long value);

    void AddControllerAllocations(int measurementIndex, long value);

    void AddModifierAllocations(int measurementIndex, long value);

    void AddSkeletonAllocations(int measurementIndex, long value);

    void AddExchangeAllocations(int measurementIndex, long value);

    void AddCommitAllocations(int measurementIndex, long value);
}

public readonly record struct AlsP4TimingResult(
    long GatherCommitP95Microseconds,
    long WorkerP95Microseconds,
    long TotalP99Microseconds);

public sealed class AlsP4HarnessContext : IAlsP3RuntimeMeasurement
{
    private const int CounterStride = 16;
    public const int RequiredWarmupFrames = 120;
    public const int RequiredMeasurementFrames = 600;

    private readonly long[] _firstMeasurementFrames;
    private readonly long[] _gatherStarts;
    private readonly long[] _gatherEnds;
    private readonly long[] _workerStarts;
    private readonly long[] _workerEnds;
    private readonly long[] _commitStarts;
    private readonly long[] _commitEnds;
    private readonly long[] _advanceCounts;
    private readonly long[] _modifierCounts;
    private readonly long[] _commitCounts;
    private readonly long[] _modelAllocations;
    private readonly long[] _curveAllocations;
    private readonly long[] _controllerAllocations;
    private readonly long[] _modifierAllocations;
    private readonly long[] _skeletonAllocations;
    private readonly long[] _exchangeAllocations;
    private readonly long[] _commitAllocations;
    private int _active;

    public AlsP4HarnessContext(int characterCount, int warmupFrames, int measurementFrames)
    {
        if (characterCount is not (1 or 10))
        {
            throw new ArgumentOutOfRangeException(nameof(characterCount));
        }
        if (warmupFrames != RequiredWarmupFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(warmupFrames));
        }
        if (measurementFrames != RequiredMeasurementFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(measurementFrames));
        }

        CharacterCount = characterCount;
        WarmupFrames = warmupFrames;
        MeasurementFrames = measurementFrames;
        var sampleCount = checked(characterCount * measurementFrames);
        _firstMeasurementFrames = new long[characterCount];
        _gatherStarts = new long[sampleCount];
        _gatherEnds = new long[sampleCount];
        _workerStarts = new long[sampleCount];
        _workerEnds = new long[sampleCount];
        _commitStarts = new long[sampleCount];
        _commitEnds = new long[sampleCount];
        var allocationSlotCount = checked(characterCount * CounterStride);
        _advanceCounts = new long[allocationSlotCount];
        _modifierCounts = new long[allocationSlotCount];
        _commitCounts = new long[allocationSlotCount];
        _modelAllocations = new long[allocationSlotCount];
        _curveAllocations = new long[allocationSlotCount];
        _controllerAllocations = new long[allocationSlotCount];
        _modifierAllocations = new long[allocationSlotCount];
        _skeletonAllocations = new long[allocationSlotCount];
        _exchangeAllocations = new long[allocationSlotCount];
        _commitAllocations = new long[allocationSlotCount];
    }

    public int CharacterCount { get; }

    public int WarmupFrames { get; }

    public int MeasurementFrames { get; }

    public long ModelAllocations => SumCounters(_modelAllocations);

    public long CurveAllocations => SumCounters(_curveAllocations);

    public long ControllerAllocations => SumCounters(_controllerAllocations);

    public long ModifierAllocations => SumCounters(_modifierAllocations);

    public long SkeletonAllocations => SumCounters(_skeletonAllocations);

    public long ExchangeAllocations => SumCounters(_exchangeAllocations);

    public long CommitAllocations => SumCounters(_commitAllocations);

    public void Start(ReadOnlySpan<long> firstMeasurementFrames)
    {
        if (firstMeasurementFrames.Length != CharacterCount)
        {
            throw new ArgumentException(
                "P4 measurement requires one first frame per character.",
                nameof(firstMeasurementFrames));
        }
        if (Volatile.Read(ref _active) != 0)
        {
            throw new InvalidOperationException("P4 measurement has already started.");
        }
        for (var index = 0; index < firstMeasurementFrames.Length; index++)
        {
            if (firstMeasurementFrames[index] <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(firstMeasurementFrames));
            }
            _firstMeasurementFrames[index] = firstMeasurementFrames[index];
        }
        Volatile.Write(ref _active, 1);
    }

    public bool TryGetMeasurementIndex(
        in AlsFrameIdentity identity,
        out int measurementIndex)
    {
        var characterIndex = checked((int)identity.CharacterId);
        if (Volatile.Read(ref _active) == 0 ||
            (uint)characterIndex >= (uint)CharacterCount)
        {
            measurementIndex = -1;
            return false;
        }
        var localFrame = identity.FrameId - _firstMeasurementFrames[characterIndex];
        if ((ulong)localFrame >= (ulong)MeasurementFrames)
        {
            measurementIndex = -1;
            return false;
        }
        measurementIndex = checked((characterIndex * MeasurementFrames) + (int)localFrame);
        return true;
    }

    public void RecordGatherStart(int measurementIndex, long timestamp) =>
        RecordOnce(_gatherStarts, measurementIndex, timestamp, "Gather start");

    public void RecordGatherEnd(int measurementIndex, long timestamp) =>
        RecordOnce(_gatherEnds, measurementIndex, timestamp, "Gather end");

    public void RecordWorkerStart(int measurementIndex, long timestamp) =>
        RecordOnce(_workerStarts, measurementIndex, timestamp, "Worker start");

    public void RecordWorkerAdvance(int measurementIndex, long elapsedTicks)
    {
        ValidateIndex(measurementIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedTicks);
        Interlocked.Increment(ref _advanceCounts[CounterIndex(measurementIndex)]);
    }

    public void RecordWorkerEnd(int measurementIndex, long timestamp) =>
        RecordOnce(_workerEnds, measurementIndex, timestamp, "Worker end");

    public void RecordModifierAdvance(int measurementIndex, int transactionCount)
    {
        ValidateIndex(measurementIndex);
        if (transactionCount != 1)
        {
            throw new InvalidOperationException(
                $"P4 modifier must publish exactly one transaction per frame; observed {transactionCount}.");
        }
        Interlocked.Increment(ref _modifierCounts[CounterIndex(measurementIndex)]);
    }

    public void RecordCommitStart(int measurementIndex, long timestamp) =>
        RecordOnce(_commitStarts, measurementIndex, timestamp, "Commit start");

    public void RecordCommitEnd(int measurementIndex, long timestamp)
    {
        RecordOnce(_commitEnds, measurementIndex, timestamp, "Commit end");
        Interlocked.Increment(ref _commitCounts[CounterIndex(measurementIndex)]);
    }

    public long GetAdvanceCount(int characterIndex) =>
        Interlocked.Read(ref _advanceCounts[characterIndex * CounterStride]);

    public long GetModifierCount(int characterIndex) =>
        Interlocked.Read(ref _modifierCounts[characterIndex * CounterStride]);

    public long GetCommitCount(int characterIndex) =>
        Interlocked.Read(ref _commitCounts[characterIndex * CounterStride]);

    internal long GetWorkerStartTimestamp(int characterIndex, int localFrame) =>
        Interlocked.Read(ref _workerStarts[
            checked((characterIndex * MeasurementFrames) + localFrame)]);

    public void AddModelAllocations(int measurementIndex, long value) =>
        AddCounter(_modelAllocations, measurementIndex, value);

    public void AddCurveAllocations(int measurementIndex, long value) =>
        AddCounter(_curveAllocations, measurementIndex, value);

    public void AddControllerAllocations(int measurementIndex, long value) =>
        AddCounter(_controllerAllocations, measurementIndex, value);

    public void AddModifierAllocations(int measurementIndex, long value) =>
        AddCounter(_modifierAllocations, measurementIndex, value);

    public void AddSkeletonAllocations(int measurementIndex, long value) =>
        AddCounter(_skeletonAllocations, measurementIndex, value);

    public void AddExchangeAllocations(int measurementIndex, long value) =>
        AddCounter(_exchangeAllocations, measurementIndex, value);

    public void AddCommitAllocations(int measurementIndex, long value) =>
        AddCounter(_commitAllocations, measurementIndex, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddCounter(long[] counters, int measurementIndex, long value)
    {
        ValidateIndex(measurementIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (value == 0)
        {
            return;
        }
        var characterIndex = measurementIndex / MeasurementFrames;
        Interlocked.Add(ref counters[characterIndex * CounterStride], value);
    }

    private long SumCounters(long[] counters)
    {
        var total = 0L;
        for (var characterIndex = 0; characterIndex < CharacterCount; characterIndex++)
        {
            total = checked(total + Interlocked.Read(
                ref counters[characterIndex * CounterStride]));
        }
        return total;
    }

    public AlsP4TimingResult StopAndCalculatePercentiles()
    {
        Volatile.Write(ref _active, 0);
        for (var characterIndex = 0; characterIndex < CharacterCount; characterIndex++)
        {
            RequireCount(_advanceCounts, characterIndex, "animation advances");
            RequireCount(_modifierCounts, characterIndex, "modifier transactions");
            RequireCount(_commitCounts, characterIndex, "commits");
        }

        var gatherCommitTicks = new long[MeasurementFrames];
        var workerEnvelopeTicks = new long[MeasurementFrames];
        var totalTicks = new long[MeasurementFrames];
        for (var localFrame = 0; localFrame < MeasurementFrames; localFrame++)
        {
            var firstGather = long.MaxValue;
            var firstWorker = long.MaxValue;
            var lastWorker = 0L;
            var lastCommit = 0L;
            var mainTicks = 0L;
            for (var characterIndex = 0; characterIndex < CharacterCount; characterIndex++)
            {
                var index = checked((characterIndex * MeasurementFrames) + localFrame);
                var gatherStart = RequireTimestamp(_gatherStarts[index], "Gather start", index);
                var gatherEnd = RequireTimestamp(_gatherEnds[index], "Gather end", index);
                var workerStart = RequireTimestamp(_workerStarts[index], "Worker start", index);
                var workerEnd = RequireTimestamp(_workerEnds[index], "Worker end", index);
                var commitStart = RequireTimestamp(_commitStarts[index], "Commit start", index);
                var commitEnd = RequireTimestamp(_commitEnds[index], "Commit end", index);
                if (gatherEnd < gatherStart || workerEnd < workerStart || commitEnd < commitStart)
                {
                    throw new InvalidOperationException(
                        $"P4 measurement timestamps are out of order at sample {index}.");
                }
                mainTicks = checked(mainTicks + (gatherEnd - gatherStart) + (commitEnd - commitStart));
                firstGather = Math.Min(firstGather, gatherStart);
                firstWorker = Math.Min(firstWorker, workerStart);
                lastWorker = Math.Max(lastWorker, workerEnd);
                lastCommit = Math.Max(lastCommit, commitEnd);
            }
            if (lastWorker < firstWorker || lastCommit < firstGather)
            {
                throw new InvalidOperationException(
                    $"P4 local-frame timing envelope is invalid at frame {localFrame}.");
            }
            gatherCommitTicks[localFrame] = mainTicks;
            workerEnvelopeTicks[localFrame] = lastWorker - firstWorker;
            totalTicks[localFrame] = lastCommit - firstGather;
        }

        Array.Sort(gatherCommitTicks);
        Array.Sort(workerEnvelopeTicks);
        Array.Sort(totalTicks);
        return new AlsP4TimingResult(
            ToMicroseconds(gatherCommitTicks[PercentileIndex(MeasurementFrames, 95)]),
            ToMicroseconds(workerEnvelopeTicks[PercentileIndex(MeasurementFrames, 95)]),
            ToMicroseconds(totalTicks[PercentileIndex(MeasurementFrames, 99)]));
    }

    private void RequireCount(long[] values, int characterIndex, string label)
    {
        var count = Interlocked.Read(ref values[characterIndex * CounterStride]);
        if (count != MeasurementFrames)
        {
            throw new InvalidOperationException(
                $"P4 character {characterIndex} recorded {count} {label}; expected {MeasurementFrames}.");
        }
    }

    private void RecordOnce(long[] values, int measurementIndex, long timestamp, string label)
    {
        ValidateIndex(measurementIndex);
        if (timestamp <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }
        if (Interlocked.CompareExchange(ref values[measurementIndex], timestamp, 0L) != 0L)
        {
            throw new InvalidOperationException(
                $"P4 {label} was recorded more than once for sample {measurementIndex}.");
        }
    }

    private void ValidateIndex(int measurementIndex)
    {
        if ((uint)measurementIndex >= (uint)_gatherStarts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(measurementIndex));
        }
    }

    private int CounterIndex(int measurementIndex) =>
        (measurementIndex / MeasurementFrames) * CounterStride;

    private static long RequireTimestamp(long value, string label, int index) =>
        value > 0
            ? value
            : throw new InvalidOperationException(
                $"P4 {label} was not recorded for sample {index}.");

    private static int PercentileIndex(int count, int percentile) =>
        checked(((count * percentile) + 99) / 100 - 1);

    private static long ToMicroseconds(long ticks) =>
        checked(((ticks * 1_000_000L) + Stopwatch.Frequency - 1L) / Stopwatch.Frequency);
}
