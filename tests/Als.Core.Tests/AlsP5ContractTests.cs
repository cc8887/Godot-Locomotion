using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Simulation;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsP5ContractTests
{
    private const ulong CanonicalLayoutDigest = 17865444901286441077UL;

    [Fact]
    public void P5EnumsFreezeStorageAndNumericMappings()
    {
        AssertEnum<byte, AlsTimelineEventKind>(
            ("Generic", 0), ("Footstep", 1), ("SetAction", 2),
            ("SetGroundedEntry", 3), ("EarlyBlendOut", 4), ("RootMotionScale", 5));
        AssertEnum<byte, AlsTimelineTickMode>(("Queued", 0), ("BranchingPoint", 1));
        AssertEnum<byte, AlsTimelineSourceKind>(
            ("Animation", 0), ("Montage", 1), ("MontageSegmentAnimation", 2));
        AssertEnum<byte, AlsActionCommand>(
            ("None", 0), ("Start", 1), ("Cancel", 2), ("CancelForRuntimeFailure", 3));
        AssertEnum<byte, AlsTransitionFoot>(("Left", 0), ("Right", 1));
        AssertEnum<byte, AlsTimelineFoot>(("Unspecified", 0), ("Left", 1), ("Right", 2));
        AssertEnum<byte, AlsTimelineAction>(
            ("None", 0), ("Rolling", 1), ("Mantling", 2),
            ("Ragdolling", 3), ("GettingUp", 4));
        AssertEnum<byte, AlsTimelineGroundedEntryMode>(("None", 0), ("FromRoll", 1));
        AssertEnum<byte, AlsTimelineLocomotionMode>(
            ("Grounded", 0), ("InAir", 1), ("Mantling", 2),
            ("Ragdoll", 3), ("Recovering", 4));
        AssertEnum<byte, AlsTimelineRotationMode>(
            ("VelocityDirection", 0), ("LookingDirection", 1), ("Aiming", 2));
        AssertEnum<byte, AlsTimelineStance>(("Standing", 0), ("Crouching", 1));
        AssertEnum<byte, AlsAnimationEventPhase>(
            ("Trigger", 0), ("Begin", 1), ("Tick", 2), ("End", 3));
        AssertEnum<ushort, AlsP5FailureCode>(
            ("None", 0), ("InvalidDeltaTime", 1), ("NonFiniteInput", 2),
            ("InvalidBinding", 3), ("InvalidTimeline", 4), ("InvalidSyncGroup", 5),
            ("EventBufferOverflow", 6), ("StalePreparedFrame", 7), ("NonFiniteOutput", 8));
        AssertEnum<ushort, AlsActionResultCode>(
            ("None", 0), ("Accepted", 1), ("Completed", 2),
            ("RejectedInvalidRequest", 3), ("RejectedMissingDefinition", 4),
            ("RejectedBusy", 5), ("RejectedLowerPriority", 6),
            ("InterruptedByReplacement", 7), ("InterruptedByExplicitCancel", 8),
            ("InterruptedByEarlyBlendOut", 9), ("InterruptedByLifecycle", 10),
            ("InterruptedByGeneration", 11), ("InterruptedByRuntimeFailure", 12));
        AssertEnum<byte, AlsP5OccurrenceSourceKind>(
            ("Base", 1), ("Turn", 2), ("Rotate", 3),
            ("Transition", 4), ("ActionMontage", 5), ("ActionSequence", 6));
    }

    [Fact]
    public void P5ValueContractsAreSequentialAndUnmanaged()
    {
        AssertContract<AlsCompactEventPayload>();
        AssertContract<AlsAnimationEvent>();
        AssertContract<AlsActionRequest>();
        AssertContract<AlsActionOutcome>();
        AssertContract<AlsActionPlayerState>();
        AssertContract<AlsDynamicTransitionState>();
        AssertContract<AlsLaneBlendState>();
        AssertContract<AlsLaneGraphSource>();
        AssertContract<AlsLaneGraphInstruction>();
        AssertContract<AlsP5OccurrenceLayoutEntry>();
        AssertContract<AlsSyncResult>();
        AssertContract<AlsDynamicTransitionQueuedSelection>();
        AssertContract<AlsDynamicTransitionPlaybackSummary>();
        AssertContract<AlsActionPlayback>();
        AssertContract<AlsP5FailureRecord>();
        AssertContract<AlsEventBuffer>();
        AssertContract<AlsActionOutcomeBuffer>();
    }

    [Fact]
    public void P5ReadonlyContractsKeepTheFrozenDeclarationOrder()
    {
        AssertPropertyOrder<AlsCompactEventPayload>(
            "SemanticId", "EnumValue0", "EnumValue1", "EnumValue2", "ScalarValue0", "Flags",
            "TerminationReason");
        AssertPropertyOrder<AlsAnimationEvent>(
            "EventId", "SourceAnimationId", "SourceActionId", "OccurrenceHandleId",
            "PlaybackEpoch", "PlaybackCycle", "OwnerToken", "EventSequence", "BoundaryOrdinal",
            "AnimationTime", "Weight", "Kind", "Phase", "Payload");
        AssertPropertyOrder<AlsActionRequest>(
            "RequestId", "Command", "ActionDefinitionId", "StartSectionId", "Priority",
            "SlotGeneration");
        AssertPropertyOrder<AlsActionOutcome>(
            "RequestId", "ActionDefinitionId", "PlaybackEpoch", "ResultCode");
        AssertPropertyOrder<AlsLaneGraphSource>(
            "OccurrenceHandleId", "AnimationId", "BindingIndex", "PlaybackEpoch",
            "PreviousClipTime", "CurrentClipTime", "ContributingDeltaSeconds", "PlayRate", "Active");
        AssertPropertyOrder<AlsLaneGraphInstruction>(
            "Outgoing", "Incoming", "LaneWeight", "IncomingMix",
            "OutgoingEffectiveWeight", "IncomingEffectiveWeight");
        AssertPropertyOrder<AlsP5OccurrenceLayoutEntry>(
            "SourceKind", "SourceBindingIndex", "GraphSlotIndex", "OccurrenceHandleId",
            "AuthorityGroupId");
        AssertPropertyOrder<AlsSyncResult>(
            "GroupId", "LeaderOccurrenceHandleId", "LeaderAnimationId", "LeaderPlaybackEpoch",
            "PreviousMarkerId", "NextMarkerId", "Cycle", "Phase", "LeftFootPhase", "RightFootPhase");
        AssertPropertyOrder<AlsDynamicTransitionQueuedSelection>(
            "AnimationId", "Foot", "BlendSeconds", "PlayRate", "Active");
        AssertPropertyOrder<AlsDynamicTransitionPlaybackSummary>(
            "AnimationId", "Foot", "BlendSeconds", "PlayRate", "EffectiveWeight", "Active");
        AssertPropertyOrder<AlsActionPlayback>(
            "OccurrenceHandleId", "ActionDefinitionId", "AnimationId", "SectionId", "SegmentId",
            "PlaybackEpoch", "PreviousTime", "CurrentTime", "PreviousClipTime", "CurrentClipTime",
            "FinalSegmentDeltaSeconds", "PlayRate", "BlendSeconds", "EffectiveWeight", "Active");
        AssertPropertyOrder<AlsP5FailureRecord>(
            "Identity", "Code", "LastCommittedResultDigest", "AttemptOrdinal");

        AssertStorageFieldOrder<AlsCompactEventPayload>(
            ("SemanticId", typeof(int)), ("EnumValue0", typeof(int)),
            ("EnumValue1", typeof(int)), ("EnumValue2", typeof(int)),
            ("ScalarValue0", typeof(float)), ("Flags", typeof(ushort)),
            ("TerminationReason", typeof(AlsActionResultCode)));
        AssertStorageFieldOrder<AlsAnimationEvent>(
            ("EventId", typeof(int)), ("SourceAnimationId", typeof(int)),
            ("SourceActionId", typeof(int)), ("OccurrenceHandleId", typeof(int)),
            ("PlaybackEpoch", typeof(long)), ("PlaybackCycle", typeof(long)),
            ("OwnerToken", typeof(ulong)), ("EventSequence", typeof(long)),
            ("BoundaryOrdinal", typeof(int)), ("AnimationTime", typeof(float)),
            ("Weight", typeof(float)), ("Kind", typeof(AlsTimelineEventKind)),
            ("Phase", typeof(AlsAnimationEventPhase)), ("Payload", typeof(AlsCompactEventPayload)));
        AssertStorageFieldOrder<AlsActionRequest>(
            ("RequestId", typeof(long)), ("Command", typeof(AlsActionCommand)),
            ("ActionDefinitionId", typeof(int)), ("StartSectionId", typeof(int)),
            ("Priority", typeof(int)), ("SlotGeneration", typeof(uint)));
        AssertStorageFieldOrder<AlsActionOutcome>(
            ("RequestId", typeof(long)), ("ActionDefinitionId", typeof(int)),
            ("PlaybackEpoch", typeof(long)), ("ResultCode", typeof(AlsActionResultCode)));
        AssertStorageFieldOrder<AlsLaneGraphSource>(
            ("OccurrenceHandleId", typeof(int)), ("AnimationId", typeof(int)),
            ("BindingIndex", typeof(int)), ("PlaybackEpoch", typeof(long)),
            ("PreviousClipTime", typeof(float)), ("CurrentClipTime", typeof(float)),
            ("ContributingDeltaSeconds", typeof(float)), ("PlayRate", typeof(float)),
            ("Active", typeof(byte)));
        AssertStorageFieldOrder<AlsLaneGraphInstruction>(
            ("Outgoing", typeof(AlsLaneGraphSource)), ("Incoming", typeof(AlsLaneGraphSource)),
            ("LaneWeight", typeof(float)), ("IncomingMix", typeof(float)),
            ("OutgoingEffectiveWeight", typeof(float)), ("IncomingEffectiveWeight", typeof(float)));
        AssertStorageFieldOrder<AlsP5OccurrenceLayoutEntry>(
            ("SourceKind", typeof(AlsP5OccurrenceSourceKind)),
            ("SourceBindingIndex", typeof(int)), ("GraphSlotIndex", typeof(int)),
            ("OccurrenceHandleId", typeof(int)), ("AuthorityGroupId", typeof(int)));
        AssertStorageFieldOrder<AlsSyncResult>(
            ("GroupId", typeof(int)), ("LeaderOccurrenceHandleId", typeof(int)),
            ("LeaderAnimationId", typeof(int)), ("LeaderPlaybackEpoch", typeof(long)),
            ("PreviousMarkerId", typeof(int)), ("NextMarkerId", typeof(int)),
            ("Cycle", typeof(long)), ("Phase", typeof(float)),
            ("LeftFootPhase", typeof(float)), ("RightFootPhase", typeof(float)));
        AssertStorageFieldOrder<AlsDynamicTransitionQueuedSelection>(
            ("AnimationId", typeof(int)), ("Foot", typeof(AlsTransitionFoot)),
            ("BlendSeconds", typeof(float)), ("PlayRate", typeof(float)), ("Active", typeof(byte)));
        AssertStorageFieldOrder<AlsDynamicTransitionPlaybackSummary>(
            ("AnimationId", typeof(int)), ("Foot", typeof(AlsTransitionFoot)),
            ("BlendSeconds", typeof(float)), ("PlayRate", typeof(float)),
            ("EffectiveWeight", typeof(float)), ("Active", typeof(byte)));
        AssertStorageFieldOrder<AlsActionPlayback>(
            ("OccurrenceHandleId", typeof(int)), ("ActionDefinitionId", typeof(int)),
            ("AnimationId", typeof(int)), ("SectionId", typeof(int)), ("SegmentId", typeof(int)),
            ("PlaybackEpoch", typeof(long)), ("PreviousTime", typeof(float)),
            ("CurrentTime", typeof(float)), ("PreviousClipTime", typeof(float)),
            ("CurrentClipTime", typeof(float)), ("FinalSegmentDeltaSeconds", typeof(float)),
            ("PlayRate", typeof(float)), ("BlendSeconds", typeof(float)),
            ("EffectiveWeight", typeof(float)), ("Active", typeof(byte)));
        AssertStorageFieldOrder<AlsP5FailureRecord>(
            ("Identity", typeof(AlsFrameIdentity)), ("Code", typeof(AlsP5FailureCode)),
            ("LastCommittedResultDigest", typeof(ulong)), ("AttemptOrdinal", typeof(uint)));

        Assert.DoesNotContain(
            typeof(AlsAnimationEvent).GetConstructors(BindingFlags.Instance | BindingFlags.Public),
            constructor => constructor.GetParameters().Length == 4);
    }

    [Fact]
    public void CompactPayloadDoesNotRejectReservedBitsAtConstructionBoundary()
    {
        var payload = new AlsCompactEventPayload(1, 2, 3, 4, 5f, ushort.MaxValue, (AlsActionResultCode)65535);

        Assert.Equal(ushort.MaxValue, payload.Flags);
        Assert.Equal((AlsActionResultCode)65535, payload.TerminationReason);
    }

    [Fact]
    public void DefaultFrameInputCarriesGenerationBoundNoActionRequest()
    {
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(10, 20, 30), 1f / 60f);

        Assert.Equal(-1, input.ActionRequest.RequestId);
        Assert.Equal(AlsActionCommand.None, input.ActionRequest.Command);
        Assert.Equal(-1, input.ActionRequest.ActionDefinitionId);
        Assert.Equal(-1, input.ActionRequest.StartSectionId);
        Assert.Equal(0, input.ActionRequest.Priority);
        Assert.Equal(30U, input.ActionRequest.SlotGeneration);
        Assert.Equal(-1, AlsActionRequest.None.RequestId);
        Assert.Equal(0U, AlsActionRequest.None.SlotGeneration);
    }

    [Fact]
    public void PositionalSyntheticInputUsesTheFrozenNoActionRequest()
    {
        var input = AlsSyntheticInputSource.Create(
            new AlsFrameIdentity(10, 20, 9),
            1f / 60f);

        Assert.Equal(-1, input.ActionRequest.RequestId);
        Assert.Equal(AlsActionCommand.None, input.ActionRequest.Command);
        Assert.Equal(-1, input.ActionRequest.ActionDefinitionId);
        Assert.Equal(-1, input.ActionRequest.StartSectionId);
        Assert.Equal(0, input.ActionRequest.Priority);
        Assert.Equal(0U, input.ActionRequest.SlotGeneration);
    }

    [Fact]
    public void DefaultRuntimeStateUsesFrozenInactiveP5Identities()
    {
        var state = AlsRuntimeState.CreateDefault();

        Assert.Equal(-1, state.ActionPlayer.ActionDefinitionId);
        Assert.Equal(-1, state.ActionPlayer.SectionId);
        Assert.Equal(-1, state.ActionPlayer.SegmentBindingIndex);
        Assert.Equal(-1, state.ActionPlayer.RequestId);
        Assert.Equal(0, state.ActionPlayer.LastProcessedRequestId);
        Assert.Equal(-1, state.ActionPlayer.LastProcessedCommandRequestId);
        Assert.Equal(AlsActionCommand.None, state.ActionPlayer.LastProcessedCommand);
        Assert.Equal(0L, state.ActionPlayer.PlaybackEpoch);
        Assert.Equal(-1, state.DynamicTransition.AnimationId);
        Assert.Equal(-1, state.DynamicTransition.QueuedAnimationId);
        Assert.Equal(0, state.ActionPlayer.Priority);
        Assert.Equal((byte)0, state.ActionPlayer.Playing);
        Assert.Equal((byte)0, state.ActionPlayer.Interruptible);
        Assert.Equal(0L, state.DynamicTransition.PlaybackEpoch);
        Assert.Equal(0, state.DynamicTransition.CooldownFrames);
        Assert.Equal(AlsTransitionFoot.Left, state.DynamicTransition.Foot);
        Assert.Equal(AlsTransitionFoot.Left, state.DynamicTransition.QueuedFoot);
        Assert.Equal((byte)0, state.DynamicTransition.Active);
        Assert.Equal((byte)0, state.DynamicTransition.Queued);
        AssertPositiveZero(state.ActionPlayer.PlaybackTime);
        AssertPositiveZero(state.DynamicTransition.PreviousPlaybackTime);
        AssertPositiveZero(state.DynamicTransition.PlaybackTime);
        AssertLaneInactive(state.ActionBlendLane);
        AssertLaneInactive(state.DynamicTransitionBlendLane);
        AlsRuntimeState.ValidateP5Defaults(in state);

        var clrZero = default(AlsRuntimeState);
        Assert.Throws<InvalidOperationException>(() => AlsRuntimeState.ValidateP5Defaults(in clrZero));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public void P5DefaultValidationRejectsNondefaultTransitionFootStorage(byte rawFoot)
    {
        var state = AlsRuntimeState.CreateDefault();
        state.DynamicTransition.Foot = (AlsTransitionFoot)rawFoot;
        Assert.Throws<InvalidOperationException>(() => AlsRuntimeState.ValidateP5Defaults(in state));

        state = AlsRuntimeState.CreateDefault();
        state.DynamicTransition.QueuedFoot = (AlsTransitionFoot)rawFoot;
        Assert.Throws<InvalidOperationException>(() => AlsRuntimeState.ValidateP5Defaults(in state));
    }

    [Theory]
    [InlineData("action-time")]
    [InlineData("transition-previous")]
    [InlineData("transition-current")]
    [InlineData("action-lane-clip")]
    [InlineData("action-lane-weight")]
    [InlineData("action-lane-mix")]
    [InlineData("action-lane-blend")]
    [InlineData("transition-lane-clip")]
    [InlineData("transition-lane-weight")]
    [InlineData("transition-lane-mix")]
    [InlineData("transition-lane-blend")]
    public void P5DefaultValidationRejectsSignedZeroInEveryStateFloat(string field)
    {
        var state = AlsRuntimeState.CreateDefault();
        var negativeZero = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));

        switch (field)
        {
            case "action-time": state.ActionPlayer.PlaybackTime = negativeZero; break;
            case "transition-previous": state.DynamicTransition.PreviousPlaybackTime = negativeZero; break;
            case "transition-current": state.DynamicTransition.PlaybackTime = negativeZero; break;
            case "action-lane-clip": state.ActionBlendLane.OutgoingClipTime = negativeZero; break;
            case "action-lane-weight": state.ActionBlendLane.LaneWeight = negativeZero; break;
            case "action-lane-mix": state.ActionBlendLane.IncomingMix = negativeZero; break;
            case "action-lane-blend": state.ActionBlendLane.BlendSeconds = negativeZero; break;
            case "transition-lane-clip": state.DynamicTransitionBlendLane.OutgoingClipTime = negativeZero; break;
            case "transition-lane-weight": state.DynamicTransitionBlendLane.LaneWeight = negativeZero; break;
            case "transition-lane-mix": state.DynamicTransitionBlendLane.IncomingMix = negativeZero; break;
            case "transition-lane-blend": state.DynamicTransitionBlendLane.BlendSeconds = negativeZero; break;
        }

        Assert.Throws<InvalidOperationException>(() => AlsRuntimeState.ValidateP5Defaults(in state));
    }

    [Fact]
    public void DefaultFrameResultUsesFrozenInactiveP5Summaries()
    {
        var result = AlsFrameResult.CreateDefault(new AlsFrameIdentity(10, 20, 30));

        Assert.Equal(-1, result.Sync.GroupId);
        Assert.Equal(-1, result.Sync.LeaderOccurrenceHandleId);
        Assert.Equal(-1, result.Sync.LeaderAnimationId);
        Assert.Equal(-1, result.Sync.PreviousMarkerId);
        Assert.Equal(-1, result.Sync.NextMarkerId);
        Assert.Equal(0L, result.Sync.LeaderPlaybackEpoch);
        Assert.Equal(0L, result.Sync.Cycle);
        Assert.Equal(-1, result.DynamicTransition.AnimationId);
        Assert.Equal(AlsTransitionFoot.Left, result.DynamicTransition.Foot);
        Assert.Equal((byte)0, result.DynamicTransition.Active);
        Assert.Equal(-1, result.ActionPlayback.OccurrenceHandleId);
        Assert.Equal(-1, result.ActionPlayback.ActionDefinitionId);
        Assert.Equal(-1, result.ActionPlayback.AnimationId);
        Assert.Equal(-1, result.ActionPlayback.SectionId);
        Assert.Equal(-1, result.ActionPlayback.SegmentId);
        Assert.Equal(0L, result.ActionPlayback.PlaybackEpoch);
        Assert.Equal((byte)0, result.ActionPlayback.Active);
        AssertPositiveZero(result.Sync.Phase);
        AssertPositiveZero(result.Sync.LeftFootPhase);
        AssertPositiveZero(result.Sync.RightFootPhase);
        AssertPositiveZero(result.DynamicTransition.BlendSeconds);
        AssertPositiveZero(result.DynamicTransition.PlayRate);
        AssertPositiveZero(result.DynamicTransition.EffectiveWeight);
        AssertPositiveZero(result.ActionPlayback.PreviousTime);
        AssertPositiveZero(result.ActionPlayback.CurrentTime);
        AssertPositiveZero(result.ActionPlayback.PreviousClipTime);
        AssertPositiveZero(result.ActionPlayback.CurrentClipTime);
        AssertPositiveZero(result.ActionPlayback.FinalSegmentDeltaSeconds);
        AssertPositiveZero(result.ActionPlayback.PlayRate);
        AssertPositiveZero(result.ActionPlayback.BlendSeconds);
        AssertPositiveZero(result.ActionPlayback.EffectiveWeight);
        Assert.Equal(0, result.ActionOutcomes.Count);
        Assert.Equal(AlsP5FailureCode.None, result.P5FailureCode);
    }

    [Fact]
    public void LayoutViewBindsFieldsWithoutValidation()
    {
        var entries = CanonicalEntries();
        var view = new AlsP5OccurrenceLayoutView(99, 0, entries);

        Assert.Equal(99, view.Version);
        Assert.Equal(0UL, view.Digest);
        Assert.True(view.Entries.SequenceEqual(entries));
    }

    [Fact]
    public void LayoutViewIsStackOnlyAndBindsStackallocSpanInConstantTime()
    {
        Assert.True(typeof(AlsP5OccurrenceLayoutView).IsByRefLike);
        Span<AlsP5OccurrenceLayoutEntry> entries = stackalloc AlsP5OccurrenceLayoutEntry[1];
        entries[0] = new AlsP5OccurrenceLayoutEntry(
            AlsP5OccurrenceSourceKind.Base, 0, 0, 0, 0);

        var view = new AlsP5OccurrenceLayoutView(1, ReferenceLayoutDigest(1, entries), entries);

        Assert.Equal(1, view.Entries.Length);
        Assert.Equal(entries[0], view.Entries[0]);
    }

    [Fact]
    public void LayoutValidatorAcceptsCanonicalFrozenDigestWithoutMutation()
    {
        var entries = CanonicalEntries();
        var before = entries.ToArray();

        Assert.Equal(CanonicalLayoutDigest, ReferenceLayoutDigest(1, entries));
        AlsP5OccurrenceLayoutContract.Validate(1, CanonicalLayoutDigest, entries);

        Assert.Equal(before, entries);
    }

    [Fact]
    public void LayoutValidatorAcceptsFrozenTask5ThirtySevenEntryLayout()
    {
        var entries = Task5Entries();

        Assert.Equal(37, entries.Length);
        Assert.Equal(0xD6FEF54173240D32UL, ReferenceLayoutDigest(1, entries));
        AlsP5OccurrenceLayoutContract.Validate(1, 0xD6FEF54173240D32UL, entries);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("zero-digest")]
    [InlineData("stale-digest")]
    [InlineData("unknown-kind")]
    [InlineData("negative-binding")]
    [InlineData("negative-slot")]
    [InlineData("negative-handle")]
    [InlineData("negative-authority")]
    [InlineData("duplicate-key")]
    [InlineData("duplicate-handle")]
    [InlineData("nonordinal-handle")]
    [InlineData("sparse-authority")]
    [InlineData("authority-out-of-range")]
    [InlineData("transition-slot")]
    [InlineData("action-montage-slot")]
    [InlineData("action-sequence-slot")]
    public void LayoutValidatorRejectsEveryFrozenInvalidityWithoutMutation(string invalidity)
    {
        var entries = CanonicalEntries();
        var version = 1;
        var digest = CanonicalLayoutDigest;

        switch (invalidity)
        {
            case "version": version = 2; break;
            case "zero-digest": digest = 0; break;
            case "stale-digest": digest++; break;
            case "unknown-kind": entries[0] = entries[0] with { SourceKind = (AlsP5OccurrenceSourceKind)7 }; break;
            case "negative-binding": entries[0] = entries[0] with { SourceBindingIndex = -1 }; break;
            case "negative-slot": entries[0] = entries[0] with { GraphSlotIndex = -1 }; break;
            case "negative-handle": entries[0] = entries[0] with { OccurrenceHandleId = -1 }; break;
            case "negative-authority": entries[0] = entries[0] with { AuthorityGroupId = -1 }; break;
            case "duplicate-key": entries[1] = entries[1] with { SourceKind = entries[0].SourceKind, SourceBindingIndex = entries[0].SourceBindingIndex }; break;
            case "duplicate-handle": entries[1] = entries[1] with { OccurrenceHandleId = 0 }; break;
            case "nonordinal-handle": entries[1] = entries[1] with { OccurrenceHandleId = 2 }; break;
            case "sparse-authority": entries[1] = entries[1] with { AuthorityGroupId = 2 }; entries[2] = entries[2] with { AuthorityGroupId = 2 }; break;
            case "authority-out-of-range": entries[2] = entries[2] with { AuthorityGroupId = 3 }; break;
            case "transition-slot": entries[1] = entries[1] with { GraphSlotIndex = 1 }; break;
            case "action-montage-slot": entries[2] = entries[2] with { SourceKind = AlsP5OccurrenceSourceKind.ActionMontage, GraphSlotIndex = 1 }; break;
            case "action-sequence-slot": entries[2] = entries[2] with { GraphSlotIndex = 1 }; break;
        }

        if (invalidity is not "zero-digest" and not "stale-digest")
        {
            digest = ReferenceLayoutDigest(version, entries);
        }

        var before = entries.ToArray();
        Assert.Throws<ArgumentException>(() =>
            AlsP5OccurrenceLayoutContract.Validate(version, digest, entries));
        Assert.Equal(before, entries);
    }

    [Fact]
    public void LayoutValidatorRejectsEmptySpan()
    {
        Assert.Throws<ArgumentException>(() =>
            AlsP5OccurrenceLayoutContract.Validate(1, 1, ReadOnlySpan<AlsP5OccurrenceLayoutEntry>.Empty));
    }

    [Fact]
    public void LayoutValidatorAllocatesNothingAfterWarmup()
    {
        var entries = CanonicalEntries();
        for (var index = 0; index < 100; index++)
        {
            AlsP5OccurrenceLayoutContract.Validate(1, CanonicalLayoutDigest, entries);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            AlsP5OccurrenceLayoutContract.Validate(1, CanonicalLayoutDigest, entries);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static AlsP5OccurrenceLayoutEntry[] CanonicalEntries() =>
    [
        new(AlsP5OccurrenceSourceKind.Base, 10, 2, 0, 0),
        new(AlsP5OccurrenceSourceKind.Transition, 20, 0, 1, 1),
        new(AlsP5OccurrenceSourceKind.ActionSequence, 30, 0, 2, 1),
    ];

    private static AlsP5OccurrenceLayoutEntry[] Task5Entries()
    {
        var entries = new AlsP5OccurrenceLayoutEntry[37];
        var handle = 0;
        AddBank(AlsP5OccurrenceSourceKind.Base, 22, 0);
        AddBank(AlsP5OccurrenceSourceKind.Turn, 8, 0);
        AddBank(AlsP5OccurrenceSourceKind.Rotate, 4, 0);
        entries[handle] = new(AlsP5OccurrenceSourceKind.Transition, 0, 0, handle++, 1);
        entries[handle] = new(AlsP5OccurrenceSourceKind.ActionMontage, 0, 0, handle++, 2);
        entries[handle] = new(AlsP5OccurrenceSourceKind.ActionSequence, 0, 0, handle, 3);
        return entries;

        void AddBank(AlsP5OccurrenceSourceKind kind, int count, int authority)
        {
            for (var index = 0; index < count; index++)
            {
                entries[handle] = new(kind, index, index, handle++, authority);
            }
        }
    }

    private static ulong ReferenceLayoutDigest(
        int version,
        ReadOnlySpan<AlsP5OccurrenceLayoutEntry> entries)
    {
        var digest = 14695981039346656037UL;
        AppendUInt32(ref digest, unchecked((uint)version));
        AppendUInt32(ref digest, unchecked((uint)entries.Length));

        foreach (ref readonly var entry in entries)
        {
            AppendByte(ref digest, (byte)entry.SourceKind);
            AppendUInt32(ref digest, unchecked((uint)entry.SourceBindingIndex));
            AppendUInt32(ref digest, unchecked((uint)entry.GraphSlotIndex));
            AppendUInt32(ref digest, unchecked((uint)entry.OccurrenceHandleId));
            AppendUInt32(ref digest, unchecked((uint)entry.AuthorityGroupId));
        }

        return digest;
    }

    private static void AppendUInt32(ref ulong digest, uint value)
    {
        for (var index = 0; index < 4; index++)
        {
            AppendByte(ref digest, (byte)(value >> (index * 8)));
        }
    }

    private static void AppendByte(ref ulong digest, byte value)
    {
        digest ^= value;
        digest *= 1099511628211UL;
    }

    private static void AssertLaneInactive(in AlsLaneBlendState lane)
    {
        Assert.Equal(-1, lane.OutgoingOccurrenceHandleId);
        Assert.Equal(-1, lane.OutgoingAnimationId);
        Assert.Equal(-1, lane.OutgoingBindingIndex);
        Assert.Equal(0L, lane.OutgoingPlaybackEpoch);
        Assert.Equal(0f, lane.OutgoingClipTime);
        Assert.Equal(0f, lane.LaneWeight);
        Assert.Equal(0f, lane.IncomingMix);
        Assert.Equal(0f, lane.BlendSeconds);
        Assert.Equal((byte)0, lane.VisualActive);
        Assert.Equal((byte)0, lane.OutgoingActive);
        AssertPositiveZero(lane.OutgoingClipTime);
        AssertPositiveZero(lane.LaneWeight);
        AssertPositiveZero(lane.IncomingMix);
        AssertPositiveZero(lane.BlendSeconds);
    }

    private static void AssertContract<T>() where T : struct
    {
        Assert.Equal(LayoutKind.Sequential, typeof(T).StructLayoutAttribute?.Value);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    private static void AssertPropertyOrder<T>(params string[] expected)
    {
        var actual = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static property => property.MetadataToken)
            .Select(static property => property.Name);
        Assert.Equal(expected, actual);
    }

    private static void AssertStorageFieldOrder<T>(params (string Name, Type Type)[] expected)
    {
        var actual = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(static field => field.MetadataToken)
            .Select(static field => (NormalizeStorageFieldName(field.Name), field.FieldType));
        Assert.Equal(expected, actual);
    }

    private static string NormalizeStorageFieldName(string name)
    {
        const string suffix = ">k__BackingField";
        return name.Length > suffix.Length + 1 && name[0] == '<' && name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[1..^suffix.Length]
            : name;
    }

    private static void AssertPositiveZero(float value) =>
        Assert.Equal(0, BitConverter.SingleToInt32Bits(value));

    private static void AssertEnum<TStorage, TEnum>(params (string Name, int Value)[] expected)
        where TStorage : struct
        where TEnum : struct, Enum
    {
        Assert.Equal(typeof(TStorage), Enum.GetUnderlyingType(typeof(TEnum)));
        Assert.Equal(expected.Select(static item => item.Name), Enum.GetNames<TEnum>());
        Assert.Equal(
            expected.Select(static item => item.Value),
            Enum.GetValues<TEnum>().Select(static value => Convert.ToInt32(value)));
    }
}
