using System.Globalization;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsRefactoredWeaponRuleCompiler
{
    public static AlsRefactoredWeaponRule[] Compile(AlsRefactoredAnimationCatalog catalog, AlsRefactoredWeaponMachineResources resources)
    {
        if (catalog.IndexDigest != resources.CatalogDigest) throw new ArgumentException("Foreign weapon rule catalog.");
        var source = AlsRefactoredWeaponMachineResources.Blueprint(resources.Kind);
        var payload = catalog.Read(source);
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => n.GetProperty("compiledNodeIndex").GetInt32());
        var machine = nodes[resources.CompiledNode].GetProperty("path").GetString()!;
        return resources.Edges.ToArray().Select((edge, index) => CompileGraph(
            AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, source,
                nodes[edge.RuleNode].GetProperty("graph").GetString()!),
            source[(source.LastIndexOf('.') + 1)..] + machine[source.Length..], index)).ToArray();
    }

    internal static AlsRefactoredWeaponRule CompileGraph(string text, string machine, int edge)
    {
        var graph = new Graph(text, directProperties: true); var visited = new HashSet<string>();
        var root = graph.One("AnimGraphNode_TransitionResult", ""); visited.Add(root.Name);
        Node Follow(Node node, string pin, string output = "ReturnValue")
        {
            var (other, value) = graph.Follow(node, pin);
            Require(value.Name == output, "Unexpected weapon predicate output."); visited.Add(other.Name); return other;
        }
        void Function(Node node, string member, string owner)
        {
            Require(node.Member == member && node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/" + owner + "'\"", StringComparison.Ordinal), "Foreign weapon predicate function.");
            Require(node.Pins.Values.Where(p => p.Name == "self").All(p => p.Links == ""), "Foreign predicate receiver.");
        }
        void Property(Node node, string path)
        {
            var parts = Regex.Matches(node.Body, "(?m)^      Path\\((\\d+)\\)=\"([^\"]+)\"");
            Require(node.Kind == "K2Node_PropertyAccess" && parts.Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).SequenceEqual(Enumerable.Range(0, parts.Count)) &&
                string.Join(".", parts.Select(m => m.Groups[2].Value)) == "GetParent." + path, "Foreign weapon parent property.");
        }
        void Tag(Node node, string property, string tag, bool negate = false)
        {
            Require(node.Kind is "K2Node_PromotableOperator" or "K2Node_CallFunction", "Unsupported tag predicate.");
            Function(node, negate ? "NotEqual_GameplayTag" : "EqualEqual_GameplayTag", "GameplayTags.BlueprintGameplayTagLibrary");
            Property(Follow(node, "A", "Value"), property);
            // Graph.Literal is intended for scalar literals; gameplay tags contain escaped quotes.
            var pin = node.Pins.Values.Single(p => p.Name == "B"); Require(!pin.Output && pin.Links == "", "Connected tag constant.");
            var line = node.Body.Split('\n').Single(l => l.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal));
            Require(line.Contains(",DefaultValue=\"(TagName=\\\"" + tag + "\\\")\",", StringComparison.Ordinal), "Foreign weapon tag constant.");
        }
        var expression = Follow(root, "bCanEnterTransition");
        AlsRefactoredWeaponRule rule;
        if (edge is 0 or 1 or 4)
        {
            Tag(expression, "RotationMode", "Als.RotationMode.Aiming", edge == 1);
            rule = new(edge == 1 ? AlsRefactoredWeaponRuleKind.NotAiming : AlsRefactoredWeaponRuleKind.Aiming);
        }
        else if (edge is 2 or 3)
        {
            Require(expression.Kind == "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported weapon delay gate.");
            Function(expression, "BooleanAND", "Engine.KismetMathLibrary");
            Property(Follow(expression, "A", "Value"), edge == 2 ? "TransitionsState.bTransitionsAllowed" : "LocomotionState.bMoving");
            var comparison = Follow(expression, "B");
            Require(comparison.Kind == "K2Node_PromotableOperator", "Unsupported weapon time comparison.");
            Function(comparison, "GreaterEqual_DoubleDouble", "Engine.KismetMathLibrary");
            var clock = Follow(comparison, "A"); graph.Self(clock);
            Require(clock.Kind == "K2Node_AnimGetter" && clock.Member == "GetInstanceCurrentStateElapsedTime" &&
                clock.Body.Contains("SourceNode=\"/Script/AnimGraph.AnimGraphNode_StateMachine'" + machine + "'\"", StringComparison.Ordinal) && graph.Literal(clock, "MachineIndex") == "0", "Foreign weapon state clock.");
            var delay = double.Parse(graph.Literal(comparison, "B"), CultureInfo.InvariantCulture);
            Require(delay == 3, "Weapon delay differs.");
            rule = new(edge == 2 ? AlsRefactoredWeaponRuleKind.AllowedAfterDelay : AlsRefactoredWeaponRuleKind.MovingAfterDelay, delay);
        }
        else if (edge == 5)
        {
            Require(expression.Kind == "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported weapon fallback gate.");
            Function(expression, "BooleanOR", "Engine.KismetMathLibrary");
            Tag(Follow(expression, "A"), "LocomotionMode", "Als.LocomotionMode.InAir");
            Tag(Follow(expression, "B"), "Gait", "Als.Gait.Sprinting");
            rule = new(AlsRefactoredWeaponRuleKind.SprintOrAir);
        }
        else throw new ArgumentOutOfRangeException(nameof(edge));
        Require(graph.Nodes.All(n => visited.Contains(n.Name)), "Unconsumed weapon predicate node.");
        Require(graph.Nodes.All(n => n.Pins.Values.All(p => p.Name is "self" or "A" or "B" or "ReturnValue" or "Value" or "ErrorTolerance" or "MachineIndex" or "bCanEnterTransition")), "Extra weapon predicate operand.");
        return rule;
    }
    private static void Require(bool value, string message) { if (!value) throw new ArgumentException(message); }
}
