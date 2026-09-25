using System.Globalization;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

internal static class AlsRefactoredDirectionRuleCompiler
{
    internal static AlsRefactoredDirectionRule Compile(string text, string machine, string[] states)
    {
        var graph = new Graph(text, true); var visited = new HashSet<string>();
        var root = graph.One("AnimGraphNode_TransitionResult", ""); visited.Add(root.Name);
        var bindings = Regex.Matches(root.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True");
        if (bindings.Count != 0)
        {
            Require(bindings.Count == 1 && graph.Nodes.Count() == 1 && bindings[0].Groups[1].Value == "bCanEnterTransition" && root.Pins.Values.All(p => p.Links == ""), "Unexpected direction rule binding.");
            var path = string.Join(".", Regex.Matches(bindings[0].Groups[2].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value));
            const string prefix = "GetParent.GroundedState.MovementDirection.b";
            Require(path.StartsWith(prefix, StringComparison.Ordinal) && Enum.TryParse<AlsRefactoredDirectionRuleKind>(path[prefix.Length..], out _), "Foreign movement direction.");
            var kind = Enum.Parse<AlsRefactoredDirectionRuleKind>(path[prefix.Length..]);
            Require(kind is >= AlsRefactoredDirectionRuleKind.Forward and <= AlsRefactoredDirectionRuleKind.Right, "Non-direction binding.");
            return new(kind);
        }
        Node Follow(Node node, string input, string output = "ReturnValue")
        {
            var (other, pin) = graph.Follow(node, input); Require(pin.Name == output, "Unexpected direction output."); visited.Add(other.Name); return other;
        }
        void MathNode(Node node, string name)
        {
            Require(node.Kind is "K2Node_CallFunction" or "K2Node_PromotableOperator" or "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported direction operator.");
            Require(node.Member == name && node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/Engine.KismetMathLibrary'\"", StringComparison.Ordinal), "Foreign direction function.");
        }
        void Property(Node node, string property)
        {
            var parts = Regex.Matches(node.Body, "(?m)^      Path\\((\\d+)\\)=\"([^\"]+)\"");
            Require(node.Kind == "K2Node_PropertyAccess" && parts.Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).SequenceEqual(Enumerable.Range(0, parts.Count)) &&
                string.Join(".", parts.Select(m => m.Groups[2].Value)) == "GetParent." + property, "Foreign direction input property.");
        }
        double Number(Node node, string pin)
        {
            var value = graph.Literal(node, pin); return value == "" ? 0 : double.Parse(value, CultureInfo.InvariantCulture);
        }
        var and = Follow(root, "bCanEnterTransition"); MathNode(and, "BooleanAND");
        var hips = Follow(and, "A"); var feet = Follow(and, "B");
        MathNode(feet, "LessEqual_DoubleDouble"); Property(Follow(feet, "A", "Value"), "FeetState.FeetCrossingAmount");
        Require(Number(feet, "B") == 0, "Feet crossing threshold changed.");
        AlsRefactoredDirectionRule rule;
        if (hips.Member == "Less_DoubleDouble")
        {
            Require(and.Pins.Values.Count(p => !p.Output && p.Name != "self") == 3, "Unexpected unlock operand count.");
            MathNode(hips, "Less_DoubleDouble"); Require(Number(hips, "B") == .5, "Unlock threshold changed.");
            var abs = Follow(hips, "A"); MathNode(abs, "Abs"); Property(Follow(abs, "A", "Value"), "GroundedState.HipsDirectionLockAmount");
            var full = Follow(and, "C"); MathNode(full, "GreaterEqual_DoubleDouble"); Require(Number(full, "B") == 1, "Full-state threshold changed.");
            var weight = Follow(full, "A"); graph.Self(weight);
            Require(weight.Kind == "K2Node_AnimGetter" && weight.Member == "GetInstanceStateWeight" && graph.Literal(weight, "MachineIndex") == "0" && graph.Literal(weight, "StateIndex") == "0", "Unsupported state weight getter.");
            Require(weight.Body.Contains("SourceNode=\"/Script/AnimGraph.AnimGraphNode_StateMachine'" + machine + "'\"", StringComparison.Ordinal), "Foreign direction machine.");
            var state = Regex.Match(weight.Body, "SourceStateNode=\"/Script/AnimGraph.AnimStateNode'([^']+)'\"").Groups[1].Value;
            var index = Array.IndexOf(states, state); Require(index >= 0, "Foreign direction state."); rule = new(AlsRefactoredDirectionRuleKind.Unlock, index);
        }
        else
        {
            Require(and.Pins.Values.Count(p => !p.Output && p.Name != "self") == 2, "Unexpected hip-lock operand count.");
            var left = hips.Member == "LessEqual_DoubleDouble"; MathNode(hips, left ? "LessEqual_DoubleDouble" : "GreaterEqual_DoubleDouble");
            Property(Follow(hips, "A", "Value"), "GroundedState.HipsDirectionLockAmount");
            Require(Number(hips, "B") == (left ? -.5 : .5), "Hip lock threshold changed.");
            rule = new(left ? AlsRefactoredDirectionRuleKind.HipsLeft : AlsRefactoredDirectionRuleKind.HipsRight);
        }
        Require(graph.Nodes.All(n => visited.Contains(n.Name)), "Unconsumed direction predicate node.");
        Require(graph.Nodes.All(n => n.Pins.Values.All(p => p.Name is "self" or "A" or "B" or "C" or "ReturnValue" or "Value" or "ErrorTolerance" or "MachineIndex" or "StateIndex" or "bCanEnterTransition")), "Unexpected predicate operand.");
        return rule;
    }
    private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
}
