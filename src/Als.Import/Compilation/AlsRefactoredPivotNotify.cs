using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Generated Standing direction notification, not a sequence notify.
/// Dispatch after graph/Parent update so Movement Details reads it next frame.</summary>
public sealed class AlsRefactoredPivotNotify
{
    private readonly AlsRefactoredDirectionResources _direction;

    public AlsRefactoredPivotNotify(AlsRefactoredAnimationCatalog catalog, AlsRefactoredDirectionResources direction)
    {
        if (direction.Crouching || direction.CatalogDigest != catalog.IndexDigest)
            throw new ArgumentException("Foreign Pivot direction graph.");
        _direction = direction;
        var source = AlsRefactoredRotatePlayers.Blueprint(false);
        var text = catalog.Read(source).GetProperty("nativeText").GetString()!;
        var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(text, source, source + ":EventGraph"), true);
        var entry = graph.Nodes.Single(n => n.Kind == "K2Node_Event" && n.Body.Contains("MemberName=\"AnimNotify_ActivatePivot\"", StringComparison.Ordinal));
        var call = graph.Nodes.Single(n => n.Kind == "K2Node_CallFunction" && n.Member == "ActivatePivot");
        graph.Function(call, "ActivatePivot", "ALS.AlsAnimationInstance");
        graph.Links(call, "execute", (entry, "then"));
        var execute = call.Pins.Values.Single(p => p.Name == "execute");
        if (entry.Pins.Values.Single(p => p.Name == "then").Links != call.Name + " " + execute.Id + "," ||
            call.Pins.Values.Single(p => p.Name == "then").Links != "")
            throw new ArgumentException("Pivot notification execution differs.");
        var (parent, pin) = graph.Follow(call, "self");
        graph.Self(parent);
        if (parent.Member != "GetParent" || pin.Name != "ReturnValue")
            throw new ArgumentException("Pivot notification receiver differs.");
    }

    public int Dispatch(AlsRefactoredDirectionRuntime direction, AlsRefactoredMovementParentRuntime parent, in AlsFrameIdentity identity)
    {
        if (!ReferenceEquals(direction.Resources, _direction)) throw new ArgumentException("Foreign Pivot source.");
        direction.ValidateCommit(identity.FrameId);
        parent.ValidateContext(identity, _direction.CatalogDigest);
        var update = direction.Candidate;
        for (var i = 0; i < update.EventCount; i++)
        {
            var e = update.GetEvent(i);
            if (e.Kind != AlsGroundedEventKind.TransitionStarted || e.NotifyIndex != 0 ||
                (uint)e.SourceIndex >= _direction.Edges.Length || _direction.Edges[e.SourceIndex].StartNotify != 0)
                throw new ArgumentException("Unimplemented direction notification.");
        }
        // Generated-class notifications append directly, without source weight,
        // chance or follower filtering. Preserve repeated transition events.
        for (var i = 0; i < update.EventCount; i++) parent.ActivatePivot(identity.FrameId);
        return update.EventCount;
    }
}
