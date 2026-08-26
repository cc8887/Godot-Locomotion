using System.Threading;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

public sealed class AlsP3aHarnessContext
{
    public const int WarmupFrames = 120;
    public const int MeasurementFrames = 600;
    public const int TotalFrames = WarmupFrames + MeasurementFrames;
    public const int ReplacementFrame = 420;
    public const float DeltaTime = 1f / 60f;
    public const int CoverageWalking = 1 << 0;
    public const int CoverageRunning = 1 << 1;
    public const int CoverageSprinting = 1 << 2;
    public const int CoverageCrouching = 1 << 3;
    public const int CoverageJump = 1 << 4;
    public const int CoverageLand = 1 << 5;
    public const int CoverageForward = 1 << 6;
    public const int CoverageRight = 1 << 7;
    public const int CoverageBackward = 1 << 8;
    public const int CoverageLeft = 1 << 9;
    public const int CoverageLookingDirection = 1 << 10;
    public const int CoverageVelocityDirection = 1 << 11;
    public const int CoverageAiming = 1 << 12;
    public const int RequiredCoverage = (1 << 13) - 1;

    private string? _failure;

    public AlsP3aHarnessContext(
        AlsHarnessMode mode,
        int characterCount,
        int mainManagedThreadId,
        AlsLocomotionSettings settings,
        in AlsMotorSettings motorSettings)
    {
        Mode = mode;
        Entries = new AlsP3aHarnessEntry[characterCount];
        Registry = new AlsSlotRegistry(characterCount);
        MainManagedThreadId = mainManagedThreadId;
        Settings = settings;
        MotorSettings = motorSettings;
    }

    public AlsHarnessMode Mode { get; }

    public AlsP3aHarnessEntry[] Entries { get; }

    public AlsSlotRegistry Registry { get; }

    public int MainManagedThreadId { get; }

    public AlsLocomotionSettings Settings { get; }

    public AlsMotorSettings MotorSettings { get; }

    public AlsP3aHarnessEntry SpareEntry { get; set; } = null!;

    public long PublishedFrameId;

    public ulong Digest = AlsResultDigest.OffsetBasis;

    public long MissingResults;

    public long StaleResults;

    public long GenerationMismatches;

    public long LaggedResults;

    public long GatherMotorAllocations;

    public long FirstGatherAllocationFrame;

    public long ModelAllocations;

    public long FirstModelAllocationFrame;

    public long ExchangeAllocations;

    public long CommitAllocations;

    public int ReplacementCount;

    public AlsSlotHandle ReplacementHandle;

    public bool OldGenerationRejected;

    public bool NewGenerationCommitted;

    public bool ReplacementFrameCommitted;

    public int MeasurementCoverage;

    public long AffinityViolations;

    public string? Failure => Volatile.Read(ref _failure);

    public void RecordFailure(Exception exception) =>
        Interlocked.CompareExchange(ref _failure, exception.ToString(), null);
}

public sealed class AlsP3aHarnessEntry
{
    public AlsP3aHarnessEntry(AlsSlotHandle handle)
    {
        Handle = handle;
        Exchange = new AlsFrameExchange();
    }

    public AlsSlotHandle Handle { get; }

    public AlsFrameExchange Exchange { get; }

    public AlsCharacterMotor Motor { get; set; } = null!;

    public AlsP3aWorkerRoot Worker { get; set; } = null!;

    public AlsFrameInput PendingInput;

    public long LastMotorFrameId;

    public int ObservedOffMainThread;

    public int HasCommittedResult;

    public AlsLocomotionState PreviousCommittedLocomotionState;

    public int HasPublishedResult;

    public long PublishedResultFrameId;

    public int PublishedResultCharacterId;

    public int PublishedResultGeneration;
}
