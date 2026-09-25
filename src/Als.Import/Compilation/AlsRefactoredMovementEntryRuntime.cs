using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original cache66 source callbacks before node119. Begin must run
/// once on the selected deferred cache context; Complete follows Source.Update.
/// The enclosing cache scheduler and full character transaction own invocation.</summary>
public sealed class AlsRefactoredMovementEntryRuntime
{
    private readonly AlsRefactoredStandingResources _resources;
    private readonly AlsRefactoredStanceCallbackRuntime _callbacks;
    private AlsRefactoredMovementParentRuntime? _owner;
    private AlsFrameIdentity _identity;
    private bool _prepared, _complete;
    public AlsRefactoredMovementEntryRuntime(AlsRefactoredStandingResources resources, AlsRefactoredStanceCallbacks callbacks)
    {
        if (resources.CatalogDigest != callbacks.CatalogDigest || resources.MovementCallbacks.ToArray().Any(c => !callbacks.Nodes.Contains(c)))
            throw new ArgumentException("Foreign Movement entry callbacks.");
        _resources = resources; _callbacks = new(callbacks);
    }
    public void Begin(in AlsPoseUpdateContext context, AlsRefactoredMovementParentRuntime parent, bool initializeInstance = false)
    {
        if (_prepared || _owner is not null && !ReferenceEquals(_owner,parent) || context.UpdateCounter is not { HasUpdated: true } || !context.HasSharedContext)
            throw new ArgumentException("Invalid Movement entry owner/context.");
        parent.ValidateContext(context.Identity,_resources.CatalogDigest);
        try
        {
            _callbacks.Prepare(context.Identity.FrameId,context.UpdateCounter.Value,initializeInstance);
            foreach (var node in _resources.MovementCallbacks)
            {
                var command = _callbacks.Enter(context.Identity.FrameId,node.PropertyIndex);
                if (command is not null) parent.Apply(context.Identity,_resources.CatalogDigest,command);
            }
            _identity = context.Identity; _owner ??= parent; _prepared = true; _complete = false;
        }
        catch { _callbacks.Cancel(); throw; }
    }
    public void Complete(long frame)
    {
        if (!_prepared || _complete || frame != _identity.FrameId) throw new ArgumentException("Invalid Movement entry source completion.");
        for (var i = _resources.MovementCallbacks.Length - 1; i >= 0; i--) _callbacks.Leave(frame,_resources.MovementCallbacks[i].PropertyIndex);
        _callbacks.ValidateCommit(frame); _complete = true;
    }
    public void ValidateCommit(long frame)
    {
        if (!_prepared || !_complete || frame != _identity.FrameId) throw new ArgumentException("Movement entry source has not completed.");
        _callbacks.ValidateCommit(frame);
    }
    public void Commit(long frame) { ValidateCommit(frame); _callbacks.Commit(frame); Cancel(); }
    public void Cancel() { _prepared = _complete = false; _callbacks.Cancel(); }
}
