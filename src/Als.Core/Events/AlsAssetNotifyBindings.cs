using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

public enum AlsAssetNotifyFilterType : byte { None, Lod }

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyRange(int AnimationId, int Offset, int Count);

/// <summary>Native queue policy, shared by every playback of the same authored notify.
/// Object IDs identify notify instances, not their class or physical playback handle.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsAssetNotifyPolicy(int EventId, int SourceIndex, int TrackIndex,
    int NotifyObjectId, int StateObjectId, int NameId, float WeightThreshold, float Chance,
    AlsAssetNotifyFilterType FilterType, int FilterLod, AlsTimelineTickMode TickMode,
    bool FilterViaRequest, bool OnDedicatedServer, bool OnFollower, byte StateBehaviorFlags = 0);
