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
}
