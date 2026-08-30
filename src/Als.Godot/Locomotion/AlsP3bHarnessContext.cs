using System.Diagnostics;
using System.Threading;
using GodotAls.Core.Contracts;

namespace GodotAls.Locomotion;

public sealed class AlsP3bHarnessContext : IAlsP3RuntimeMeasurement
{
    public const int WarmupFrames = 120;
    public const int MeasurementFrames = 600;

    private readonly long[] _firstMeasurementFrames;
    private readonly long[] _workerElapsedTicks;
    private readonly long[] _advanceCounts;
    private int _active;

    public AlsP3bHarnessContext(int characterCount)
    {
        if (characterCount is not (1 or 10))
        {
            throw new ArgumentOutOfRangeException(nameof(characterCount));
        }

        CharacterCount = characterCount;
        _firstMeasurementFrames = new long[characterCount];
        _workerElapsedTicks = new long[checked(characterCount * MeasurementFrames)];
        _advanceCounts = new long[characterCount];
    }

    public int CharacterCount { get; }

    public long ModelAllocations;

    public long ControllerAllocations;

    public long SkeletonAllocations;

    public long ExchangeAllocations;

    public long CommitAllocations;

    public void Start(ReadOnlySpan<long> firstMeasurementFrames)
    {
        if (firstMeasurementFrames.Length != CharacterCount)
        {
            throw new ArgumentException(
                "P3B measurement requires one first frame per character.",
                nameof(firstMeasurementFrames));
        }
        if (Volatile.Read(ref _active) != 0)
        {
            throw new InvalidOperationException("P3B measurement has already started.");
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
        if ((ulong)localFrame >= MeasurementFrames)
        {
            measurementIndex = -1;
            return false;
        }

        measurementIndex = checked((characterIndex * MeasurementFrames) + (int)localFrame);
        return true;
    }

    public void RecordWorkerAdvance(int measurementIndex, long elapsedTicks)
    {
        if ((uint)measurementIndex >= (uint)_workerElapsedTicks.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(measurementIndex));
        }
        if (elapsedTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
        }

        _workerElapsedTicks[measurementIndex] = elapsedTicks;
        var characterIndex = measurementIndex / MeasurementFrames;
        Interlocked.Increment(ref _advanceCounts[characterIndex]);
    }

    public void RecordGatherStart(int measurementIndex, long timestamp) { }

    public void RecordGatherEnd(int measurementIndex, long timestamp) { }

    public void RecordWorkerStart(int measurementIndex, long timestamp) { }

    public void RecordWorkerEnd(int measurementIndex, long timestamp) { }

    public void RecordModifierAdvance(int measurementIndex, int transactionCount) { }

    public void RecordCommitStart(int measurementIndex, long timestamp) { }

    public void RecordCommitEnd(int measurementIndex, long timestamp) { }

    public long GetAdvanceCount(int characterIndex) =>
        Interlocked.Read(ref _advanceCounts[characterIndex]);

    public void AddModelAllocations(int measurementIndex, long value) =>
        Interlocked.Add(ref ModelAllocations, value);

    public void AddCurveAllocations(int measurementIndex, long value) =>
        Interlocked.Add(ref ControllerAllocations, value);

    public void AddControllerAllocations(int measurementIndex, long value) =>
        Interlocked.Add(ref ControllerAllocations, value);

    public void AddModifierAllocations(int measurementIndex, long value) =>
        Interlocked.Add(ref ControllerAllocations, value);

    public void AddSkeletonAllocations(int measurementIndex, long value) =>
        Interlocked.Add(ref SkeletonAllocations, value);

    public void AddExchangeAllocations(int measurementIndex, long value) =>
        Interlocked.Add(ref ExchangeAllocations, value);

    public void AddCommitAllocations(int measurementIndex, long value) =>
        Interlocked.Add(ref CommitAllocations, value);

    public (long P95Microseconds, long P99Microseconds) StopAndCalculatePercentiles()
    {
        Volatile.Write(ref _active, 0);
        for (var index = 0; index < _advanceCounts.Length; index++)
        {
            if (Interlocked.Read(ref _advanceCounts[index]) != MeasurementFrames)
            {
                throw new InvalidOperationException(
                    $"P3B character {index} did not record exactly {MeasurementFrames} worker advances.");
            }
        }

        Array.Sort(_workerElapsedTicks);
        var p95Ticks = _workerElapsedTicks[PercentileIndex(_workerElapsedTicks.Length, 95)];
        var p99Ticks = _workerElapsedTicks[PercentileIndex(_workerElapsedTicks.Length, 99)];
        return (ToMicroseconds(p95Ticks), ToMicroseconds(p99Ticks));
    }

    private static int PercentileIndex(int count, int percentile) =>
        checked(((count * percentile) + 99) / 100 - 1);

    private static long ToMicroseconds(long ticks) =>
        checked(((ticks * 1_000_000L) + Stopwatch.Frequency - 1L) / Stopwatch.Frequency);
}
