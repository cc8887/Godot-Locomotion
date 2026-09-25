using System.Globalization;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

internal static class AlsRefactoredMovementDetailsRuleCompiler
{
    internal static AlsRefactoredMovementDetailsRule Compile(string text, string standingMachine, bool automatic)
    {
        var graph = new Graph(text, true); var visited = new HashSet<string>(); var active = new HashSet<string>();
        var root = graph.One("AnimGraphNode_TransitionResult", ""); visited.Add(root.Name);
        var bindings = Regex.Matches(root.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True");
        if (bindings.Count > 0)
        {
            Require(!automatic && bindings.Count == 1 && graph.Nodes.Count() == 1 && root.Pins.Values.All(p => p.Links == "") &&
                bindings[0].Groups[1].Value == "bCanEnterTransition" && bindings[0].Groups[2].Value == "\"GetParent\",\"StandingState\",\"bPivotActive\"", "Unexpected pivot binding.");
            return AlsRefactoredMovementDetailsRule.Pivot;
        }
        if (automatic)
        {
            Require(graph.Nodes.Count() == 1 && root.Pins.Values.All(p => p.Links == "") && graph.Literal(root, "bCanEnterTransition").Equals("False", StringComparison.OrdinalIgnoreCase), "Automatic rule contains executable logic.");
            return AlsRefactoredMovementDetailsRule.AutomaticRemainingTime;
        }
        string Input(Node node, string name)
        {
            var pin = node.Pins.Values.Single(p => p.Name == name && !p.Output);
            if (pin.Links == "")
            {
                var line = Regex.Matches(node.Body, @"(?m)^      CustomProperties Pin [^\r\n]+").Single(m => m.Value.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal)).Value;
                var literal = Regex.Match(line, "(?:^|,)DefaultValue=\"((?:\\\\.|[^\"])*)\"").Groups[1].Value.Replace("\\\"", "\"");
                return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number.ToString("R", CultureInfo.InvariantCulture) : literal;
            }
            var (other, output) = graph.Follow(node, name);
            Require(output.Name == (other.Kind == "K2Node_PropertyAccess" ? "Value" : "ReturnValue"), "Foreign predicate output.");
            return Visit(other);
        }
        string Visit(Node node)
        {
            Require(active.Add(node.Name), "Cyclic movement predicate."); visited.Add(node.Name); string value;
            if (node.Kind == "K2Node_PropertyAccess")
            {
                var parts = Regex.Matches(node.Body, "(?m)^      Path\\((\\d+)\\)=\"([^\"]+)\"");
                Require(parts.Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).SequenceEqual(Enumerable.Range(0, parts.Count)), "Unordered property path.");
                value = string.Join(".", parts.Select(m => m.Groups[2].Value));
                Require(value is "GetParent.Gait" or "GetParent.PoseState.GroundedAmount" or "GetParent.PoseState.UnweightedGaitRunningAmount", "Foreign movement input.");
            }
            else if (node.Kind == "K2Node_AnimGetter")
            {
                graph.Self(node);
                Require(node.Member == "GetInstanceMachineWeight" && graph.Literal(node, "MachineIndex") == "0" &&
                    node.Body.Contains("SourceNode=\"/Script/AnimGraph.AnimGraphNode_StateMachine'" + standingMachine + "'\"", StringComparison.Ordinal), "Foreign standing machine getter.");
                value = "StandingMachineWeight";
            }
            else
            {
                Require(node.Kind is "K2Node_CallFunction" or "K2Node_PromotableOperator" or "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported predicate node.");
                var tags = node.Member is "EqualEqual_GameplayTag" or "IsGameplayTagValid";
                Require(node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/" + (tags ? "GameplayTags.BlueprintGameplayTagLibrary" : "Engine.KismetMathLibrary") + "'\"", StringComparison.Ordinal), "Foreign predicate function.");
                var args = node.Member switch
                {
                    "BooleanAND" or "BooleanOR" => node.Pins.Values.Where(p => !p.Output && p.Name != "self").Select(p => p.Name).ToArray(),
                    "Less_DoubleDouble" or "GreaterEqual_DoubleDouble" or "EqualEqual_GameplayTag" => new[] { "A", "B" },
                    "Not_PreBool" => new[] { "A" }, "IsGameplayTagValid" => new[] { "GameplayTag" },
                    _ => throw new ArgumentException("Unsupported movement predicate operation.")
                };
                Require(node.Pins.Values.All(p => p.Output || p.Name is "self" or "ErrorTolerance" || args.Contains(p.Name)), "Unconsumed predicate operand.");
                value = node.Member + "(" + string.Join(",", args.Select(a => Input(node, a))) + ")";
            }
            active.Remove(node.Name); return value;
        }
        var signature = Input(root, "bCanEnterTransition");
        Require(graph.Nodes.All(n => visited.Contains(n.Name)), "Unconsumed movement predicate node.");
        const string gait = "BooleanOR(EqualEqual_GameplayTag(GetParent.Gait,(TagName=\"Als.Gait.Running\")),EqualEqual_GameplayTag(GetParent.Gait,(TagName=\"Als.Gait.Sprinting\")))";
        return signature switch
        {
            "BooleanAND(" + gait + ",GreaterEqual_DoubleDouble(GetParent.PoseState.GroundedAmount,1),Less_DoubleDouble(StandingMachineWeight,1))" => AlsRefactoredMovementDetailsRule.StartWhileEntering,
            "BooleanAND(" + gait + ",GreaterEqual_DoubleDouble(GetParent.PoseState.GroundedAmount,1),GreaterEqual_DoubleDouble(StandingMachineWeight,1))" => AlsRefactoredMovementDetailsRule.StartFromWalk,
            "BooleanAND(" + gait + ",Less_DoubleDouble(GetParent.PoseState.GroundedAmount,1))" => AlsRefactoredMovementDetailsRule.RunWhileNotGrounded,
            "BooleanAND(BooleanOR(EqualEqual_GameplayTag(GetParent.Gait,(TagName=\"Als.Gait.Walking\")),Not_PreBool(IsGameplayTagValid(GetParent.Gait))),Less_DoubleDouble(GetParent.PoseState.UnweightedGaitRunningAmount,0.2))" => AlsRefactoredMovementDetailsRule.Walk,
            _ => throw new ArgumentException("Movement predicate differs: " + signature)
        };
    }
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
