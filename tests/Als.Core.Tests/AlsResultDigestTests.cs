using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Events;
using GodotAls.Core.Simulation;

namespace GodotAls.Core.Tests;

public sealed class AlsResultDigestTests
{
    [Fact]
    public void DefaultP3ResultDigestRemainsByteCompatible()
    {
        var digest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(
            ref digest,
            AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1)));

        Assert.Equal(13994752607362685853UL, digest);
    }

    [Fact]
    public void SyntheticP3SequenceDigestRemainsByteCompatible()
    {
        Assert.Equal(1797804522282832714UL, EvaluateDigest(120));
    }

    [Fact]
    public void FullyActiveP4DigestVectorLocksMarkerVersionOrderAndMultiplicity()
    {
        var result = CreateFullyActiveP4DigestVector();
        var digest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(ref digest, result);

        // This locks the P4 marker, version 2, declaration order, and exactly-once hashing.
        Assert.Equal(18222991105242177202UL, digest);
    }

    [Fact]
    public void EquivalentRunsProduceTheSameDigest()
    {
        var first = EvaluateDigest(120);
        var second = EvaluateDigest(120);

        Assert.Equal(first, second);
    }

    [Fact]
    public void DigestChangesWhenAResultChanges()
    {
        var identity = new AlsFrameIdentity(1, 0, 1);
        var first = AlsFrameResult.CreateDefault(identity);
        var second = AlsFrameResult.CreateDefault(identity);
        second.ErrorCode = 7;
        var firstDigest = AlsResultDigest.OffsetBasis;
        var secondDigest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(ref firstDigest, first);
        AlsResultDigest.Append(ref secondDigest, second);

        Assert.NotEqual(firstDigest, secondDigest);
    }

    [Fact]
    public void WorkerTimingDoesNotAffectDigest()
    {
        var identity = new AlsFrameIdentity(1, 0, 1);
        var first = AlsFrameResult.CreateDefault(identity);
        var second = AlsFrameResult.CreateDefault(identity);
        first.WorkerElapsedTicks = 10;
        second.WorkerElapsedTicks = 20;
        var firstDigest = AlsResultDigest.OffsetBasis;
        var secondDigest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(ref firstDigest, first);
        AlsResultDigest.Append(ref secondDigest, second);

        Assert.Equal(firstDigest, secondDigest);
    }

    [Fact]
    public void DigestIncludesEveryP3LocomotionOutput()
    {
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.ActualGait = AlsGait.Running);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.ActualStance = AlsStance.Crouching);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.ActualRotationMode = AlsRotationMode.Aiming);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.AnimationState = AlsAnimationState.JumpStart);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.BlendCoordinates = new Vector2(1.25f, 0f));
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.BlendCoordinates = new Vector2(0f, -2.5f));
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.Stride = 0.75f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.PlayRate = 1.1f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.Lean = new Vector2(-0.5f, 0f));
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.Lean = new Vector2(0f, 0.25f));
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.AnimationPhase = 0.625f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.TargetYaw = 135f);
    }

    [Fact]
    public void DigestIncludesEveryP4PoseOutput()
    {
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.AimRelativeYaw = 0.1f,
            static (ref AlsFrameResult result) => result.AimRelativeYaw = 0.2f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.AimRelativePitch = 0.2f,
            static (ref AlsFrameResult result) => result.AimRelativePitch = 0.3f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.HeadWeight = 0.3f,
            static (ref AlsFrameResult result) => result.HeadWeight = 0.4f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.SpineWeight = 0.4f,
            static (ref AlsFrameResult result) => result.SpineWeight = 0.5f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.UpperBodyWeight = 0.5f,
            static (ref AlsFrameResult result) => result.UpperBodyWeight = 0.6f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.SpineResidualYaw = 0.6f,
            static (ref AlsFrameResult result) => result.SpineResidualYaw = 0.7f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.TurnAnimationId = 1,
            static (ref AlsFrameResult result) => result.TurnAnimationId = 2);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.TurnCurveId = 2,
            static (ref AlsFrameResult result) => result.TurnCurveId = 3);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.TurnPhase = 0.7f,
            static (ref AlsFrameResult result) => result.TurnPhase = 0.8f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.TurnPlayRate = 0.8f,
            static (ref AlsFrameResult result) => result.TurnPlayRate = 0.9f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.TurnNominalDegrees = 90,
            static (ref AlsFrameResult result) => result.TurnNominalDegrees = 180);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.TurnDirection = -1,
            static (ref AlsFrameResult result) => result.TurnDirection = 1);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.TurnActive = 1,
            static (ref AlsFrameResult result) => result.TurnActive = 2);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.TurnYawDelta = 0.9f,
            static (ref AlsFrameResult result) => result.TurnYawDelta = 1.0f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RotateAnimationId = 3,
            static (ref AlsFrameResult result) => result.RotateAnimationId = 4);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RotateCurveId = 4,
            static (ref AlsFrameResult result) => result.RotateCurveId = 5);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RotatePhase = 0.11f,
            static (ref AlsFrameResult result) => result.RotatePhase = 0.12f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RotatePlayRate = 0.12f,
            static (ref AlsFrameResult result) => result.RotatePlayRate = 0.13f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RotateDirection = -1,
            static (ref AlsFrameResult result) => result.RotateDirection = 1);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RotateActive = 1,
            static (ref AlsFrameResult result) => result.RotateActive = 2);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RotateYawDelta = 0.13f,
            static (ref AlsFrameResult result) => result.RotateYawDelta = 0.14f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) =>
                result.LeftFootPose = result.LeftFootPose with { LockAmount = 0.3f },
            static (ref AlsFrameResult result) =>
                result.LeftFootPose = result.LeftFootPose with { LockAmount = 0.4f });
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) =>
                result.LeftFootPose = result.LeftFootPose with { PlatformId = 5 },
            static (ref AlsFrameResult result) =>
                result.LeftFootPose = result.LeftFootPose with { PlatformId = 6 });
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) =>
                result.RightFootPose = result.RightFootPose with { LockAmount = 0.5f },
            static (ref AlsFrameResult result) =>
                result.RightFootPose = result.RightFootPose with { LockAmount = 0.6f });
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) =>
                result.RightFootPose = result.RightFootPose with { PlatformId = 6 },
            static (ref AlsFrameResult result) =>
                result.RightFootPose = result.RightFootPose with { PlatformId = 7 });
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.LeftFootReleaseReason = AlsFootReleaseReason.RayMiss,
            static (ref AlsFrameResult result) => result.LeftFootReleaseReason = AlsFootReleaseReason.BaseChanged);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RightFootReleaseReason = AlsFootReleaseReason.WeightLost,
            static (ref AlsFrameResult result) => result.RightFootReleaseReason = AlsFootReleaseReason.Teleported);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.LeftFootIkWeight = 0.25f,
            static (ref AlsFrameResult result) => result.LeftFootIkWeight = 0.75f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RightFootIkWeight = 0.25f,
            static (ref AlsFrameResult result) => result.RightFootIkWeight = 0.75f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.LeftFootLockCurve = 0.25f,
            static (ref AlsFrameResult result) => result.LeftFootLockCurve = 0.75f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.RightFootLockCurve = 0.25f,
            static (ref AlsFrameResult result) => result.RightFootLockCurve = 0.75f);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.P4ModifierOperationTicks = 17,
            static (ref AlsFrameResult result) => result.P4ModifierOperationTicks = 18);
        AssertP4ValueChangesDigest(
            static (ref AlsFrameResult result) => result.P4ReasonCode = (AlsP4ReasonCode)1,
            static (ref AlsFrameResult result) => result.P4ReasonCode = (AlsP4ReasonCode)2);
    }

    [Fact]
    public void SignedZeroP4MutationActivatesTheVersionedExtension()
    {
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.AimRelativeYaw = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)));
    }

    [Fact]
    public void DigestIncludesEveryP4FootRotationComponentByRawBits()
    {
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { Rotation = result.LeftFootPose.Rotation with { X = RawFloat(0x3F000001) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { Rotation = result.LeftFootPose.Rotation with { Y = RawFloat(0x3F000002) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { Rotation = result.LeftFootPose.Rotation with { Z = RawFloat(0x3F000003) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { Rotation = result.LeftFootPose.Rotation with { W = RawFloat(0x3F000004) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { Rotation = result.RightFootPose.Rotation with { X = RawFloat(0x3E800001) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { Rotation = result.RightFootPose.Rotation with { Y = RawFloat(0x3E800002) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { Rotation = result.RightFootPose.Rotation with { Z = RawFloat(0x3E800003) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { Rotation = result.RightFootPose.Rotation with { W = RawFloat(0x3E800004) } });
    }

    [Fact]
    public void DigestIncludesEveryP4NestedVectorComponentByRawBits()
    {
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.PelvisOffset = result.PelvisOffset with { X = RawFloat(0x3F100001) });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.PelvisOffset = result.PelvisOffset with { Y = RawFloat(0x3F100002) });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.PelvisOffset = result.PelvisOffset with { Z = RawFloat(0x3F100003) });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { Position = result.LeftFootPose.Position with { X = RawFloat(0x3F200001) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { Position = result.LeftFootPose.Position with { Y = RawFloat(0x3F200002) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { Position = result.LeftFootPose.Position with { Z = RawFloat(0x3F200003) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { Position = result.RightFootPose.Position with { X = RawFloat(0x3F300001) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { Position = result.RightFootPose.Position with { Y = RawFloat(0x3F300002) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { Position = result.RightFootPose.Position with { Z = RawFloat(0x3F300003) } });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.NextLeftFootProbeOrigin = result.NextLeftFootProbeOrigin with { X = RawFloat(0x3F400001) });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.NextLeftFootProbeOrigin = result.NextLeftFootProbeOrigin with { Y = RawFloat(0x3F400002) });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.NextLeftFootProbeOrigin = result.NextLeftFootProbeOrigin with { Z = RawFloat(0x3F400003) });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.NextRightFootProbeOrigin = result.NextRightFootProbeOrigin with { X = RawFloat(0x3F500001) });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.NextRightFootProbeOrigin = result.NextRightFootProbeOrigin with { Y = RawFloat(0x3F500002) });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.NextRightFootProbeOrigin = result.NextRightFootProbeOrigin with { Z = RawFloat(0x3F500003) });
    }

    [Fact]
    public void DigestDistinguishesQuietNanPayloadsWithinOneP4Component()
    {
        var first = CreateActiveP4Result();
        first.LeftFootPose = first.LeftFootPose with
        {
            Rotation = first.LeftFootPose.Rotation with { X = RawFloat(0x7FC00001) },
        };
        var second = first;
        second.LeftFootPose = second.LeftFootPose with
        {
            Rotation = second.LeftFootPose.Rotation with { X = RawFloat(0x7FC00002) },
        };

        AssertP4ExtensionActive(first);
        AssertP4ExtensionActive(second);
        AssertResultsHaveDifferentDigests(first, second);
    }

    [Fact]
    public void NestedSignedZeroP4ComponentsActivateTheVersionedExtension()
    {
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with
            {
                Rotation = result.LeftFootPose.Rotation with { X = RawFloat(0x80000000) },
            });
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with
            {
                Position = result.RightFootPose.Position with { Y = RawFloat(0x80000000) },
            });
    }

    [Fact]
    public void DigestIncludesNestedSignedZeroP4RawBitsWithinAnActiveExtension()
    {
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with
            {
                Rotation = result.LeftFootPose.Rotation with { X = RawFloat(0x80000000) },
            });
        AssertActiveP4MutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with
            {
                Position = result.RightFootPose.Position with { Y = RawFloat(0x80000000) },
            });
    }

    [Fact]
    public void DigestIncludesEveryP5EventIdentityAndPayloadField()
    {
        AssertP5EventMutationChangesDigest(static value => value with { EventId = value.EventId + 1 });
        AssertP5EventMutationChangesDigest(static value => value with { SourceAnimationId = value.SourceAnimationId + 1 });
        AssertP5EventMutationChangesDigest(static value => value with { SourceActionId = value.SourceActionId + 1 });
        AssertP5EventMutationChangesDigest(static value => value with { OccurrenceHandleId = value.OccurrenceHandleId + 1 });
        AssertP5EventMutationChangesDigest(static value => value with { PlaybackEpoch = value.PlaybackEpoch + 1 });
        AssertP5EventMutationChangesDigest(static value => value with { PlaybackCycle = value.PlaybackCycle + 1 });
        AssertP5EventMutationChangesDigest(static value => value with { OwnerToken = value.OwnerToken + 1 });
        AssertP5EventMutationChangesDigest(static value => value with { EventSequence = value.EventSequence + 1 });
        AssertP5EventMutationChangesDigest(static value => value with { BoundaryOrdinal = value.BoundaryOrdinal + 1 });
        AssertP5EventMutationChangesDigest(static value => value with { AnimationTime = RawFloat(0x3F100001) });
        AssertP5EventMutationChangesDigest(static value => value with { Weight = RawFloat(0x3F200001) });
        AssertP5EventMutationChangesDigest(static value => value with { Kind = AlsTimelineEventKind.RootMotionScale });
        AssertP5EventMutationChangesDigest(static value => value with { Phase = AlsAnimationEventPhase.End });
        AssertP5EventMutationChangesDigest(static value => value with { Payload = value.Payload with { SemanticId = value.Payload.SemanticId + 1 } });
        AssertP5EventMutationChangesDigest(static value => value with { Payload = value.Payload with { EnumValue0 = value.Payload.EnumValue0 + 1 } });
        AssertP5EventMutationChangesDigest(static value => value with { Payload = value.Payload with { EnumValue1 = value.Payload.EnumValue1 + 1 } });
        AssertP5EventMutationChangesDigest(static value => value with { Payload = value.Payload with { EnumValue2 = value.Payload.EnumValue2 + 1 } });
        AssertP5EventMutationChangesDigest(static value => value with { Payload = value.Payload with { ScalarValue0 = RawFloat(0x3F300001) } });
        AssertP5EventMutationChangesDigest(static value => value with { Payload = value.Payload with { Flags = (ushort)(value.Payload.Flags + 1) } });
        AssertP5EventMutationChangesDigest(static value => value with { Payload = value.Payload with { TerminationReason = AlsActionResultCode.Completed } });
    }

    [Fact]
    public void DigestIncludesEveryP5ResultSummaryField()
    {
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { GroupId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { LeaderOccurrenceHandleId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { LeaderAnimationId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { LeaderPlaybackEpoch = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { PreviousMarkerId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { NextMarkerId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { Cycle = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { Phase = RawFloat(0x3F010001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { LeftFootPhase = RawFloat(0x3F020001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.Sync = value.Sync with { RightFootPhase = RawFloat(0x3F030001) });

        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.DynamicTransition = value.DynamicTransition with { AnimationId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.DynamicTransition = value.DynamicTransition with { Foot = AlsTransitionFoot.Right });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.DynamicTransition = value.DynamicTransition with { BlendSeconds = RawFloat(0x3F040001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.DynamicTransition = value.DynamicTransition with { PlayRate = RawFloat(0x3F050001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.DynamicTransition = value.DynamicTransition with { EffectiveWeight = RawFloat(0x3F060001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.DynamicTransition = value.DynamicTransition with { Active = 1 });

        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { OccurrenceHandleId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { ActionDefinitionId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { AnimationId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { SectionId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { SegmentId = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { PlaybackEpoch = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { PreviousTime = RawFloat(0x3F070001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { CurrentTime = RawFloat(0x3F080001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { PreviousClipTime = RawFloat(0x3F090001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { CurrentClipTime = RawFloat(0x3F0A0001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { FinalSegmentDeltaSeconds = RawFloat(0x3F0B0001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { PlayRate = RawFloat(0x3F0C0001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { BlendSeconds = RawFloat(0x3F0D0001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { EffectiveWeight = RawFloat(0x3F0E0001) });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.ActionPlayback = value.ActionPlayback with { Active = 1 });
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) => value.P5FailureCode = (AlsP5FailureCode)0xFEDC);
    }

    [Fact]
    public void DigestIncludesOutcomeFieldsAndOrderButNotUnusedStorage()
    {
        AssertP5MutationChangesDigest(static (ref AlsFrameResult value) =>
            value.ActionOutcomes.TryAdd(new AlsActionOutcome(1, 2, 3, AlsActionResultCode.Accepted)));

        var baselineOutcome = new AlsActionOutcome(10, 20, 30, AlsActionResultCode.Accepted);
        AssertP5OutcomeMutationChangesDigest(baselineOutcome, baselineOutcome with { RequestId = 11 });
        AssertP5OutcomeMutationChangesDigest(baselineOutcome, baselineOutcome with { ActionDefinitionId = 21 });
        AssertP5OutcomeMutationChangesDigest(baselineOutcome, baselineOutcome with { PlaybackEpoch = 31 });
        AssertP5OutcomeMutationChangesDigest(baselineOutcome, baselineOutcome with { ResultCode = AlsActionResultCode.Completed });

        var first = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        first.ActionOutcomes.TryAdd(baselineOutcome);
        first.ActionOutcomes.TryAdd(baselineOutcome with { RequestId = 11 });
        var second = AlsFrameResult.CreateDefault(first.Identity);
        second.ActionOutcomes.TryAdd(baselineOutcome with { RequestId = 11 });
        second.ActionOutcomes.TryAdd(baselineOutcome);
        AssertResultsHaveDifferentDigests(first, second);

        var cleared = first;
        cleared.ActionOutcomes.Clear();
        var cleanDefault = AlsFrameResult.CreateDefault(first.Identity);
        var clearedDigest = AlsResultDigest.OffsetBasis;
        var defaultDigest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.Append(ref clearedDigest, cleared);
        AlsResultDigest.Append(ref defaultDigest, cleanDefault);
        Assert.Equal(defaultDigest, clearedDigest);
    }

    [Fact]
    public void TransactionShapedP5ResultsIncludeEventsAndOutcomesWithoutTokenMetadata()
    {
        var identity = new AlsFrameIdentity(91, 3, 7);
        var first = AlsFrameResult.CreateDefault(identity);
        first.TypedEvents.TryAdd(new AlsAnimationEvent(
            1, 2, 3, 4, 5, 6, 7, 0, 8, 0.25f, 0.5f,
            AlsTimelineEventKind.SetAction, AlsAnimationEventPhase.Trigger,
            new AlsCompactEventPayload(9, 10, 11, 12, 0.75f, 0, AlsActionResultCode.None)));
        first.ActionOutcomes.TryAdd(new AlsActionOutcome(
            13, 3, 5, AlsActionResultCode.Accepted));
        var second = first;
        second.ActionOutcomes.Clear();
        second.ActionOutcomes.TryAdd(new AlsActionOutcome(
            13, 3, 5, AlsActionResultCode.InterruptedByReplacement));
        var firstDigest = AlsResultDigest.OffsetBasis;
        var secondDigest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(ref firstDigest, first);
        AlsResultDigest.Append(ref secondDigest, second);

        Assert.NotEqual(firstDigest, secondDigest);
    }

    [Fact]
    public void SignedZeroP5FloatActivatesExtensionAndRetainsRawBits()
    {
        var first = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        first.Sync = first.Sync with { Phase = RawFloat(0x80000000) };
        var second = first;
        second.Sync = second.Sync with { Phase = RawFloat(0x00000000) };

        AssertResultsHaveDifferentDigests(first, second);
    }

    [Fact]
    public void P5DigestDistinguishesQuietNanPayloadBits()
    {
        var first = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        first.Sync = first.Sync with { Phase = RawFloat(0x7FC00001) };
        var second = first;
        second.Sync = second.Sync with { Phase = RawFloat(0x7FC00002) };

        AssertResultsHaveDifferentDigests(first, second);
    }

    [Fact]
    public void PayloadSignedZeroAloneActivatesP5AndPayloadNanBitsRemainDistinct()
    {
        var positiveZero = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        positiveZero.TypedEvents.TryAdd(CreateLegacyEvent(default));
        var negativeZero = AlsFrameResult.CreateDefault(positiveZero.Identity);
        negativeZero.TypedEvents.TryAdd(CreateLegacyEvent(
            new AlsCompactEventPayload(0, 0, 0, 0, RawFloat(0x80000000), 0, AlsActionResultCode.None)));
        AssertResultsHaveDifferentDigests(positiveZero, negativeZero);

        var firstNan = AlsFrameResult.CreateDefault(positiveZero.Identity);
        firstNan.TypedEvents.TryAdd(CreateLegacyEvent(
            new AlsCompactEventPayload(0, 0, 0, 0, RawFloat(0x7FC00001), 0, AlsActionResultCode.None)));
        var secondNan = AlsFrameResult.CreateDefault(positiveZero.Identity);
        secondNan.TypedEvents.TryAdd(CreateLegacyEvent(
            new AlsCompactEventPayload(0, 0, 0, 0, RawFloat(0x7FC00002), 0, AlsActionResultCode.None)));
        AssertResultsHaveDifferentDigests(firstNan, secondNan);
    }

    [Fact]
    public void CanonicalP5SummaryVectorFreezesMarkerOrderAndStorageWidths()
    {
        var result = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        ApplyCanonicalP5Summary(ref result);
        var digest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(ref digest, result);

        var expected = 13994752607362685853UL;
        AppendP5Reference(ref expected, result);
        Assert.Equal(1132633367238012033UL, expected);
        Assert.Equal(expected, digest);
    }

    [Fact]
    public void CanonicalP5ExtensionFollowsTheCompleteP4Extension()
    {
        var result = CreateFullyActiveP4DigestVector();
        ApplyCanonicalP5Summary(ref result);
        var digest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(ref digest, result);

        var expected = 18222991105242177202UL;
        AppendP5Reference(ref expected, result);
        Assert.Equal(9323201923248110348UL, expected);
        Assert.Equal(expected, digest);
    }

    [Fact]
    public void ClearedEventStorageDoesNotActivateOrEnterTheP5Digest()
    {
        var result = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        result.TypedEvents.TryAdd(CreateP5Event());
        result.TypedEvents.Clear();
        var clean = AlsFrameResult.CreateDefault(result.Identity);
        var resultDigest = AlsResultDigest.OffsetBasis;
        var cleanDigest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(ref resultDigest, result);
        AlsResultDigest.Append(ref cleanDigest, clean);

        Assert.Equal(cleanDigest, resultDigest);
    }

    private static void AssertP5EventMutationChangesDigest(
        Func<AlsAnimationEvent, AlsAnimationEvent> mutate)
    {
        var baseline = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        var animationEvent = CreateP5Event();
        baseline.TypedEvents.TryAdd(animationEvent);
        var changed = AlsFrameResult.CreateDefault(baseline.Identity);
        changed.TypedEvents.TryAdd(mutate(animationEvent));
        AssertResultsHaveDifferentDigests(baseline, changed);
    }

    private static void AssertP5OutcomeMutationChangesDigest(
        AlsActionOutcome baselineOutcome,
        AlsActionOutcome changedOutcome)
    {
        var baseline = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        baseline.ActionOutcomes.TryAdd(baselineOutcome);
        var changed = AlsFrameResult.CreateDefault(baseline.Identity);
        changed.ActionOutcomes.TryAdd(changedOutcome);
        AssertResultsHaveDifferentDigests(baseline, changed);
    }

    private static void AssertP5MutationChangesDigest(ResultMutation mutate)
    {
        var baseline = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        var changed = baseline;
        mutate(ref changed);
        AssertResultsHaveDifferentDigests(baseline, changed);
    }

    private static AlsAnimationEvent CreateP5Event() => new(
        EventId: 1,
        SourceAnimationId: 2,
        SourceActionId: 3,
        OccurrenceHandleId: 4,
        PlaybackEpoch: 5,
        PlaybackCycle: 6,
        OwnerToken: 7,
        EventSequence: 8,
        BoundaryOrdinal: 9,
        AnimationTime: RawFloat(0x3E800001),
        Weight: RawFloat(0x3F000001),
        Kind: AlsTimelineEventKind.EarlyBlendOut,
        Phase: AlsAnimationEventPhase.Tick,
        Payload: new AlsCompactEventPayload(
            10, 11, 12, 13, RawFloat(0x3F400001), 0x000F,
            AlsActionResultCode.InterruptedByEarlyBlendOut));

    private static AlsAnimationEvent CreateLegacyEvent(AlsCompactEventPayload payload) => new(
        EventId: 1,
        SourceAnimationId: -1,
        SourceActionId: -1,
        OccurrenceHandleId: -1,
        PlaybackEpoch: 0,
        PlaybackCycle: 0,
        OwnerToken: 0,
        EventSequence: 0,
        BoundaryOrdinal: 0,
        AnimationTime: 0.25f,
        Weight: 1f,
        Kind: AlsTimelineEventKind.Generic,
        Phase: AlsAnimationEventPhase.Trigger,
        Payload: payload);

    private static void ApplyCanonicalP5Summary(ref AlsFrameResult result)
    {
        result.Sync = new AlsSyncResult(
            1, 2, 3, 4, 5, 6, 7,
            RawFloat(0x3E800001), RawFloat(0x3F000001), RawFloat(0x3F400001));
        result.DynamicTransition = new AlsDynamicTransitionPlaybackSummary(
            11, AlsTransitionFoot.Right, RawFloat(0x3D800001),
            RawFloat(0x3F800001), RawFloat(0x3F200001), 1);
        result.ActionPlayback = new AlsActionPlayback(
            12, 13, 14, 15, 16, 17,
            RawFloat(0x3D010001), RawFloat(0x3D020001),
            RawFloat(0x3D030001), RawFloat(0x3D040001),
            RawFloat(0x3D050001), RawFloat(0x3F800002),
            RawFloat(0x3D060001), RawFloat(0x3F600001), 1);
        result.ActionOutcomes.TryAdd(
            new AlsActionOutcome(21, 22, 23, AlsActionResultCode.InterruptedByReplacement));
        result.ActionOutcomes.TryAdd(
            new AlsActionOutcome(24, 25, 26, AlsActionResultCode.Accepted));
        result.P5FailureCode = (AlsP5FailureCode)0xFEDC;
    }

    private static void AppendP5Reference(ref ulong digest, in AlsFrameResult result)
    {
        AppendReferenceByte(ref digest, (byte)'P');
        AppendReferenceByte(ref digest, (byte)'5');
        AppendReferenceByte(ref digest, (byte)'A');
        AppendReferenceByte(ref digest, (byte)'1');

        for (var index = 0; index < result.TypedEvents.Count; index++)
        {
            var animationEvent = result.TypedEvents[index];
            AppendReferenceInt32(ref digest, animationEvent.EventId);
            AppendReferenceInt32(ref digest, animationEvent.SourceAnimationId);
            AppendReferenceInt32(ref digest, animationEvent.SourceActionId);
            AppendReferenceInt32(ref digest, animationEvent.OccurrenceHandleId);
            AppendReferenceInt64(ref digest, animationEvent.PlaybackEpoch);
            AppendReferenceInt64(ref digest, animationEvent.PlaybackCycle);
            AppendReferenceUInt64(ref digest, animationEvent.OwnerToken);
            AppendReferenceInt64(ref digest, animationEvent.EventSequence);
            AppendReferenceInt32(ref digest, animationEvent.BoundaryOrdinal);
            AppendReferenceFloat(ref digest, animationEvent.AnimationTime);
            AppendReferenceFloat(ref digest, animationEvent.Weight);
            AppendReferenceByte(ref digest, (byte)animationEvent.Kind);
            AppendReferenceByte(ref digest, (byte)animationEvent.Phase);
            AppendReferenceInt32(ref digest, animationEvent.Payload.SemanticId);
            AppendReferenceInt32(ref digest, animationEvent.Payload.EnumValue0);
            AppendReferenceInt32(ref digest, animationEvent.Payload.EnumValue1);
            AppendReferenceInt32(ref digest, animationEvent.Payload.EnumValue2);
            AppendReferenceFloat(ref digest, animationEvent.Payload.ScalarValue0);
            AppendReferenceUInt16(ref digest, animationEvent.Payload.Flags);
            AppendReferenceUInt16(ref digest, (ushort)animationEvent.Payload.TerminationReason);
        }

        AppendReferenceInt32(ref digest, result.Sync.GroupId);
        AppendReferenceInt32(ref digest, result.Sync.LeaderOccurrenceHandleId);
        AppendReferenceInt32(ref digest, result.Sync.LeaderAnimationId);
        AppendReferenceInt64(ref digest, result.Sync.LeaderPlaybackEpoch);
        AppendReferenceInt32(ref digest, result.Sync.PreviousMarkerId);
        AppendReferenceInt32(ref digest, result.Sync.NextMarkerId);
        AppendReferenceInt64(ref digest, result.Sync.Cycle);
        AppendReferenceFloat(ref digest, result.Sync.Phase);
        AppendReferenceFloat(ref digest, result.Sync.LeftFootPhase);
        AppendReferenceFloat(ref digest, result.Sync.RightFootPhase);

        AppendReferenceInt32(ref digest, result.DynamicTransition.AnimationId);
        AppendReferenceByte(ref digest, (byte)result.DynamicTransition.Foot);
        AppendReferenceFloat(ref digest, result.DynamicTransition.BlendSeconds);
        AppendReferenceFloat(ref digest, result.DynamicTransition.PlayRate);
        AppendReferenceFloat(ref digest, result.DynamicTransition.EffectiveWeight);
        AppendReferenceByte(ref digest, result.DynamicTransition.Active);

        AppendReferenceInt32(ref digest, result.ActionPlayback.OccurrenceHandleId);
        AppendReferenceInt32(ref digest, result.ActionPlayback.ActionDefinitionId);
        AppendReferenceInt32(ref digest, result.ActionPlayback.AnimationId);
        AppendReferenceInt32(ref digest, result.ActionPlayback.SectionId);
        AppendReferenceInt32(ref digest, result.ActionPlayback.SegmentId);
        AppendReferenceInt64(ref digest, result.ActionPlayback.PlaybackEpoch);
        AppendReferenceFloat(ref digest, result.ActionPlayback.PreviousTime);
        AppendReferenceFloat(ref digest, result.ActionPlayback.CurrentTime);
        AppendReferenceFloat(ref digest, result.ActionPlayback.PreviousClipTime);
        AppendReferenceFloat(ref digest, result.ActionPlayback.CurrentClipTime);
        AppendReferenceFloat(ref digest, result.ActionPlayback.FinalSegmentDeltaSeconds);
        AppendReferenceFloat(ref digest, result.ActionPlayback.PlayRate);
        AppendReferenceFloat(ref digest, result.ActionPlayback.BlendSeconds);
        AppendReferenceFloat(ref digest, result.ActionPlayback.EffectiveWeight);
        AppendReferenceByte(ref digest, result.ActionPlayback.Active);

        AppendReferenceInt32(ref digest, result.ActionOutcomes.Count);
        for (var index = 0; index < result.ActionOutcomes.Count; index++)
        {
            var outcome = result.ActionOutcomes[index];
            AppendReferenceInt64(ref digest, outcome.RequestId);
            AppendReferenceInt32(ref digest, outcome.ActionDefinitionId);
            AppendReferenceInt64(ref digest, outcome.PlaybackEpoch);
            AppendReferenceUInt16(ref digest, (ushort)outcome.ResultCode);
        }

        AppendReferenceUInt16(ref digest, (ushort)result.P5FailureCode);
    }

    private static void AppendReferenceFloat(ref ulong digest, float value) =>
        AppendReferenceInt32(ref digest, BitConverter.SingleToInt32Bits(value));

    private static void AppendReferenceInt32(ref ulong digest, int value) =>
        AppendReferenceUInt32(ref digest, unchecked((uint)value));

    private static void AppendReferenceInt64(ref ulong digest, long value) =>
        AppendReferenceUInt64(ref digest, unchecked((ulong)value));

    private static void AppendReferenceUInt16(ref ulong digest, ushort value)
    {
        AppendReferenceByte(ref digest, (byte)value);
        AppendReferenceByte(ref digest, (byte)(value >> 8));
    }

    private static void AppendReferenceUInt32(ref ulong digest, uint value)
    {
        for (var index = 0; index < 4; index++)
        {
            AppendReferenceByte(ref digest, (byte)(value >> (index * 8)));
        }
    }

    private static void AppendReferenceUInt64(ref ulong digest, ulong value)
    {
        for (var index = 0; index < 8; index++)
        {
            AppendReferenceByte(ref digest, (byte)(value >> (index * 8)));
        }
    }

    private static void AppendReferenceByte(ref ulong digest, byte value)
    {
        digest ^= value;
        digest *= 1099511628211UL;
    }

    private static void AssertMutationChangesDigest(ResultMutation mutate)
    {
        var baseline = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        var changed = baseline;
        mutate(ref changed);
        AssertResultsHaveDifferentDigests(baseline, changed);
    }

    private static void AssertActiveP4MutationChangesDigest(ResultMutation mutate)
    {
        var baseline = CreateActiveP4Result();
        var changed = baseline;
        mutate(ref changed);
        AssertP4ExtensionActive(baseline);
        AssertP4ExtensionActive(changed);
        AssertResultsHaveDifferentDigests(baseline, changed);
    }

    private static void AssertP4ValueChangesDigest(
        ResultMutation setFirstValue,
        ResultMutation setSecondValue)
    {
        var first = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        var second = first;
        setFirstValue(ref first);
        setSecondValue(ref second);
        AssertP4ExtensionActive(first);
        AssertP4ExtensionActive(second);
        AssertResultsHaveDifferentDigests(first, second);
    }

    private static AlsFrameResult CreateActiveP4Result()
    {
        var result = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        result.P4ReasonCode = (AlsP4ReasonCode)1;
        return result;
    }

    private static void AssertResultsHaveDifferentDigests(
        in AlsFrameResult first,
        in AlsFrameResult second)
    {
        var baselineDigest = AlsResultDigest.OffsetBasis;
        var changedDigest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(ref baselineDigest, first);
        AlsResultDigest.Append(ref changedDigest, second);

        Assert.NotEqual(baselineDigest, changedDigest);
    }

    private static void AssertP4ExtensionActive(in AlsFrameResult result)
    {
        var inactive = AlsFrameResult.CreateDefault(result.Identity);
        var inactiveDigest = AlsResultDigest.OffsetBasis;
        var activeDigest = AlsResultDigest.OffsetBasis;
        AlsResultDigest.Append(ref inactiveDigest, inactive);
        AlsResultDigest.Append(ref activeDigest, result);
        Assert.NotEqual(inactiveDigest, activeDigest);
    }

    private static float RawFloat(uint bits) =>
        BitConverter.Int32BitsToSingle(unchecked((int)bits));

    private static AlsFrameResult CreateFullyActiveP4DigestVector()
    {
        var result = AlsFrameResult.CreateDefault(new AlsFrameIdentity(77, 3, 5));
        result.AimRelativeYaw = RawFloat(0x3D010101);
        result.AimRelativePitch = RawFloat(0x3D020202);
        result.HeadWeight = RawFloat(0x3D030303);
        result.SpineWeight = RawFloat(0x3D040404);
        result.UpperBodyWeight = RawFloat(0x3D050505);
        result.SpineResidualYaw = RawFloat(0x3D060606);
        result.TurnAnimationId = 101;
        result.TurnCurveId = 102;
        result.TurnPhase = RawFloat(0x3D090909);
        result.TurnPlayRate = RawFloat(0x3D0A0A0A);
        result.TurnNominalDegrees = -1234;
        result.TurnDirection = -7;
        result.TurnActive = 0xA1;
        result.TurnYawDelta = RawFloat(0x3D0E0E0E);
        result.RotateAnimationId = 201;
        result.RotateCurveId = 202;
        result.RotatePhase = RawFloat(0x3D111111);
        result.RotatePlayRate = RawFloat(0x3D121212);
        result.RotateDirection = 9;
        result.RotateActive = 0xB2;
        result.RotateYawDelta = RawFloat(0x3D151515);
        result.PelvisOffset = new Vector3(
            RawFloat(0x3D161601),
            RawFloat(0x3D161602),
            RawFloat(0x3D161603));
        result.LeftFootPose = new AlsFootPoseOutput(
            new Vector3(
                RawFloat(0x3D171701),
                RawFloat(0x3D171702),
                RawFloat(0x3D171703)),
            new Quaternion(
                RawFloat(0x3D181801),
                RawFloat(0x3D181802),
                RawFloat(0x3D181803),
                RawFloat(0x3D181804)),
            RawFloat(0x3D191919),
            301);
        result.RightFootPose = new AlsFootPoseOutput(
            new Vector3(
                RawFloat(0x3D1A1A01),
                RawFloat(0x3D1A1A02),
                RawFloat(0x3D1A1A03)),
            new Quaternion(
                RawFloat(0x3D1B1B01),
                RawFloat(0x3D1B1B02),
                RawFloat(0x3D1B1B03),
                RawFloat(0x3D1B1B04)),
            RawFloat(0x3D1C1C1C),
            302);
        result.LeftFootReleaseReason = AlsFootReleaseReason.BaseChanged;
        result.RightFootReleaseReason = AlsFootReleaseReason.Teleported;
        result.LeftFootIkWeight = RawFloat(0x3D1C1D1D);
        result.RightFootIkWeight = RawFloat(0x3D1C1E1E);
        result.LeftFootLockCurve = RawFloat(0x3D1C1F1F);
        result.RightFootLockCurve = RawFloat(0x3D1C2020);
        result.NextLeftFootProbeOrigin = new Vector3(
            RawFloat(0x3D1D1D01),
            RawFloat(0x3D1D1D02),
            RawFloat(0x3D1D1D03));
        result.NextRightFootProbeOrigin = new Vector3(
            RawFloat(0x3D1E1E01),
            RawFloat(0x3D1E1E02),
            RawFloat(0x3D1E1E03));
        result.P4ModifierOperationTicks = 0x0102030405060708L;
        result.P4ReasonCode = (AlsP4ReasonCode)0x2345;
        return result;
    }

    private static ulong EvaluateDigest(int frames)
    {
        var digest = AlsResultDigest.OffsetBasis;
        var state = default(AlsRuntimeState);
        var result = default(AlsFrameResult);

        for (var frame = 1; frame <= frames; frame++)
        {
            var input = AlsSyntheticInputSource.Create(new AlsFrameIdentity(frame, 0, 1), 1f / 60f);
            AlsSyntheticLocomotionModel.Evaluate(input, ref state, ref result);
            AlsResultDigest.Append(ref digest, result);
        }

        return digest;
    }

    private delegate void ResultMutation(ref AlsFrameResult result);
}
