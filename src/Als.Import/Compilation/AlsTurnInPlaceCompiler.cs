using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Actions;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsTurnInPlaceCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private static readonly string[] Fields = ["Animation_2_7BA2980D4E85CC9E320EB98B57C73B3A",
        "AnimatedAngle_11_0BE5A89E455AEBE851DAA49A1FCA81A5", "SlotName_5_73864C9E40AE26D9F294038C7099722B",
        "PlayRate_8_47A596764C9AFE145D75C49448F776A8", "ScaleTurnAngle_14_ED8450744340CD92E19052A9E8766866"];

    public static AlsDynamicMontageAsset[] CompileMontageAssets(string json, AlsAnimationSetDefinition set, AlsPoseAnimationProfile pose)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            root.GetProperty("skeleton").GetString() == set.Skeletons[pose.SkeletonId].ObjectPath, "Wrong montage skeleton.");
        var slotGroups = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match group in Regex.Matches(root.GetProperty("skeletonText").GetString()!, @"(?m)^   SlotGroups\((\d+)\)=\(([^\r\n]+)\)"))
        {
            var slots = Regex.Match(group.Groups[2].Value, @"SlotNames=\(((?:""[^""]*""\s*,?\s*)*)\)");
            Require(slots.Success, "Malformed native slot list.");
            foreach (Match slot in Regex.Matches(slots.Groups[1].Value, @"""([^""]+)"""))
                Require(slotGroups.TryAdd(slot.Groups[1].Value, int.Parse(group.Groups[1].Value, CultureInfo.InvariantCulture)), "Duplicate montage slot membership.");
        }
        var records = root.GetProperty("assets").EnumerateArray().ToArray();
        Require(records.Length == 8 && records.Select(a => a.GetProperty("path").GetString()).Distinct().Count() == 8, "Incomplete turn montage assets.");
        var result = new List<AlsDynamicMontageAsset>();
        foreach (var turn in pose.Turns)
        {
            var sequence = set.Animations[turn.AnimationId]; var record = records.Single(a => a.GetProperty("path").GetString() == sequence.ObjectPath);
            Require(record.GetProperty("rateScale").GetSingle() == 1 && !record.GetProperty("rootMotionEnabled").GetBoolean() &&
                !sequence.RootMotionEnabled && sequence.AdditiveType == 0, "Turn montage requires original unit-rate, in-place sequence.");
            var slot = turn.Stance == AlsPoseStance.Standing ? AlsTurnSlot.Standing : AlsTurnSlot.Crouching;
            Require(slotGroups.TryGetValue(slot == AlsTurnSlot.Standing ? "(N) Turn/Rotate" : "(CLF) Turn/Rotate", out var group), "Missing turn slot group.");
            result.Add(new(sequence.Id, slot, group, sequence.PlayLength));
        }
        return result.ToArray();
    }

    public static AlsTurnInPlaceModel Compile(string json, AlsAnimationSetDefinition set, AlsPoseAnimationProfile pose)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("source").GetString() == Source, "Wrong turn source.");
        var data = root.GetProperty("graphs").EnumerateArray().Single(g => g.GetProperty("name").GetString() == "TurnInPlace");
        Require(data.GetProperty("path").GetString() == Source + ":TurnInPlace", "Wrong turn graph path.");
        var graph = new Graph(data.GetProperty("nativeText").GetString()!);
        ValidateGraph(graph);
        var defaults = root.GetProperty("defaultsText").GetString()!;
        string Default(string name)
        {
            var matches = Regex.Matches(defaults, @"(?m)^   " + Regex.Escape(name) + @"=([^\r\n]+)");
            Require(matches.Count == 1, "Missing turn default: " + name); return matches[0].Groups[1].Value;
        }
        var assets = new AlsTurnAsset[8];
        var turns = pose.Turns;
        Require(turns.Length == 8, "Turn pose bindings are incomplete.");
        for (var i = 0; i < assets.Length; i++)
        {
            var name = (i < 4 ? "N" : "CLF") + "_TurnIP_" + (i % 2 == 0 ? "L" : "R") + (i % 4 < 2 ? "90" : "180");
            var text = Default(name);
            var members = Regex.Matches(text, @"(\w+)=(""[^""]*""|[^,()]+)")
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim('"'), StringComparer.Ordinal);
            Require(members.Count == Fields.Length && Fields.All(members.ContainsKey), "Unexpected turn struct fields.");
            var assetRef = members[Fields[0]];
            Require(assetRef.StartsWith("/Script/Engine.AnimSequence'", StringComparison.Ordinal) && assetRef.EndsWith('\''), "Wrong turn asset type.");
            var path = assetRef[(assetRef.IndexOf('\'') + 1)..^1];
            var animation = set.Animations.SingleOrDefault(a => a.ObjectPath == path);
            Require(animation is not null && animation.SkeletonId == pose.SkeletonId && animation.AdditiveType == 0,
                "Missing or incompatible exported turn sequence: " + path);
            var angle = Number(members[Fields[1]]); var rate = Number(members[Fields[3]]);
            var scale = bool.Parse(members[Fields[4]]);
            Require(members[Fields[2]] == (i < 4 ? "(N) Turn/Rotate" : "(CLF) Turn/Rotate"), "Turn slot differs from pose graph.");
            var nominal = i % 4 < 2 ? 90 : 180; var direction = i % 2 == 0 ? -1 : 1;
            var binding = turns.Single(t => (int)t.Stance == i / 4 && t.Direction == direction && t.NominalDegrees == nominal);
            Require(binding.AnimationId == animation!.Id && binding.BasePlayRate == rate && binding.ScaleAngle == (scale ? 1 : 0) &&
                angle == direction * nominal && binding.BlendSeconds == Number(graph.Literal(graph.Named("K2Node_CallFunction_15"), "BlendInTime")),
                "Pose profile differs from TurnInPlace CDO.");
            assets[i] = new(animation.Id, i < 4 ? AlsTurnSlot.Standing : AlsTurnSlot.Crouching, angle, rate, scale);
        }
        var play = graph.Named("K2Node_CallFunction_15");
        return new(Number(Default("Turn180Threshold")), assets, Number(graph.Literal(play, "BlendInTime")),
            Number(graph.Literal(play, "BlendOutTime")), int.Parse(graph.Literal(play, "LoopCount"), CultureInfo.InvariantCulture),
            Number(graph.Literal(play, "BlendOutTriggerTime")));
    }

    private static void ValidateGraph(Graph g)
    {
        var entry = g.One("K2Node_FunctionEntry", "TurnInPlace"); var sequence = g.Named("K2Node_ExecutionSequence_0");
        Require(sequence.Kind == "K2Node_ExecutionSequence" && sequence.Pins.Values.Count(p => p.Output) == 3, "Wrong turn execution sequence.");
        g.Link(sequence, "execute", entry, "then");
        var angle = g.Named("K2Node_VariableSet_0"); Local(angle, "TurnAngle", "K2Node_VariableSet");
        Edge(g, angle, sequence, "then_0"); Empty(angle, "then");
        var (delta, yaw) = g.Follow(angle, "TurnAngle"); g.Function(delta, "NormalizedDeltaRotator", "Engine.KismetMathLibrary");
        Require(yaw.Name == "ReturnValue_Yaw", "Turn must use normalized yaw.");
        Expression(g, delta, "A", "Local.TargetRotation");
        var (actor, rotation) = g.Follow(delta, "B"); g.Function(actor, "K2_GetActorRotation", "Engine.Actor");
        Require(rotation.Name == "ReturnValue", "Wrong actor rotation output."); Expression(g, actor, "self", "Character");
        var threshold = Branch(g, "K2Node_IfThenElse_0"); Edge(g, threshold, sequence, "then_1");
        Expression(g, threshold, "Condition", "Less_DoubleDouble(Abs(Local.TurnAngle),Turn180Threshold)");
        var small = Branch(g, "K2Node_IfThenElse_11"); var large = Branch(g, "K2Node_IfThenElse_10");
        Edge(g, small, threshold, "then"); Edge(g, large, threshold, "else");
        foreach (var branch in new[] { small, large }) Expression(g, branch, "Condition", "Less_DoubleDouble(Local.TurnAngle,0)");
        int[] switches = [4, 5, 2, 3]; int[,] setters = { { 6, 7 }, { 5, 8 }, { 3, 22 }, { 4, 21 } };
        for (var i = 0; i < 4; i++)
        {
            var selection = g.Named("K2Node_SwitchEnum_" + switches[i]);
            Require(selection.Kind == "K2Node_SwitchEnum" && selection.Body.Contains("ALS_Stance.ALS_Stance'", StringComparison.Ordinal), "Wrong turn stance domain.");
            Edge(g, selection, i < 2 ? small : large, i % 2 == 0 ? "then" : "else"); Expression(g, selection, "Selection", "Stance");
            Require(selection.Pins.Values.Where(p => p.Output).Select(p => p.Name).Order().SequenceEqual(new[] { "NewEnumerator0", "NewEnumerator1" }), "Unsupported stance outputs.");
            for (var stance = 0; stance < 2; stance++)
            {
                var setter = g.Named("K2Node_VariableSet_" + setters[i, stance]); Local(setter, "TargetTurnAsset", "K2Node_VariableSet");
                Edge(g, setter, selection, "NewEnumerator" + stance); Empty(setter, "then");
                Expression(g, setter, "TargetTurnAsset", (stance == 0 ? "N" : "CLF") + "_TurnIP_" + (i % 2 == 0 ? "L" : "R") + (i < 2 ? "90" : "180"));
            }
        }
        var gate = Branch(g, "K2Node_IfThenElse_1"); Edge(g, gate, sequence, "then_2"); Empty(gate, "else");
        Expression(g, gate, "Condition", "BooleanOR(Local.OverrideCurrent,Not_PreBool(IsPlayingSlotAnimation(Asset.Animation,Asset.SlotName)))");
        var play = g.One("K2Node_CallFunction", "PlaySlotAnimationAsDynamicMontage"); g.Self(play); Edge(g, play, gate, "then");
        Expression(g, play, "Asset", "Asset.Animation"); Expression(g, play, "SlotNodeName", "Asset.SlotName");
        Expression(g, play, "InPlayRate", "Multiply_DoubleDouble(Asset.PlayRate,Local.PlayRateScale)");
        Expression(g, play, "InTimeToStartMontageAt", "Local.StartTime"); Empty(play, "ReturnValue");
        Require(Number(g.Literal(play, "BlendInTime")) >= 0 && Number(g.Literal(play, "BlendOutTime")) >= 0 &&
            Number(g.Literal(play, "LoopCount")) == 1 && Number(g.Literal(play, "BlendOutTriggerTime")) == 0, "Unsupported dynamic montage options.");
        var scaling = Branch(g, "K2Node_IfThenElse_2"); Edge(g, scaling, play, "then");
        Expression(g, scaling, "Condition", "Asset.ScaleTurnAngle");
        var scaled = g.Named("K2Node_VariableSet_1"); var unscaled = g.Named("K2Node_VariableSet_13");
        foreach (var setter in new[] { scaled, unscaled })
        { Require(setter.Kind == "K2Node_VariableSet" && setter.Member == "RotationScale", "Wrong scale setter."); g.Self(setter); Empty(setter, "then"); }
        Edge(g, scaled, scaling, "then"); Edge(g, unscaled, scaling, "else");
        Expression(g, scaled, "RotationScale", "Multiply_DoubleDouble(Divide_DoubleDouble(Local.TurnAngle,Asset.AnimatedAngle),Asset.PlayRate,Local.PlayRateScale)");
        Expression(g, unscaled, "RotationScale", "Multiply_DoubleDouble(Asset.PlayRate,Local.PlayRateScale)");
    }

    private static Node Branch(Graph g, string name)
    { var node = g.Named(name); Require(node.Kind == "K2Node_IfThenElse", "Wrong turn branch kind."); return node; }
    private static void Edge(Graph g, Node node, Node expected, string output)
    { var (actual, pin) = g.FollowReroutes(node, "execute"); Require(actual == expected && pin.Name == output, "Wrong turn execution edge."); }
    private static void Empty(Node node, string pin) => Require(node.Pins.Values.Single(p => p.Name == pin).Links == "", "Unexpected turn continuation.");
    private static void Local(Node node, string name, string kind)
    {
        Require(node.Kind == kind && node.Member == name && node.Body.Contains("MemberScope=\"TurnInPlace\"", StringComparison.Ordinal) &&
            !node.Body.Contains("bSelfContext=True", StringComparison.Ordinal) && !node.Body.Contains("MemberParent=", StringComparison.Ordinal), "Wrong turn local owner.");
    }
    private static void Expression(Graph g, Node node, string input, string expected) =>
        Require(Input(g, node, input, 0) == expected, "Wrong turn expression at " + node.Name + "." + input);
    private static string Input(Graph g, Node node, string input, int depth)
    {
        Require(depth < 32, "Cyclic turn expression.");
        if (node.Pins.Values.Single(p => p.Name == input && !p.Output).Links == "")
        {
            var value = g.Literal(node, input);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n.ToString("G", CultureInfo.InvariantCulture) : value;
        }
        var (other, pin) = g.FollowReroutes(node, input);
        if (other.Kind == "K2Node_VariableGet")
        {
            if (other.Body.Contains("MemberScope=", StringComparison.Ordinal))
            {
                Local(other, other.Member, "K2Node_VariableGet");
                if (other.Member == "TargetTurnAsset")
                {
                    var field = Array.FindIndex(Fields, f => pin.Name == "TargetTurnAsset_" + f);
                    Require(field >= 0, "Wrong turn struct output."); return "Asset." + Fields[field].Split('_')[0];
                }
                Require(pin.Name == other.Member, "Wrong turn local output."); return "Local." + other.Member;
            }
            g.Self(other); Require(pin.Name == other.Member, "Wrong turn self output."); return other.Member;
        }
        Require(pin.Name == "ReturnValue" && other.Kind is "K2Node_CallFunction" or "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported turn expression.");
        if (other.Member == "IsPlayingSlotAnimation")
        {
            Require(other.Kind == "K2Node_CallFunction", "Wrong slot query kind."); g.Self(other);
            return "IsPlayingSlotAnimation(" + Input(g, other, "Asset", depth + 1) + "," + Input(g, other, "SlotNodeName", depth + 1) + ")";
        }
        Require(other.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/Engine.KismetMathLibrary'\"", StringComparison.Ordinal), "Wrong turn math owner.");
        string[] args = other.Member switch
        {
            "Abs" or "Not_PreBool" => ["A"], "Divide_DoubleDouble" or "Less_DoubleDouble" => ["A", "B"],
            "BooleanOR" or "Multiply_DoubleDouble" => other.Pins.Values.Where(p => !p.Output && p.Name != "self").Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(),
            _ => throw new ArgumentException("Unsupported turn operation: " + other.Member),
        };
        return other.Member + "(" + string.Join(",", args.Select(arg => Input(g, other, arg, depth + 1))) + ")";
    }
    private static float Number(string text)
    { var value = float.Parse(text, CultureInfo.InvariantCulture); Require(float.IsFinite(value), "Non-finite turn value."); return value; }
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
