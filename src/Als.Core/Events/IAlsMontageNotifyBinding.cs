namespace GodotAls.Core.Events;

// A shared policy namespace lets the final AnimInstance queue merge Montage,
// proxy and Slot references without losing the first accepted State context.
public interface IAlsMontageNotifyBinding
{
    int SourcePolicyCount { get; }
    ReadOnlySpan<AlsAssetNotifyPolicy> Policies { get; }
    bool TryTimeline(in AlsAssetNotifyReference reference, out AlsTimelineEventDefinition definition);
}
