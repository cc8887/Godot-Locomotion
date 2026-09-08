using GodotAls.Core.Actions;

namespace GodotAls.Import.Compilation;

public enum AlsP5CurveCombineMode : byte
{
    AdditiveToDefault = 1,
}

public enum AlsP5LoopPolicy : byte
{
    Once,
    Loop,
}

public enum AlsP5TransitionStance : byte
{
    Standing,
    Crouching,
}

public enum AlsP5TransitionFoot : byte
{
    Left,
    Right,
}

public enum AlsCompiledActionTimelineSourceKind : byte
{
    Montage = 1,
    Sequence = 2,
}

public readonly record struct AlsCompiledEventSemantic(
    AlsCompiledTimelineEventKind Kind,
    int SemanticId);

public sealed record AlsCompiledCurveSemantic(
    float MissingValue,
    AlsP5CurveCombineMode CombineMode,
    float ClampMinimum,
    float ClampMaximum,
    int[] AnimationCurveIds)
{
    private int[] _animationCurveIds = AnimationCurveIds.ToArray();

    public int[] AnimationCurveIds
    {
        get => _animationCurveIds.ToArray();
        init => _animationCurveIds = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public readonly record struct AlsCompiledSyncMember(
    int GroupMemberIndex,
    int AnimationId,
    float DurationSeconds,
    int LeftMarkerId,
    int RightMarkerId,
    AlsP5LoopPolicy LoopPolicy,
    bool CanLead);

public sealed record AlsCompiledSyncGroup(int GroupId, AlsCompiledSyncMember[] Members)
{
    private AlsCompiledSyncMember[] _members = Members.ToArray();

    public AlsCompiledSyncMember[] Members
    {
        get => _members.ToArray();
        init => _members = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public readonly record struct AlsCompiledTransitionSlot(
    int SlotIndex,
    AlsP5TransitionStance Stance,
    AlsP5TransitionFoot Foot,
    int AnimationId,
    int AdditiveBaseAnimationId);

public sealed record AlsCompiledDynamicTransition(
    float DistanceMeters,
    float BlendSeconds,
    float PlayRate,
    int CooldownFrames,
    AlsCompiledTransitionSlot[] Slots)
{
    private AlsCompiledTransitionSlot[] _slots = Slots.ToArray();

    public AlsCompiledTransitionSlot[] Slots
    {
        get => _slots.ToArray();
        init => _slots = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public readonly record struct AlsCompiledActionSectionBinding(
    int ActionDefinitionId,
    int SectionId,
    int NextSectionId,
    float StartTime,
    float EndTime);

public sealed record AlsCompiledActionDefinition(
    int DefinitionId,
    int MontageId,
    float MontageDurationSeconds,
    int SlotId,
    int StartSectionId,
    int Priority,
    bool Interruptible,
    float PlayRate,
    float BlendSeconds,
    AlsP5LoopPolicy LoopPolicy,
    AlsCompiledActionSectionBinding[] Sections,
    AlsActionLifecycleSettings Lifecycle = default)
{
    private AlsCompiledActionSectionBinding[] _sections = Sections.ToArray();

    public AlsCompiledActionSectionBinding[] Sections
    {
        get => _sections.ToArray();
        init => _sections = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public readonly record struct AlsCompiledActionSegmentBinding(
    int ActionDefinitionId,
    int SlotId,
    int SegmentId,
    int AnimationId,
    float MontageStartTime,
    float MontageEndTime,
    float AnimationStartTime,
    float AnimationEndTime,
    float PlayRate,
    int LoopCount);

public readonly record struct AlsCompiledActionTimelineEntry(
    int ActionDefinitionId,
    int EventId,
    AlsCompiledTimelineEventKind Kind,
    AlsCompiledActionTimelineSourceKind SourceKind,
    int SourceAssetId,
    int SegmentBindingIndex,
    int SourceIndex,
    int TrackIndex,
    float TimeSeconds,
    float DurationSeconds,
    float TriggerWeightThreshold,
    AlsCompiledTimelineTickMode TickMode,
    AlsCompiledTimelinePayloadDefinition Payload,
    int BoundaryOrdinal);

public readonly record struct AlsCompiledDemoCases(
    int TransitionSlotIndex,
    int RollActionDefinitionId);

public sealed record AlsP5aAnimationRuntimeProfile(
    int SchemaVersion,
    AlsCompiledEventSemantic[] EventSemantics,
    AlsCompiledCurveSemantic AllowTransitions,
    AlsCompiledSyncGroup[] SyncGroups,
    AlsCompiledDynamicTransition DynamicTransition,
    AlsCompiledActionDefinition[] Actions,
    AlsCompiledActionSegmentBinding[] SegmentBindings,
    AlsCompiledActionTimelineEntry[] TimelineEntries,
    AlsCompiledDemoCases DemoCases)
{
    private AlsCompiledEventSemantic[] _eventSemantics = EventSemantics.ToArray();
    private AlsCompiledSyncGroup[] _syncGroups = SyncGroups.ToArray();
    private AlsCompiledActionDefinition[] _actions = Actions.ToArray();
    private AlsCompiledActionSegmentBinding[] _segmentBindings = SegmentBindings.ToArray();
    private AlsCompiledActionTimelineEntry[] _timelineEntries = TimelineEntries.ToArray();

    public AlsCompiledEventSemantic[] EventSemantics
    {
        get => _eventSemantics.ToArray();
        init => _eventSemantics = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsCompiledSyncGroup[] SyncGroups
    {
        get => _syncGroups.ToArray();
        init => _syncGroups = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsCompiledActionDefinition[] Actions
    {
        get => _actions.ToArray();
        init => _actions = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsCompiledActionSegmentBinding[] SegmentBindings
    {
        get => _segmentBindings.ToArray();
        init => _segmentBindings = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsCompiledActionTimelineEntry[] TimelineEntries
    {
        get => _timelineEntries.ToArray();
        init => _timelineEntries = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}
