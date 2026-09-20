using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using GodotAls.Core.Sync;
using GodotAls.Core.Events;

namespace GodotAls.Core.Contracts;

public enum AlsLocomotionSourceKind : byte { BlendSpace, Sequence, TeleportEvaluator }
public enum AlsLocomotionSourceDomain : byte { Cycle, Detail, Stop, Standing, MainGrounded, Crouching, Jump, MainMovement, Overlay, Ragdoll }
public enum AlsSourceRateInput : byte { Constant, StandingPlayRate, RotateRate, CrouchingPlayRate, JumpPlayRate, FlailRate }
public enum AlsSourceLoopInput : byte { Constant, RotateLeft, RotateRight }
public readonly record struct AlsSourceRotationInput(float Rate, bool Left, bool Right);
public enum AlsSourceAxisInput : byte { None, StrideBlend, WalkRunBlend, LeanLeftRight, LeanForwardBack }

/// <summary>Immutable source-local binding. Sample ranges are not separate BlendSpace group members.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLocomotionSourcePlayerBinding(int PlayerId, int CompiledNodeIndex,
    AlsLocomotionSourceKind Kind, AlsLocomotionSourceDomain Domain, int SyncGroupId,
    float StartPosition, float DefaultPlayRate, float PlayRateBasis, AlsSourceRateInput PlayRateInput, bool Loop,
    int SampleStart, int SampleCount, int DetailSlot, AlsSourceAxisInput InputX, AlsSourceAxisInput InputY,
    AlsSourceLoopInput LoopInput = AlsSourceLoopInput.Constant);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLocomotionSourceSampleBinding(int SampleId, int PlayerId, int SourceIndex, int AnimationId,
    float X, float Y, float Z, float SampleRateScale, float AssetRateScale, float DurationSeconds,
    int AdditiveBaseAnimationId, int SequenceIndex = -1);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLocomotionSourceSyncBinding(int PlayerId, int AssetId, ulong MarkerMask,
    bool AllowMarkers, bool LegacyLength, bool MatchSyncPhases, AlsBlendSpaceNotifyMode NotifyMode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] AlsAssetSyncRole Role = AlsAssetSyncRole.CanBeLeader);

/// <summary>Full SHA-256 of the compiled source profile, with its runtime contract and skeleton.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLocomotionSourceStamp(int Version, int SkeletonId,
    ulong Digest0, ulong Digest1, ulong Digest2, ulong Digest3)
{
    public bool IsValid => Version == AlsLocomotionSourceView.CurrentVersion && SkeletonId >= 0 &&
        (Digest0 | Digest1 | Digest2 | Digest3) != 0;
}

/// <summary>Borrowed immutable source-local tables. Not the full P5 occurrence layout.</summary>
public readonly ref struct AlsLocomotionSourceView(AlsLocomotionSourceStamp stamp,
    ReadOnlySpan<AlsLocomotionSourcePlayerBinding> players, ReadOnlySpan<AlsLocomotionSourceSampleBinding> samples,
    ReadOnlySpan<AlsLocomotionSourceSyncBinding> syncPlayers, ReadOnlySpan<int> groupIds,
    ReadOnlySpan<AlsAssetSyncSequence> sequences, ReadOnlySpan<AlsAssetSyncMarker> markers,
    ReadOnlySpan<AlsAssetNotifyRange> notifyRanges = default,
    ReadOnlySpan<AlsAssetNotifyDefinition> notifyDefinitions = default,
    ReadOnlySpan<AlsAssetNotifyPolicy> notifyPolicies = default)
{
    public const int CurrentVersion = 2;
    public readonly AlsLocomotionSourceStamp Stamp = stamp;
    public readonly ReadOnlySpan<AlsLocomotionSourcePlayerBinding> Players = players;
    public readonly ReadOnlySpan<AlsLocomotionSourceSampleBinding> Samples = samples;
    public readonly ReadOnlySpan<AlsLocomotionSourceSyncBinding> SyncPlayers = syncPlayers;
    public readonly ReadOnlySpan<int> GroupIds = groupIds;
    public readonly ReadOnlySpan<AlsAssetSyncSequence> Sequences = sequences;
    public readonly ReadOnlySpan<AlsAssetSyncMarker> Markers = markers;
    public readonly ReadOnlySpan<AlsAssetNotifyRange> NotifyRanges = notifyRanges;
    public readonly ReadOnlySpan<AlsAssetNotifyDefinition> NotifyDefinitions = notifyDefinitions;
    public readonly ReadOnlySpan<AlsAssetNotifyPolicy> NotifyPolicies = notifyPolicies;
}

/// <summary>Resolved graph contribution; time and epoch belong to the caller's candidate state.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLocomotionSourceUpdate(int PlayerId, long Epoch, float Time, float Weight,
    int SampleStart, int SampleCount, bool RequestedInertialization = false);

/// <summary>Source-owned resolved sample, in native cache order. No asset metadata duplicated per frame.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLocomotionSampleUpdate(int SampleId, float Weight, float CachedPlayRate);
