using System.Globalization;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

internal static class AlsRefactoredLocomotionRuleCompiler
{
    internal static AlsRefactoredLocomotionRule Compile(string text, bool automatic = false)
    {
        var graph = new Graph(text, true); var root = graph.One("AnimGraphNode_TransitionResult", "");
        var visited = new HashSet<string> { root.Name }; var active = new HashSet<string>();
        Node Follow(Node node, string pin)
        {
            var (other, output) = graph.Follow(node, pin);
            Require(output.Name == "Value", "Foreign Locomotion property output.");
            visited.Add(other.Name); return other;
        }
        string Property(Node node)
        {
            Require(node.Kind == "K2Node_PropertyAccess", "Expected Locomotion Parent input.");
            var parts = Regex.Matches(node.Body, "(?m)^      Path\\((\\d+)\\)=\"([^\"]+)\"");
            Require(parts.Select(m => int.Parse(m.Groups[1].Value)).SequenceEqual(Enumerable.Range(0, parts.Count)), "Unordered Locomotion input path.");
            return string.Join(".", parts.Select(m => m.Groups[2].Value));
        }
        void Function(Node node, string owner)
        {
            Require(node.Kind is "K2Node_CallFunction" or "K2Node_PromotableOperator" or "K2Node_CommutativeAssociativeBinaryOperator",
                "Unsupported Locomotion predicate node.");
            Require(node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/" + owner + "'\"", StringComparison.Ordinal) &&
                node.Pins.Values.Where(p => p.Name == "self").All(p => p.Links == ""), "Foreign Locomotion predicate function.");
        }
        AlsRefactoredLocomotionRule Input(Node node, string name)
        {
            var pin = node.Pins.Values.Single(p => p.Name == name && !p.Output);
            if (pin.Links == "") return new(bool.Parse(graph.Literal(node, name))
                ? AlsRefactoredLocomotionPredicate.True : AlsRefactoredLocomotionPredicate.False);
            var (other, output) = graph.Follow(node, name);
            Require(output.Name == (other.Kind == "K2Node_PropertyAccess" ? "Value" : "ReturnValue"), "Foreign Locomotion boolean output.");
            return Visit(other);
        }
        AlsRefactoredLocomotionRule Visit(Node node)
        {
            Require(active.Add(node.Name), "Cyclic Locomotion predicate."); visited.Add(node.Name);
            AlsRefactoredLocomotionRule rule;
            if (node.Kind == "K2Node_PropertyAccess")
                rule = new(Property(node) switch
                {
                    "GetParent.InAirState.bJumped" => AlsRefactoredLocomotionPredicate.Jumped,
                    "GetParent.LocomotionState.bHasInput" => AlsRefactoredLocomotionPredicate.HasInput,
                    "GetParent.RotateInPlaceState.bRotatingLeft" => AlsRefactoredLocomotionPredicate.RotateLeft,
                    "GetParent.RotateInPlaceState.bRotatingRight" => AlsRefactoredLocomotionPredicate.RotateRight,
                    "GetParent.MovementBase.bHasRelativeLocation" => AlsRefactoredLocomotionPredicate.RelativeLocation,
                    _ => throw new ArgumentException("Foreign Locomotion boolean input.")
                });
            else if (node.Member is "EqualEqual_GameplayTag" or "NotEqual_GameplayTag")
            {
                Function(node, "GameplayTags.BlueprintGameplayTagLibrary");
                var property = Property(Follow(node, "A"));
                var pin = node.Pins.Values.Single(p => p.Name == "B"); Require(pin.Links == "" && !pin.Output, "Connected Locomotion tag literal.");
                var line = node.Body.Split('\n').Single(l => l.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal));
                var tag = new[] { "Als.Stance.Standing", "Als.LocomotionMode.Grounded", "Als.LocomotionMode.InAir" }
                    .SingleOrDefault(t => line.Contains(",DefaultValue=\"(TagName=\\\"" + t + "\\\")\",", StringComparison.Ordinal));
                rule = new((property, tag) switch
                {
                    ("GetParent.Stance", "Als.Stance.Standing") => AlsRefactoredLocomotionPredicate.Standing,
                    ("GetParent.LocomotionMode", "Als.LocomotionMode.Grounded") => AlsRefactoredLocomotionPredicate.Grounded,
                    ("GetParent.LocomotionMode", "Als.LocomotionMode.InAir") => AlsRefactoredLocomotionPredicate.InAir,
                    _ => throw new ArgumentException("Foreign Locomotion tag.")
                });
                if (node.Member == "NotEqual_GameplayTag") rule = new(AlsRefactoredLocomotionPredicate.Not, rule);
            }
            else
            {
                Function(node, "Engine.KismetMathLibrary");
                if (node.Member is "GreaterEqual_DoubleDouble" or "Greater_DoubleDouble" or "LessEqual_DoubleDouble")
                {
                    var property = Property(Follow(node, "A"));
                    var pin = node.Pins.Values.Single(p => p.Name == "B"); Require(!pin.Output && pin.Links == "", "Connected numeric threshold.");
                    // K2's unconnected numeric pin has an implicit zero when no
                    // DefaultValue field is serialized (both FootPlanted rules).
                    var line = node.Body.Split('\n').Single(l => l.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal));
                    var literal = Regex.Match(line, ",DefaultValue=\"([^\"]*)\"");
                    var number = literal.Success ? float.Parse(literal.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                    rule = (property, node.Member, number) switch
                    {
                        ("GetParent.LocomotionState.Speed", "GreaterEqual_DoubleDouble", 650) => new(AlsRefactoredLocomotionPredicate.SpeedAtLeast, threshold: number),
                        ("GetParent.FeetState.FootPlantedAmount", "Greater_DoubleDouble", 0) => new(AlsRefactoredLocomotionPredicate.FootPositive),
                        ("GetParent.FeetState.FootPlantedAmount", "LessEqual_DoubleDouble", 0) => new(AlsRefactoredLocomotionPredicate.FootNonPositive),
                        _ => throw new ArgumentException("Foreign Locomotion numeric predicate.")
                    };
                }
                else
                {
                    Require(node.Member is "BooleanAND" or "BooleanOR", "Unsupported Locomotion boolean operator.");
                    var operands = node.Pins.Values.Where(p => !p.Output && p.Name != "self").Select(p => p.Name).Order().ToArray();
                    Require(operands.Length is >= 2 and <= 4 && operands.SequenceEqual(new[] { "A", "B", "C", "D" }.Take(operands.Length)), "Foreign Locomotion operand.");
                    var kind = node.Member == "BooleanAND" ? AlsRefactoredLocomotionPredicate.And : AlsRefactoredLocomotionPredicate.Or;
                    rule = Input(node, "A");
                    foreach (var operand in operands.Skip(1)) rule = new(kind, rule, Input(node, operand));
                }
            }
            active.Remove(node.Name); return rule;
        }
        var result = Input(root, "bCanEnterTransition");
        Require(graph.Nodes.All(n => visited.Contains(n.Name)), "Unconsumed Locomotion predicate node.");
        Require(!automatic || graph.Nodes.Count() == 1 && result.Kind == AlsRefactoredLocomotionPredicate.False, "Executable automatic Locomotion rule.");
        return result;
    }
    private static void Require(bool value, string message) { if (!value) throw new ArgumentException(message); }
}
