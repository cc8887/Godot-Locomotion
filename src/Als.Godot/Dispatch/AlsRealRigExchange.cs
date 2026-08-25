using System.Threading;
using GodotAls.Core.Contracts;

namespace GodotAls.Dispatch;

public readonly record struct AlsRealRigInput(AlsFrameIdentity Identity, double DeltaTime);

public readonly record struct AlsRealRigResult(AlsFrameIdentity Identity, ulong PoseDigest);

public sealed class AlsRealRigExchange
{
    private const long UnpublishedFrame = -1;

    private readonly AlsRealRigInput[] _inputs = new AlsRealRigInput[2];
    private readonly AlsRealRigResult[] _results = new AlsRealRigResult[2];
    private readonly long[] _publishedInputFrames = [UnpublishedFrame, UnpublishedFrame];
    private readonly long[] _publishedResultFrames = [UnpublishedFrame, UnpublishedFrame];
    private readonly long[] _consumedResultFrames = [UnpublishedFrame, UnpublishedFrame];

    public void PublishInput(AlsRealRigInput input)
    {
        var slot = Slot(input.Identity.FrameId);
        Volatile.Write(ref _publishedInputFrames[slot], UnpublishedFrame);
        _inputs[slot] = input;
        Volatile.Write(ref _publishedInputFrames[slot], input.Identity.FrameId);
    }

    public bool TryReadInput(AlsFrameIdentity identity, out AlsRealRigInput input)
    {
        var slot = Slot(identity.FrameId);
        if (Volatile.Read(ref _publishedInputFrames[slot]) != identity.FrameId)
        {
            input = default;
            return false;
        }

        input = _inputs[slot];
        return input.Identity == identity;
    }

    public void PublishResult(AlsRealRigResult result)
    {
        var slot = Slot(result.Identity.FrameId);
        Volatile.Write(ref _publishedResultFrames[slot], UnpublishedFrame);
        _results[slot] = result;
        Volatile.Write(ref _publishedResultFrames[slot], result.Identity.FrameId);
    }

    public bool TryConsumeResult(
        AlsFrameIdentity identity,
        out AlsRealRigResult result,
        out bool stale)
    {
        var slot = Slot(identity.FrameId);
        if (Volatile.Read(ref _publishedResultFrames[slot]) != identity.FrameId ||
            Volatile.Read(ref _consumedResultFrames[slot]) == identity.FrameId)
        {
            result = default;
            stale = false;
            return false;
        }

        result = _results[slot];
        if (result.Identity != identity)
        {
            stale = true;
            result = default;
            return false;
        }

        Volatile.Write(ref _consumedResultFrames[slot], identity.FrameId);
        stale = false;
        return true;
    }

    private static int Slot(long frameId) => (int)(frameId & 1L);
}
