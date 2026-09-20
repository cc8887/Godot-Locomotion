using GodotAls.Core.Contracts;

namespace GodotAls.Core.Actions;

// Main-thread input capture only. The worker owns acceptance and playback.
// Reads never consume an edge or create another request during frame retry.
public sealed class AlsCapturedActionRequests
{
    public AlsFrameIdentity Identity { get; private set; }
    private AlsActionRequest _request = AlsActionRequest.None;

    public void ValidateCapture(AlsFrameIdentity identity, in AlsActionRequest request)
    {
        if (identity.FrameId <= 0 || identity.SlotGeneration == 0 ||
            Identity.SlotGeneration != 0 && (identity.CharacterId != Identity.CharacterId ||
                identity.SlotGeneration < Identity.SlotGeneration ||
                identity.SlotGeneration == Identity.SlotGeneration && identity.FrameId != Identity.FrameId + 1 ||
                identity.SlotGeneration > Identity.SlotGeneration && identity.FrameId < Identity.FrameId))
            throw new ArgumentException("Action capture requires the next frame of this character, or a newer generation.");
        var valid = request.SlotGeneration == identity.SlotGeneration && (request.Command switch
        {
            AlsActionCommand.None => request.RequestId == -1 && request.ActionDefinitionId == -1 && request.StartSectionId == -1 && request.Priority == 0,
            AlsActionCommand.Start => request.RequestId > 0 && request.ActionDefinitionId >= 0 && request.StartSectionId >= 0 && request.Priority >= 0,
            AlsActionCommand.Cancel => request.RequestId > 0 && request.ActionDefinitionId >= 0 && request.StartSectionId == -1 && request.Priority == 0,
            _ => false, // Runtime failure cancellation is not a player command.
        });
        if (!valid) throw new ArgumentException("Action request does not match its captured input identity or command shape.");
    }

    public void Capture(AlsFrameIdentity identity, in AlsActionRequest request)
    {
        ValidateCapture(identity, request); Identity = identity; _request = request;
    }

    public AlsActionRequest Read(AlsFrameIdentity identity) => Identity.SlotGeneration != 0 && identity == Identity
        ? _request : throw new InvalidOperationException("Action input was not captured for this exact frame, character and generation.");

    // A semantic deactivation discards input that has not reached Gather.
    // Already published frame copies remain immutable and are fenced by the worker.
    public void DiscardUnpublished(long publishedFrame)
    {
        if (publishedFrame < 0) throw new ArgumentOutOfRangeException(nameof(publishedFrame));
        if (Identity.FrameId > publishedFrame) _request = AlsActionRequest.None with { SlotGeneration = Identity.SlotGeneration };
    }
}
