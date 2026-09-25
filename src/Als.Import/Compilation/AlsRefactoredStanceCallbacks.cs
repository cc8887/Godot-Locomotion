using System.Text.Json;
using System.Text.RegularExpressions;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public enum AlsRefactoredStanceFunction
{
    RefreshGroundedMovement, SetHipsDirection, InitializeStandingMovement,
    RefreshStandingMovement, ResetPivot, RefreshCrouchingMovement,
    RefreshDynamicTransitions, RefreshRotateInPlace, InitializeTurnInPlace, RefreshTurnInPlace
}

public sealed record AlsRefactoredStanceCallback(int PropertyIndex, int SourcePropertyIndex,
    AlsRefactoredStanceFunction Function, bool OnBecomeRelevant, string HipsDirection);

/// <summary>Original stance CallFunction nodes, including their source recursion
/// links. Parent updates must run before traversing SourcePropertyIndex. This is
/// a binding table, not a substitute for the state/cache graph traversal.</summary>
public sealed class AlsRefactoredStanceCallbacks
{
    private readonly AlsRefactoredStanceCallback[] _nodes;
    public ReadOnlySpan<AlsRefactoredStanceCallback> Nodes => _nodes;
    public string CatalogDigest { get; }
    public AlsRefactoredStanceCallbacks(AlsRefactoredAnimationCatalog catalog, bool crouching)
    {
        CatalogDigest = catalog.IndexDigest;
        _nodes = Compile(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(crouching)), crouching);
    }

    internal static AlsRefactoredStanceCallback[] Compile(JsonElement payload, bool crouching)
    {
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(crouching);
        Expect(payload, new { source = blueprint, @class = "AnimBlueprint" });
        var text = payload.GetProperty("nativeText").GetString()!;
        var all = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => n.GetProperty("propertyIndex").GetInt32());
        var result = new List<AlsRefactoredStanceCallback>();
        foreach (var (id, node) in all)
        {
            if (node.GetProperty("class").GetString() != "AnimGraphNode_CallFunction") continue;
            var path = node.GetProperty("path").GetString()!;
            var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(text, blueprint, node.GetProperty("graph").GetString()!), true);
            var outer = graph.Named(path.Split('.')[^1]);
            Require(outer.Kind == "AnimGraphNode_CallFunction", "Callback node class differs.");
            var innerName = Regex.Match(outer.Body, "(?m)^      InnerGraph=\"/Script/Engine.EdGraph'([^']+)'\"").Groups[1].Value;
            Require(innerName != "", "Missing callback function graph.");
            var inner = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(text, blueprint, path + "." + innerName), true);
            var calls = inner.Nodes.ToArray();
            Require(calls.Length == 1 && calls[0].Kind == "K2Node_CallFunction", "Unsupported callback expression graph.");
            var call = calls[0]; inner.Self(call);
            Require(Enum.TryParse<AlsRefactoredStanceFunction>(call.Member, out var function) && Enum.IsDefined(function), "Unknown stance function.");
            Require(call.Pins.Values.All(p => p.Links == ""), "Connected callback arguments are not supported.");
            // The editor inner-call template keeps Forward for all six nodes.
            // The exposed outer pin is copied into the generated event stub.
            var hips = function == AlsRefactoredStanceFunction.SetHipsDirection ? graph.Literal(outer, "NewHipsDirection") : "";
            Require(hips == "" || hips is "Forward" or "Backward" or "LeftForward" or "LeftBackward" or "RightForward" or "RightBackward", "Unknown hips direction.");
            Require(call.Pins.Values.All(p => p.Name is "execute" or "then" or "self" || function == AlsRefactoredStanceFunction.SetHipsDirection && p.Name == "NewHipsDirection"), "Unexpected callback argument.");
            Require(outer.Pins.Values.All(p => p.Name is "Source" or "Pose" || function == AlsRefactoredStanceFunction.SetHipsDirection && p.Name == "NewHipsDirection"), "Unexpected exposed callback argument.");
            var runtime = node.GetProperty("runtime");
            var site = runtime.GetProperty("callSite").GetString()!;
            Require(site is "OnUpdate" or "OnBecomeRelevant", "Unsupported stance callback phase.");
            foreach (var policy in new[] { runtime, node.GetProperty("authoredProperties").GetProperty("Node") })
            {
                Expect(policy, new { callSite = site });
                foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(policy.GetProperty(cb), new { functionName = "None" });
            }
            var expectedRelevant = function is AlsRefactoredStanceFunction.SetHipsDirection or AlsRefactoredStanceFunction.ResetPivot or AlsRefactoredStanceFunction.InitializeStandingMovement or AlsRefactoredStanceFunction.InitializeTurnInPlace;
            Require((site == "OnBecomeRelevant") == expectedRelevant, "Stance function call phase changed.");
            var source = runtime.GetProperty("source").GetProperty("linkId").GetInt32();
            Require(runtime.GetProperty("source").GetProperty("sourceLinkId").GetInt32() == id && all.ContainsKey(source), "Invalid callback source identity.");
            var (linked, pin) = graph.Follow(outer, "Source");
            Require(pin.Name == "Pose" && all[source].GetProperty("path").GetString() == node.GetProperty("graph").GetString() + "." + linked.Name, "Callback source differs from authored graph.");
            result.Add(new(id, source, function, expectedRelevant, hips));
        }
        Require(result.Count == (crouching ? 12 : 15), "Incomplete stance callbacks.");
        return result.OrderBy(n => n.PropertyIndex).ToArray();
    }
    private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
}
