using System.Threading;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Exchange;

public sealed class AlsFrameExchange
{
    private const long UnpublishedFrame = -1;

    private readonly AlsFrameInput[] _inputs = new AlsFrameInput[2];
    private readonly AlsFrameResult[] _results = new AlsFrameResult[2];
    private readonly long[] _publishedInputFrames = [UnpublishedFrame, UnpublishedFrame];
    private readonly long[] _publishedResultFrames = [UnpublishedFrame, UnpublishedFrame];
    private readonly long[] _consumedResultFrames = [UnpublishedFrame, UnpublishedFrame];

    public void PublishInput(AlsFrameInput input)
    {
        var slot = Slot(input.Identity.FrameId);

        Volatile.Write(ref _publishedInputFrames[slot], UnpublishedFrame);
        _inputs[slot] = input;
        Volatile.Write(ref _publishedInputFrames[slot], input.Identity.FrameId);
    }

    public bool TryReadInput(AlsFrameIdentity identity, out AlsFrameInput input)
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

    public void PublishResult(AlsFrameResult result)
    {
        var slot = Slot(result.Identity.FrameId);

        Volatile.Write(ref _publishedResultFrames[slot], UnpublishedFrame);
        _results[slot] = result;
        Volatile.Write(ref _publishedResultFrames[slot], result.Identity.FrameId);
    }

    public bool TryConsumeResult(
        AlsFrameIdentity identity,
        out AlsFrameResult result)
    {
        var slot = Slot(identity.FrameId);

        if (Volatile.Read(ref _publishedResultFrames[slot]) != identity.FrameId ||
            Volatile.Read(ref _consumedResultFrames[slot]) == identity.FrameId)
        {
            result = default;
            return false;
        }

        result = _results[slot];

        if (result.Identity != identity)
        {
            result = default;
            return false;
        }

        Volatile.Write(ref _consumedResultFrames[slot], identity.FrameId);
        return true;
    }

    private static int Slot(long frameId) => (int)(frameId & 1L);
}
