using GodotAls.Core.Exchange;

namespace GodotAls.Dispatch;

public sealed class AlsHarnessEntry
{
    public AlsHarnessEntry(
        AlsSlotHandle handle,
        AlsFrameExchange exchange,
        long startFrame)
    {
        Handle = handle;
        Exchange = exchange;
        StartFrame = startFrame;
    }

    public AlsSlotHandle Handle { get; }

    public AlsFrameExchange Exchange { get; }

    public long StartFrame { get; }

    public AlsVisualWorkerRoot Worker { get; set; } = null!;

    public int ObservedOffMainThread;
}
