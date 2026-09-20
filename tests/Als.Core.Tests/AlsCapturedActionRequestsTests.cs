using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsCapturedActionRequestsTests
{
    [Fact]
    public void RetryReadsPreserveRequestAndPhysicalAcceptance()
    {
        var input = new AlsCapturedActionRequests(); var id = new AlsFrameIdentity(12, 7, 3);
        var request = new AlsActionRequest(12, AlsActionCommand.Start, 8, 2, 100, 3);
        input.Capture(id, request);
        var bank = new AlsMontageRuntime([], [new(8, 14, AlsMontageSlot.BaseLayer, 3, 1.5f, 0, 1,
            new(AlsActionLifecycleMode.MontageAutoBlendOut, .1f, AlsActionBlendOption.Linear, .3f, AlsActionBlendOption.Linear, -1))]);
        var actions = new AlsMontageActionRuntime(bank, [new(8, 2, 0, 1, .2f, true)]);
        Prepare(); var first = bank.Candidate.ToArray(); var outcome = actions.Outcomes[0];
        actions.Discard(); Prepare();
        Assert.Equal(first, bank.Candidate.ToArray()); Assert.Equal(outcome, actions.Outcomes[0]);
        Assert.Equal(request, input.Read(id)); actions.Commit(id);
        var next = new AlsFrameIdentity(13, 7, 3);
        input.Capture(next, request); // Replayed external request ID is still idempotent at the worker.
        actions.Begin(next, 1f / 60); actions.ApplyRequest(input.Read(next)); actions.Complete();
        Assert.Equal(0, actions.Outcomes.Count); Assert.Single(bank.Candidate.ToArray());
        void Prepare() { actions.Begin(id, 1f / 60); actions.ApplyRequest(input.Read(id)); actions.Complete(); }
    }

    [Fact]
    public void WrongFrameCharacterOrGenerationCannotReadOrOverwriteCapture()
    {
        var input = new AlsCapturedActionRequests(); var id = new AlsFrameIdentity(5, 7, 3);
        var none = AlsActionRequest.None with { SlotGeneration = 3 }; input.Capture(id, none);
        foreach (var bad in new[] { new AlsFrameIdentity(6, 7, 3), new(5, 8, 3), new(5, 7, 4) })
            Assert.Throws<InvalidOperationException>(() => input.Read(bad));
        foreach (var bad in new[] { id, new AlsFrameIdentity(7, 7, 3), new(6, 8, 3), new(6, 7, 2) })
            Assert.Throws<ArgumentException>(() => input.Capture(bad, none with { SlotGeneration = bad.SlotGeneration }));
        Assert.Equal(none, input.Read(id));
        var nextGeneration = new AlsFrameIdentity(6, 7, 4);
        Assert.Throws<ArgumentException>(() => input.Capture(nextGeneration, none));
        input.Capture(nextGeneration, none with { SlotGeneration = 4 });
        Assert.Throws<InvalidOperationException>(() => input.Read(id));
    }

    [Theory]
    [InlineData(AlsActionCommand.CancelForRuntimeFailure, 1, 0, -1, 0)]
    [InlineData(AlsActionCommand.Start, 0, 0, 0, 100)]
    [InlineData(AlsActionCommand.Start, 1, 0, -1, 100)]
    [InlineData(AlsActionCommand.Cancel, 1, 0, 0, 0)]
    [InlineData(AlsActionCommand.Cancel, 1, 0, -1, 100)]
    [InlineData(AlsActionCommand.None, 1, -1, -1, 0)]
    public void RejectsInvalidExternalCommands(AlsActionCommand command, long request, int definition, int section, int priority)
    {
        var input = new AlsCapturedActionRequests();
        Assert.Throws<ArgumentException>(() => input.Capture(new(1, 7, 3), new(request, command, definition, section, priority, 3)));
        Assert.Equal(default, input.Identity);
    }

    [Fact]
    public void CapturedRequestsAllocateNothingAfterWarmup()
    {
        var input = new AlsCapturedActionRequests(); var none = AlsActionRequest.None with { SlotGeneration = 1 };
        for (var i = 1; i <= 300; i++) input.Capture(new(i, 7, 1), none);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 301; i <= 1300; i++) { var id = new AlsFrameIdentity(i, 7, 1); input.Capture(id, none); _ = input.Read(id); _ = input.Read(id); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
