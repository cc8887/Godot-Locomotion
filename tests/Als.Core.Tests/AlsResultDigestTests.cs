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
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.AimRelativeYaw = 0.1f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.AimRelativePitch = 0.2f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.HeadWeight = 0.3f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.SpineWeight = 0.4f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.UpperBodyWeight = 0.5f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.SpineResidualYaw = 0.6f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.TurnAnimationId = 1);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.TurnCurveId = 2);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.TurnPhase = 0.7f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.TurnPlayRate = 0.8f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.TurnNominalDegrees = 90);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.TurnDirection = -1);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.TurnActive = 1);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.TurnYawDelta = 0.9f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.RotateAnimationId = 3);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.RotateCurveId = 4);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.RotatePhase = 0.11f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.RotatePlayRate = 0.12f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.RotateDirection = 1);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.RotateActive = 1);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.RotateYawDelta = 0.13f);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.PelvisOffset = new Vector3(1f, 2f, 3f));
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { Position = new Vector3(1f, 0f, 0f) });
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f) });
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { LockAmount = 0.3f });
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.LeftFootPose = result.LeftFootPose with { PlatformId = 5 });
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { Position = new Vector3(0f, 1f, 0f) });
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f) });
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { LockAmount = 0.5f });
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.RightFootPose = result.RightFootPose with { PlatformId = 6 });
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.NextLeftFootProbeOrigin = new Vector3(4f, 5f, 6f));
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.NextRightFootProbeOrigin = new Vector3(7f, 8f, 9f));
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.P4ModifierElapsedTicks = 17);
        AssertMutationChangesDigest(static (ref AlsFrameResult result) => result.P4ReasonCode = (AlsP4ReasonCode)1);
    }

    [Fact]
    public void SignedZeroP4MutationActivatesTheVersionedExtension()
    {
        AssertMutationChangesDigest(static (ref AlsFrameResult result) =>
            result.AimRelativeYaw = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)));
    }

    private static void AssertMutationChangesDigest(ResultMutation mutate)
    {
        var baseline = AlsFrameResult.CreateDefault(new AlsFrameIdentity(1, 0, 1));
        var changed = baseline;
        mutate(ref changed);
        var baselineDigest = AlsResultDigest.OffsetBasis;
        var changedDigest = AlsResultDigest.OffsetBasis;

        AlsResultDigest.Append(ref baselineDigest, baseline);
        AlsResultDigest.Append(ref changedDigest, changed);

        Assert.NotEqual(baselineDigest, changedDigest);
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
