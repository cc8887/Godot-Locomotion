using System.Globalization;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

internal static class AlsRefactoredGroundedRuleCompiler
{
    internal static AlsRefactoredGroundedRule Compile(string text, bool automatic = false)
    {
        var graph = new Graph(text, true); var root = graph.One("AnimGraphNode_TransitionResult", "");
        var visited = new HashSet<string> { root.Name }; var active = new HashSet<string>();
        Node Follow(Node node, string pin, string output = "ReturnValue")
        {
            var (other, value) = graph.Follow(node, pin);
            Require(value.Name == output, "Foreign Grounded predicate output.");
            visited.Add(other.Name); return other;
        }
        string Property(Node node)
        {
            Require(node.Kind == "K2Node_PropertyAccess", "Expected Grounded Parent input.");
            var parts = Regex.Matches(node.Body, "(?m)^      Path\\((\\d+)\\)=\"([^\"]+)\"");
            Require(parts.Select(m => int.Parse(m.Groups[1].Value)).SequenceEqual(Enumerable.Range(0, parts.Count)), "Unordered Grounded input path.");
            return string.Join(".", parts.Select(m => m.Groups[2].Value));
        }
        void Function(Node node, string owner)
        {
            Require(node.Kind is "K2Node_CallFunction" or "K2Node_PromotableOperator" or "K2Node_CommutativeAssociativeBinaryOperator",
                "Unsupported Grounded predicate node.");
            Require(node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/" + owner + "'\"", StringComparison.Ordinal) &&
                node.Pins.Values.Where(p => p.Name == "self").All(p => p.Links == ""), "Foreign Grounded predicate function.");
        }
        AlsRefactoredGroundedRule Input(Node node, string name)
        {
            var pin = node.Pins.Values.Single(p => p.Name == name && !p.Output);
            if (pin.Links != "")
            {
                var (other, output) = graph.Follow(node, name);
                Require(output.Name == (other.Kind == "K2Node_PropertyAccess" ? "Value" : "ReturnValue"), "Foreign Grounded boolean output.");
                return Visit(other);
            }
            return new(bool.Parse(graph.Literal(node, name)) ? AlsRefactoredGroundedPredicate.True : AlsRefactoredGroundedPredicate.False);
        }
        AlsRefactoredGroundedRule Visit(Node node)
        {
            Require(active.Add(node.Name), "Cyclic Grounded predicate."); visited.Add(node.Name);
            AlsRefactoredGroundedRule rule;
            if (node.Kind == "K2Node_PropertyAccess")
                rule = new(Property(node) switch
                {
                    "GetParent.LocomotionState.bMovingSmooth" => AlsRefactoredGroundedPredicate.Moving,
                    "GetParent.RotateInPlaceState.bRotatingLeft" => AlsRefactoredGroundedPredicate.RotateLeft,
                    "GetParent.RotateInPlaceState.bRotatingRight" => AlsRefactoredGroundedPredicate.RotateRight,
                    _ => throw new ArgumentException("Foreign Grounded boolean input.")
                });
            else if (node.Member == "EqualEqual_GameplayTag")
            {
                Function(node, "GameplayTags.BlueprintGameplayTagLibrary");
                var property = Property(Follow(node, "A", "Value"));
                var pin = node.Pins.Values.Single(p => p.Name == "B"); Require(pin.Links == "" && !pin.Output, "Connected Grounded tag literal.");
                var line = node.Body.Split('\n').Single(l => l.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal));
                var tag = new[] { "Als.Stance.Standing", "Als.Stance.Crouching", "Als.GroundedEntryMode.FromRoll" }
                    .SingleOrDefault(t => line.Contains(",DefaultValue=\"(TagName=\\\"" + t + "\\\")\",", StringComparison.Ordinal));
                rule = new((property, tag) switch
                {
                    ("GetParent.Stance", "Als.Stance.Standing") => AlsRefactoredGroundedPredicate.Standing,
                    ("GetParent.Stance", "Als.Stance.Crouching") => AlsRefactoredGroundedPredicate.Crouching,
                    ("GetParent.GroundedEntryMode", "Als.GroundedEntryMode.FromRoll") => AlsRefactoredGroundedPredicate.FromRoll,
                    _ => throw new ArgumentException("Foreign Grounded gameplay tag: " + property + "/" + tag)
                });
            }
            else if (node.Member == "IsGameplayTagValid")
            {
                Function(node, "GameplayTags.BlueprintGameplayTagLibrary");
                var pin = node.Pins.Values.Single(p => !p.Output && p.Name != "self");
                Require(Property(Follow(node, pin.Name, "Value")) == "GetParent.Stance", "Foreign Grounded validity check.");
                rule = new(AlsRefactoredGroundedPredicate.ValidStance);
            }
            else
            {
                Function(node, "Engine.KismetMathLibrary");
                if (node.Member == "GreaterEqual_DoubleDouble")
                {
                    var property = Property(Follow(node, "A", "Value"));
                    var number = float.Parse(graph.Literal(node, "B"), CultureInfo.InvariantCulture);
                    Require(number == .5f, "Grounded pose threshold differs.");
                    rule = new(property switch
                    {
                        "GetParent.PoseState.StandingAmount" => AlsRefactoredGroundedPredicate.StandingAmountAtLeast,
                        "GetParent.PoseState.CrouchingAmount" => AlsRefactoredGroundedPredicate.CrouchingAmountAtLeast,
                        _ => throw new ArgumentException("Foreign Grounded pose input.")
                    }, threshold: number);
                }
                else if (node.Member == "Not_PreBool") rule = new(AlsRefactoredGroundedPredicate.Not, Input(node, "A"));
                else
                {
                    Require(node.Member is "BooleanAND" or "BooleanOR", "Unsupported Grounded boolean operator.");
                    var operands = node.Pins.Values.Where(p => !p.Output && p.Name != "self").Select(p => p.Name).Order().ToArray();
                    Require(operands.Length is 2 or 3 && operands.SequenceEqual(new[] { "A", "B", "C" }.Take(operands.Length)), "Foreign Grounded operand.");
                    var kind = node.Member == "BooleanAND" ? AlsRefactoredGroundedPredicate.And : AlsRefactoredGroundedPredicate.Or;
                    rule = new(kind, Input(node, "A"), Input(node, "B"));
                    if (operands.Length == 3) rule = new(kind, rule, Input(node, "C"));
                }
            }
            active.Remove(node.Name); return rule;
        }
        var result = Input(root, "bCanEnterTransition");
        Require(graph.Nodes.All(n => visited.Contains(n.Name)), "Unconsumed Grounded predicate node.");
        Require(!automatic || graph.Nodes.Count() == 1 && result.Kind == AlsRefactoredGroundedPredicate.False, "Executable automatic Grounded rule.");
        return result;
    }
    private static void Require(bool value, string message) { if (!value) throw new ArgumentException(message); }
}
