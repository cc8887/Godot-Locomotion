using System.Globalization;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

internal static class AlsRefactoredStandingRuleCompiler
{
    internal static AlsRefactoredStandingRule Compile(string text, string machine, string move, string stop, bool automatic)
    {
        var graph = new Graph(text, true); var root = graph.One("AnimGraphNode_TransitionResult", "");
        var visited = new HashSet<string> { root.Name }; var active = new HashSet<string>();
        var bindings = Regex.Matches(root.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True");
        if (bindings.Count > 0)
        {
            Require(!automatic && bindings.Count == 1 && graph.Nodes.Count() == 1 && root.Pins.Values.All(p => p.Links == "") &&
                bindings[0].Groups[1].Value == "bCanEnterTransition", "Unsupported Standing property binding.");
            return bindings[0].Groups[2].Value switch
            {
                "\"GetParent\",\"LocomotionState\",\"bMovingSmooth\"" => AlsRefactoredStandingRule.Moving,
                "\"GetParent\",\"RotateInPlaceState\",\"bRotatingLeft\"" => AlsRefactoredStandingRule.RotateLeft,
                "\"GetParent\",\"RotateInPlaceState\",\"bRotatingRight\"" => AlsRefactoredStandingRule.RotateRight,
                _ => throw new ArgumentException("Foreign Standing input.")
            };
        }
        if (automatic)
        {
            Require(graph.Nodes.Count() == 1 && root.Pins.Values.All(p => p.Links == "") &&
                graph.Literal(root, "bCanEnterTransition").Equals("False", StringComparison.OrdinalIgnoreCase), "Executable automatic Standing predicate.");
            return AlsRefactoredStandingRule.Automatic;
        }
        string Input(Node node, string name)
        {
            var pin = node.Pins.Values.Single(p => p.Name == name && !p.Output);
            if (pin.Links == "")
            {
                var literal = graph.Literal(node, name);
                return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number.ToString("R", CultureInfo.InvariantCulture) : literal;
            }
            var (other, output) = graph.Follow(node, name);
            Require(output.Name == (other.Kind == "K2Node_PropertyAccess" ? "Value" : "ReturnValue"), "Foreign Standing predicate output.");
            return Visit(other);
        }
        string Visit(Node node)
        {
            Require(active.Add(node.Name), "Cyclic Standing predicate."); visited.Add(node.Name); string value;
            if (node.Kind == "K2Node_PropertyAccess")
            {
                var parts = Regex.Matches(node.Body, "(?m)^      Path\\((\\d+)\\)=\"([^\"]+)\"");
                Require(parts.Select(m => int.Parse(m.Groups[1].Value)).SequenceEqual(Enumerable.Range(0, parts.Count)), "Unordered Standing property path.");
                value = string.Join(".", parts.Select(m => m.Groups[2].Value));
                Require(value == "GetParent.LocomotionState.bMovingSmooth", "Foreign Standing input.");
            }
            else if (node.Kind == "K2Node_AnimGetter")
            {
                graph.Self(node);
                Require(node.Member == "GetInstanceStateWeight" && graph.Literal(node, "MachineIndex") == "0" && graph.Literal(node, "StateIndex") == "0" &&
                    node.Body.Contains("SourceNode=\"/Script/AnimGraph.AnimGraphNode_StateMachine'" + machine + "'\"", StringComparison.Ordinal), "Foreign Standing weight getter.");
                value = node.Body.Contains("SourceStateNode=\"/Script/AnimGraph.AnimStateNode'" + move + "'\"", StringComparison.Ordinal) ? "MoveWeight" :
                    node.Body.Contains("SourceStateNode=\"/Script/AnimGraph.AnimStateNode'" + stop + "'\"", StringComparison.Ordinal) ? "StopWeight" : throw new ArgumentException("Foreign Standing weight state.");
            }
            else
            {
                Require(node.Kind is "K2Node_CallFunction" or "K2Node_PromotableOperator" or "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported Standing node.");
                Require(node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/Engine.KismetMathLibrary'\"", StringComparison.Ordinal), "Foreign Standing function.");
                string[] args = node.Member switch { "BooleanAND" or "GreaterEqual_DoubleDouble" => ["A", "B"], "Not_PreBool" => ["A"], _ => throw new ArgumentException("Unsupported Standing operator.") };
                Require(node.Pins.Values.All(p => p.Output || p.Name == "self" || args.Contains(p.Name) ||
                    node.Member == "GreaterEqual_DoubleDouble" && p.Name == "ErrorTolerance" && p.Links == ""), "Unconsumed Standing operand.");
                value = node.Member + "(" + string.Join(",", args.Select(a => Input(node, a))) + ")";
            }
            active.Remove(node.Name); return value;
        }
        var signature = Input(root, "bCanEnterTransition"); Require(graph.Nodes.All(n => visited.Contains(n.Name)), "Unconsumed Standing node.");
        return signature switch
        {
            "Not_PreBool(GetParent.LocomotionState.bMovingSmooth)" => AlsRefactoredStandingRule.Stopping,
            "BooleanAND(Not_PreBool(GetParent.LocomotionState.bMovingSmooth),GreaterEqual_DoubleDouble(MoveWeight,1))" => AlsRefactoredStandingRule.StoppingFullyMoving,
            "GreaterEqual_DoubleDouble(StopWeight,1)" => AlsRefactoredStandingRule.StopFull,
            _ => throw new ArgumentException("Standing predicate differs: " + signature)
        };
    }
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
