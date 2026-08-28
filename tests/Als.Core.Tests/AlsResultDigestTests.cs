using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
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

        // This locks the P4 marker, version 1, declaration order, and exactly-once hashing.
        Assert.Equal(17974511757808246964UL, digest);
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
