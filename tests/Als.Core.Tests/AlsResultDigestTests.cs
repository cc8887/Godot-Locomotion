using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Simulation;

namespace GodotAls.Core.Tests;

public sealed class AlsResultDigestTests
{
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
